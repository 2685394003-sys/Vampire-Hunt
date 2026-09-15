using System;
using VampireHunt.Contracts;

namespace VampireHunt.Player.Abilities.ProjectileWeapon
{
    /// <summary>
    /// Pure data definition for a projectile-based primary weapon (sniper, auto-rifle, ...).
    /// Mirrors <see cref="SwordWave.SwordWaveAbilityDefinition"/> but adds a per-weapon damage
    /// multiplier and damage tag so multiple projectile weapons can share one runtime.
    /// Single-player only; the server-authoritative path degrades to local state offline.
    /// </summary>
    public sealed class ProjectileWeaponAbilityDefinition
    {
        public uint AbilityId { get; }
        public AbilitySlot Slot { get; }
        public DamageTags WeaponTag { get; }
        public float DamageMultiplier { get; }
        public float BaseCooldown { get; }
        public float BaseTravelDistance { get; }
        public float ProjectileSpeed { get; }
        public float ProjectileSize { get; }
        public int ProjectileCount { get; }
        public int PierceCount { get; }
        public float SpreadAngle { get; }
        public float SpawnForwardOffset { get; }
        public float SpawnHeight { get; }
        public float KnockbackMultiplier { get; }
        public ElementId Element { get; }
        public float ElementMastery { get; }
        public StatusEffectSpec[] OnHitStatuses { get; }

        public ProjectileWeaponAbilityDefinition(
            uint abilityId,
            AbilitySlot slot,
            DamageTags weaponTag,
            float damageMultiplier,
            float baseCooldown,
            float baseTravelDistance,
            float projectileSpeed,
            float projectileSize,
            int projectileCount,
            int pierceCount,
            float spreadAngle,
            float spawnForwardOffset,
            float spawnHeight,
            float knockbackMultiplier,
            ElementId element,
            float elementMastery,
            StatusEffectSpec[] onHitStatuses)
        {
            AbilityId = abilityId;
            Slot = slot;
            WeaponTag = weaponTag;
            DamageMultiplier = Math.Max(0f, damageMultiplier);
            BaseCooldown = Math.Max(0.01f, baseCooldown);
            BaseTravelDistance = Math.Max(0.1f, baseTravelDistance);
            ProjectileSpeed = Math.Max(0.1f, projectileSpeed);
            ProjectileSize = Math.Max(0.1f, projectileSize);
            ProjectileCount = Math.Max(1, projectileCount);
            PierceCount = Math.Max(1, pierceCount);
            SpreadAngle = Math.Max(0f, spreadAngle);
            SpawnForwardOffset = Math.Max(0f, spawnForwardOffset);
            SpawnHeight = spawnHeight;
            KnockbackMultiplier = Math.Max(0f, knockbackMultiplier);
            Element = element;
            ElementMastery = Math.Max(0f, elementMastery);
            OnHitStatuses = onHitStatuses ?? Array.Empty<StatusEffectSpec>();
        }
    }

    /// <summary>Pure cast-plan builder for projectile weapons. Reused by sniper, rifle, etc.</summary>
    public sealed class ProjectileWeaponAbilityRuntime : ICombatAbility
    {
        private readonly ProjectileWeaponAbilityDefinition m_Definition;
        private double m_NextReadyTime;

        public uint AbilityId => m_Definition.AbilityId;
        public AbilitySlot Slot => m_Definition.Slot;

        public ProjectileWeaponAbilityRuntime(ProjectileWeaponAbilityDefinition definition)
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

            float cooldown = m_Definition.BaseCooldown / context.CooldownMultiplier;
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
                TravelDistance = m_Definition.BaseTravelDistance * context.RangeMultiplier,
                ProjectileSpeed = m_Definition.ProjectileSpeed,
                Knockback = context.Knockback * m_Definition.KnockbackMultiplier,
                ProjectileSize = m_Definition.ProjectileSize,
                ProjectileCount = m_Definition.ProjectileCount,
                PierceCount = m_Definition.PierceCount,
                SpreadAngle = m_Definition.SpreadAngle,
                Tags = tags,
                Element = m_Definition.Element,
                ElementMastery = m_Definition.ElementMastery
            };

            for (int i = 0; i < m_Definition.OnHitStatuses.Length; i++)
            {
                plan.OnHitStatuses.Add(m_Definition.OnHitStatuses[i]);
            }

            return true;
        }

        public void Reset()
        {
            m_NextReadyTime = 0d;
        }
    }
}
