using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Abilities;

namespace VampireHunt.Infrastructure.Netcode
{
    public struct BossAbilityNetworkState : INetworkSerializable, IEquatable<BossAbilityNetworkState>
    {
        public int PhaseNumber;
        public uint AbilityId;
        public ulong CastSequence;
        public BossAbilityCastPhase CastPhase;
        public double CastStartServerTime;
        public double PhaseStartServerTime;
        public double CastEndServerTime;
        public ulong TargetEntityId;
        public Vector3 TargetPosition;
        public Vector3 Direction;
        public uint RandomSeed;
        public uint Revision;

        public bool IsCasting => AbilityId != 0 && CastPhase != BossAbilityCastPhase.None;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PhaseNumber);
            serializer.SerializeValue(ref AbilityId);
            serializer.SerializeValue(ref CastSequence);
            byte castPhase = (byte)CastPhase;
            serializer.SerializeValue(ref castPhase);
            if (serializer.IsReader)
            {
                CastPhase = (BossAbilityCastPhase)castPhase;
            }
            serializer.SerializeValue(ref CastStartServerTime);
            serializer.SerializeValue(ref PhaseStartServerTime);
            serializer.SerializeValue(ref CastEndServerTime);
            serializer.SerializeValue(ref TargetEntityId);
            serializer.SerializeValue(ref TargetPosition);
            serializer.SerializeValue(ref Direction);
            serializer.SerializeValue(ref RandomSeed);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(BossAbilityNetworkState other) =>
            PhaseNumber == other.PhaseNumber &&
            AbilityId == other.AbilityId &&
            CastSequence == other.CastSequence &&
            CastPhase == other.CastPhase &&
            CastStartServerTime.Equals(other.CastStartServerTime) &&
            PhaseStartServerTime.Equals(other.PhaseStartServerTime) &&
            CastEndServerTime.Equals(other.CastEndServerTime) &&
            TargetEntityId == other.TargetEntityId &&
            TargetPosition.Equals(other.TargetPosition) &&
            Direction.Equals(other.Direction) &&
            RandomSeed == other.RandomSeed &&
            Revision == other.Revision;

        public override bool Equals(object obj) => obj is BossAbilityNetworkState other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)AbilityId;
                hash = (hash * 397) ^ CastSequence.GetHashCode();
                hash = (hash * 397) ^ (int)CastPhase;
                hash = (hash * 397) ^ Revision.GetHashCode();
                return hash;
            }
        }

        public static BossAbilityNetworkState FromSnapshot(in BossAbilitySnapshot snapshot)
        {
            return new BossAbilityNetworkState
            {
                PhaseNumber = snapshot.PhaseNumber,
                AbilityId = snapshot.AbilityId,
                CastSequence = snapshot.CastSequence,
                CastPhase = snapshot.CastPhase,
                CastStartServerTime = snapshot.CastStartServerTime,
                PhaseStartServerTime = snapshot.PhaseStartServerTime,
                CastEndServerTime = snapshot.CastEndServerTime,
                TargetEntityId = snapshot.TargetEntityId,
                TargetPosition = ToVector3(snapshot.TargetPosition),
                Direction = ToVector3(snapshot.Direction),
                RandomSeed = snapshot.RandomSeed,
                Revision = snapshot.Revision
            };
        }

        private static Vector3 ToVector3(VampireHunt.Contracts.Float3 value) =>
            new Vector3(value.X, value.Y, value.Z);
    }

    /// <summary>
    /// Replicates only the compact Boss ability timeline. It never selects, starts or ticks
    /// abilities; the authoritative server driver publishes snapshots into this component.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossAbilityStateReplicator : NetworkBehaviour
    {
        private readonly NetworkVariable<BossAbilityNetworkState> m_State =
            new NetworkVariable<BossAbilityNetworkState>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private BossAbilityNetworkState m_OfflineState;
        private uint m_LastNotifiedRevision = uint.MaxValue;

        public event Action<BossAbilityNetworkState> StateChanged;

        public BossAbilityNetworkState CurrentState => IsSpawned ? m_State.Value : m_OfflineState;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_State.OnValueChanged += HandleStateChanged;
            m_OfflineState = default;
            m_LastNotifiedRevision = uint.MaxValue;
            Notify(m_State.Value);
        }

        public override void OnNetworkDespawn()
        {
            m_State.OnValueChanged -= HandleStateChanged;
            m_LastNotifiedRevision = uint.MaxValue;
            base.OnNetworkDespawn();
        }

        public bool PublishServer(in BossAbilitySnapshot snapshot, bool force = false)
        {
            if (!IsSpawned || !IsServer) return false;
            BossAbilityNetworkState next = BossAbilityNetworkState.FromSnapshot(snapshot);
            if (!force && next.Equals(m_State.Value)) return false;
            m_State.Value = next;
            Notify(next);
            return true;
        }

        public bool PublishOffline(in BossAbilitySnapshot snapshot, bool force = false)
        {
            if (IsSpawned) return false;
            BossAbilityNetworkState next = BossAbilityNetworkState.FromSnapshot(snapshot);
            if (!force && next.Equals(m_OfflineState)) return false;
            m_OfflineState = next;
            Notify(next);
            return true;
        }

        private void HandleStateChanged(BossAbilityNetworkState previous, BossAbilityNetworkState current)
        {
            Notify(current);
        }

        private void Notify(BossAbilityNetworkState state)
        {
            if (m_LastNotifiedRevision == state.Revision) return;
            m_LastNotifiedRevision = state.Revision;
            StateChanged?.Invoke(state);
        }
    }
}
