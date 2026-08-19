using System;
using System.Collections.Generic;
using VampireHunt.Core;

namespace VampireHunt.Player.Domain
{
    /// <summary>Immutable runtime copy produced by PlayerDefinitionSpecFactory.</summary>
    public readonly struct PlayerSpec
    {
        private readonly IReadOnlyDictionary<PlayerStat, float> baseValues;

        public int MaxHealth { get; }
        public float MaxStamina { get; }
        public float DashStaminaCost { get; }
        public float StaminaRecoveryPerSecond { get; }
        public double DashCooldown { get; }
        public double DashDuration { get; }
        public double DashSpeedMultiplier { get; }
        public double AttackInterval { get; }
        public float AttackConeAngle { get; }
        public float KnockbackForce { get; }
        public float KnockbackDuration { get; }
        public float StunDuration { get; }

        public PlayerSpec(
            int maxHealth,
            float maxStamina,
            float dashStaminaCost,
            float staminaRecoveryPerSecond,
            double dashCooldown,
            double dashDuration,
            double dashSpeedMultiplier,
            double attackInterval,
            IReadOnlyDictionary<PlayerStat, float> baseValues,
            float attackConeAngle = 110f,
            float knockbackForce = 5f,
            float knockbackDuration = 0.2f,
            float stunDuration = 0.2f)
        {
            MaxHealth = Math.Max(1, maxHealth);
            MaxStamina = Math.Max(0f, maxStamina);
            DashStaminaCost = Math.Max(0f, dashStaminaCost);
            StaminaRecoveryPerSecond = Math.Max(0f, staminaRecoveryPerSecond);
            DashCooldown = Math.Max(0d, dashCooldown);
            DashDuration = Math.Max(0d, dashDuration);
            DashSpeedMultiplier = Math.Max(1d, dashSpeedMultiplier);
            AttackInterval = Math.Max(0d, attackInterval);
            AttackConeAngle = ClampFinite(attackConeAngle, 0f, 180f, 110f);
            KnockbackForce = NonNegativeFinite(knockbackForce, 5f);
            KnockbackDuration = NonNegativeFinite(knockbackDuration, 0.2f);
            StunDuration = NonNegativeFinite(stunDuration, 0.2f);

            Dictionary<PlayerStat, float> copy = new();
            if (baseValues != null)
            {
                foreach (KeyValuePair<PlayerStat, float> pair in baseValues)
                {
                    if (!float.IsNaN(pair.Value) && !float.IsInfinity(pair.Value))
                        copy[pair.Key] = pair.Value;
                }
            }
            this.baseValues = copy;
        }

        internal PlayerAggregate CreateAggregate(EntityId id)
        {
            return new PlayerAggregate(
                id,
                new PlayerVitals(MaxHealth, invincibilityDuration: GetBaseValue(PlayerStat.InvincibleTime)),
                new PlayerRunStats(baseValues),
                new PlayerCombatState(AttackInterval),
                new PlayerMobilityState(
                    MaxStamina,
                    DashStaminaCost,
                    StaminaRecoveryPerSecond,
                    DashCooldown,
                    DashDuration,
                    DashSpeedMultiplier),
                new PlayerProgression(),
                new BloodPactLoadout());
        }

        public float GetBaseValue(PlayerStat stat) =>
            baseValues != null && baseValues.TryGetValue(stat, out float value) ? value : 0f;

        private static float NonNegativeFinite(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(0f, value);

        private static float ClampFinite(float value, float minimum, float maximum, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? fallback
                : value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
