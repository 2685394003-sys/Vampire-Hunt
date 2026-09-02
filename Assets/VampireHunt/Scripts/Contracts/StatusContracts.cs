using VampireHunt.SharedKernel;

namespace VampireHunt.Contracts
{
    public enum ElementId : byte
    {
        None = 0,
        Fire = 1,
        Ice = 2,
        Lightning = 3
    }

    public enum StatusStackPolicy : byte
    {
        RefreshDuration = 0,
        AddStacksAndRefresh = 1,
        ReplaceIfStronger = 2,
        /// <summary>叠层但不刷新总时长：整组层数在同一时刻到期，时间结束后需重新叠层。</summary>
        AddStacksKeepDuration = 3
    }

    /// <summary>状态 ID 与配置表「异常状态」statusId（buffs_0XX）对齐（v2.2）。</summary>
    public static class StatusEffectIds
    {
        /// <summary>buffs_002 灼烧：叠层 DoT。</summary>
        public const uint Burn = 2;
        /// <summary>buffs_003 冻结：硬控（ActionBlock），由霜寒叠满触发。</summary>
        public const uint Frozen = 3;
        /// <summary>buffs_004 霜寒：叠层减速 + 轻微 DoT。</summary>
        public const uint Frost = 4;
        /// <summary>buffs_007 闪电：叠层连锁，满层触发引雷。</summary>
        public const uint Lightning = 7;
        /// <summary>buffs_008 炸裂：冰×火→AOE+击退（元素反应层数状态）。</summary>
        public const uint Detonate = 8;
        /// <summary>buffs_009 碎裂：火×冰→百分比伤害（元素反应层数状态）。</summary>
        public const uint Shatter = 9;
        /// <summary>buffs_010 燃爆：结算灼烧剩余伤害并清除灼烧。</summary>
        public const uint Explode = 10;
        /// <summary>buffs_011 引雷：闪电满层触发的一次性伤害。</summary>
        public const uint Thunder = 11;
    }

    /// <summary>
    /// 状态施放规格。层数 <see cref="Stacks"/> 自 v2.3 起为 <b>float</b>（支持小数层数，如 0.15 层灼烧）。
    /// <see cref="ElementMastery"/> 为<b>属性精通</b>：影响所有元素效果——挂火/挂冰/挂闪电的层数、
    /// 以及闪电连锁时按「源怪层数 × 属性精通」复制可传导状态层数，均由它决定。
    /// </summary>
    public readonly struct StatusEffectSpec
    {
        public uint StatusId { get; }
        public float Stacks { get; }
        public float Duration { get; }
        public float Magnitude { get; }
        public ElementId Element { get; }
        public float ElementMastery { get; }

        public StatusEffectSpec(
            uint statusId, float stacks, float duration, float magnitude, ElementId element, float elementMastery = 1f)
        {
            StatusId = statusId;
            Stacks = stacks > 0f ? stacks : 1f;
            Duration = duration;
            Magnitude = magnitude;
            Element = element;
            ElementMastery = elementMastery > 0f ? elementMastery : 1f;
        }
    }

    public readonly struct StatusApplicationRequest
    {
        public EntityId Source { get; }
        public EntityId Target { get; }
        public StatusEffectSpec Spec { get; }

        public StatusApplicationRequest(EntityId source, EntityId target, in StatusEffectSpec spec)
        {
            Source = source;
            Target = target;
            Spec = spec;
        }
    }

    public interface IStatusEffectTarget
    {
        bool TryApplyStatus(in StatusApplicationRequest request);
        bool HasStatus(uint statusId);
        bool RemoveStatus(uint statusId);
    }

    public interface ICombatEntityIdentity
    {
        EntityId CombatEntityId { get; }
    }
}
