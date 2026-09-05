using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    public interface ICombatAbilityNetworkExecutor
    {
        uint AbilityId { get; }
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
        [Tooltip("已获服务器接受的技能表现中继器；它不依赖服务器本机是否安装 Presenter。")]
        [SerializeField] private CombatAbilityPresentationNetworkRelay presentationRelay;

        private ICombatAbilityNetworkExecutor[] m_Executors;
        private CombatAbilityRequestValidator m_RequestValidator;

        private void Awake()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            var executors = new System.Collections.Generic.List<ICombatAbilityNetworkExecutor>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICombatAbilityNetworkExecutor executor) executors.Add(executor);
            }
            m_Executors = executors.ToArray();
            m_RequestValidator = new CombatAbilityRequestValidator(maxOriginDistance, maxCompatibilityScalar);
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
            if (m_Executors == null) return;
            m_RequestValidator ??= new CombatAbilityRequestValidator(maxOriginDistance, maxCompatibilityScalar);
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
                if (m_Executors[i].ExecuteServer(
                        NetworkManager, rpcParams.Receive.SenderClientId, message))
                {
                    // 广播由独立中继器负责；服务器本机没有/关闭 Presenter 也不会阻断远端表现。
                    presentationRelay?.PublishServer(message.AbilityId, message.Origin, message.Direction,
                        message.TravelDistance);
                }
                return;
            }
        }
    }
}
