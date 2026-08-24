using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Integration
{
    public struct BossBodyNetworkState : INetworkSerializable, IEquatable<BossBodyNetworkState>
    {
        private const byte StaggeredFlag = 1 << 0;
        private const byte LeftHandFlag = 1 << 1;
        private const byte RightHandFlag = 1 << 2;

        public float NormalizedHealth;
        public byte Flags;

        public bool IsStaggered => (Flags & StaggeredFlag) != 0;
        public bool IsLeftHandFunctional => (Flags & LeftHandFlag) != 0;
        public bool IsRightHandFunctional => (Flags & RightHandFlag) != 0;

        public BossBodyNetworkState(
            float normalizedHealth,
            bool isStaggered,
            bool isLeftHandFunctional,
            bool isRightHandFunctional)
        {
            NormalizedHealth = Mathf.Clamp01(normalizedHealth);
            Flags = 0;
            SetFlag(ref Flags, StaggeredFlag, isStaggered);
            SetFlag(ref Flags, LeftHandFlag, isLeftHandFunctional);
            SetFlag(ref Flags, RightHandFlag, isRightHandFunctional);
        }

        public BossBodyNetworkState WithHealth(float normalizedHealth) =>
            new BossBodyNetworkState(
                normalizedHealth,
                IsStaggered,
                IsLeftHandFunctional,
                IsRightHandFunctional);

        public BossBodyNetworkState WithStaggered(bool value) =>
            new BossBodyNetworkState(
                NormalizedHealth,
                value,
                IsLeftHandFunctional,
                IsRightHandFunctional);

        public BossBodyNetworkState WithHands(bool leftFunctional, bool rightFunctional) =>
            new BossBodyNetworkState(NormalizedHealth, IsStaggered, leftFunctional, rightFunctional);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NormalizedHealth);
            serializer.SerializeValue(ref Flags);
        }

        public bool Equals(BossBodyNetworkState other) =>
            NormalizedHealth.Equals(other.NormalizedHealth) && Flags == other.Flags;

        private static void SetFlag(ref byte flags, byte flag, bool enabled)
        {
            if (enabled) flags |= flag;
            else flags &= (byte)~flag;
        }
    }

    /// <summary>
    /// Replicated read model for health/stagger/hand availability. A future Boss vitals
    /// component writes it on the server; skills receive only IBossBodyState.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossBodyStateHost : NetworkBehaviour, IBossBodyState, IBossBodyStateControl,
        ICombatEntityIdentity
    {
        private const ulong NetworkBossEntityPrefix = 1UL << 63;

        [Min(1)] [SerializeField] private ulong offlineEntityId = 9000001UL;
        [Range(0f, 1f)] [SerializeField] private float initialNormalizedHealth = 1f;
        [SerializeField] private bool initialLeftHandFunctional = true;
        [SerializeField] private bool initialRightHandFunctional = true;

        private readonly NetworkVariable<BossBodyNetworkState> m_State =
            new NetworkVariable<BossBodyNetworkState>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private BossBodyNetworkState m_OfflineState;

        public GameplayEntityId EntityId => CombatEntityId;
        public GameplayEntityId CombatEntityId => IsSpawned
            ? new GameplayEntityId(NetworkBossEntityPrefix | NetworkObjectId)
            : new GameplayEntityId(offlineEntityId);
        public float NormalizedHealth => Current.NormalizedHealth;
        public bool IsAlive => NormalizedHealth > 0f;
        public bool IsStaggered => Current.IsStaggered;
        public bool IsLeftHandFunctional => Current.IsLeftHandFunctional;
        public bool IsRightHandFunctional => Current.IsRightHandFunctional;

        private BossBodyNetworkState Current => IsSpawned ? m_State.Value : m_OfflineState;

        private void Awake()
        {
            m_OfflineState = CreateInitialState();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) m_State.Value = CreateInitialState();
        }

        public bool TrySetNormalizedHealthServer(float value)
        {
            return TryWrite(Current.WithHealth(value));
        }

        public bool TrySetNormalizedHealth(float value) => TrySetNormalizedHealthServer(value);

        public bool TrySetStaggeredServer(bool value)
        {
            return TryWrite(Current.WithStaggered(value));
        }

        public bool TrySetStaggered(bool value) => TrySetStaggeredServer(value);

        public bool TrySetHandStateServer(bool leftFunctional, bool rightFunctional)
        {
            return TryWrite(Current.WithHands(leftFunctional, rightFunctional));
        }

        public bool TrySetHandState(bool leftFunctional, bool rightFunctional) =>
            TrySetHandStateServer(leftFunctional, rightFunctional);

        private bool TryWrite(in BossBodyNetworkState value)
        {
            if (IsSpawned && !IsServer) return false;
            if (Current.Equals(value)) return false;
            if (IsSpawned) m_State.Value = value;
            else m_OfflineState = value;
            return true;
        }

        private BossBodyNetworkState CreateInitialState() =>
            new BossBodyNetworkState(
                initialNormalizedHealth,
                false,
                initialLeftHandFunctional,
                initialRightHandFunctional);

        private void OnValidate()
        {
            if (offlineEntityId == 0) offlineEntityId = 9000001UL;
            initialNormalizedHealth = Mathf.Clamp01(initialNormalizedHealth);
        }
    }
}
