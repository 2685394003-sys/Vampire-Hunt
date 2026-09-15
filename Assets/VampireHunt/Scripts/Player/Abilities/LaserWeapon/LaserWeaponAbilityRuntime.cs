using System;
using VampireHunt.Contracts;

namespace VampireHunt.Player.Abilities.LaserWeapon
{
    /// <summary>
    /// Pure data definition for a continuous hitscan laser weapon.
    /// Unlike projectile weapons, the laser resolves instantly via a sphere-cast on the server
    /// (no projectile object); "tick" is the hold-to-fire cadence, gated by the same cooldown
    /// mechanism that drives auto-rifle fire rate.
    /// </summary>
    public sealed class LaserWeaponAbilityDefinition
    {
        public uint AbilityId { get; }
        public AbilitySlot Slot { get; }
        public DamageTags WeaponTag { get; }
        public float DamageMultiplier { get; }
        public float TickInterval { get; }
        public float Range { get; }
        public float Width { get; }
        public int PierceCount { get; }
        public float SpawnHeight { get; }
        public float SpawnForwardOffset { get; }
        public float KnockbackMultiplier { get; }
        public ElementId Element { get; }
        public float ElementMastery { get; }
        public StatusEffectSpec[] OnHitStatuses { get; }

        public LaserWeaponAbilityDefinition(
            uint abilityId,
            AbilitySlot slot,
            DamageTags weaponTag,
            float damageMultiplier,
            float tickInterval,
            float range,
            float width,
            int pierceCount,
            float spawnHeight,
            float spawnForwardOffset,
            float knockbackMultiplier,
            ElementId element,
            float elementMastery,
            StatusEffectSpec[] onHitStatuses)
        {
            AbilityId = abilityId;
            Slot = slot;
            WeaponTag = weaponTag;
            DamageMultiplier = Math.Max(0f, damageMultiplier);
            TickInterval = Math.Max(0.01f, tickInterval);
            Range = Math.Max(0.1f, range);
            Width = Math.Max(0.1f, width);
            PierceCount = Math.Max(1, pierceCount);
            SpawnHeight = spawnHeight;
            SpawnForwardOffset = Math.Max(0f, spawnForwardOffset);
            KnockbackMultiplier = Math.Max(0f, knockbackMultiplier);
            Element = element;
            ElementMastery = Math.Max(0f, elementMastery);
            OnHitStatuses = onHitStatuses ?? Array.Empty<StatusEffectSpec>();
        }
    }

    /// <summary>Pure cast-plan builder for the continuous laser. Tick interval maps to cooldown.</summary>
    public sealed class LaserWeaponAbilityRuntime : ICombatAbility
    {
        private readonly LaserWeaponAbilityDefinition m_Definition;
        private double m_NextReadyTime;

        public uint AbilityId => m_Definition.AbilityId;
        public AbilitySlot Slot => m_Definition.Slot;

        public LaserWeaponAbilityRuntime(LaserWeaponAbilityDefinition definition)
        {
            m_Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public bool TryBuildCast(in CombatAbilityActivationContext context, out AbilityCastPlan plan)
        {
            plan = null;
            if (context.Time < m_NextReadyTime || context.Damage <= 0f) return false;

            Float3 direction = context.Forward.Normalized();
            if (direction.SqrMagnitude <= 0.0001f) return false;

            float damage = context.Damage * m_Definition.DamageMultiplier;
            DamageTags tags = DamageTags.Projectile | m_Definition.WeaponTag;
            if (context.Random01 < context.CriticalChance)
            {
                damage *= context.CriticalDamageMultiplier;
                tags |= DamageTags.Critical;
            }

            float cooldown = m_Definition.TickInterval / context.CooldownMultiplier;
            m_NextReadyTime = context.Time + cooldown;

            plan = new AbilityCastPlan
            {
                AbilityId = AbilityId,
                Slot = Slot,
                Caster = context.Caster,
                Sequence = context.Sequence,
                Origin = context.Origin + Float3.Up * m_Definition.SpawnHeight +
                         direction * m_Definition.SpawnForwardOffset,
                Direction = direction,
                Damage = damage,
                Cooldown = cooldown,
                TravelDistance = m_Definition.Range * context.RangeMultiplier,
                ProjectileSpeed = 0f,
                Knockback = context.Knockback * m_Definition.KnockbackMultiplier,
                ProjectileSize = m_Definition.Width,
                ProjectileCount = 1,
                PierceCount = m_Definition.PierceCount,
                SpreadAngle = 0f,
                Tags = tags,
                Element = m_Definition.Element,
                ElementMastery = m_Definition.ElementMastery
            };

            for (int i = 0; i < m_Definition.OnHitStatuses.Length; i++)
                plan.OnHitStatuses.Add(m_Definition.OnHitStatuses[i]);

            return true;
        }

        public void Reset()
        {
            m_NextReadyTime = 0d;
        }
    }
}
