using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Enemies;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Presentation.Enemies;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Client-server adapter for one server-owned enemy. The aggregate decides
    /// state; this component supplies Unity targeting, movement and NGO state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class EnemyNetworkActor : HitProcessor, IDamageReceiver, ITrustedCombatHitTarget, ICombatEntityIdentity
    {
        private const uint SwordWaveAttackId = 1;
        private const float BrainInterval = 0.1f;
        private const float TargetRefreshInterval = 0.25f;
        private const float KnockbackDecay = 8f;

        [Header("Configuration")]
        [SerializeField] private EnemyArchetypeAsset archetype;

        [Header("Unity Adapters")]
        [SerializeField] private CharacterController characterController;
        [SerializeField] private EnemyPresenter presenter;
        [SerializeField] private CombatModifierHost modifierHost;
        [SerializeField] private CombatStatusHost statusHost;
        [SerializeField] private EnemyLootDropper lootDropper;
        [SerializeField] private DamagePresentationEvent onDamagePresented;

        [Header("Lifecycle")]
        [Min(0f)] [SerializeField] private float deathDespawnDelay = 1.25f;

        private readonly NetworkVariable<EnemyNetworkState> m_ReplicatedState =
            new NetworkVariable<EnemyNetworkState>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly EnemyApplicationService m_Application = new EnemyApplicationService();
        private EnemyAggregate m_Aggregate;
        private NetworkObject m_TargetPlayer;
        private ulong m_PreparedEntityId;
        private ulong m_TrustedHitSequence;
        private float m_NextBrainTime;
        private float m_NextTargetRefreshTime;
        private float m_DeathDespawnTime = float.PositiveInfinity;
        private Vector3 m_KnockbackVelocity;
        private bool m_RewardGranted;
        private bool m_WasMovementBlocked;
        private double m_BlockStartedTime;

        public EnemyNetworkState ReplicatedState => m_ReplicatedState.Value;
        public bool IsDead => m_ReplicatedState.Value.State == EnemyState.Dead;
        public GameplayEntityId CombatEntityId => m_Aggregate != null
            ? m_Aggregate.Id
            : new GameplayEntityId(m_ReplicatedState.Value.EntityId);

        private void Awake()
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
            if (presenter == null) presenter = GetComponentInChildren<EnemyPresenter>();
            if (modifierHost == null) modifierHost = GetComponent<CombatModifierHost>();
            if (statusHost == null) statusHost = GetComponent<CombatStatusHost>();
            if (lootDropper == null) lootDropper = GetComponent<EnemyLootDropper>();
        }

        public void PrepareServerSpawn(ulong entityId)
        {
            m_PreparedEntityId = entityId;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_ReplicatedState.OnValueChanged += HandleReplicatedStateChanged;

            if (IsServer)
            {
                InitializeServerAggregate();
            }

            ApplyReplicatedState(m_ReplicatedState.Value);
        }

        public override void OnNetworkDespawn()
        {
            m_ReplicatedState.OnValueChanged -= HandleReplicatedStateChanged;
            m_TargetPlayer = null;
            m_Aggregate = null;
            m_KnockbackVelocity = Vector3.zero;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || m_Aggregate == null) return;

            if (m_Aggregate.IsDead)
            {
                if (Time.unscaledTime >= m_DeathDespawnTime && NetworkObject.IsSpawned)
                {
                    NetworkObject.Despawn();
                }
                return;
            }

            if (HandleMovementBlock()) return;

            if (Time.unscaledTime >= m_NextTargetRefreshTime)
            {
                m_NextTargetRefreshTime = Time.unscaledTime + TargetRefreshInterval;
                RefreshTarget();
            }

            if (Time.unscaledTime >= m_NextBrainTime)
            {
                m_NextBrainTime = Time.unscaledTime + BrainInterval;
                TickBrain();
            }

            MoveAndFaceTarget();
        }

        protected override void HandleHit(HitInfo info)
        {
            if (!IsServer || m_Aggregate == null || m_Aggregate.IsDead || info.amount <= 0f) return;

            var request = new DamageRequest(
                new GameplayEntityId(info.attackerId + 1UL),
                m_Aggregate.Id,
                SwordWaveAttackId,
                ++m_TrustedHitSequence,
                info.amount,
                DamageTags.Projectile | DamageTags.SwordWave);

            bool damageApplied = TryApplyDamage(request, out ResolvedDamage result);

            if (!damageApplied) return;

            if (!result.IsCancelled && result.Amount > 0f && info.impactForce.sqrMagnitude > 0f)
            {
                m_KnockbackVelocity += info.impactForce;
            }

        }

        public bool SubmitTrustedHit(in TrustedCombatHit hit)
        {
            if (!IsSpawned) return false;
            if (IsServer) return ApplyTrustedHit(hit);
            RequestTrustedHitRpc(TrustedCombatHitNetworkMessage.FromDomain(hit));
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestTrustedHitRpc(TrustedCombatHitNetworkMessage message, RpcParams rpcParams = default)
        {
            var request = new DamageRequest(
                new GameplayEntityId(rpcParams.Receive.SenderClientId + 1UL), m_Aggregate?.Id ?? GameplayEntityId.None,
                message.AttackId, message.Sequence, message.Damage, (DamageTags)message.Tags);
            int statusCount = Mathf.Min(4, message.Statuses.Count);
            var statuses = new StatusEffectSpec[statusCount];
            for (int i = 0; i < statusCount; i++) statuses[i] = message.Statuses.Get(i).ToDomain();
            ApplyTrustedHit(new TrustedCombatHit(request, (ElementId)message.Element, statuses,
                new Float3(message.ImpactForce.x, message.ImpactForce.y, message.ImpactForce.z)));
        }

        private bool ApplyTrustedHit(in TrustedCombatHit hit)
        {
            if (!IsServer || m_Aggregate == null || m_Aggregate.IsDead || hit.Damage.BaseDamage <= 0f) return false;
            float reactionMultiplier = statusHost != null
                ? statusHost.ResolveElementReaction(hit.Element)
                : 1f;
            var request = new DamageRequest(hit.Damage.Source, m_Aggregate.Id, hit.Damage.AttackId,
                hit.Damage.Sequence, hit.Damage.BaseDamage * reactionMultiplier, hit.Damage.Tags);
            if (!TryApplyDamage(request, out ResolvedDamage result)) return false;

            if (!result.IsCancelled && result.Amount > 0f)
            {
                Vector3 force = new Vector3(hit.ImpactForce.X, hit.ImpactForce.Y, hit.ImpactForce.Z);
                m_KnockbackVelocity += force;
                if (!m_Aggregate.IsDead && statusHost != null)
                {
                    for (int i = 0; i < hit.Statuses.Length; i++)
                    {
                        var application = new StatusApplicationRequest(request.Source, m_Aggregate.Id, hit.Statuses[i]);
                        statusHost.TryApplyStatus(application);
                    }
                }
            }
            return true;
        }

        public bool TryApplyDamage(in DamageRequest request, out ResolvedDamage result)
        {
            result = modifierHost != null
                ? modifierHost.ResolveIncoming(request)
                : new DamageContext(request).ToResult();

            if (!IsServer || m_Aggregate == null) return false;
            float healthBefore = m_Aggregate.CurrentHealth;
            if (!m_Application.ApplyDamage(m_Aggregate, result)) return false;
            float healthAfter = m_Aggregate.CurrentHealth;
            var resolution = new CombatResolutionRecord(result, healthBefore, healthAfter);
            modifierHost?.NotifyOutcome(result);
            RaiseDamagePresentationRpc(request.Source.Value, m_Aggregate.Id.Value, request.AttackId,
                request.Sequence, result.Amount, (uint)result.Tags, transform.position + Vector3.up);
            ServerCombatResolutionRouter.Publish(NetworkManager, resolution);

            // Schedule authoritative cleanup before publishing presentation
            // state. A client/server presentation failure must never strand a
            // dead network entity in the simulation.
            if (m_Aggregate.IsDead)
            {
                ScheduleDeathDespawn();
                GrantDeathRewardFromSource(request.Source);
                lootDropper?.SpawnDropsOnce(archetype.LootTable, m_Aggregate.Id.Value, transform.position);
            }

            PublishState();
            return true;
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void RaiseDamagePresentationRpc(ulong sourceEntityId, ulong targetEntityId,
            uint attackId, ulong sequence, float amount, uint tags, Vector3 worldPosition)
        {
            onDamagePresented?.Raise(new DamagePresentationPayload
            {
                sourceEntityId = sourceEntityId,
                targetEntityId = targetEntityId,
                attackId = attackId,
                sequence = sequence,
                amount = amount,
                tags = (DamageTags)tags,
                worldPosition = worldPosition
            });
        }

        private void ScheduleDeathDespawn()
        {
            if (!float.IsPositiveInfinity(m_DeathDespawnTime)) return;
            m_DeathDespawnTime = Time.unscaledTime + deathDespawnDelay;
        }

        private void InitializeServerAggregate()
        {
            if (archetype == null)
            {
                Debug.LogError("[EnemyNetworkActor] EnemyArchetypeAsset is not assigned.", this);
                enabled = false;
                return;
            }

            ulong entityId = m_PreparedEntityId != 0
                ? m_PreparedEntityId
                : (1UL << 32) + NetworkObjectId + 1UL;
            double serverTime = NetworkManager.ServerTime.Time;
            m_Aggregate = new EnemyAggregate(new GameplayEntityId(entityId), archetype.ToDefinition(), serverTime);
            m_TargetPlayer = null;
            m_TrustedHitSequence = 0;
            m_KnockbackVelocity = Vector3.zero;
            m_NextBrainTime = Time.unscaledTime;
            m_NextTargetRefreshTime = Time.unscaledTime;
            m_RewardGranted = false;
            m_DeathDespawnTime = float.PositiveInfinity;
            m_WasMovementBlocked = false;
            m_BlockStartedTime = 0d;
            PublishState();
        }

        private bool HandleMovementBlock()
        {
            bool blocked = statusHost != null && statusHost.IsMovementBlocked;
            double serverTime = NetworkManager.ServerTime.Time;
            if (blocked && !m_WasMovementBlocked)
            {
                m_WasMovementBlocked = true;
                m_BlockStartedTime = serverTime;
            }
            else if (!blocked && m_WasMovementBlocked)
            {
                m_WasMovementBlocked = false;
                m_Aggregate.DelayCurrentState(serverTime - m_BlockStartedTime);
                PublishState();
            }
            return blocked;
        }

        private void TickBrain()
        {
            uint revisionBeforeTick = m_Aggregate.Revision;
            bool hasTarget = IsTargetValid(m_TargetPlayer);
            float distance = hasTarget
                ? Vector3.Distance(transform.position, m_TargetPlayer.transform.position)
                : float.MaxValue;
            double serverTime = NetworkManager.ServerTime.Time;

            if (hasTarget)
            {
                m_Aggregate.SetTarget(new GameplayEntityId(m_TargetPlayer.OwnerClientId + 1UL), serverTime);
            }
            else if (!m_Aggregate.TargetId.IsNone)
            {
                m_Aggregate.ClearTarget(serverTime);
            }

            EnemyTickResult tick = m_Application.Tick(
                m_Aggregate,
                new EnemyTickInput(hasTarget, distance, serverTime));

            if (tick.ShouldCommitAttack && m_Aggregate.TryCommitAttack())
            {
                CommitMeleeAttack();
            }

            // SetTarget/ClearTarget can transition before Aggregate.Tick captures
            // its local previous state, so Revision is the authoritative change
            // detector for everything performed in this brain step.
            if (m_Aggregate.Revision != revisionBeforeTick) PublishState();
        }

        private void MoveAndFaceTarget()
        {
            if (characterController != null && characterController.enabled && m_KnockbackVelocity.sqrMagnitude > 0.0001f)
            {
                characterController.Move(m_KnockbackVelocity * Time.deltaTime);
                m_KnockbackVelocity = Vector3.MoveTowards(
                    m_KnockbackVelocity,
                    Vector3.zero,
                    KnockbackDecay * Time.deltaTime);
            }

            if (!IsTargetValid(m_TargetPlayer)) return;

            Vector3 direction = m_TargetPlayer.transform.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f) return;
            direction.Normalize();

            if (m_Aggregate.State == EnemyState.Approaching && characterController != null && characterController.enabled)
            {
                characterController.Move(direction * (m_Aggregate.Definition.MoveSpeed * Time.deltaTime));
            }

            if (m_Aggregate.State == EnemyState.Approaching ||
                m_Aggregate.State == EnemyState.Telegraphing ||
                m_Aggregate.State == EnemyState.Attacking)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 540f * Time.deltaTime);
            }
        }

        private void RefreshTarget()
        {
            NetworkObject bestTarget = null;
            float bestSqrDistance = float.MaxValue;
            float maxSqrDistance = m_Aggregate.Definition.DetectionRange * m_Aggregate.Definition.DetectionRange;

            var clients = NetworkManager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                NetworkObject player = clients[i].PlayerObject;
                if (!IsTargetValid(player)) continue;

                float sqrDistance = (player.transform.position - transform.position).sqrMagnitude;
                if (sqrDistance > maxSqrDistance || sqrDistance >= bestSqrDistance) continue;
                bestSqrDistance = sqrDistance;
                bestTarget = player;
            }

            m_TargetPlayer = bestTarget;
        }

        private static bool IsTargetValid(NetworkObject player)
        {
            if (player == null || !player.IsSpawned) return false;
            return !player.TryGetComponent<CorePlayerState>(out var state) || state.IsActive;
        }

        private void CommitMeleeAttack()
        {
            if (!IsTargetValid(m_TargetPlayer)) return;

            float distance = Vector3.Distance(transform.position, m_TargetPlayer.transform.position);
            if (distance > m_Aggregate.Definition.AttackBreakRange) return;

            Vector3 direction = m_TargetPlayer.transform.position - transform.position;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0f ? direction.normalized : transform.forward;
            var hitInfo = new HitInfo
            {
                amount = m_Aggregate.Definition.AttackDamage,
                hitPoint = m_TargetPlayer.transform.position + Vector3.up,
                hitNormal = -direction,
                attackerId = m_Aggregate.Id.Value,
                impactForce = direction * m_Aggregate.Definition.AttackKnockback
            };

            var behaviours = m_TargetPlayer.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is IHittable hittable)) continue;
                hittable.OnHit(hitInfo);
                return;
            }
        }

        private void GrantDeathReward(ulong attackerClientId)
        {
            if (m_RewardGranted || !NetworkManager.ConnectedClients.TryGetValue(attackerClientId, out var client)) return;
            if (client.PlayerObject == null) return;

            var behaviours = client.PlayerObject.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is IScarletRewardReceiver receiver)) continue;
                if (receiver.TryGrantScarlet(m_Aggregate.Definition.ScarletReward, m_Aggregate.Id))
                {
                    m_RewardGranted = true;
                }
                return;
            }
        }

        private void GrantDeathRewardFromSource(GameplayEntityId source)
        {
            if (source.IsNone || source.Value == 0) return;
            GrantDeathReward(source.Value - 1UL);
        }

        private void PublishState()
        {
            if (!IsServer || m_Aggregate == null) return;
            m_ReplicatedState.Value = EnemyNetworkState.FromSnapshot(m_Aggregate.CaptureSnapshot());
        }

        private void HandleReplicatedStateChanged(EnemyNetworkState previous, EnemyNetworkState current)
        {
            try
            {
                ApplyReplicatedState(current);
            }
            catch (global::System.Exception exception)
            {
                // Replicated gameplay state is already committed. Keep the
                // server simulation alive and surface the presentation fault.
                Debug.LogException(exception, this);
            }
        }

        private void ApplyReplicatedState(in EnemyNetworkState state)
        {
            if (presenter != null)
            {
                presenter.Apply(state);
            }
            if (characterController != null)
            {
                characterController.enabled = state.State != EnemyState.Dead;
            }
        }
    }
}
