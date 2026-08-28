using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Infrastructure.Netcode;

namespace VampireHunt.Player
{
    /// <summary>
    /// 玩家脱战高速回体力：当玩家周围 <see cref="noEnemyRadius"/> 内没有怪物时，
    /// 体力恢复速率乘以 <see cref="noEnemyRegenMultiplier"/>（默认 2，即 15→30/秒）。
    /// 服务器权威（CoreStatsHandler 的再生在服务器执行）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StaminaRegenBoost : NetworkBehaviour
    {
        [Tooltip("判定\"无怪物\"的半径（米）。")]
        [Min(1f)] [SerializeField] private float noEnemyRadius = 25f;
        [Tooltip("无怪物时体力恢复倍率（2 = 15→30/秒）。")]
        [Min(1f)] [SerializeField] private float noEnemyRegenMultiplier = 2f;
        [Tooltip("怪物扫描间隔（秒），降低性能开销。")]
        [Min(0.05f)] [SerializeField] private float scanInterval = 0.25f;

        private CoreStatsHandler m_Stats;
        private float m_NextScanTime;
        private bool m_HasEnemyNearby;

        private void Awake()
        {
            m_Stats = GetComponent<CoreStatsHandler>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || m_Stats == null) return;

            if (Time.unscaledTime >= m_NextScanTime)
            {
                m_NextScanTime = Time.unscaledTime + scanInterval;
                m_HasEnemyNearby = HasEnemyWithin(noEnemyRadius);
            }

            m_Stats.StaminaRegenRateMultiplier = m_HasEnemyNearby ? 1f : noEnemyRegenMultiplier;
        }

        private bool HasEnemyWithin(float radius)
        {
            float sqrRadius = radius * radius;
            EnemyNetworkActor[] enemies = FindObjectsByType<EnemyNetworkActor>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                EnemyNetworkActor enemy = enemies[i];
                if (enemy == null || !enemy.IsSpawned) continue;
                if ((enemy.transform.position - transform.position).sqrMagnitude <= sqrRadius)
                    return true;
            }
            return false;
        }
    }
}
