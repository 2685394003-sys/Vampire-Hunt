using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Player.Abilities.CircleField;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// 圆型领域的 Unity 组合根（composition root）：把配置资产转成运行时能力。
    /// 同时对外暴露运行期系数接口，供血契系统（领域·扩大 / 层数强化等）调用。
    /// </summary>
    public sealed class CircleFieldAbilityProvider : MonoBehaviour, ICombatAbilityProvider
    {
        [SerializeField] private CircleFieldAbilityAsset definition;

        private CircleFieldAbilityRuntime m_Runtime;

        /// <summary>最近一次创建的运行时；CombatAbilityHost 只在组合阶段调用一次 CreateAbility。</summary>
        public CircleFieldAbilityRuntime Runtime => m_Runtime;

        /// <summary>当前实际领域半径（米），表现层与实际伤害共用同一口径。</summary>
        public float CurrentRadius => m_Runtime != null
            ? m_Runtime.CurrentRadius
            : (definition != null ? definition.BaseRadius : 0f);

        public ICombatAbility CreateAbility()
        {
            if (definition == null)
            {
                Debug.LogError("[CircleFieldAbilityProvider] 圆型领域配置资产未指定（definition is not assigned）。", this);
                return null;
            }
            m_Runtime = definition.CreateRuntime();
            return m_Runtime;
        }

        // ── 运行期系数接口（血契系统入口）─────────────────────────────

        /// <summary>设置基础伤害继承系数（1 = 全额继承玩家基础伤害）。</summary>
        public void SetBaseDamageInheritRatio(float ratio)
        {
            if (m_Runtime != null) m_Runtime.BaseDamageInheritRatio = ratio;
        }

        /// <summary>设置 buff 触发数量系数（每次触发挂载层数 = 配置层数 × 此系数）。</summary>
        public void SetBuffTriggerCountMultiplier(float multiplier)
        {
            if (m_Runtime != null) m_Runtime.BuffTriggerCountMultiplier = multiplier;
        }

        /// <summary>设置半径缩放（1 = 配置半径）：「领域·扩大」类血契调用。</summary>
        public void SetRadiusScale(float scale)
        {
            if (m_Runtime != null) m_Runtime.RadiusScale = scale;
        }
    }
}
