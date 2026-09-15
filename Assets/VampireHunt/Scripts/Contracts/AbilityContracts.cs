using System;
using System.Collections.Generic;
using VampireHunt.SharedKernel;

namespace VampireHunt.Contracts
{
    public enum AbilitySlot : byte
    {
        Primary = 0,
        Secondary = 1,
        Special = 2
    }

    [Serializable]
    public readonly struct Float3 : IEquatable<Float3>
    {
        public static readonly Float3 Zero = new Float3(0f, 0f, 0f);
        public static readonly Float3 Up = new Float3(0f, 1f, 0f);

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public float SqrMagnitude => X * X + Y * Y + Z * Z;

        public Float3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Float3 Normalized()
        {
            float magnitude = (float)Math.Sqrt(SqrMagnitude);
            return magnitude > 0.0001f ? this * (1f / magnitude) : Zero;
        }

        public bool Equals(Float3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Float3 other && Equals(other);
        public override int GetHashCode() => (X, Y, Z).GetHashCode();

        public static Float3 operator +(Float3 left, Float3 right) =>
            new Float3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        public static Float3 operator *(Float3 value, float scalar) =>
            new Float3(value.X * scalar, value.Y * scalar, value.Z * scalar);
    }

    public readonly struct CombatAbilityActivationContext
    {
        public EntityId Caster { get; }
        public ulong Sequence { get; }
        public double Time { get; }
        public Float3 Origin { get; }
        public Float3 Forward { get; }
        /// <summary>Mouse/aim ground point (world-space). Optional; abilities that lob projectiles use it as the target landing point.</summary>
        public Float3 AimPoint { get; }
        public float Damage { get; }
        public float CooldownMultiplier { get; }
        public float RangeMultiplier { get; }
        public float CriticalChance { get; }
        public float CriticalDamageMultiplier { get; }
        public float Knockback { get; }
        public float Random01 { get; }

        public CombatAbilityActivationContext(
            EntityId caster,
            ulong sequence,
            double time,
            Float3 origin,
            Float3 forward,
            float damage,
            float cooldownMultiplier,
            float rangeMultiplier,
            float criticalChance,
            float criticalDamageMultiplier,
            float knockback,
            float random01,
            Float3 aimPoint = default)
        {
            Caster = caster;
            Sequence = sequence;
            Time = time;
            Origin = origin;
            Forward = forward;
            AimPoint = aimPoint;
            Damage = Math.Max(0f, damage);
            CooldownMultiplier = Math.Max(0.05f, cooldownMultiplier);
            RangeMultiplier = Math.Max(0.1f, rangeMultiplier);
            CriticalChance = Math.Max(0f, Math.Min(1f, criticalChance));
            CriticalDamageMultiplier = Math.Max(1f, criticalDamageMultiplier);
            Knockback = Math.Max(0f, knockback);
            Random01 = Math.Max(0f, Math.Min(1f, random01));
        }
    }

    /// <summary>Pure execution plan produced by an ability and consumed by a network adapter.</summary>
    public sealed class AbilityCastPlan
    {
        public uint AbilityId { get; set; }
        public AbilitySlot Slot { get; set; }
        public EntityId Caster { get; set; }
        public ulong Sequence { get; set; }
        public Float3 Origin { get; set; }
        public Float3 Direction { get; set; }
        public float Damage { get; set; }
        public float Cooldown { get; set; }
        public float TravelDistance { get; set; }
        public float ProjectileSpeed { get; set; }
        public float Knockback { get; set; }
        public float ProjectileSize { get; set; } = 1f;
        public int ProjectileCount { get; set; } = 1;
        public int PierceCount { get; set; } = 1;
        public float SpreadAngle { get; set; }
        /// <summary>扇形张开全角（度）：剑气等扇形武器每颗弹丸自身的判定/视觉角度。与 SpreadAngle（多弹丸排布散布）解耦。</summary>
        public float FanAngle { get; set; }
        public DamageTags Tags { get; set; }
        public ElementId Element { get; set; }
        /// <summary>
        /// 属性精通：影响所有元素效果——挂火/挂冰/挂闪电的层数、以及闪电连锁传导的复制层数。
        /// 由武器运行时从武器定义写入，元素注入（血契/调试）读取后烙到元素状态上。
        /// </summary>
        public float ElementMastery { get; set; } = 1f;
        public bool IsCancelled { get; set; }
        public List<StatusEffectSpec> OnHitStatuses { get; } = new List<StatusEffectSpec>();
    }

    public interface ICombatAbility
    {
        uint AbilityId { get; }
        AbilitySlot Slot { get; }
        bool TryBuildCast(in CombatAbilityActivationContext context, out AbilityCastPlan plan);
        void Reset();
    }

    public interface ICombatAbilityProvider
    {
        ICombatAbility CreateAbility();
    }

    public interface ICombatAbilitySink
    {
        bool TryExecute(AbilityCastPlan plan);
    }

    public interface IAbilityCastModifier
    {
        int Priority { get; }
        void Modify(AbilityCastPlan plan);
    }

    public interface ICombatAbilityModifierTarget
    {
        bool RegisterAbilityModifier(IAbilityCastModifier modifier);
        bool UnregisterAbilityModifier(IAbilityCastModifier modifier);
    }

    /// <summary>
    /// 武器解锁目标端口：「获得武器」类血契（2001 血穿魔弹 / 3001 血飞魔剑 /
    /// 4001 血爆魔阵 / 5001 血光魔炮）生效时，把活动主武器切换到目标武器。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="VampireHunt.Infrastructure.Integration.CombatAbilityHost"/> 实现。
    /// 它与 GameplayEffectHost 挂在同一 GameObject，Unlock 模块（EffectModuleTypeIds.Unlock）
    /// 的工厂通过端口机制（EffectPortCollection.TryGet）自动定位到它，无需显式接线。
    /// </remarks>
    public interface IWeaponUnlockTarget
    {
        /// <summary>把活动主武器切到 <paramref name="abilityId"/>（武器 id：狙击 110 / 步枪 120 / 导弹 130 / 激光 140）。
        /// 返回 false 表示该端没有这把武器（字典无此 id，理论不发生）。</summary>
        bool TryUnlockWeapon(uint abilityId);
    }

    /// <summary>
    /// 领域激活目标端口：「获得领域」类血契（6001 荒芜降临）生效时激活常驻圆型领域。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="VampireHunt.Infrastructure.Integration.CircleFieldAuraDriver"/> 实现
    /// （其代码注释即以「荒芜之契」为血契接线点）。端口由 Unlock 模块工厂自动定位。
    /// </remarks>
    public interface IFieldActivationTarget
    {
        /// <summary>激活常驻领域；<paramref name="damageInheritRatio"/> &gt; 0 时同时把
        /// 基础伤害继承系数设为该值（1 = 全额继承玩家基础伤害）。返回 true 表示已激活。</summary>
        bool ActivateField(float damageInheritRatio);
    }
}
