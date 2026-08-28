using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Combat;
using VampireHunt.Contracts;
using VampireHunt.Effects;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Systems;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    public struct StatusEffectNetworkState : INetworkSerializable, IEquatable<StatusEffectNetworkState>
    {
        public uint StatusId;
        public ulong SourceEntityId;
        public int Stacks;
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
            StatusId == other.StatusId && SourceEntityId == other.SourceEntityId && Stacks == other.Stacks &&
            Magnitude.Equals(other.Magnitude) && StartTime.Equals(other.StartTime) && EndTime.Equals(other.EndTime) &&
            BlockFlags == other.BlockFlags && PresentationCueId == other.PresentationCueId && Element == other.Element;
    }

    /// <summary>Server-owned status lifecycle backed exclusively by modular gameplay effects.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(GameplayEffectHost))]
    public sealed class CombatStatusHost : NetworkBehaviour, IStatusEffectTarget, IEffectCommandSink
    {
        [SerializeField] private StatusEffectCatalogAsset catalog;
        [SerializeField] private GameplayEffectHost effectHost;
        [SerializeField] private StatusEffectPresentationEvent onStatusAdded;
        [SerializeField] private StatusEffectPresentationEvent onStatusRemoved;

        private readonly StatusEffectCollection m_Statuses = new StatusEffectCollection();
        private readonly List<StatusEffectSnapshot> m_Snapshots = new List<StatusEffectSnapshot>();
        private readonly List<StatusEffectSnapshot> m_Expired = new List<StatusEffectSnapshot>();
        private readonly List<EffectCommand> m_Commands = new List<EffectCommand>();
        private readonly NetworkList<StatusEffectNetworkState> m_ReplicatedStatuses =
            new NetworkList<StatusEffectNetworkState>();

        private IDamageReceiver m_DamageReceiver;
        private ICombatEntityIdentity m_Identity;
        private ElementReactionResolver m_Reactions;
        private uint m_LastPublishedRevision;
        private ulong m_PeriodicSequence;
        private bool m_ProcessingCommands;

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
            if (!IsServer || !ApplyStatusInternal(request)) return false;
            ProcessCommands();
            PublishIfDirty();
            return true;
        }

        public float ResolveElementReaction(ElementId incomingElement)
        {
            if (!IsServer || m_Reactions == null) return 1f;
            ElementReactionResult result = m_Reactions.Resolve(incomingElement, m_Statuses.Has);
            if (result.ConsumedStatusId != 0) RemoveStatusInternal(result.ConsumedStatusId);
            PublishIfDirty();
            return result.DamageMultiplier;
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
                key, status.Source, target, status.Stacks, status.Magnitude, status.StartTime, status.EndTime);
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
