using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Encounter;

namespace VampireHunt.Infrastructure.Netcode
{
    public struct BossEncounterNetworkState : INetworkSerializable, IEquatable<BossEncounterNetworkState>
    {
        private const byte HudVisibleFlag = 1 << 0;

        public byte StateValue;
        public byte StageNumber;
        public byte Flags;
        public float GuardHealth;
        public float MaxGuardHealth;
        public float Health;
        public float MaxHealth;
        public uint Revision;

        public BossEncounterState State => (BossEncounterState)StateValue;
        public bool HudVisible => (Flags & HudVisibleFlag) != 0;

        public static BossEncounterNetworkState FromDomain(in BossEncounterSnapshot snapshot)
        {
            return new BossEncounterNetworkState
            {
                StateValue = (byte)snapshot.State,
                StageNumber = (byte)Mathf.Clamp(snapshot.StageNumber, 1, 3),
                Flags = snapshot.HudVisible ? HudVisibleFlag : (byte)0,
                GuardHealth = snapshot.GuardHealth,
                MaxGuardHealth = snapshot.MaxGuardHealth,
                Health = snapshot.Health,
                MaxHealth = snapshot.MaxHealth,
                Revision = snapshot.Revision
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref StateValue);
            serializer.SerializeValue(ref StageNumber);
            serializer.SerializeValue(ref Flags);
            serializer.SerializeValue(ref GuardHealth);
            serializer.SerializeValue(ref MaxGuardHealth);
            serializer.SerializeValue(ref Health);
            serializer.SerializeValue(ref MaxHealth);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(BossEncounterNetworkState other) =>
            StateValue == other.StateValue && StageNumber == other.StageNumber && Flags == other.Flags &&
            GuardHealth.Equals(other.GuardHealth) && MaxGuardHealth.Equals(other.MaxGuardHealth) &&
            Health.Equals(other.Health) && MaxHealth.Equals(other.MaxHealth) && Revision == other.Revision;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossEncounterStateReplicator : NetworkBehaviour
    {
        private readonly NetworkVariable<BossEncounterNetworkState> m_State =
            new NetworkVariable<BossEncounterNetworkState>(default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public BossEncounterNetworkState Current => m_State.Value;
        public event Action<BossEncounterNetworkState> StateChanged;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_State.OnValueChanged += HandleChanged;
            StateChanged?.Invoke(m_State.Value);
        }

        public override void OnNetworkDespawn()
        {
            m_State.OnValueChanged -= HandleChanged;
            base.OnNetworkDespawn();
        }

        public bool PublishServer(in BossEncounterSnapshot snapshot, bool force = false)
        {
            if (!IsServer) return false;
            BossEncounterNetworkState next = BossEncounterNetworkState.FromDomain(snapshot);
            if (!force && m_State.Value.Equals(next)) return false;
            m_State.Value = next;
            return true;
        }

        private void HandleChanged(BossEncounterNetworkState previous, BossEncounterNetworkState current)
        {
            StateChanged?.Invoke(current);
        }
    }
}
