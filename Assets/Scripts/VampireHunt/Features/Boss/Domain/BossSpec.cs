using System;
using System.Collections;
using System.Collections.Generic;
using VampireHunt.Boss.Contracts;

namespace VampireHunt.Boss.Domain
{
    public readonly struct BossPhaseSpec
    {
        public BossPhase Phase { get; }
        public float EnterAtHealthRatio { get; }

        public BossPhaseSpec(BossPhase phase, float enterAtHealthRatio)
        {
            if (phase == BossPhase.Dormant)
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (float.IsNaN(enterAtHealthRatio) || enterAtHealthRatio <= 0f || enterAtHealthRatio > 1f)
                throw new ArgumentOutOfRangeException(nameof(enterAtHealthRatio));

            Phase = phase;
            EnterAtHealthRatio = enterAtHealthRatio;
        }
    }

    public sealed class PhaseSpecSet : IReadOnlyList<BossPhaseSpec>
    {
        private readonly BossPhaseSpec[] items;

        public int Count => items.Length;
        public BossPhaseSpec this[int index] => items[index];

        public PhaseSpecSet(IEnumerable<BossPhaseSpec> phases)
        {
            if (phases == null) throw new ArgumentNullException(nameof(phases));
            List<BossPhaseSpec> copy = new(phases);
            if (copy.Count == 0) throw new ArgumentException("At least one Boss phase is required.", nameof(phases));
            copy.Sort((left, right) => right.EnterAtHealthRatio.CompareTo(left.EnterAtHealthRatio));

            HashSet<BossPhase> ids = new();
            float previousThreshold = float.PositiveInfinity;
            for (int i = 0; i < copy.Count; i++)
            {
                BossPhaseSpec phase = copy[i];
                if (!ids.Add(phase.Phase))
                    throw new ArgumentException($"Duplicate Boss phase '{phase.Phase}'.", nameof(phases));
                if (phase.EnterAtHealthRatio >= previousThreshold)
                    throw new ArgumentException("Boss phase thresholds must be unique and descending.", nameof(phases));
                previousThreshold = phase.EnterAtHealthRatio;
            }

            items = copy.ToArray();
        }

        public BossPhase Resolve(float healthRatio)
        {
            float ratio = Clamp01(healthRatio);
            BossPhase result = items[0].Phase;
            for (int i = 0; i < items.Length; i++)
            {
                if (ratio <= items[i].EnterAtHealthRatio)
                    result = items[i].Phase;
            }
            return result;
        }

