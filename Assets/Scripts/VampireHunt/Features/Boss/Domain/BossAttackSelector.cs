using System;
using System.Collections.Generic;
using VampireHunt.Boss.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Boss.Domain
{
    internal readonly struct BossAttackSelectionContext
    {
        public BossPhase Phase { get; }
        public BossAttackState State { get; }

        internal BossAttackSelectionContext(BossPhase phase, BossAttackState state)
        {
            Phase = phase;
            State = state;
        }
    }

    internal sealed class BossAttackSelector
    {
        private readonly BossAttackSpecSet specs;
        private readonly IRandomSource random;
        private readonly List<BossAttackSpec> candidates = new();

        public BossAttackSelector(BossAttackSpecSet specs, IRandomSource random)
        {
            this.specs = specs ?? throw new ArgumentNullException(nameof(specs));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public BossAttackId Select(BossAttackSelectionContext context)
        {
            if (context.State == null || context.State.IsExecuting)
                return BossAttackId.None;

            candidates.Clear();
            float totalWeight = 0f;
            for (int i = 0; i < specs.Count; i++)
            {
                BossAttackSpec spec = specs[i];
                if ((int)context.Phase < (int)spec.MinimumPhase ||
                    !context.State.Cooldowns.IsReady(spec.Id) || spec.Weight <= 0f)
                    continue;
                candidates.Add(spec);
                totalWeight += spec.Weight;
            }

            if (candidates.Count == 0 || totalWeight <= 0f)
                return BossAttackId.None;

            float roll = Clamp01(random.NextFloat()) * totalWeight;
            float cumulative = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += candidates[i].Weight;
                if (roll <= cumulative)
                    return candidates[i].Id;
            }
            return candidates[candidates.Count - 1].Id;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value <= 0f) return 0f;
            return value >= 1f ? 0.999999f : value;
        }
    }
}
