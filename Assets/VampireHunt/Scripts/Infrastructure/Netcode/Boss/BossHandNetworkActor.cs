using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;
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
        [Tooltip("Boss 手的状态宿主（CombatStatusHost）；命中时把武器元素状态挂上去，并受其 elementResist 减免。")]
        [SerializeField] private CombatStatusHost statusHost;

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

        private void Awake()
        {
            if (statusHost == null) statusHost = GetComponent<CombatStatusHost>();
        }

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
            if (IsServer)
            {
                ApplyHitStatuses(hit.Damage.Source, hit.Statuses);
                return TryApplyDamage(hit.Damage, out _);
            }
            RequestDamageRpc(TrustedCombatHitNetworkMessage.FromDomain(hit));
            return true;
        }

        public bool TryApplyDamage(in DamageRequest request, out ResolvedDamage result)
        {
            result = default;
            if (!IsSpawned || !IsServer || !IsFunctional) return false;
            ServerCombatActivity.Interaction(NetworkManager, request.Source, CombatEntityId, request.AttackId, request.Sequence);
            var validated = new DamageRequest(request.Source, CombatEntityId, request.AttackId,
                request.Sequence, request.BaseDamage, request.Tags);
            float healthBefore = m_Health.Value;
            m_Health.Value = Mathf.Max(0f, m_Health.Value - validated.BaseDamage);
            if (m_Health.Value <= 0f) m_BrokenTime = Time.unscaledTime;
            result = new ResolvedDamage(validated, validated.BaseDamage, validated.Tags, false);
            PublishHandResolution(result, healthBefore, m_Health.Value);
            return true;
        }

        /// <summary>
        /// 把玩家对 Boss 手部（hand）的命中补发到结算路由，理由与
        /// <c>BossVitalsReceiver.PublishBossResolution</c> 完全一致：玩家打手时使魔要能跟着撞手。
        /// </summary>
        /// <remarks>
        /// 手部有真实血量，但同样<b>压制 WasKilled</b>：手部被打爆是「破坏」而非「击杀」，
        /// 计进监控插件的「每分钟击杀数」会污染该指标。因此前后值整体抬高一个 baseline，
        /// 保证 <c>TargetHealthAfter &gt; 0</c>，派生出的 WasKilled 恒为 false。
        /// </remarks>
        private void PublishHandResolution(in ResolvedDamage damage, float healthBefore, float healthAfter)
        {
            float applied = healthBefore - healthAfter;
            if (applied <= 0f) return;

            const float baseline = 1f;
            var record = new CombatResolutionRecord(damage, healthBefore + baseline, healthAfter + baseline);
            ServerCombatResolutionRouter.Publish(NetworkManager, record);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestDamageRpc(TrustedCombatHitNetworkMessage message, RpcParams rpcParams = default)
        {
            var source = new GameplayEntityId(rpcParams.Receive.SenderClientId + 1UL);
            ApplyHitStatuses(source, message.Statuses.ToSpecs());
            TryApplyDamage(new DamageRequest(source, CombatEntityId, message.AttackId,
                message.Sequence, message.Damage, (DamageTags)message.Tags), out _);
        }

        /// <summary>把命中携带的元素状态挂到 Boss 手（经 <see cref="statusHost"/>，自动应用 elementResist 减免）。</summary>
        private void ApplyHitStatuses(GameplayEntityId source, StatusEffectSpec[] statuses)
        {
            if (statusHost == null || statuses == null || statuses.Length == 0) return;
            for (int i = 0; i < statuses.Length; i++)
                statusHost.TryApplyStatus(new StatusApplicationRequest(source, CombatEntityId, statuses[i]));
        }
    }
}
