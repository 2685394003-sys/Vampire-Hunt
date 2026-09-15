using Unity.Netcode;
using UnityEngine;
using VampireHunt.Bootstrap;

namespace VampireHunt.Infrastructure.Unity
{
    /// <summary>
    /// 菜单暂停控制器。
    /// 在「单人模式」下，打开血契菜单或商店菜单时冻结整个游戏模拟（Time.timeScale = 0），
    /// 并暂停 22 分钟全局倒计时（RunClock）；关闭菜单时恢复。
    ///
    /// 设计要点：
    /// - 仅单人模式生效（Host 且没有其它已连接客户端）。联机时一个玩家的菜单不应冻结整局服务器，
    ///   因此联机下不做任何处理（timeScale 保持不变）。
    /// - 采用引用计数：血契菜单与商店菜单两个暂停源可叠加，只有全部释放后才真正恢复。
    /// - 控制器为静态类，由 Presenter 在菜单打开/关闭时调用，不新增任何预制体组件。
    /// </summary>
    public static class MenuPauseController
    {
        private static int s_PactRequests;
        private static int s_ShopRequests;
        private static int s_FreeCameraRequests;
        private static VampireHuntGameManager s_GameManager;

        public static bool IsPaused => (s_PactRequests + s_ShopRequests + s_FreeCameraRequests) > 0;

        public static void RequestPactPause() => RequestPause(ref s_PactRequests);
        public static void ReleasePactPause() => ReleasePause(ref s_PactRequests);
        public static void RequestShopPause() => RequestPause(ref s_ShopRequests);
        public static void ReleaseShopPause() => ReleasePause(ref s_ShopRequests);

        /// <summary>自由相机观察模式的暂停源：单人下进入自由相机时冻结整局，多人下不生效。</summary>
        public static void RequestFreeCameraPause() => RequestPause(ref s_FreeCameraRequests);

        /// <summary>退出自由相机时释放暂停。</summary>
        public static void ReleaseFreeCameraPause() => ReleasePause(ref s_FreeCameraRequests);

        private static void RequestPause(ref int counter)
        {
            if (!IsSinglePlayer()) return;
            counter++;
            if (IsPaused) ApplyPause(true);
        }

        private static void ReleasePause(ref int counter)
        {
            // 无条件递减：若该次暂停发生在非单人模式，计数本就没增加，Mathf.Max 会兜住；
            // 若暂停期间有玩家加入（单人 → 多人），这里仍能把计数归零并解除冻结，
            // 否则 timeScale 会永久卡在 0（原实现直接 return，会把整局冻死）。
            counter = Mathf.Max(0, counter - 1);

            // 只在「确实被冻结」时恢复，避免把 Boss 慢动作等非 1 的 timeScale 重置掉。
            if (!IsPaused && Time.timeScale == 0f) ApplyPause(false);
        }

        /// <summary>单人模式 = 本机是 Host 且没有其它已连接客户端。</summary>
        private static bool IsSinglePlayer()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return false;
            return nm.IsHost && nm.ConnectedClients.Count <= 1;
        }

        private static void ApplyPause(bool paused)
        {
            // 冻结/恢复本地模拟（敌人移动、攻击、刷怪均依赖 deltaTime / FixedUpdate）
            Time.timeScale = paused ? 0f : 1f;

            // 暂停/恢复 22 分钟全局倒计时（服务器权威，单人 Host 即服务器）
            if (s_GameManager == null)
            {
                s_GameManager = Object.FindAnyObjectByType<VampireHuntGameManager>();
            }
            if (s_GameManager != null)
            {
                if (paused) s_GameManager.TryPauseClock();
                else s_GameManager.TryResumeClock();
            }
        }
    }
}
