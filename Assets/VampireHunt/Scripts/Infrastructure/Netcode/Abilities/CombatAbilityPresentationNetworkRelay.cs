using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// 已获服务器接受的玩家技能表现中继器。它只复制瞬时表现事件，不参与输入、校验、执行或伤害；
    /// 服务器无需安装任何 VFX/音频 Presenter，具体客户端自行决定是否能表现该 AbilityId。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CombatAbilityPresentationNetworkRelay : NetworkBehaviour
    {
        [Tooltip("实现 ICombatAbilityPresentationSink 的客户端表现组件。留空时自动从同物体查找。")]
        [SerializeField] private MonoBehaviour presenter;

        private ICombatAbilityPresentationSink m_PresentationSink;

        private void Awake()
        {
            ResolveSink();
        }

        public bool PublishServer(uint abilityId, Vector3 origin, Vector3 direction, float range)
        {
            if (!IsSpawned || !IsServer) return false;
            PresentAbilityRpc(abilityId, origin, direction, range);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server,
            Delivery = RpcDelivery.Unreliable)]
        private void PresentAbilityRpc(uint abilityId, Vector3 origin, Vector3 direction, float range)
        {
            ResolveSink();
            if (m_PresentationSink == null || presenter == null || !presenter.isActiveAndEnabled ||
                !m_PresentationSink.HandlesAbility(abilityId)) return;
            m_PresentationSink.PresentAbility(new CombatAbilityPresentationCue(
                abilityId,
                new Float3(origin.x, origin.y, origin.z),
                new Float3(direction.x, direction.y, direction.z),
                range));
        }

        private void ResolveSink()
        {
            m_PresentationSink = presenter as ICombatAbilityPresentationSink;
            if (m_PresentationSink != null) return;
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is ICombatAbilityPresentationSink sink)) continue;
                presenter = behaviours[i];
                m_PresentationSink = sink;
                break;
            }
        }
    }
}
