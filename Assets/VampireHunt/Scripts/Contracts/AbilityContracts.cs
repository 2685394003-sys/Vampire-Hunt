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
        public DamageTags Tags { get; set; }
        public ElementId Element { get; set; }
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
}
