using System;
using VampireHunt.Contracts;

namespace VampireHunt.Player.Abilities.Missile
{
    /// <summary>
    /// Pure data definition for the missile bomb weapon (White Pigeon / Peachone style).
    /// The elevation angle is carried on the cast plan's SpreadAngle, and the blast radius on
    /// ProjectileSize, so no new network fields are required.
    /// </summary>
    public sealed class MissileAbilityDefinition
    {
        public uint AbilityId { get; }
        public AbilitySlot Slot { get; }
        public DamageTags WeaponTag { get; }
        public float DamageMultiplier { get; }
        public float BaseCooldown { get; }
        public float Range { get; }
        public float ProjectileSpeed { get; }
        public int PierceCount { get; }
        public float ArcAngle { get; }
        public float BlastRadius { get; }
        public float Gravity { get; }
        public int ProjectileCount { get; }
        public float SpawnHeight { get; }
        public float SpawnForwardOffset { get; }
        public float KnockbackMultiplier { get; }
        public ElementId Element { get; }
        public float ElementMastery { get; }
        public StatusEffectSpec[] OnHitStatuses { get; }

        public MissileAbilityDefinition(
            uint abilityId,
            AbilitySlot slot,
            DamageTags weaponTag,
            float damageMultiplier,
            float baseCooldown,
            float range,
            float projectileSpeed,
            int pierceCount,
            float arcAngle,
            float blastRadius,
            float gravity,
            int projectileCount,
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
            BaseCooldown = Math.Max(0.01f, baseCooldown);
            Range = Math.Max(0.1f, range);
            ProjectileSpeed = Math.Max(0.1f, projectileSpeed);
            PierceCount = Math.Max(0, pierceCount);
            ArcAngle = Math.Max(0f, Math.Min(89.9f, arcAngle));
            BlastRadius = Math.Max(0.1f, blastRadius);
            Gravity = Math.Max(0.1f, gravity);
            ProjectileCount = Math.Max(1, projectileCount);
            SpawnHeight = spawnHeight;
            SpawnForwardOffset = Math.Max(0f, spawnForwardOffset);
            KnockbackMultiplier = Math.Max(0f, knockbackMultiplier);
            Element = element;
            ElementMastery = Math.Max(0f, elementMastery);
            OnHitStatuses = onHitStatuses ?? Array.Empty<StatusEffectSpec>();
        }
    }

    /// <summary>Pure cast-plan builder for the missile bomb. Elevation rides SpreadAngle, blast radius rides ProjectileSize.</summary>
    public sealed class MissileAbilityRuntime : ICombatAbility
    {
        private readonly MissileAbilityDefinition m_Definition;
        private double m_NextReadyTime;

        public uint AbilityId => m_Definition.AbilityId;
        public AbilitySlot Slot => m_Definition.Slot;

        public MissileAbilityRuntime(MissileAbilityDefinition definition)
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

            // Landing distance: use the aim ground point when available, else the max range.
            float maxRange = m_Definition.Range * context.RangeMultiplier;
            Float3 aimDelta = new Float3(
                context.AimPoint.X - context.Origin.X, 0f, context.AimPoint.Z - context.Origin.Z);
            float aimDistSqr = aimDelta.X * aimDelta.X + aimDelta.Z * aimDelta.Z;
            float distance = aimDistSqr > 0.25f ? Math.Min((float)Math.Sqrt(aimDistSqr), maxRange) : maxRange;

            // Launch speed so the parabola lands at `distance`: d = v^2 * sin(2θ) / g.
            float theta = m_Definition.ArcAngle * (float)(Math.PI / 180.0);
            float sin2 = Math.Max(0.1f, (float)Math.Sin(2.0 * theta));
            float speed = (float)Math.Sqrt(distance * m_Definition.Gravity / sin2);

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
                TravelDistance = distance,
                ProjectileSpeed = speed,
                Knockback = context.Knockback * m_Definition.KnockbackMultiplier,
                ProjectileSize = m_Definition.BlastRadius,   // repurposed: blast radius
                ProjectileCount = m_Definition.ProjectileCount,
                PierceCount = m_Definition.PierceCount,
                SpreadAngle = m_Definition.ArcAngle,          // repurposed: launch elevation
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
