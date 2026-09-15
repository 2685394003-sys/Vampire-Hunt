using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Effects;
using VampireHunt.Enemies;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Navigation;
using VampireHunt.Presentation.Enemies;
using VampireHunt.Progression;
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
        [SerializeField] private EnemyAffixCatalogAsset affixCatalog;

        [Header("Unity Adapters")]
        [SerializeField] private CharacterController characterController;
        [SerializeField] private EnemyNavigationAgent navigationAgent;
        [SerializeField] private EnemyPresenter presenter;
        [SerializeField] private CombatModifierHost modifierHost;
        [SerializeField] private CombatStatusHost statusHost;
        [SerializeField] private GameplayEffectHost effectHost;
        [SerializeField] private EnemyLootDropper lootDropper;
        [SerializeField] private DamagePresentationEvent onDamagePresented;

        [Header("Attack Adapter")]
        [Tooltip("Component implementing IEnemyAttackExecutor. Existing melee prefabs fall back to a runtime melee adapter.")]
        [SerializeField] private MonoBehaviour attackExecutorBehaviour;
        [SerializeField] private Transform attackOrigin;
        [SerializeField] private LayerMask attackBlockingMask = 1 << 0;

        [Header("Lifecycle")]
        [Min(0f)] [SerializeField] private float deathDespawnDelay = 1.25f;

        private readonly NetworkVariable<EnemyNetworkState> m_ReplicatedState =
            new NetworkVariable<EnemyNetworkState>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkList<EnemyAffixStackNetworkState> m_ReplicatedAffixes =
            new NetworkList<EnemyAffixStackNetworkState>();

        private readonly EnemyApplicationService m_Application = new EnemyApplicationService();
        private EnemyAggregate m_Aggregate;
        private NetworkObject m_TargetPlayer;
        private ulong m_PreparedEntityId;
        private EnemyAffixSpawnSnapshot m_PreparedAffixes = EnemyAffixSpawnSnapshot.Empty;
        private EnemyAffixCatalog m_DomainAffixCatalog;
        private ulong m_TrustedHitSequence;
        private float m_NextBrainTime;
        private float m_NextTargetRefreshTime;
        private float m_DeathDespawnTime = float.PositiveInfinity;
        private Vector3 m_KnockbackVelocity;
        private float m_VerticalVelocity;
        private bool m_RewardGranted;
        private bool m_WasMovementBlocked;
        private double m_BlockStartedTime;
        private IEnemyAttackExecutor m_AttackExecutor;
        private EnemyMovementIntent m_MovementIntent;
        private Vector3 m_AttackAimDirection;
        private bool m_MissingAttackExecutorReported;

        public EnemyNetworkState ReplicatedState => m_ReplicatedState.Value;
        public bool IsDead => m_ReplicatedState.Value.State == EnemyState.Dead;
        public string ArchetypeStableId => archetype != null ? archetype.StableId : string.Empty;
        public int SpawnCost => archetype != null ? archetype.SpawnCost : 1;
        public GameplayEntityId CombatEntityId => m_Aggregate != null
            ? m_Aggregate.Id
            : new GameplayEntityId(m_ReplicatedState.Value.EntityId);

        private void Awake()
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
            if (navigationAgent == null) navigationAgent = GetComponent<EnemyNavigationAgent>();
            if (presenter == null) presenter = GetComponentInChildren<EnemyPresenter>();
            if (modifierHost == null) modifierHost = GetComponent<CombatModifierHost>();
            if (statusHost == null) statusHost = GetComponent<CombatStatusHost>();
            if (effectHost == null) effectHost = GetComponent<GameplayEffectHost>();
            if (lootDropper == null) lootDropper = GetComponent<EnemyLootDropper>();
            ResolveSerializedAttackExecutor();
            if (affixCatalog == null)
            {
                EnemyAffixRunState runState = FindAnyObjectByType<EnemyAffixRunState>();
                if (runState != null) affixCatalog = runState.CatalogAsset;
            }
            m_DomainAffixCatalog = affixCatalog != null
                ? affixCatalog.CreateCatalog()
                : new EnemyAffixCatalog(null);
        }

        public void PrepareServerSpawn(ulong entityId, EnemyAffixSpawnSnapshot affixes)
        {
            m_PreparedEntityId = entityId;
            m_PreparedAffixes = affixes ?? EnemyAffixSpawnSnapshot.Empty;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_ReplicatedState.OnValueChanged += HandleReplicatedStateChanged;
            m_ReplicatedAffixes.OnListChanged += HandleAffixListChanged;

            if (IsServer)
            {
                InitializeServerAggregate();
            }
            navigationAgent?.SetServerActive(IsServer);

            for (int i = 0; i < m_ReplicatedAffixes.Count; i++)
                InstallOrUpdateAffixEffect(m_ReplicatedAffixes[i]);

            ApplyReplicatedState(m_ReplicatedState.Value);
        }

        public override void OnNetworkDespawn()
        {
            m_ReplicatedState.OnValueChanged -= HandleReplicatedStateChanged;
            m_ReplicatedAffixes.OnListChanged -= HandleAffixListChanged;
            effectHost?.RemoveSourceKind(EffectSourceKind.EnemyAffix);
            m_TargetPlayer = null;
            m_Aggregate = null;
            m_PreparedAffixes = EnemyAffixSpawnSnapshot.Empty;
            m_KnockbackVelocity = Vector3.zero;
            m_VerticalVelocity = 0f;
            m_MovementIntent = EnemyMovementIntent.None;
            m_AttackAimDirection = Vector3.zero;
            navigationAgent?.SetServerActive(false);
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || m_Aggregate == null) return;

            if (m_Aggregate.IsDead)
            {
                navigationAgent?.Stop(true);
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
            EnemyArchetypeDefinition definition = archetype.ToDefinition();
            var statsBuilder = new EnemyRuntimeStatsBuilder(definition);
            var appliedAffixes = new System.Collections.Generic.List<EnemyAffixStackNetworkState>();
            for (int i = 0; i < m_PreparedAffixes.Entries.Length; i++)
            {
                EnemyAffixSpawnEntry entry = m_PreparedAffixes.Entries[i];
                if (entry.Definition == null || entry.Stacks <= 0 ||
                    !entry.Definition.AppliesTo(definition.StableId)) continue;
                for (int modifierIndex = 0;
                     modifierIndex < entry.Definition.StatModifiers.Length;
                     modifierIndex++)
                {
                    statsBuilder.Add(entry.Definition.StatModifiers[modifierIndex], entry.Stacks);
                }
                appliedAffixes.Add(new EnemyAffixStackNetworkState
                {
                    AffixId = entry.Definition.AffixId,
                    Stacks = entry.Stacks
                });
            }
            m_Aggregate = new EnemyAggregate(
                new GameplayEntityId(entityId),
                definition,
                statsBuilder.Build(),
                serverTime);
            m_ReplicatedAffixes.Clear();
            for (int i = 0; i < appliedAffixes.Count; i++)
                m_ReplicatedAffixes.Add(appliedAffixes[i]);
            m_TargetPlayer = null;
            m_TrustedHitSequence = 0;
            m_KnockbackVelocity = Vector3.zero;
            m_VerticalVelocity = 0f;
            m_NextBrainTime = Time.unscaledTime;
            m_NextTargetRefreshTime = Time.unscaledTime;
            m_RewardGranted = false;
            m_DeathDespawnTime = float.PositiveInfinity;
            m_WasMovementBlocked = false;
            m_BlockStartedTime = 0d;
            m_MovementIntent = EnemyMovementIntent.None;
            m_AttackAimDirection = Vector3.zero;
            EnsureAttackExecutor(definition);
            navigationAgent?.ResetRuntime();
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
                m_MovementIntent = EnemyMovementIntent.None;
                navigationAgent?.Stop();
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
            EnemyState stateBeforeTick = m_Aggregate.State;
            bool hasTarget = IsTargetValid(m_TargetPlayer);
            float distance = hasTarget
                ? Vector3.Distance(transform.position, m_TargetPlayer.transform.position)
                : float.MaxValue;
            double serverTime = NetworkManager.ServerTime.Time;
            bool hasLineOfSight = hasTarget && HasLineOfSight(m_TargetPlayer);

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
                new EnemyTickInput(hasTarget, distance, serverTime, hasLineOfSight));

            m_MovementIntent = tick.MovementIntent;
            if (stateBeforeTick != m_Aggregate.State && m_Aggregate.State == EnemyState.Telegraphing)
                CaptureAttackAimDirection();

            if (tick.ShouldCommitAttack && m_Aggregate.TryCommitAttack())
            {
                CommitAttack();
            }

            // SetTarget/ClearTarget can transition before Aggregate.Tick captures
            // its local previous state, so Revision is the authoritative change
            // detector for everything performed in this brain step.
            if (m_Aggregate.Revision != revisionBeforeTick) PublishState();
        }

        private void MoveAndFaceTarget()
        {
            bool canMove = characterController != null && characterController.enabled;
            Vector3 movement = Vector3.zero;
            if (m_KnockbackVelocity.sqrMagnitude > 0.0001f)
            {
                movement += m_KnockbackVelocity;
                m_KnockbackVelocity = Vector3.MoveTowards(
                    m_KnockbackVelocity,
                    Vector3.zero,
                    KnockbackDecay * Time.deltaTime);
            }

            Vector3 facingDirection = Vector3.zero;
            if (IsTargetValid(m_TargetPlayer))
            {
                Vector3 directDirection = m_TargetPlayer.transform.position - transform.position;
                directDirection.y = 0f;
                if (directDirection.sqrMagnitude > 0.0001f)
                    directDirection.Normalize();

                if (m_MovementIntent != EnemyMovementIntent.None)
                {
                    Vector3 destination = ResolveMovementDestination(m_MovementIntent, directDirection);
                    Vector3 navigationVelocity;
                    if (navigationAgent != null)
                    {
                        float stoppingDistance = ResolveStoppingDistance(m_MovementIntent);
                        if (navigationAgent.TryGetDesiredVelocity(
                                destination,
                                m_Aggregate.RuntimeStats.MoveSpeed,
                                stoppingDistance,
                                out navigationVelocity))
                        {
                            movement += navigationVelocity;
                            facingDirection = navigationVelocity.sqrMagnitude > 0.0001f
                                ? navigationVelocity.normalized
                                : directDirection;
                        }
                    }
                    else
                    {
                        Vector3 fallbackDirection = destination - transform.position;
                        fallbackDirection.y = 0f;
                        if (fallbackDirection.sqrMagnitude > 0.0001f)
                            fallbackDirection.Normalize();
                        movement += fallbackDirection * m_Aggregate.RuntimeStats.MoveSpeed;
                        facingDirection = fallbackDirection.sqrMagnitude > 0.0001f
                            ? fallbackDirection
                            : directDirection;
                    }
                }
                else
                {
                    navigationAgent?.Stop();
                    if (m_Aggregate.State == EnemyState.Telegraphing ||
                        m_Aggregate.State == EnemyState.Attacking)
                    {
                        facingDirection = m_Aggregate.Definition.CombatStyle == EnemyCombatStyle.RangedOrbit &&
                                          m_AttackAimDirection.sqrMagnitude > 0.0001f
                            ? m_AttackAimDirection
                            : directDirection;
                    }
                }
            }
            else
            {
                navigationAgent?.Stop(true);
            }

            if (canMove)
            {
                if (characterController.isGrounded && m_VerticalVelocity < 0f)
                    m_VerticalVelocity = -2f;
                else
                    m_VerticalVelocity += Physics.gravity.y * Time.deltaTime;
                movement.y += m_VerticalVelocity;
                characterController.Move(movement * Time.deltaTime);
                navigationAgent?.SyncToTransform();
            }

            if (facingDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 540f * Time.deltaTime);
            }
        }

        private Vector3 ResolveMovementDestination(
            EnemyMovementIntent intent,
            Vector3 directDirection)
        {
            if (intent == EnemyMovementIntent.Approach ||
                m_Aggregate.Definition.CombatStyle != EnemyCombatStyle.RangedOrbit)
                return m_TargetPlayer.transform.position;

            Vector3 targetPosition = m_TargetPlayer.transform.position;
            Vector3 outward = transform.position - targetPosition;
            outward.y = 0f;
            if (outward.sqrMagnitude <= 0.0001f) outward = -directDirection;
            if (outward.sqrMagnitude <= 0.0001f) outward = transform.right;
            outward.Normalize();

            float orbitSign = (m_Aggregate.Id.Value & 1UL) == 0UL ? 1f : -1f;
            Vector3 tangent = Vector3.Cross(Vector3.up, outward) * orbitSign;
            float preferredDistance = (m_Aggregate.Definition.PreferredRangeMin +
                                       m_Aggregate.Definition.PreferredRangeMax) * 0.5f;
            if (intent == EnemyMovementIntent.Retreat)
            {
                return targetPosition + outward * m_Aggregate.Definition.PreferredRangeMax + tangent * 0.75f;
            }

            return targetPosition + outward * preferredDistance + tangent * 3f;
        }

        private float ResolveStoppingDistance(EnemyMovementIntent intent)
        {
            if (m_Aggregate.Definition.CombatStyle == EnemyCombatStyle.RangedOrbit)
            {
                return intent == EnemyMovementIntent.Approach
                    ? Mathf.Max(0.1f, m_Aggregate.Definition.PreferredRangeMax * 0.9f)
                    : 0.2f;
            }
            return Mathf.Max(0.1f, m_Aggregate.RuntimeStats.AttackRange * 0.85f);
        }

        private void RefreshTarget()
        {
            if (IsTargetValid(m_TargetPlayer) && m_Aggregate != null &&
                (m_Aggregate.State == EnemyState.Telegraphing ||
                 m_Aggregate.State == EnemyState.Attacking))
                return;

            NetworkObject bestTarget = null;
            float bestSqrDistance = float.MaxValue;
            float maxSqrDistance =
                m_Aggregate.RuntimeStats.DetectionRange * m_Aggregate.RuntimeStats.DetectionRange;

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

        private void CommitAttack()
        {
            if (!IsTargetValid(m_TargetPlayer)) return;
            if (m_AttackExecutor == null)
            {
                ReportMissingAttackExecutor();
                return;
            }

            Vector3 direction;
            if (m_Aggregate.Definition.CombatStyle == EnemyCombatStyle.RangedOrbit &&
                m_AttackAimDirection.sqrMagnitude > 0.0001f)
            {
                direction = m_AttackAimDirection;
            }
            else
            {
                direction = m_TargetPlayer.transform.position - transform.position;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
            }

            Vector3 origin = attackOrigin != null ? attackOrigin.position : transform.position + Vector3.up;
            var context = new EnemyAttackExecutionContext(
                m_Aggregate.Id,
                m_Aggregate.Definition.AttackId,
                m_Aggregate.AttackSequence,
                m_Aggregate.RuntimeStats.AttackDamage,
                m_Aggregate.RuntimeStats.AttackKnockback,
                m_Aggregate.RuntimeStats.AttackBreakRange,
                origin,
                direction,
                m_TargetPlayer);
            m_AttackExecutor.TryExecute(context);
        }

        private void CaptureAttackAimDirection()
        {
            if (!IsTargetValid(m_TargetPlayer))
            {
                m_AttackAimDirection = transform.forward;
                return;
            }
            Vector3 direction = m_TargetPlayer.transform.position + Vector3.up -
                                (attackOrigin != null ? attackOrigin.position : transform.position + Vector3.up);
            direction.y = 0f;
            m_AttackAimDirection = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : transform.forward;
        }

        private bool HasLineOfSight(NetworkObject target)
        {
            if (!IsTargetValid(target) || attackBlockingMask.value == 0) return IsTargetValid(target);
            Vector3 origin = attackOrigin != null ? attackOrigin.position : transform.position + Vector3.up;
            Vector3 destination = target.transform.position + Vector3.up;
            return !Physics.Linecast(
                origin,
                destination,
                attackBlockingMask,
                QueryTriggerInteraction.Ignore);
        }

        private void ResolveSerializedAttackExecutor()
        {
            m_AttackExecutor = attackExecutorBehaviour as IEnemyAttackExecutor;
            if (m_AttackExecutor != null) return;
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is IEnemyAttackExecutor executor)) continue;
                m_AttackExecutor = executor;
                attackExecutorBehaviour = behaviours[i];
                return;
            }
        }

        private void EnsureAttackExecutor(EnemyArchetypeDefinition definition)
        {
            ResolveSerializedAttackExecutor();
            if (m_AttackExecutor != null || definition == null) return;
            if (definition.CombatStyle == EnemyCombatStyle.MeleeChase)
            {
                EnemyMeleeAttackExecutor fallback = GetComponent<EnemyMeleeAttackExecutor>();
                if (fallback == null) fallback = gameObject.AddComponent<EnemyMeleeAttackExecutor>();
                attackExecutorBehaviour = fallback;
                m_AttackExecutor = fallback;
                return;
            }
            ReportMissingAttackExecutor();
        }

        private void ReportMissingAttackExecutor()
        {
            if (m_MissingAttackExecutorReported) return;
            m_MissingAttackExecutorReported = true;
            Debug.LogError("[EnemyNetworkActor] No IEnemyAttackExecutor is configured for this archetype.", this);
        }

        private void GrantDeathReward(ulong attackerClientId)
        {
            if (m_RewardGranted || !NetworkManager.ConnectedClients.TryGetValue(attackerClientId, out var client)) return;
            if (client.PlayerObject == null) return;

            var behaviours = client.PlayerObject.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is IScarletRewardReceiver receiver)) continue;
                if (receiver.TryGrantScarlet(m_Aggregate.RuntimeStats.ScarletReward, m_Aggregate.Id))
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
            m_ReplicatedState.Value = EnemyNetworkState.FromSnapshot(
                m_Aggregate.CaptureSnapshot(),
                m_AttackAimDirection);
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

        private void HandleAffixListChanged(NetworkListEvent<EnemyAffixStackNetworkState> change)
        {
            switch (change.Type)
            {
                case NetworkListEvent<EnemyAffixStackNetworkState>.EventType.Add:
                case NetworkListEvent<EnemyAffixStackNetworkState>.EventType.Insert:
                case NetworkListEvent<EnemyAffixStackNetworkState>.EventType.Value:
                    InstallOrUpdateAffixEffect(change.Value);
                    break;
                case NetworkListEvent<EnemyAffixStackNetworkState>.EventType.Remove:
                case NetworkListEvent<EnemyAffixStackNetworkState>.EventType.RemoveAt:
                    RemoveAffixEffect(change.Value.AffixId);
                    break;
                case NetworkListEvent<EnemyAffixStackNetworkState>.EventType.Full:
                    effectHost?.RemoveSourceKind(EffectSourceKind.EnemyAffix);
                    for (int i = 0; i < m_ReplicatedAffixes.Count; i++)
                        InstallOrUpdateAffixEffect(m_ReplicatedAffixes[i]);
                    break;
            }
        }

        private void InstallOrUpdateAffixEffect(in EnemyAffixStackNetworkState affix)
        {
            if (effectHost == null || affix.AffixId == 0 || affix.Stacks <= 0 ||
                m_DomainAffixCatalog == null ||
                !m_DomainAffixCatalog.TryGet(affix.AffixId, out EnemyAffixDefinition definition)) return;
            GameplayEntityId entity = CombatEntityId;
            var key = new EffectSourceKey(EffectSourceKind.EnemyAffix, affix.AffixId);
            var state = new EffectRuntimeState(
                key,
                GameplayEntityId.None,
                entity,
                affix.Stacks,
                1f,
                0d,
                double.PositiveInfinity);
            effectHost.SetSource(definition.RuntimeEffects, state, statusHost);
        }

        private void RemoveAffixEffect(uint affixId)
        {
            if (affixId == 0) return;
            var key = new EffectSourceKey(EffectSourceKind.EnemyAffix, affixId);
            effectHost?.RemoveSource(key);
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
