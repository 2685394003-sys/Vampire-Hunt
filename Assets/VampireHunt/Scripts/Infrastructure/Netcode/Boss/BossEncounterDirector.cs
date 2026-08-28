using Unity.Netcode;
using UnityEngine;
using VampireHunt.Bootstrap;
using VampireHunt.Boss.Encounter;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity.Boss;
using VampireHunt.Navigation;
using VampireHunt.Systems;
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
        [SerializeField] private BossHandCoordinator handCoordinator;
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
        private double m_LastPlayerDamageTime = double.NegativeInfinity;

        public BossEncounterConfigAsset Config => config;
        public BossEncounterState State => m_Aggregate?.State ?? BossEncounterState.Dormant;
        public int StageNumber => m_Aggregate?.StageNumber ?? 1;

        private void Awake()
        {
            if (stateReplicator == null) stateReplicator = GetComponent<BossEncounterStateReplicator>();
            if (abilityDriver == null) abilityDriver = GetComponent<BossAbilityServerDriver>();
            if (roamingMovement == null) roamingMovement = GetComponent<BossRoamingMovement>();
            if (bodyState == null) bodyState = GetComponent<BossBodyStateHost>();
            if (handCoordinator == null) handCoordinator = GetComponent<BossHandCoordinator>();
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
            // 单人模式菜单暂停时冻结整个 Boss 遭遇模拟（ServerTime 是墙钟，不受 Time.timeScale 影响）。
            if (MenuPauseController.IsPaused) return;
            if (runManager == null) runManager = FindAnyObjectByType<VampireHuntGameManager>();
            ResolveTargetQuery();

            if (!m_SpawnPositionChosen)
                m_SpawnPositionChosen = roamingMovement != null &&
                                        roamingMovement.TrySpawnInPlayerAnnulusServer(NextRandom());

            bool hasTarget = TryGetNearest(out BossPlayerTarget target, out float distance);
            // Boss 血量 HUD：玩家距离 Boss ≤ BossHudShowDistance 时显示，超出则隐藏
            m_Aggregate.SetEngaged(hasTarget && distance <= config.BossHudShowDistance);

            // 玩家全部死亡 → Boss 退出战斗，格挡条恢复满（本体血保留）
            if (!hasTarget) m_Aggregate.ResetToRoaming();

            // 格挡条脱战恢复：无伤害超过 GuardRegenDelaySeconds 后持续恢复
            if (m_Aggregate.State == BossEncounterState.RoamingIdle ||
                m_Aggregate.State == BossEncounterState.RoamingEvade)
            {
                if (NetworkManager.ServerTime.Time - m_LastPlayerDamageTime >= config.GuardRegenDelaySeconds)
                    m_Aggregate.RegenerateGuard(config.GuardRegenPerSecond * Time.deltaTime);
            }

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
            m_LastPlayerDamageTime = NetworkManager.ServerTime.Time;
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
                    // 远离玩家（evade）时也继续自动施法攻击，而不是停下技能
                    abilityDriver?.SetAutomaticCastsServer(true);
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
                    // 战斗中有目标 → 环形游走 + 距离抖动；无目标 → 减速停下
                    if (hasTarget)
                        roamingMovement?.TickBattleServer(target);
                    else
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
                        // Boss 传送后，左右手跟随传送到新位置
                        handCoordinator?.TeleportHandsToBossServer();
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
                    // 击破本阶段 → 给整局 run 倒计时加时奖励（阶段1 = +6min，阶段2 = +8min；阶段3 击破即通关不另加）
                    TryExtendClockForClearedStage(m_Aggregate.StageNumber);
                    // 进入下一阶段 → 左右手立刻复活
                    handCoordinator?.RestoreAllHandsServer();
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

        // 击破某阶段时按配置给整局 run 倒计时加时（服务器权威；仅在未进入终局/大厅时生效）。
        // 调用时机：ObserveStateTransition 进入 PhaseTransition（此时 m_Aggregate.StageNumber 仍是刚被清空的阶段 N）。
        private void TryExtendClockForClearedStage(int clearedStage)
        {
            if (runManager == null || config == null) return;
            float bonus = config.GetStageClearBonusSeconds(clearedStage);
            if (bonus > 0f)
            {
                runManager.TryExtendClock(bonus);
                Debug.Log($"[BossEncounterDirector] 击破阶段 {clearedStage} → run 倒计时 +{bonus:F0}s");
            }
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
