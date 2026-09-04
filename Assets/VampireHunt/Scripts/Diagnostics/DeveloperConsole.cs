using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Diagnostics
{
    /// <summary>
    /// 开发者控制台（调试用）：左上角面板，F3 开关（与运行时统计监控同键同步开关，一个左上、一个右上互不遮挡）。
    ///
    /// 两个作弊开关（开关状态直接写入本地玩家 CoreStatsHandler 的服务器权威标志）：
    ///   - 无限生命：玩家受伤不掉血（PlayerCombatReceiver 完全拦截 + CoreStatsHandler Health 扣减兜底）
    ///   - 无限体力：疾跑/冲刺不耗体力（CoreStatsHandler 消耗入口拦截）
    /// 开启瞬间会把对应属性拉满；每帧同步标志，覆盖玩家重生后更换的对象。
    ///
    /// 仅编辑器 / Development Build 生效；纯客户端（非 Host/Server）拦截不生效，属已知限制。
    /// </summary>
    public sealed class DeveloperConsole : MonoBehaviour
    {
        [Header("显示")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F3;

        private bool m_Visible = true;
        private bool m_InfiniteHealth;
        private bool m_InfiniteStamina;

        private CoreStatsHandler m_CoreStats;
        private NetworkObject m_PlayerObject;
        private bool m_Wired;

        private static GUIStyle s_BoldLabel;

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) m_Visible = !m_Visible;

            // 延迟绑定本地玩家（玩家物体可能晚于控制台生成；重生更换物体后重新绑定）
            if (!m_Wired || m_PlayerObject == null || !m_PlayerObject.IsSpawned || m_CoreStats == null)
            {
                TryWire();
            }

            // 每帧把开关状态同步到权威端标志（幂等；覆盖重生后的新对象）
            if (m_CoreStats != null)
            {
                m_CoreStats.InfiniteHealth = m_InfiniteHealth;
                m_CoreStats.InfiniteStamina = m_InfiniteStamina;
            }
        }

        private void TryWire()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;
            NetworkClient localClient = nm.LocalClient;
            if (localClient == null || localClient.PlayerObject == null) return;

            NetworkObject player = localClient.PlayerObject;
            var stats = player.GetComponent<CoreStatsHandler>();
            if (stats == null) return;

            m_PlayerObject = player;
            m_CoreStats = stats;
            m_Wired = true;
        }

        private void OnGUI()
        {
            if (!m_Visible) return;
            if (s_BoldLabel == null) s_BoldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            const int w = 280;
            const int h = 168;
            int x = 12;
            int y = 12;

            GUILayout.BeginArea(new Rect(x, y, w, h), GUI.skin.box);
            GUILayout.Label("开发者控制台 (F3 开关)", s_BoldLabel);

            // 无限生命
            bool newHealth = GUILayout.Toggle(m_InfiniteHealth, "无限生命（不掉血）");
            if (newHealth != m_InfiniteHealth)
            {
                m_InfiniteHealth = newHealth;
                if (m_InfiniteHealth) TopUpStat(StatKeys.Health);
            }

            // 无限体力
            bool newStamina = GUILayout.Toggle(m_InfiniteStamina, "无限体力（疾跑/冲刺不耗）");
            if (newStamina != m_InfiniteStamina)
            {
                m_InfiniteStamina = newStamina;
                if (m_InfiniteStamina) TopUpStat(StatKeys.Stamina);
            }

            if (m_CoreStats != null)
            {
                GUILayout.Label($"生命 {m_CoreStats.GetCurrentValue(StatKeys.Health):F0} / {m_CoreStats.GetMaxValue(StatKeys.Health):F0}");
                GUILayout.Label($"体力 {m_CoreStats.GetCurrentValue(StatKeys.Stamina):F0} / {m_CoreStats.GetMaxValue(StatKeys.Stamina):F0}");
            }
            else
            {
                GUILayout.Label("等待本地玩家接入…");
            }

            GUILayout.EndArea();
        }

        private void TopUpStat(int statHash)
        {
            if (m_CoreStats == null) return;
            float max = m_CoreStats.GetMaxValue(statHash);
            float current = m_CoreStats.GetCurrentValue(statHash);
            if (current < max)
            {
                m_CoreStats.ModifyStat(statHash, max - current, 0, ModificationSource.Direct);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 编辑器 / 开发构建中自动生成控制台，不修改任何预制体或场景
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (FindAnyObjectByType<DeveloperConsole>() != null) return;
            var go = new GameObject("DeveloperConsole");
            go.AddComponent<DeveloperConsole>();
            DontDestroyOnLoad(go);
        }
#endif
    }
}