        public IEnumerator<BossPhaseSpec> GetEnumerator() => ((IEnumerable<BossPhaseSpec>)items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0f) return 0f;
            return value >= 1f ? 1f : value;
        }
    }

    public readonly struct BossAttackSpec
    {
        public BossAttackId Id { get; }
        public float Cooldown { get; }
        public float Weight { get; }
        public int Damage { get; }
        public BossPhase MinimumPhase { get; }
        public float TelegraphSeconds { get; }
        public float ActiveSeconds { get; }
        public float Range { get; }
        public float Width { get; }
        public float Knockback { get; }
        public int ProjectileCount { get; }
        public float ProjectileSpeed { get; }
        public PresentationCueId Cue { get; }

        public BossAttackSpec(
            BossAttackId id,
            float cooldown,
            float weight,
            int damage,
            BossPhase minimumPhase,
            float telegraphSeconds,
            float activeSeconds,
            float range,
            float width,
            float knockback = 0f,
            int projectileCount = 0,
            float projectileSpeed = 0f,
            PresentationCueId cue = default)
        {
            if (id == BossAttackId.None) throw new ArgumentOutOfRangeException(nameof(id));
            if (cooldown < 0f || float.IsNaN(cooldown)) throw new ArgumentOutOfRangeException(nameof(cooldown));
            if (weight < 0f || float.IsNaN(weight)) throw new ArgumentOutOfRangeException(nameof(weight));
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (telegraphSeconds < 0f || float.IsNaN(telegraphSeconds)) throw new ArgumentOutOfRangeException(nameof(telegraphSeconds));
            if (activeSeconds < 0f || float.IsNaN(activeSeconds)) throw new ArgumentOutOfRangeException(nameof(activeSeconds));
            if (range < 0f || width < 0f || float.IsNaN(range) || float.IsNaN(width))
                throw new ArgumentOutOfRangeException(nameof(range));
            if (knockback < 0f || projectileCount < 0 || projectileSpeed < 0f)
                throw new ArgumentOutOfRangeException(nameof(knockback));

            Id = id;
            Cooldown = cooldown;
            Weight = weight;
            Damage = damage;
            MinimumPhase = minimumPhase;
            TelegraphSeconds = telegraphSeconds;
            ActiveSeconds = activeSeconds;
            Range = range;
            Width = width;
            Knockback = knockback;
            ProjectileCount = projectileCount;
            ProjectileSpeed = projectileSpeed;
            Cue = cue;
        }
    }

    public sealed class BossAttackSpecSet : IReadOnlyList<BossAttackSpec>
    {
        private readonly BossAttackSpec[] items;
        private readonly Dictionary<BossAttackId, BossAttackSpec> byId;

        public int Count => items.Length;
        public BossAttackSpec this[int index] => items[index];

        public BossAttackSpecSet(IEnumerable<BossAttackSpec> attacks)
        {
            if (attacks == null) throw new ArgumentNullException(nameof(attacks));
            items = new List<BossAttackSpec>(attacks).ToArray();
            if (items.Length == 0) throw new ArgumentException("At least one Boss attack is required.", nameof(attacks));
            byId = new Dictionary<BossAttackId, BossAttackSpec>(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                if (!byId.TryAdd(items[i].Id, items[i]))
                    throw new ArgumentException($"Duplicate Boss attack '{items[i].Id}'.", nameof(attacks));
            }
        }

        public bool TryGet(BossAttackId id, out BossAttackSpec spec) => byId.TryGetValue(id, out spec);
        public BossAttackSpec Get(BossAttackId id) => byId.TryGetValue(id, out BossAttackSpec value)
            ? value
            : throw new KeyNotFoundException($"Boss attack '{id}' is not configured.");

        public IEnumerator<BossAttackSpec> GetEnumerator() => ((IEnumerable<BossAttackSpec>)items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
    }

    public sealed class BossSpec
    {
        public int MaxHealth { get; }
        public PhaseSpecSet Phases { get; }
        public BossAttackSpecSet Attacks { get; }
        public int GuardIntegrity { get; }
        public float GuardDamageReduction { get; }
        public float StaggerSeconds { get; }
        public float ContractTriggerHealthRatio { get; }
        public float ContractSeconds { get; }
        public float ContractCountdownRate { get; }

        public BossSpec(
            int maxHealth,
            PhaseSpecSet phases,
            BossAttackSpecSet attacks,
            int guardIntegrity = 0,
            float guardDamageReduction = 0f,
            float staggerSeconds = 4f,
            float contractTriggerHealthRatio = 0.2f,
            float contractSeconds = 30f,
            float contractCountdownRate = 2f)
        {
            if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            if (guardIntegrity < 0) throw new ArgumentOutOfRangeException(nameof(guardIntegrity));
            if (guardDamageReduction < 0f || guardDamageReduction > 1f || float.IsNaN(guardDamageReduction))
                throw new ArgumentOutOfRangeException(nameof(guardDamageReduction));
            if (staggerSeconds <= 0f || contractSeconds < 0f || contractCountdownRate <= 0f)
                throw new ArgumentOutOfRangeException(nameof(staggerSeconds));
            if (contractTriggerHealthRatio <= 0f || contractTriggerHealthRatio >= 1f)
                throw new ArgumentOutOfRangeException(nameof(contractTriggerHealthRatio));

            MaxHealth = maxHealth;
            Phases = phases ?? throw new ArgumentNullException(nameof(phases));
            Attacks = attacks ?? throw new ArgumentNullException(nameof(attacks));
            GuardIntegrity = guardIntegrity;
            GuardDamageReduction = guardDamageReduction;
            StaggerSeconds = staggerSeconds;
            ContractTriggerHealthRatio = contractTriggerHealthRatio;
            ContractSeconds = contractSeconds;
            ContractCountdownRate = contractCountdownRate;
        }
    }
}
