using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    public enum BossHandSide : byte { Left = 0, Right = 1 }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossHandNetworkActor : NetworkBehaviour, IDamageReceiver,
        ITrustedCombatHitTarget, ICombatEntityIdentity
    {
        [Min(1f)] [SerializeField] private float defaultMaxHealth = 100f;
        [Tooltip("手被击破后多少秒自动恢复。")]
        [Min(0f)] [SerializeField] private float handRestoreDelaySeconds = 20f;

        private readonly NetworkVariable<float> m_Health = new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> m_Flags = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Transform m_BossRoot;
        private Vector3 m_LocalOffset;
        private float m_MaxHealth;
        private float m_BrokenTime = -1f;

        public BossHandSide Side => (m_Flags.Value & 1) != 0 ? BossHandSide.Right : BossHandSide.Left;
        public bool IsIndependent => (m_Flags.Value & 2) != 0;
        public bool IsFunctional => m_Health.Value > 0f;
        public GameplayEntityId CombatEntityId => IsSpawned
            ? new GameplayEntityId((1UL << 62) | NetworkObjectId)
            : GameplayEntityId.None;

        public void PrepareServer(Transform bossRoot, BossHandSide side, Vector3 localOffset, float maxHealth)
        {
            m_BossRoot = bossRoot;
            m_LocalOffset = localOffset;
            m_MaxHealth = Mathf.Max(1f, maxHealth);
            m_Flags.Value = side == BossHandSide.Right ? (byte)1 : (byte)0;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                if (m_MaxHealth <= 0f) m_MaxHealth = defaultMaxHealth;
                m_Health.Value = m_MaxHealth;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || m_BossRoot == null) return;

            // 被击破后 handRestoreDelaySeconds 秒自动恢复
            if (!IsFunctional && m_BrokenTime >= 0f &&
                Time.unscaledTime - m_BrokenTime >= handRestoreDelaySeconds)
            {
                m_Health.Value = m_MaxHealth;
                m_BrokenTime = -1f;
            }

            if (!IsFunctional || IsIndependent) return;
            transform.position = m_BossRoot.TransformPoint(m_LocalOffset);
            transform.rotation = m_BossRoot.rotation;
        }

        public void RestoreServer()
        {
            if (!IsServer) return;
            m_Health.Value = m_MaxHealth;
            m_BrokenTime = -1f;
        }

        public void TeleportToBossServer()
        {
            if (!IsServer || m_BossRoot == null) return;
            transform.position = m_BossRoot.TransformPoint(m_LocalOffset);
            transform.rotation = m_BossRoot.rotation;
        }

        public bool SetIndependentServer(bool value)
        {
            if (!IsServer) return false;
            byte next = value ? (byte)(m_Flags.Value | 2) : (byte)(m_Flags.Value & ~2);
            if (next == m_Flags.Value) return false;
            m_Flags.Value = next;
            return true;
        }

        public bool SubmitTrustedHit(in TrustedCombatHit hit)
        {
            if (!IsSpawned) return false;
            if (IsServer) return TryApplyDamage(hit.Damage, out _);
            RequestDamageRpc(TrustedCombatHitNetworkMessage.FromDomain(hit));
            return true;
        }

        public bool TryApplyDamage(in DamageRequest request, out ResolvedDamage result)
        {
            result = default;
            if (!IsServer || !IsFunctional || request.BaseDamage <= 0f) return false;
            var validated = new DamageRequest(request.Source, CombatEntityId, request.AttackId,
                request.Sequence, request.BaseDamage, request.Tags);
            m_Health.Value = Mathf.Max(0f, m_Health.Value - validated.BaseDamage);
            if (m_Health.Value <= 0f) m_BrokenTime = Time.unscaledTime;
            result = new ResolvedDamage(validated, validated.BaseDamage, validated.Tags, false);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestDamageRpc(TrustedCombatHitNetworkMessage message, RpcParams rpcParams = default)
        {
            var source = new GameplayEntityId(rpcParams.Receive.SenderClientId + 1UL);
            TryApplyDamage(new DamageRequest(source, CombatEntityId, message.AttackId,
                message.Sequence, message.Damage, (DamageTags)message.Tags), out _);
        }
    }
}
