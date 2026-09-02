using System;
using VampireHunt.Contracts;

namespace VampireHunt.Player.Abilities.SwordWave
{
    public sealed class SwordWaveAbilityDefinition
    {
        public uint AbilityId { get; }
        public AbilitySlot Slot { get; }
        public float BaseCooldown { get; }
        public float BaseTravelDistance { get; }
        public float ProjectileSpeed { get; }
        public float ProjectileSize { get; }
        public int ProjectileCount { get; }
        public int PierceCount { get; }
        public float SpreadAngle { get; }
        /// <summary>扇形张开全角（度）：每颗剑气弹丸自身的判定/视觉角度（与 SpreadAngle 排布散布解耦）。</summary>
        public float FanAngle { get; }
        public float SpawnForwardOffset { get; }
        public float SpawnHeight { get; }
        public float KnockbackMultiplier { get; }
        public ElementId Element { get; }
        public float ElementMastery { get; }
        public StatusEffectSpec[] OnHitStatuses { get; }

        public SwordWaveAbilityDefinition(
            uint abilityId,
            AbilitySlot slot,
            float baseCooldown,
            float baseTravelDistance,
            float projectileSpeed,
            float projectileSize,
            int projectileCount,
            int pierceCount,
            float spreadAngle,
            float fanAngle,
            float spawnForwardOffset,
            float spawnHeight,
            float knockbackMultiplier,
            ElementId element,
            float elementMastery,
            StatusEffectSpec[] onHitStatuses)
        {
            AbilityId = abilityId;
            Slot = slot;
            BaseCooldown = Math.Max(0.01f, baseCooldown);
            BaseTravelDistance = Math.Max(0.1f, baseTravelDistance);
            ProjectileSpeed = Math.Max(0.1f, projectileSpeed);
            ProjectileSize = Math.Max(0.1f, projectileSize);
            ProjectileCount = Math.Max(1, projectileCount);
            PierceCount = Math.Max(1, pierceCount);
            SpreadAngle = Math.Max(0f, spreadAngle);
            FanAngle = Math.Max(0f, fanAngle);
            SpawnForwardOffset = Math.Max(0f, spawnForwardOffset);
            SpawnHeight = spawnHeight;
            KnockbackMultiplier = Math.Max(0f, knockbackMultiplier);
            Element = element;
            ElementMastery = Math.Max(0f, elementMastery);
            OnHitStatuses = onHitStatuses ?? Array.Empty<StatusEffectSpec>();
        }
    }

    public sealed class SwordWaveAbilityRuntime : ICombatAbility
    {
        private readonly SwordWaveAbilityDefinition m_Definition;
        private double m_NextReadyTime;

        public uint AbilityId => m_Definition.AbilityId;
        public AbilitySlot Slot => m_Definition.Slot;

        public SwordWaveAbilityRuntime(SwordWaveAbilityDefinition definition)
        {
            m_Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public bool TryBuildCast(in CombatAbilityActivationContext context, out AbilityCastPlan plan)
        {
            plan = null;
            if (context.Time < m_NextReadyTime || context.Damage <= 0f) return false;

            Float3 direction = context.Forward.Normalized();
            if (direction.SqrMagnitude <= 0.0001f) return false;

            float damage = context.Damage;
            DamageTags tags = DamageTags.Projectile | DamageTags.SwordWave;
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
                FanAngle = m_Definition.FanAngle,
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
