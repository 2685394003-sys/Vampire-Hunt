using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Combat;
using VampireHunt.Contracts;
using VampireHunt.Effects;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    public struct StatusEffectNetworkState : INetworkSerializable, IEquatable<StatusEffectNetworkState>
    {
        public uint StatusId;
        public ulong SourceEntityId;
        public float Stacks;
        public float Magnitude;
        public double StartTime;
        public double EndTime;
        public byte BlockFlags;
        public uint PresentationCueId;
        public byte Element;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref StatusId);
            serializer.SerializeValue(ref SourceEntityId);
            serializer.SerializeValue(ref Stacks);
            serializer.SerializeValue(ref Magnitude);
            serializer.SerializeValue(ref StartTime);
            serializer.SerializeValue(ref EndTime);
            serializer.SerializeValue(ref BlockFlags);
            serializer.SerializeValue(ref PresentationCueId);
            serializer.SerializeValue(ref Element);
        }

        public bool Equals(StatusEffectNetworkState other) =>
            StatusId == other.StatusId && SourceEntityId == other.SourceEntityId && Stacks.Equals(other.Stacks) &&
            Magnitude.Equals(other.Magnitude) && StartTime.Equals(other.StartTime) && EndTime.Equals(other.EndTime) &&
            BlockFlags == other.BlockFlags && PresentationCueId == other.PresentationCueId && Element == other.Element;
    }

    /// <summary>Server-owned status lifecycle backed exclusively by modular gameplay effects.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(GameplayEffectHost))]
    public sealed class CombatStatusHost : NetworkBehaviour, IStatusEffectTarget, IEffectCommandSink, IStatusEffectExecutor
    {
        [SerializeField] private StatusEffectCatalogAsset catalog;
        [SerializeField] private GameplayEffectHost effectHost;
        [SerializeField] private StatusEffectPresentationEvent onStatusAdded;
        [SerializeField] private StatusEffectPresentationEvent onStatusRemoved;
        [Tooltip("传导减免：目标对元素状态传导的抗性。挂层数 = 基础 × 属性精通 × 此值。普通怪/玩家 = 1（不减免），Boss/手 = 0.05。")]
        [SerializeField, Min(0f)] private float elementResist = 1f;

        private readonly StatusEffectCollection m_Statuses = new StatusEffectCollection();
        private readonly List<StatusEffectSnapshot> m_Snapshots = new List<StatusEffectSnapshot>();
        private readonly List<StatusEffectSnapshot> m_Expired = new List<StatusEffectSnapshot>();
        private readonly List<EffectCommand> m_Commands = new List<EffectCommand>();
        private readonly Collider[] m_ChainHitBuffer = new Collider[128];
        private readonly List<EnemyNetworkActor> m_ChainTargets = new List<EnemyNetworkActor>(32);
        private readonly HashSet<ulong> m_ChainTargetIds = new HashSet<ulong>();
        private readonly List<StatusEffectSpec> m_ChainConduction = new List<StatusEffectSpec>(4);
        private readonly NetworkList<StatusEffectNetworkState> m_ReplicatedStatuses =
            new NetworkList<StatusEffectNetworkState>();

        private IDamageReceiver m_DamageReceiver;
        private ICombatEntityIdentity m_Identity;
        private ILightningChainPresentationSink m_LightningCueSink;
        private ElementReactionResolver m_Reactions;
        private uint m_LastPublishedRevision;
        private ulong m_PeriodicSequence;
        private bool m_ProcessingCommands;
        private double m_LastDetonateTime;
        private double m_LastShatterTime;

        public bool IsActionBlocked => HasBlock(EffectBlockFlags.Action);
        public bool IsMovementBlocked => HasBlock(EffectBlockFlags.Movement);

        private void Awake()
        {
            if (effectHost == null) effectHost = GetComponent<GameplayEffectHost>();
            var behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (m_DamageReceiver == null && behaviours[i] is IDamageReceiver receiver) m_DamageReceiver = receiver;
                if (m_Identity == null && behaviours[i] is ICombatEntityIdentity identity) m_Identity = identity;
                if (m_LightningCueSink == null && behaviours[i] is ILightningChainPresentationSink lightningSink)
                    m_LightningCueSink = lightningSink;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_Reactions = catalog != null ? catalog.CreateReactionResolver() : new ElementReactionResolver(null);
            m_ReplicatedStatuses.OnListChanged += HandleReplicatedStatusChanged;
            for (int i = 0; i < m_ReplicatedStatuses.Count; i++)
            {
                InstallReplicatedRuntime(m_ReplicatedStatuses[i]);
                RaiseStatusEvent(onStatusAdded, m_ReplicatedStatuses[i]);
            }
            if (IsServer) PublishIfDirty(force: true);
        }

        public override void OnNetworkDespawn()
        {
            for (int i = 0; i < m_ReplicatedStatuses.Count; i++)
                RaiseStatusEvent(onStatusRemoved, m_ReplicatedStatuses[i]);
            m_ReplicatedStatuses.OnListChanged -= HandleReplicatedStatusChanged;
            effectHost?.RemoveSourceKind(EffectSourceKind.Status);
            if (IsServer) m_Statuses.Clear();
            m_Commands.Clear();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;
            // 单人模式菜单暂停时冻结状态 DoT 结算（ServerTime 是墙钟，不受 Time.timeScale 影响）。
            if (MenuPauseController.IsPaused) return;
            ProcessCommands();
            m_Statuses.RemoveExpired(NetworkManager.ServerTime.Time, m_Expired);
            for (int i = 0; i < m_Expired.Count; i++) RemoveRuntime(m_Expired[i].StatusId);
            PublishIfDirty();
        }

        public bool TryApplyStatus(in StatusApplicationRequest request)
        {
            if (!IsSpawned || !IsServer || m_Identity == null || request.Spec.StatusId == 0 ||
                catalog == null || !catalog.TryGet(request.Spec.StatusId, out _)) return false;
            ServerCombatActivity.Interaction(NetworkManager, request.Source, m_Identity.CombatEntityId,
                request.Spec.StatusId, ++m_PeriodicSequence, CombatActivityKind.Periodic);
            // 传导减免：外部传导（武器命中、闪电连锁复制）来的状态，挂层数 × 目标抗性（普通怪 1，Boss/手 0.05）。
            // 内部触发状态（燃爆/冻结等经 ProcessCommands 直接走 ApplyStatusInternal）不在此减免。
            if (elementResist <= 0f) return false;
            StatusApplicationRequest effective = request;
            if (elementResist < 1f)
            {
                effective = new StatusApplicationRequest(
                    request.Source, request.Target,
                    new StatusEffectSpec(
                        request.Spec.StatusId, request.Spec.Stacks * elementResist,
                        request.Spec.Duration, request.Spec.Magnitude,
                        request.Spec.Element, request.Spec.ElementMastery));
            }
            if (!ApplyStatusInternal(effective)) return false;
            ProcessCommands();
            PublishIfDirty();
            return true;
        }

        /// <summary>
        /// 解析元素反应（v2.2）：炸裂=冰打火→AOE+击退；碎裂=火打冰→百分比。命中后按定义消耗 1 火 + 1 冰层数，
        /// 并受内置冷却约束（默认 0.5s）。返回结果由命中链路执行具体效果。
        /// </summary>
        public ElementReactionResult ResolveElementReaction(ElementId incomingElement)
        {
            if (!IsServer || m_Reactions == null) return ElementReactionResult.None();
            ElementReactionResult result = m_Reactions.Resolve(incomingElement, m_Statuses.Has);
            if (!result.HasReaction) return ElementReactionResult.None();

            double now = NetworkManager.ServerTime.Time;
            double last = result.Type == ElementReactionType.Shatter ? m_LastShatterTime : m_LastDetonateTime;
            if (now - last < result.Cooldown) return ElementReactionResult.None();

            if (result.Type == ElementReactionType.Shatter) m_LastShatterTime = now;
            else m_LastDetonateTime = now;

            ConsumeReactionStatus(result.ConsumeFireStatusId, result.ConsumeFireStacks);
            ConsumeReactionStatus(result.ConsumeFrostStatusId, result.ConsumeFrostStacks);
            PublishIfDirty();
            return result;
        }

        private void ConsumeReactionStatus(uint statusId, int stacks)
        {
            if (statusId == 0 || stacks <= 0 || !m_Statuses.TryConsumeStacks(statusId, stacks)) return;
            if (!m_Statuses.Has(statusId)) RemoveRuntime(statusId);
        }

        public bool HasStatus(uint statusId)
        {
            if (IsServer) return m_Statuses.Has(statusId);
            for (int i = 0; i < m_ReplicatedStatuses.Count; i++)
                if (m_ReplicatedStatuses[i].StatusId == statusId) return true;
            return false;
        }

        public bool RemoveStatus(uint statusId)
        {
            if (!IsServer || !RemoveStatusInternal(statusId)) return false;
            PublishIfDirty();
            return true;
        }

        public void Enqueue(in EffectCommand command) => m_Commands.Add(command);

        private bool ApplyStatusInternal(in StatusApplicationRequest request)
        {
            if (catalog == null || !catalog.TryGet(request.Spec.StatusId, out StatusEffectDefinition definition))
                return false;
            StatusEffectMutation mutation = m_Statuses.Apply(
                definition, request, NetworkManager.ServerTime.Time);
            if (!mutation.Applied) return false;
            InstallRuntime(definition, mutation.Current);
            return true;
        }

        private bool RemoveStatusInternal(uint statusId)
        {
            if (!m_Statuses.TryRemove(statusId, out _)) return false;
            RemoveRuntime(statusId);
            return true;
        }

        private void InstallRuntime(StatusEffectDefinition definition, in StatusEffectSnapshot status)
        {
            if (effectHost == null) return;
            var key = new EffectSourceKey(EffectSourceKind.Status, status.StatusId);
            GameplayEntityId target = m_Identity?.CombatEntityId ?? GameplayEntityId.None;
            var state = new EffectRuntimeState(
                key, status.Source, target, status.Stacks, status.Magnitude, status.StartTime, status.EndTime,
                status.ElementMastery);
            effectHost.SetSource(definition.RuntimeEffects, state, this);
        }

        private void InstallReplicatedRuntime(in StatusEffectNetworkState status)
        {
            if (effectHost == null || catalog == null ||
                !catalog.TryGet(status.StatusId, out StatusEffectDefinition definition)) return;
            var key = new EffectSourceKey(EffectSourceKind.Status, status.StatusId);
            GameplayEntityId target = m_Identity?.CombatEntityId ?? GameplayEntityId.None;
            var state = new EffectRuntimeState(
                key, new GameplayEntityId(status.SourceEntityId), target, status.Stacks,
                status.Magnitude, status.StartTime, status.EndTime);
            effectHost.SetSource(definition.RuntimeEffects, state, IsServer ? this : null);
        }

        private void RemoveRuntime(uint statusId)
        {
            var key = new EffectSourceKey(EffectSourceKind.Status, statusId);
            effectHost?.RemoveSource(key);
        }

        private void ProcessCommands()
        {
            if (m_ProcessingCommands || m_Commands.Count == 0) return;
            m_ProcessingCommands = true;
            int processed = 0;
            try
            {
                while (m_Commands.Count > 0 && processed++ < 128)
                {
                    EffectCommand command = m_Commands[0];
                    m_Commands.RemoveAt(0);
                    switch (command.Kind)
                    {
                        case EffectCommandKind.PeriodicDamage:
                            ApplyPeriodicDamage(command);
                            break;
                        case EffectCommandKind.ApplyStatus:
                            var spec = new StatusEffectSpec(
                                command.StatusId, command.StatusStacks, 0f, 0f, ElementId.None);
                            var application = new StatusApplicationRequest(
                                command.State.Source, command.State.Target, spec);
                            ApplyStatusInternal(application);
                            break;
                        case EffectCommandKind.RemoveSource:
                            if (command.State.Key.Kind == EffectSourceKind.Status)
                                RemoveStatusInternal(command.State.Key.DefinitionId);
                            break;
                    }
                }
            }
            finally
            {
                m_ProcessingCommands = false;
            }
        }

        private void ApplyPeriodicDamage(in EffectCommand command)
        {
            if (m_DamageReceiver == null || m_Identity == null || command.Amount <= 0f) return;
            var request = new DamageRequest(
                command.State.Source,
                m_Identity.CombatEntityId,
                command.State.Key.DefinitionId,
                ++m_PeriodicSequence,
                command.Amount,
                command.DamageTags | DamageTags.Status | DamageTags.Periodic);
            m_DamageReceiver.TryApplyDamage(request, out _);
        }

        public void ExecuteDetonateBurn(in EffectRuntimeState state, float multiplier)
        {
            if (!IsServer || m_DamageReceiver == null || m_Identity == null) return;
            m_Statuses.Capture(m_Snapshots);
            StatusEffectSnapshot burn = default;
            bool hasBurn = false;
            for (int i = 0; i < m_Snapshots.Count; i++)
            {
                if (m_Snapshots[i].StatusId != StatusEffectIds.Burn) continue;
                burn = m_Snapshots[i];
                hasBurn = true;
                break;
            }
            if (hasBurn && TryGetBurnTickParameters(out double interval, out float damagePerStack))
            {
                double now = NetworkManager.ServerTime.Time;
                double remaining = burn.EndTime - now;
                if (remaining > 0d)
                {
                    int remainingTicks = Math.Max(1, (int)Math.Ceiling(remaining / interval));
                    float amount = damagePerStack * burn.Stacks * burn.Magnitude * remainingTicks * multiplier;
                    if (amount > 0f)
                    {
                        var request = new DamageRequest(
                            state.Source, m_Identity.CombatEntityId, state.Key.DefinitionId,
                            ++m_PeriodicSequence, amount,
                            DamageTags.Status | DamageTags.Periodic);
                        m_DamageReceiver.TryApplyDamage(request, out _);
                    }
                }
                RemoveStatusInternal(StatusEffectIds.Burn);
            }
            if (state.Key.Kind == EffectSourceKind.Status && state.Key.DefinitionId != 0)
                RemoveStatusInternal(state.Key.DefinitionId);
            PublishIfDirty();
        }

        private bool TryGetBurnTickParameters(out double interval, out float damagePerStack)
        {
            interval = 1d;
            damagePerStack = 1f;
            if (catalog == null || !catalog.TryGet(StatusEffectIds.Burn, out StatusEffectDefinition definition)) return false;
            IEffectModuleDescriptor[] modules = definition.RuntimeEffects.Modules;
            for (int i = 0; i < modules.Length; i++)
            {
                if (modules[i] is PeriodicDamageEffectDescriptor periodic)
                {
                    interval = periodic.Interval;
                    damagePerStack = periodic.DamagePerStack;
                    return true;
                }
            }
            return false;
        }

        public void ExecuteChainLightning(
            in EffectRuntimeState state, int jumps, float radius, float decay, float chainDamage, float elementMastery)
        {
            if (!IsServer || m_Identity == null || jumps <= 0 || radius <= 0f) return;
            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position, radius, m_ChainHitBuffer, ~0, QueryTriggerInteraction.Collide);
            m_ChainTargets.Clear();
            m_ChainTargetIds.Clear();
            EnemyNetworkActor self = GetComponent<EnemyNetworkActor>();
            for (int i = 0; i < hitCount; i++)
            {
                Collider candidate = m_ChainHitBuffer[i];
                if (candidate == null) continue;
                EnemyNetworkActor actor = candidate.GetComponentInParent<EnemyNetworkActor>();
                if (actor == null || actor == self) continue;
                ulong targetId = actor.CombatEntityId.Value;
                if (targetId == 0 || !m_ChainTargetIds.Add(targetId)) continue;
                m_ChainTargets.Add(actor);
            }
            if (m_ChainTargets.Count == 0) return;
            SortChainTargetsByDistance(transform.position);

            // 捕获源怪（自身）的可传导状态，按「源层数 × 可传导性」生成复制规格（源怪状态保留）。
            m_ChainConduction.Clear();
            m_Statuses.Capture(m_Snapshots);
            for (int i = 0; i < m_Snapshots.Count; i++)
            {
                StatusEffectSnapshot snapshot = m_Snapshots[i];
                if (!IsConductionStatus(snapshot.StatusId)) continue;
                float transferStacks = snapshot.Stacks * elementMastery;
                if (transferStacks <= 0f) continue;
                m_ChainConduction.Add(new StatusEffectSpec(
                    snapshot.StatusId, transferStacks, 0f, 0f, ElementId.None));
            }

            int count = Math.Min(jumps, m_ChainTargets.Count);
            float amount = chainDamage * elementMastery;
            float visualIntensity = 1f;
            for (int i = 0; i < count; i++)
            {
                EnemyNetworkActor target = m_ChainTargets[i];

                // 广播闪电传导表现（源怪 → 被链目标），让玩家看见传导链路；强度随逐跳衰减。
                LightningChainVisualClientRpc(transform.position, target.transform.position, visualIntensity);

                if (amount > 0f)
                {
                    var request = new DamageRequest(
                        state.Source, target.CombatEntityId, state.Key.DefinitionId,
                        ++m_PeriodicSequence, amount,
                        DamageTags.Status | DamageTags.Periodic);
                    target.TryApplyDamage(request, out _);
                }
                amount *= decay;
                visualIntensity *= decay;
                if (m_ChainConduction.Count > 0 && target.TryGetComponent(out CombatStatusHost targetStatus))
                {
                    for (int k = 0; k < m_ChainConduction.Count; k++)
                    {
                        targetStatus.TryApplyStatus(new StatusApplicationRequest(
                            state.Source, target.CombatEntityId, m_ChainConduction[k]));
                    }
                }
            }
        }

        private void SortChainTargetsByDistance(Vector3 origin)
        {
            for (int i = 1; i < m_ChainTargets.Count; i++)
            {
                EnemyNetworkActor current = m_ChainTargets[i];
                float currentDistance = (current.transform.position - origin).sqrMagnitude;
                int j = i - 1;
                while (j >= 0 &&
                       (m_ChainTargets[j].transform.position - origin).sqrMagnitude > currentDistance)
                {
                    m_ChainTargets[j + 1] = m_ChainTargets[j];
                    j--;
                }
                m_ChainTargets[j + 1] = current;
            }
        }

        /// <summary>客户端表现：在源怪与目标怪之间画闪电传导折线（服务器广播，所有客户端可见）。</summary>
        [ClientRpc]
        private void LightningChainVisualClientRpc(Vector3 from, Vector3 to, float intensity)
        {
            var cue = new LightningChainPresentationCue(
                new Float3(from.x, from.y, from.z),
                new Float3(to.x, to.y, to.z),
                intensity);
            m_LightningCueSink?.PlayLightningChain(cue);
        }

        /// <summary>可被闪电传导的状态集合：灼烧/霜寒/碎裂/炸裂（层数型元素状态）。</summary>
        private static bool IsConductionStatus(uint statusId) =>
            statusId == StatusEffectIds.Burn || statusId == StatusEffectIds.Frost ||
            statusId == StatusEffectIds.Shatter || statusId == StatusEffectIds.Detonate;

        public void ExecuteThunderStrike(in EffectRuntimeState state, float damage)
        {
            if (!IsServer || m_DamageReceiver == null || m_Identity == null || damage <= 0f) return;
            var request = new DamageRequest(
                state.Source, m_Identity.CombatEntityId, state.Key.DefinitionId,
                ++m_PeriodicSequence, damage,
                DamageTags.Status | DamageTags.Periodic);
            m_DamageReceiver.TryApplyDamage(request, out _);
            if (state.Key.Kind == EffectSourceKind.Status && state.Key.DefinitionId != 0)
                RemoveStatusInternal(state.Key.DefinitionId);
            PublishIfDirty();
        }

        private bool HasBlock(EffectBlockFlags flag)
        {
            if (IsServer) return effectHost != null && effectHost.HasBlock(flag);
            for (int i = 0; i < m_ReplicatedStatuses.Count; i++)
                if ((((EffectBlockFlags)m_ReplicatedStatuses[i].BlockFlags) & flag) != 0) return true;
            return false;
        }

        private void PublishIfDirty(bool force = false)
        {
            if (!IsServer || !force && m_LastPublishedRevision == m_Statuses.Revision) return;
            m_Statuses.Capture(m_Snapshots);

            for (int i = m_ReplicatedStatuses.Count - 1; i >= 0; i--)
            {
                uint statusId = m_ReplicatedStatuses[i].StatusId;
                bool present = false;
                for (int j = 0; j < m_Snapshots.Count; j++)
                    if (m_Snapshots[j].StatusId == statusId) { present = true; break; }
                if (!present) m_ReplicatedStatuses.RemoveAt(i);
            }

            for (int i = 0; i < m_Snapshots.Count; i++)
            {
                StatusEffectSnapshot status = m_Snapshots[i];
                var networkState = new StatusEffectNetworkState
                {
                    StatusId = status.StatusId,
                    SourceEntityId = status.Source.Value,
                    Stacks = status.Stacks,
                    Magnitude = status.Magnitude,
                    StartTime = status.StartTime,
                    EndTime = status.EndTime,
                    BlockFlags = (byte)status.BlockFlags,
                    PresentationCueId = status.PresentationCueId,
                    Element = (byte)status.Element
                };

                int existing = FindReplicatedIndex(status.StatusId);
                if (existing < 0) m_ReplicatedStatuses.Add(networkState);
                else if (!m_ReplicatedStatuses[existing].Equals(networkState))
                    m_ReplicatedStatuses[existing] = networkState;
            }
            m_LastPublishedRevision = m_Statuses.Revision;
        }

        private int FindReplicatedIndex(uint statusId)
        {
            for (int i = 0; i < m_ReplicatedStatuses.Count; i++)
                if (m_ReplicatedStatuses[i].StatusId == statusId) return i;
            return -1;
        }

        private void HandleReplicatedStatusChanged(NetworkListEvent<StatusEffectNetworkState> change)
        {
            switch (change.Type)
            {
                case NetworkListEvent<StatusEffectNetworkState>.EventType.Add:
                case NetworkListEvent<StatusEffectNetworkState>.EventType.Insert:
                    InstallReplicatedRuntime(change.Value);
                    RaiseStatusEvent(onStatusAdded, change.Value);
                    break;
                case NetworkListEvent<StatusEffectNetworkState>.EventType.Value:
                    InstallReplicatedRuntime(change.Value);
                    if (change.PreviousValue.StatusId != change.Value.StatusId)
                        RaiseStatusEvent(onStatusRemoved, change.PreviousValue);
                    RaiseStatusEvent(onStatusAdded, change.Value);
                    break;
                case NetworkListEvent<StatusEffectNetworkState>.EventType.Remove:
                case NetworkListEvent<StatusEffectNetworkState>.EventType.RemoveAt:
                    RemoveRuntime(change.Value.StatusId);
                    RaiseStatusEvent(onStatusRemoved, change.Value);
                    break;
                case NetworkListEvent<StatusEffectNetworkState>.EventType.Full:
                    effectHost?.RemoveSourceKind(EffectSourceKind.Status);
                    for (int i = 0; i < m_ReplicatedStatuses.Count; i++)
                    {
                        InstallReplicatedRuntime(m_ReplicatedStatuses[i]);
                        RaiseStatusEvent(onStatusAdded, m_ReplicatedStatuses[i]);
                    }
                    break;
            }
        }

        private void RaiseStatusEvent(StatusEffectPresentationEvent channel, in StatusEffectNetworkState status)
        {
            if (channel == null || status.StatusId == 0 || m_Identity == null) return;
            channel.Raise(new StatusEffectPresentationPayload
            {
                sourceEntityId = status.SourceEntityId,
                targetEntityId = m_Identity.CombatEntityId.Value,
                statusId = status.StatusId,
                stacks = status.Stacks,
                magnitude = status.Magnitude,
                startServerTime = status.StartTime,
                endServerTime = status.EndTime,
                blockFlags = (EffectBlockFlags)status.BlockFlags,
                presentationCueId = status.PresentationCueId,
                element = (ElementId)status.Element,
                worldPosition = transform.position
            });
        }
    }
}
