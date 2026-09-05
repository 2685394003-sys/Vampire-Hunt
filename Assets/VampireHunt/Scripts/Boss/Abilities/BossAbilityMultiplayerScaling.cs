using System;

namespace VampireHunt.Boss.Abilities
{
    /// <summary>
    /// Designer-authored coefficients for one Boss ability. The authoritative server samples
    /// participant count once at cast start and turns this configuration into immutable cast data.
    /// A solo cast deliberately remains at x1 so existing ability assets keep their old balance.
    /// </summary>
    [Serializable]
    public sealed class BossAbilityMultiplayerScaling
    {
        public bool Enabled = true;
        public float DamageCoefficient = 1f;
        public float QuantityCoefficient = 1f;
        public float TelegraphCoefficient = 1f;
        public float CooldownCoefficient = 1f;

        public BossAbilityMultiplayerScaling CloneValidated()
        {
            return new BossAbilityMultiplayerScaling
            {
                Enabled = Enabled,
                DamageCoefficient = Validate(DamageCoefficient),
                QuantityCoefficient = Validate(QuantityCoefficient),
                TelegraphCoefficient = Validate(TelegraphCoefficient),
                CooldownCoefficient = Validate(CooldownCoefficient)
            };
        }

        public BossAbilityCastModifiers CreateModifiers(int participantCount)
        {
            int players = Math.Max(1, Math.Min(64, participantCount));
            if (players == 1) return BossAbilityCastModifiers.Solo;
            if (!Enabled) return new BossAbilityCastModifiers(players, 1f, 1, 1f, 1f);

            float quantityScale = players * Validate(QuantityCoefficient);
            int quantityUnits = Math.Max(1, Math.Min(64, (int)Math.Ceiling(quantityScale)));
            return new BossAbilityCastModifiers(
                players,
                players * Validate(DamageCoefficient),
                quantityUnits,
                players * Validate(TelegraphCoefficient),
                players * Validate(CooldownCoefficient));
        }

        private static float Validate(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 1f : Math.Max(0f, value);
    }

    /// <summary>Per-cast multiplayer values frozen by the server at cast start.</summary>
    public readonly struct BossAbilityCastModifiers
    {
        public static BossAbilityCastModifiers Solo =>
            new BossAbilityCastModifiers(1, 1f, 1, 1f, 1f);

        public int ParticipantCount { get; }
        public float DamageMultiplier { get; }
        public int QuantityUnits { get; }
        public float TelegraphMultiplier { get; }
        public float CooldownMultiplier { get; }

        public BossAbilityCastModifiers(
            int participantCount,
            float damageMultiplier,
            int quantityUnits,
            float telegraphMultiplier,
            float cooldownMultiplier)
        {
            ParticipantCount = Math.Max(1, Math.Min(64, participantCount));
            DamageMultiplier = Validate(damageMultiplier);
            QuantityUnits = Math.Max(1, Math.Min(64, quantityUnits));
            TelegraphMultiplier = Validate(telegraphMultiplier);
            CooldownMultiplier = Validate(cooldownMultiplier);
        }

        private static float Validate(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 1f : Math.Max(0f, value);
    }
}
