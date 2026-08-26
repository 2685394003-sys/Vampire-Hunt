using Unity.Netcode;
using UnityEngine;
using VampireHunt.Bootstrap;
using VampireHunt.Boss.Encounter;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity.Boss;
using VampireHunt.Navigation;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Server-authoritative encounter orchestrator. It coordinates atomic components but owns
    /// no hit detection, skill implementation, VFX, audio, UI or movement algorithm itself.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(BossEncounterStateReplicator))]
    [RequireComponent(typeof(BossAbilityServerDriver))]
    public sealed class BossEncounterDirector : NetworkBehaviour
    {
        [SerializeField] private BossEncounterConfigAsset config;
        [SerializeField] private BossEncounterStateReplicator stateReplicator;
        [SerializeField] private BossAbilityServerDriver abilityDriver;
        [SerializeField] private BossRoamingMovement roamingMovement;
        [SerializeField] private BossBodyStateHost bodyState;
        [SerializeField] private VampireHuntGameManager runManager;
        [SerializeField] private int runSeed = 72631;

        private BossEncounterAggregate m_Aggregate;
        private IPlayerTargetQuery m_TargetQuery;
        private double m_StateEnterTime;
        private BossEncounterState m_ObservedState;
        private int m_CurrentAbilityPhase;
        private uint m_PendingForcedAbility;
        private uint m_RandomOrdinal;
        private bool m_FrenzyTriggered;
        private bool m_SpawnPositionChosen;

        public BossEncounterConfigAsset Config => config;
        public BossEncounterState State => m_Aggregate?.State ?? BossEncounterState.Dormant;
        public int StageNumber => m_Aggregate?.StageNumber ?? 1;

        private void Awake()
        {
            if (stateReplicator == null) stateReplicator = GetComponent<BossEncounterStateReplicator>();
            if (abilityDriver == null) abilityDriver = GetComponent<BossAbilityServerDriver>();
            if (roamingMovement == null) roamingMovement = GetComponent<BossRoamingMovement>();
            if (bodyState == null) bodyState = GetComponent<BossBodyStateHost>();
            if (runManager == null) runManager = FindAnyObjectByType<VampireHuntGameManager>();
            roamingMovement?.SetConfig(config);
            ResolveTargetQuery();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return;
            if (config == null)
            {
                Debug.LogError("[BossEncounterDirector] BossEncounterConfigAsset is not assigned.", this);
                enabled = false;
                return;
            }

            m_Aggregate = new BossEncounterAggregate(config.CreateRules());
            m_Aggregate.BeginRun();
            m_ObservedState = m_Aggregate.State;
            m_StateEnterTime = NetworkManager.ServerTime.Time;
            m_CurrentAbilityPhase = 0;
            m_PendingForcedAbility = 0;
            m_RandomOrdinal = 0;
            m_FrenzyTriggered = false;
            m_SpawnPositionChosen = false;
            bodyState?.TrySetNormalizedHealth(1f);
            stateReplicator.PublishServer(m_Aggregate.CaptureSnapshot(), force: true);
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || m_Aggregate == null) return;
            if (runManager == null) runManager = FindAnyObjectByType<VampireHuntGameManager>();
            ResolveTargetQuery();

            if (!m_SpawnPositionChosen)
                m_SpawnPositionChosen = roamingMovement != null &&
                                        roamingMovement.TrySpawnInPlayerAnnulusServer(NextRandom());

            bool hasTarget = TryGetNearest(out BossPlayerTarget target, out float distance);
            if (!m_Aggregate.HudVisible && hasTarget && distance <= config.DetectionRange)
                m_Aggregate.SetEngaged();

            TickState(hasTarget, target, distance);
            TryForcePendingAbility();
            ObserveStateTransition();
            bodyState?.TrySetNormalizedHealth(m_Aggregate.MaxHealth > 0f
                ? m_Aggregate.Health / m_Aggregate.MaxHealth
                : 0f);
            stateReplicator.PublishServer(m_Aggregate.CaptureSnapshot());
        }

        public bool TryApplyPlayerDamageServer(GameplayEntityId source, float amount, in Float3 attackerPosition,
            out BossDamageOutcome outcome)
        {
            outcome = BossDamageOutcome.Ignored;
            if (!IsServer || m_Aggregate == null || amount <= 0f) return false;
            float distance = Distance(attackerPosition, ToFloat3(transform.position));
            float validatedDamage = Mathf.Min(amount, config.MaxTrustedHitDamage);
            BossEncounterState previous = m_Aggregate.State;
            outcome = m_Aggregate.ApplyDamage(validatedDamage, distance);
            if (outcome == BossDamageOutcome.Ignored) return false;
            if (previous != m_Aggregate.State) ObserveStateTransition(force: true);
            stateReplicator.PublishServer(m_Aggregate.CaptureSnapshot(), force: true);
            return true;
        }

        private void TickState(bool hasTarget, in BossPlayerTarget target, float distance)
        {
            double now = NetworkManager.ServerTime.Time;
            switch (m_Aggregate.State)
            {
                case BossEncounterState.RoamingIdle:
                case BossEncounterState.RoamingEvade:
                    EnsureAbilityPhase(config.RoamingAbilityPhaseNumber);
                    bool evade = hasTarget && distance <= config.EvadeDistance;
                    m_Aggregate.SetRoamingEvade(evade);
                    roamingMovement?.TickServer(evade);
                    abilityDriver?.SetAutomaticCastsServer(!evade);
                    if (m_Aggregate.GuardHealth <= 0f && hasTarget)
                        m_Aggregate.TryBeginStagger(distance);
                    break;

                case BossEncounterState.StaggerEffect:
                    roamingMovement?.TickServer(false);
                    abilityDriver?.SetAutomaticCastsServer(false);
                    if (now - m_StateEnterTime >= config.StaggerEffectDuration)
                        m_Aggregate.CompleteStaggerEffect();
                    break;

                case BossEncounterState.ExecutionWindow:
                    roamingMovement?.TickServer(false);
                    abilityDriver?.SetAutomaticCastsServer(false);
                    if (now - m_StateEnterTime >= config.ExecutionWindowDuration)
                        m_Aggregate.CompleteExecutionWindow();
                    break;

                case BossEncounterState.Battle:
                    roamingMovement?.TickServer(false);
                    EnsureAbilityPhase(config.GetAbilityPhaseNumber(m_Aggregate.StageNumber));
                    abilityDriver?.SetAutomaticCastsServer(true);
                    TryQueueFrenzy();
                    break;

                case BossEncounterState.PhaseTransition:
                    roamingMovement?.TickServer(false);
                    abilityDriver?.SetAutomaticCastsServer(false);
                    if (now - m_StateEnterTime >= config.PhaseTransitionDuration &&
                        m_Aggregate.CompletePhaseTransition())
                    {
                        m_CurrentAbilityPhase = 0;
                        m_SpawnPositionChosen = roamingMovement != null &&
                                                roamingMovement.TryTeleportAwayServer(NextRandom());
                    }
                    break;

                case BossEncounterState.Defeated:
                    abilityDriver?.SetAutomaticCastsServer(false);
                    roamingMovement?.TickServer(false);
                    break;
            }
        }

        private void ObserveStateTransition(bool force = false)
        {
            BossEncounterState current = m_Aggregate.State;
            if (!force && current == m_ObservedState) return;
            m_ObservedState = current;
            m_StateEnterTime = NetworkManager.ServerTime.Time;

            switch (current)
            {
                case BossEncounterState.StaggerEffect:
                    abilityDriver?.SetAutomaticCastsServer(false);
                    abilityDriver?.TryCancelActiveCastServer();
                    bodyState?.TrySetStaggered(true);
                    m_PendingForcedAbility = config.GetStaggerAbilityId(NextRandom());
                    break;
                case BossEncounterState.ExecutionWindow:
                    bodyState?.TrySetStaggered(true);
                    break;
                case BossEncounterState.Battle:
                    bodyState?.TrySetStaggered(false);
                    runManager?.TryEnterBossEncounter();
                    m_CurrentAbilityPhase = 0;
                    break;
                case BossEncounterState.PhaseTransition:
                    abilityDriver?.SetAutomaticCastsServer(false);
                    abilityDriver?.TryCancelActiveCastServer();
                    bodyState?.TrySetStaggered(false);
                    runManager?.TryBeginBossPhaseTransition();
                    m_PendingForcedAbility = config.PhaseAuraAbilityId;
                    break;
                case BossEncounterState.RoamingIdle:
                    bodyState?.TrySetStaggered(false);
                    runManager?.TryResumeExploration();
                    m_CurrentAbilityPhase = 0;
                    break;
                case BossEncounterState.Defeated:
                    abilityDriver?.SetAutomaticCastsServer(false);
                    abilityDriver?.TryCancelActiveCastServer();
                    bodyState?.TrySetNormalizedHealth(0f);
                    runManager?.TryCompleteRun();
                    break;
            }
        }

        private void EnsureAbilityPhase(int phaseNumber)
        {
            if (abilityDriver == null || phaseNumber <= 0 || m_CurrentAbilityPhase == phaseNumber) return;
            if (abilityDriver.TrySetPhaseServer(phaseNumber)) m_CurrentAbilityPhase = phaseNumber;
        }

        private void TryForcePendingAbility()
        {
            if (m_PendingForcedAbility == 0 || abilityDriver == null || abilityDriver.HasActiveCast) return;
            if (abilityDriver.TryForceAbilityServer(m_PendingForcedAbility)) m_PendingForcedAbility = 0;
        }

        private void TryQueueFrenzy()
        {
            if (m_FrenzyTriggered || m_Aggregate.StageNumber != 3 ||
                m_Aggregate.Health / m_Aggregate.MaxHealth > config.FrenzyHealthThreshold ||
                !AnyPlayerHasPact(config.RequiredFrenzyPactId)) return;
            m_FrenzyTriggered = true;
            m_PendingForcedAbility = config.FrenzyAbilityId;
        }

        private bool AnyPlayerHasPact(uint pactId)
        {
            if (pactId == 0 || NetworkManager == null) return false;
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject != null &&
                    client.PlayerObject.TryGetComponent(out PactNetworkState pacts) &&
                    pacts.GetStacks(pactId) > 0) return true;
            }
            return false;
        }

        private bool TryGetNearest(out BossPlayerTarget target, out float distance)
        {
            target = default;
            distance = float.MaxValue;
            if (m_TargetQuery == null || !m_TargetQuery.TryGetNearest(ToFloat3(transform.position), float.MaxValue,
                    out target)) return false;
            distance = Distance(ToFloat3(transform.position), target.Position);
            return true;
        }

        private void ResolveTargetQuery()
        {
            if (m_TargetQuery != null) return;
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] is IPlayerTargetQuery query) { m_TargetQuery = query; break; }
        }

        private uint NextRandom()
        {
            unchecked
            {
                uint value = (uint)runSeed ^ (++m_RandomOrdinal * 0x9E3779B9u);
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return value;
            }
        }

        private static float Distance(in Float3 left, in Float3 right)
        {
            float x = left.X - right.X;
            float y = left.Y - right.Y;
            float z = left.Z - right.Z;
            return Mathf.Sqrt(x * x + y * y + z * z);
        }

        private static Float3 ToFloat3(Vector3 value) => new Float3(value.x, value.y, value.z);
    }
}
