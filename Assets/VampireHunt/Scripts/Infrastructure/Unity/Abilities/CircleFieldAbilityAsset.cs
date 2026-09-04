using System;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Player.Abilities.CircleField;

namespace VampireHunt.Infrastructure.Unity
{
    /// <summary>
    /// 圆型领域（field）的 Inspector 配置资产（ScriptableObject）。
    /// 属于「非玩家发射型 / 其他」线路：不占主武器槽，获得后常驻，圆心跟随玩家。
    /// 每跳伤害 = 玩家基础伤害(10) × 基础伤害继承系数 × 每跳倍率。
    /// </summary>
    [CreateAssetMenu(fileName = "CircleFieldAbility", menuName = "Vampire Hunt/Combat/Circle Field Ability")]
    public sealed class CircleFieldAbilityAsset : ScriptableObject
    {
        [Serializable]
        private sealed class StatusEntry
        {
            public uint statusId = 0;
            [Min(1)] public int stacks = 1;
            [Min(0f)] public float duration = 0f;
            [Min(0f)] public float magnitude = 0f;
            public ElementId element = ElementId.None;

            public StatusEffectSpec ToSpec() =>
                new StatusEffectSpec(statusId, stacks, duration, magnitude, element);
        }

        [Header("Identity")]
        [Min(1)] [SerializeField] private uint abilityId = 160;
        [Tooltip("领域固定走 Special 槽：不参与主武器（Primary）切换。")]
        [SerializeField] private AbilitySlot slot = AbilitySlot.Special;
        [Tooltip("伤害标签（damage tag）：血契与监控按此标签过滤领域伤害。")]
        [SerializeField] private DamageTags weaponTag = DamageTags.Field;

        [Header("基础伤害继承（接口 1）")]
        [Tooltip("基础伤害继承系数：领域从玩家基础伤害属性（DamageStat，默认 10）继承的比例。1 = 全额继承。")]
        [Min(0f)] [SerializeField] private float baseDamageInheritRatio = 1f;
        [Tooltip("每跳伤害倍率：在继承后的伤害上再乘的系数，与狙击枪 damageMultiplier 同口径。")]
        [Min(0f)] [SerializeField] private float tickDamageMultiplier = 1f;

        [Header("领域")]
        [Tooltip("伤害间隔（秒）：光环每间隔结算一次范围内全部敌人。")]
        [Min(0.01f)] [SerializeField] private float tickInterval = 0.5f;
        [Tooltip("领域半径（米）：以玩家为圆心的圆形范围。可被血契 AbilityPlanModifier(TravelDistance) 再缩放。")]
        [Min(0.1f)] [SerializeField] private float radius = 4f;
        [Tooltip("领域中心相对玩家脚下的高度偏移，用于覆盖敌人碰撞体。")]
        [SerializeField] private float heightOffset = 0.9f;
        [Tooltip("击退力倍率：最终击退力 = 全局击退力(KnockbackForce) × 此倍率，方向 = 从圆心指向敌人。0 = 不击退。")]
        [Min(0f)] [SerializeField] private float knockbackMultiplier = 0f;

        [Header("buff 触发数量系数（接口 2）")]
        [Tooltip("buff 触发数量系数：领域每次触发挂载的状态层数 = 下面 onHitStatuses 里配的层数 × 此系数（四舍五入，最少 1 层）。玩家发射型武器暂无此接口。")]
        [Min(0f)] [SerializeField] private float buffTriggerCountMultiplier = 1f;

        [Header("Element and on-hit effects")]
        [SerializeField] private ElementId element = ElementId.None;
        [SerializeField] private StatusEntry[] onHitStatuses = Array.Empty<StatusEntry>();

        public uint AbilityId => abilityId;
        public float BaseRadius => radius;
        public float BaseDamageInheritRatio => baseDamageInheritRatio;
        public float BuffTriggerCountMultiplier => buffTriggerCountMultiplier;

        public CircleFieldAbilityRuntime CreateRuntime()
        {
            var specs = new StatusEffectSpec[onHitStatuses?.Length ?? 0];
            for (int i = 0; i < specs.Length; i++)
                specs[i] = onHitStatuses[i] != null ? onHitStatuses[i].ToSpec() : default;

            return new CircleFieldAbilityRuntime(new CircleFieldAbilityDefinition(
                abilityId, slot, weaponTag, baseDamageInheritRatio, tickDamageMultiplier,
                tickInterval, radius, heightOffset, knockbackMultiplier,
                buffTriggerCountMultiplier, element, specs));
        }
    }
}
