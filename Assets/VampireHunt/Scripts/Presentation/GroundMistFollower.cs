using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Presentation
{
    /// <summary>
    /// 地面粒子雾的位置驱动：只做「贴地 + 跟随本地玩家」，不参与任何玩法逻辑、不做网络同步。
    /// 挂在场景中的地面雾物体上，每个客户端各自本地表现（雾是纯视觉，粒子无需同步）。
    ///
    /// 跟随目标解析顺序：手动指定 > 本地玩家（Netcode）> 主摄像机。
    /// 高度取「向下射线打到的地面」而不是玩家自身 Y，这样在起伏地形上雾盘不会飘在半空或陷进山体。
    /// 粒子的 simulationSpace 保持 World：飘动由 velocityOverLifetime / noise 提供，
    /// 改成 Local 会让整块雾跟着玩家刚性滑动，反而失真。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GroundMistFollower : MonoBehaviour
    {
        [Tooltip("跟随目标。留空时自动解析为本地玩家；解析不到时退回主摄像机。")]
        [SerializeField] private Transform followTarget;

        [Tooltip("相对贴地点的高度（米）。抬高可缓解雾盘与地面切割（软粒子只能缓解、不能根治）。")]
        [SerializeField] private float heightOffset = 0.35f;

        [Tooltip("贴地检测层：Default(0) 地图道具/建筑 + Ground(9) 地形。")]
        [SerializeField] private LayerMask groundMask = 1 | (1 << 9);

        [Tooltip("向下打射线前先抬升的高度（米），需高于玩家可能站上的最高地形。")]
        [SerializeField, Min(1f)] private float probeLift = 60f;

        private void LateUpdate()
        {
            Transform anchor = ResolveTarget();
            if (anchor == null) return; // 解析不到就保持编辑期摆位，不强行挪动

            Vector3 p = anchor.position;
            float y = p.y + heightOffset;

            var origin = new Vector3(p.x, p.y + probeLift, p.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeLift * 2f, groundMask,
                    QueryTriggerInteraction.Ignore))
            {
                y = hit.point.y + heightOffset;
            }

            transform.position = new Vector3(p.x, y, p.z);
        }

        private Transform ResolveTarget()
        {
            if (followTarget != null) return followTarget;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.LocalClient != null && manager.LocalClient.PlayerObject != null)
            {
                return manager.LocalClient.PlayerObject.transform;
            }

            Camera main = Camera.main;
            return main != null ? main.transform : null;
        }
    }
}
