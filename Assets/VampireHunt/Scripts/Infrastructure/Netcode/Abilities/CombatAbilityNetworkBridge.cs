using Unity.Netcode;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.SharedKernel;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    public interface ICombatAbilityNetworkExecutor
    {
        uint AbilityId { get; }
        CombatIntentPolicy IntentPolicy { get; }
        bool CanExecuteServer(NetworkManager manager, ulong senderClientId, in AbilityCastNetworkMessage message);
        bool ExecuteServer(NetworkManager manager, ulong senderClientId, in AbilityCastNetworkMessage message);
    }

    /// <summary>Fixed prefab bridge from owner-side ability plans to server-side executors.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CombatAbilityNetworkBridge : NetworkBehaviour, ICombatAbilitySink
    {
        [Header("Server Compatibility Validation")]
        [Tooltip("兼容协议允许施法原点偏离服务器玩家根节点的最大距离。合法枪口/自身领域都应落在此范围内。")]
        [SerializeField, Min(1f)] private float maxOriginDistance = 25f;
        [Tooltip("兼容期客户端计划中单项数值的硬上限，只拦截异常或恶意极值，不参与正常数值平衡。")]
        [SerializeField, Min(1f)] private float maxCompatibilityScalar = 1000000f;
        [Tooltip("兼容期单次技能请求允许的最大弹体数量。它是网络安全上限，不是武器平衡数值。")]
        [SerializeField, Min(1)] private int maxCompatibilityProjectileCount = 128;
        [Tooltip("兼容期技能请求允许的最大穿透数。狙击枪使用 999，因此该安全上限必须高于正式配置。")]
        [SerializeField, Min(1)] private int maxCompatibilityPierceCount = 4096;
        [Tooltip("已获服务器接受的技能表现中继器；它不依赖服务器本机是否安装 Presenter。")]
        [SerializeField] private CombatAbilityPresentationNetworkRelay presentationRelay;

        private readonly Dictionary<uint, ulong> m_LastSequence = new Dictionary<uint, ulong>();
        private ICombatAbilityNetworkExecutor[] m_Executors;
        private CoreStatsHandler m_Stats;
        private CombatStatusHost m_Status;
        public override void OnNetworkDespawn()
        {
            m_LastSequence.Clear();
            base.OnNetworkDespawn();
        }

        private CombatAbilityRequestValidator m_RequestValidator;

        private void Awake()
        {
            m_Stats = GetComponent<CoreStatsHandler>();
            m_Status = GetComponent<CombatStatusHost>();
            var behaviours = GetComponents<MonoBehaviour>();
            var executors = new System.Collections.Generic.List<ICombatAbilityNetworkExecutor>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICombatAbilityNetworkExecutor executor) executors.Add(executor);
            }
            m_Executors = executors.ToArray();
            m_RequestValidator = CreateRequestValidator();
            if (presentationRelay == null) presentationRelay = GetComponent<CombatAbilityPresentationNetworkRelay>();
        }

        public bool TryExecute(AbilityCastPlan plan)
        {
            if (!IsSpawned || !IsOwner || plan == null) return false;
            RequestExecuteRpc(AbilityCastNetworkMessage.FromPlan(plan));
            return true;
        }

        /// <remarks>
        /// 兼容期仍传输 Owner 生成的计划，但统一经过服务器入口校验；后续可在不改执行器的前提下
        /// 将本入口替换成“输入意图 → 服务器重建计划”。
        /// </remarks>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestExecuteRpc(AbilityCastNetworkMessage message, RpcParams rpcParams = default)
        {
            if (m_Executors == null || m_Stats == null || !m_Stats.IsAlive ||
                (m_Status != null && m_Status.IsActionBlocked)) return;
            m_RequestValidator ??= CreateRequestValidator();
            if (!m_RequestValidator.IsValid(
                    NetworkObject, rpcParams.Receive.SenderClientId, message, out string rejectionReason))
            {
                Debug.LogWarning($"[CombatAbilityNetworkBridge] Rejected ability {message.AbilityId}: " +
                                 rejectionReason, this);
                return;
            }
            for (int i = 0; i < m_Executors.Length; i++)
            {
                if (m_Executors[i].AbilityId != message.AbilityId) continue;
                if (m_LastSequence.TryGetValue(message.AbilityId, out ulong last) && message.Sequence <= last) return;
                if (!m_Executors[i].CanExecuteServer(NetworkManager, rpcParams.Receive.SenderClientId, message)) return;
                m_LastSequence[message.AbilityId] = message.Sequence;
                if (message.EffectsSuppressed || m_Executors[i].ExecuteServer(
                        NetworkManager, rpcParams.Receive.SenderClientId, message))
                {
                    if (m_Executors[i].IntentPolicy == CombatIntentPolicy.Combat)
                        ServerCombatActivity.Action(NetworkManager, new VampireHunt.SharedKernel.EntityId(OwnerClientId + 1), message.AbilityId, message.Sequence);
                    if (message.EffectsSuppressed) return;
                    // 广播由独立中继器负责；服务器本机没有/关闭 Presenter 也不会阻断远端表现。
                    presentationRelay?.PublishServer(message.AbilityId, message.Origin, message.Direction,
                        message.TravelDistance);
                }
                return;
            }
        }

        private CombatAbilityRequestValidator CreateRequestValidator() =>
            new CombatAbilityRequestValidator(
                maxOriginDistance,
                maxCompatibilityScalar,
                maxCompatibilityProjectileCount,
                maxCompatibilityPierceCount);
    }
}
