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
        private ICombatAbilityNetworkExecutor[] m_Executors;

        private void Awake()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            var executors = new System.Collections.Generic.List<ICombatAbilityNetworkExecutor>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICombatAbilityNetworkExecutor executor) executors.Add(executor);
            }
            m_Executors = executors.ToArray();
        }

        public bool TryExecute(AbilityCastPlan plan)
        {
            if (!IsSpawned || !IsOwner || plan == null) return false;
            RequestExecuteRpc(AbilityCastNetworkMessage.FromPlan(plan));
            return true;
        }

        /// <remarks>Trusted prototype path: gameplay fields submitted by the owner are not hit-validated.</remarks>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestExecuteRpc(AbilityCastNetworkMessage message, RpcParams rpcParams = default)
        {
            if (m_Executors == null) return;
            for (int i = 0; i < m_Executors.Length; i++)
            {
                if (m_Executors[i].AbilityId != message.AbilityId) continue;
                m_Executors[i].ExecuteServer(NetworkManager, rpcParams.Receive.SenderClientId, message);
                return;
            }
        }
    }
}
