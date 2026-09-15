using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    internal struct CircleFieldAuraNetworkState : INetworkSerializable, IEquatable<CircleFieldAuraNetworkState>
    {
        public bool IsActive;
        public float Radius;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref IsActive);
            serializer.SerializeValue(ref Radius);
        }

        public bool Equals(CircleFieldAuraNetworkState other) =>
            IsActive == other.IsActive && Radius.Equals(other.Radius);
    }

    /// <summary>
    /// 圆形领域的持久表现状态复制器。状态由服务器写入 NetworkVariable，因此新客户端加入后
    /// 也能恢复领域开关与半径；本组件不驱动施法，也不处理命中或伤害。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CircleFieldAuraStateNetworkBridge : NetworkBehaviour,
        ICircleFieldAuraStatePublisher,
        ICircleFieldAuraReadModel
    {
        [Tooltip("表现状态允许同步的最大半径。只用于网络包防御，不参与领域伤害平衡。")]
        [SerializeField, Min(1f)] private float maxPresentationRadius = 100f;

        private readonly NetworkVariable<CircleFieldAuraNetworkState> m_State =
            new NetworkVariable<CircleFieldAuraNetworkState>(default,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NetworkObject m_NetworkObject;
        private CircleFieldAuraPresentationState m_Current;
        private CircleFieldAuraPresentationState m_Pending;
        private bool m_HasPending;

        public CircleFieldAuraPresentationState Current => m_Current;
        public event Action<CircleFieldAuraPresentationState> Changed;

        private void Awake()
        {
            m_NetworkObject = GetComponent<NetworkObject>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_State.OnValueChanged += HandleNetworkStateChanged;
            if (IsClient) ApplyReadModel(m_State.Value);

            if (!m_HasPending) return;
            CircleFieldAuraPresentationState pending = m_Pending;
            m_HasPending = false;
            if (IsServer && IsOwner) SetServerState(pending.IsActive, pending.Radius);
            else if (IsOwner) RequestStateRpc(pending.IsActive, pending.Radius);
        }

        public override void OnNetworkDespawn()
        {
            m_State.OnValueChanged -= HandleNetworkStateChanged;
            ApplyReadModel(default);
            base.OnNetworkDespawn();
        }

        public bool TryPublish(in CircleFieldAuraPresentationState state)
        {
            CircleFieldAuraPresentationState sanitized = Sanitize(state.IsActive, state.Radius);
            if (IsStandalone())
            {
                ApplyReadModel(ToNetwork(sanitized));
                return true;
            }

            if (!IsSpawned)
            {
                m_Pending = sanitized;
                m_HasPending = true;
                return true;
            }

            if (IsServer)
            {
                // 玩家对象在服务器上也有一份非 Owner 的 Driver；忽略那份本地默认值，
                // 防止它把真正 Owner 发来的激活状态覆盖回 false。服务器执行器仍可调用 SetServerState 校正。
                if (!IsOwner) return true;
                SetServerState(sanitized.IsActive, sanitized.Radius);
                return true;
            }

            // 非 Owner 客户端上的 Driver 只是同一网络预制体的镜像，不应反向发布状态。
            if (!IsOwner) return true;
            RequestStateRpc(sanitized.IsActive, sanitized.Radius);
            return true;
        }

        /// <summary>
        /// 服务器执行器可以用实际获准的施法范围校正表现半径，防止持久视觉与伤害范围分离。
        /// </summary>
        public void SetServerState(bool active, float radius)
        {
            if (!IsSpawned || !IsServer) return;
            CircleFieldAuraPresentationState sanitized = Sanitize(active, radius);
            m_State.Value = ToNetwork(sanitized);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestStateRpc(bool active, float radius)
        {
            // 这里只同步表现；真正的施法、命中与伤害仍由服务器执行器决定。
            SetServerState(active, radius);
        }

        private void HandleNetworkStateChanged(
            CircleFieldAuraNetworkState previous,
            CircleFieldAuraNetworkState current)
        {
            if (IsClient) ApplyReadModel(current);
        }

        private void ApplyReadModel(in CircleFieldAuraNetworkState state)
        {
            CircleFieldAuraPresentationState next = Sanitize(state.IsActive, state.Radius);
            if (m_Current.IsActive == next.IsActive && Mathf.Approximately(m_Current.Radius, next.Radius)) return;
            m_Current = next;
            Changed?.Invoke(m_Current);
        }

        private CircleFieldAuraPresentationState Sanitize(bool active, float radius)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius)) radius = 0f;
            radius = Mathf.Clamp(radius, 0f, Mathf.Max(1f, maxPresentationRadius));
            return new CircleFieldAuraPresentationState(active && radius > 0f, radius);
        }

        private bool IsStandalone()
        {
            NetworkManager manager = m_NetworkObject != null ? m_NetworkObject.NetworkManager : null;
            return manager == null || !manager.IsListening;
        }

        private static CircleFieldAuraNetworkState ToNetwork(in CircleFieldAuraPresentationState state) =>
            new CircleFieldAuraNetworkState { IsActive = state.IsActive, Radius = state.Radius };
    }
}
