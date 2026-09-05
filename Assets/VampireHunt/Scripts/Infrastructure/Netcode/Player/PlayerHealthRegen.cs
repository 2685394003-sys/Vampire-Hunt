using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Infrastructure.Netcode.Player
{
    /// <summary>
    /// 玩家脱战自动回血（续航）：距最后一次【生命值下降】超过 <see cref="regenDelaySeconds"/> 秒后，
    /// 每秒恢复【当前最大生命值 × <see cref="regenFractionPerSecond"/>】。
    /// <para>
    /// 恢复量按【比例】而非固定值计算，因此血量上限被血契（如 M9004_health_capacity_C1，Flat +100/层）
    /// 顶高之后，回血速度会自动跟随，不会随 build 成长而退化。
    /// </para>
    /// 服务器权威（仅在服务端写入 CoreStatsHandler，由 NetworkList 自动同步到客户端）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHealthRegen : NetworkBehaviour
    {
        [Tooltip("脱战判定：距最后一次生命值下降超过这么多秒，才开始回血。回血途中再次掉血则重新开始计时。")]
        [Min(0f)] [SerializeField] private float regenDelaySeconds = 5f;

        [Tooltip("每秒恢复【当前最大生命值】的比例。1/60 ≈ 0.0166667 → 从空血回满约需 60 秒。" +
                 "填 0 = 关闭自动回血。")]
        [Min(0f)] [SerializeField] private float regenFractionPerSecond = 1f / 60f;

        private CoreStatsHandler m_Stats;

        // 最后一次观察到生命值下降的时间戳（服务器权威）
        private float m_LastDamageTime = float.NegativeInfinity;

        // 上一帧观察到的生命值，用于检测掉血。-1 = 尚未采样。
        private float m_LastObservedHealth = -1f;

        private void Awake()
        {
            m_Stats = GetComponent<CoreStatsHandler>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || m_Stats == null) return;

            // 死亡后不再回血，避免"血量归零后自动爬起来"
            if (!m_Stats.IsAlive) return;

            float maxHealth = m_Stats.GetMaxValue(StatKeys.Health);
            float currentHealth = m_Stats.GetCurrentValue(StatKeys.Health);

            // 用"生命值下降"判定受伤，而不是监听伤害事件：
            // 1) 被无敌/护盾完全挡下的攻击不计入（没掉血就不算受伤，符合玩家直觉）；
            // 2) 近战/远程/DoT/环境等一切掉血途径自动覆盖，无需逐处接线。
            if (m_LastObservedHealth >= 0f && currentHealth < m_LastObservedHealth - 0.0001f)
            {
                m_LastDamageTime = Time.time;
            }
            m_LastObservedHealth = currentHealth;

            if (regenFractionPerSecond <= 0f || maxHealth <= 0f) return;
            if (currentHealth >= maxHealth) return;
            if (Time.time - m_LastDamageTime < regenDelaySeconds) return;

            m_Stats.ModifyStat(StatKeys.Health, maxHealth * regenFractionPerSecond * Time.deltaTime,
                0UL, ModificationSource.Regeneration);
        }
    }
}
