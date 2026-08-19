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

        public PlayerSpec(
            int maxHealth,
            float maxStamina,
            float dashStaminaCost,
            float staminaRecoveryPerSecond,
            double dashCooldown,
            double dashDuration,
            double dashSpeedMultiplier,
            double attackInterval,
            IReadOnlyDictionary<PlayerStat, float> baseValues)
        {
            MaxHealth = Math.Max(1, maxHealth);
            MaxStamina = Math.Max(0f, maxStamina);
            DashStaminaCost = Math.Max(0f, dashStaminaCost);
            StaminaRecoveryPerSecond = Math.Max(0f, staminaRecoveryPerSecond);
            DashCooldown = Math.Max(0d, dashCooldown);
            DashDuration = Math.Max(0d, dashDuration);
            DashSpeedMultiplier = Math.Max(1d, dashSpeedMultiplier);
            AttackInterval = Math.Max(0d, attackInterval);

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

        public PlayerAggregate CreateAggregate(EntityId id)
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
    }
}
