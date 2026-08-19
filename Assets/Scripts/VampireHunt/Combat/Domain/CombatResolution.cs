using System;
using System.Collections.Generic;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Stats;

namespace VampireHunt.Combat.Domain
{
    /// <summary>Reserved shared keys used by generic combat calculations.</summary>
    public static class CombatStatKeys
    {
        // PlayerRunStats uses the stable serialized PlayerStat ordinal plus one
        // for shared keys (CritRate=9 -> key 10, CritDamage=10 -> key 11).
        public static readonly StatKey CriticalChance = new StatKey(10);
        public static readonly StatKey CriticalMultiplier = new StatKey(11);
        // These generic target defenses are intentionally outside the current
        // player key range so an absent defense reads as zero, not another stat.
        public static readonly StatKey KnockbackResistance = new StatKey(100);
        public static readonly StatKey KnockbackImmunity = new StatKey(101);
    }

    public sealed class CombatResolver
    {
        private readonly IRandomSource random;
        private readonly StatKey criticalChanceKey;
        private readonly StatKey criticalMultiplierKey;

        public CombatResolver(IRandomSource randomSource)
            : this(randomSource, CombatStatKeys.CriticalChance, CombatStatKeys.CriticalMultiplier) { }

        public CombatResolver(
            IRandomSource randomSource,
            StatKey criticalChanceKey,
            StatKey criticalMultiplierKey)
        {
            random = randomSource ?? throw new ArgumentNullException(nameof(randomSource));
            if (!criticalChanceKey.IsValid) throw new ArgumentException("A valid critical chance key is required.", nameof(criticalChanceKey));
            if (!criticalMultiplierKey.IsValid) throw new ArgumentException("A valid critical multiplier key is required.", nameof(criticalMultiplierKey));
            this.criticalChanceKey = criticalChanceKey;
            this.criticalMultiplierKey = criticalMultiplierKey;
        }

        public ResolvedDamage Resolve(in DamageRequest request, IStatSnapshot sourceStats)
        {
            float chance = GetOptionalStat(sourceStats, criticalChanceKey, 0f);
            float multiplier = GetOptionalStat(sourceStats, criticalMultiplierKey, 1f);
            chance = Clamp01(chance);
            if (multiplier < 0f || float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                throw new ArgumentOutOfRangeException(nameof(sourceStats), "Critical multiplier must be finite and non-negative.");

            bool canCritical = (request.Flags & (DamageFlags.NoCritical | DamageFlags.Periodic)) == 0;
            bool wasCritical = canCritical && chance > 0f &&
                               (chance >= 1f || random.NextFloat() < chance);
            int finalDamage = request.BaseDamage;
            if (wasCritical)
                finalDamage = ToNonNegativeInt(finalDamage * multiplier);

            return new ResolvedDamage(
                request.SourceId,
                request.TargetId,
                finalDamage,
                wasCritical,
                request.Hit);
        }

        public ResolvedDamage Resolve(DamageRequest request, IStatSnapshot sourceStats) =>
            Resolve(in request, sourceStats);

        private static float GetOptionalStat(IStatSnapshot snapshot, StatKey key, float fallback)
        {
            if (snapshot == null) return fallback;
            try
            {
                float value = snapshot.GetValue(key);
                return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
            }
            catch (KeyNotFoundException)
            {
                return fallback;
            }
            catch (ArgumentException)
            {
                return fallback;
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static int ToNonNegativeInt(float value)
        {
            if (value <= 0f) return 0;
            if (float.IsNaN(value) || float.IsInfinity(value) || value >= int.MaxValue)
                return int.MaxValue;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }

    public sealed class KnockbackResolver
    {
        public KnockbackImpulse Resolve(in KnockbackRequest request, IStatSnapshot targetStats)
        {
            float resistance = GetOptionalStat(targetStats, CombatStatKeys.KnockbackResistance, 0f);
            bool immune = GetOptionalStat(targetStats, CombatStatKeys.KnockbackImmunity, 0f) >= 0.5f;
            return Resolve(in request, resistance, immune);
        }

        public KnockbackImpulse Resolve(KnockbackRequest request, IStatSnapshot targetStats) =>
            Resolve(in request, targetStats);

        public KnockbackImpulse Resolve(in KnockbackRequest request, float resistance, bool isImmune = false)
        {
            if (float.IsNaN(resistance) || float.IsInfinity(resistance))
                throw new ArgumentOutOfRangeException(nameof(resistance));
            if (resistance < 0f) resistance = 0f;
            if (resistance > 1f) resistance = 1f;
            float effectiveForce = isImmune ? 0f : request.Force * (1f - resistance);
            return new KnockbackImpulse(
                request.SourceId,
                request.TargetId,
                request.Direction.Normalized,
                effectiveForce,
                request.Duration,
                isImmune);
        }

        private static float GetOptionalStat(IStatSnapshot snapshot, StatKey key, float fallback)
        {
            if (snapshot == null) return fallback;
            try
            {
                float value = snapshot.GetValue(key);
                return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
            }
            catch (KeyNotFoundException)
            {
                return fallback;
            }
            catch (ArgumentException)
            {
                return fallback;
            }
        }
    }
}
