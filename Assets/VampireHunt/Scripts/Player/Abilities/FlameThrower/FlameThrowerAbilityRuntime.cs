using System;
using VampireHunt.Contracts;

namespace VampireHunt.Player.Abilities.FlameThrower
{
    /// <summary>
    /// Pure data definition for a short-range cone flamethrower. Resolved each tick as a
    /// forward cone (angle = <see cref="ConeAngle"/>) over an overlap sphere on the server.
    /// The flame is fixed Fire element and applies burning (Burn) on hit.
    /// </summary>
    public sealed class FlameThrowerAbilityDefinition
    {
        public uint AbilityId { get; }
        public AbilitySlot Slot { get; }
        public DamageTags WeaponTag { get; }
        public float DamageMultiplier { get; }
        public float TickInterval { get; }
        public float Range { get; }
        public float ConeAngle { get; }
        public float SpawnHeight { get; }
        public float SpawnForwardOffset { get; }
        public ElementId Element { get; }
        public StatusEffectSpec[] OnHitStatuses { get; }

        public FlameThrowerAbilityDefinition(
            uint abilityId,
            AbilitySlot slot,
            DamageTags weaponTag,
            float damageMultiplier,
            float tickInterval,
            float range,
            float coneAngle,
            float spawnHeight,
            float spawnForwardOffset,
            ElementId element,
            StatusEffectSpec[] onHitStatuses)
        {
            AbilityId = abilityId;
            Slot = slot;
            WeaponTag = weaponTag;
            DamageMultiplier = Math.Max(0f, damageMultiplier);
            TickInterval = Math.Max(0.01f, tickInterval);
            Range = Math.Max(0.1f, range);
            ConeAngle = Math.Max(0f, Math.Min(180f, coneAngle));
            SpawnHeight = spawnHeight;
            SpawnForwardOffset = Math.Max(0f, spawnForwardOffset);
            Element = element;
            OnHitStatuses = onHitStatuses ?? Array.Empty<StatusEffectSpec>();
        }
    }

    /// <summary>Pure cast-plan builder for the cone flamethrower. Cone angle rides the SpreadAngle field.</summary>
    public sealed class FlameThrowerAbilityRuntime : ICombatAbility
    {
        private readonly FlameThrowerAbilityDefinition m_Definition;
        private double m_NextReadyTime;

        public uint AbilityId => m_Definition.AbilityId;
        public AbilitySlot Slot => m_Definition.Slot;

        public FlameThrowerAbilityRuntime(FlameThrowerAbilityDefinition definition)
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
                Knockback = context.Knockback,
                ProjectileSize = m_Definition.ConeAngle * 0.5f, // half-angle reused as size hint
                ProjectileCount = 1,
                PierceCount = 1,
                SpreadAngle = m_Definition.ConeAngle,
                Tags = tags,
                Element = m_Definition.Element
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
