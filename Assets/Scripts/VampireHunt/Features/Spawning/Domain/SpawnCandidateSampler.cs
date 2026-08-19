using System;
using VampireHunt.Core;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Spawning.Domain
{
    /// <summary>Chooses legal positions through the world-query port only.</summary>
    public sealed class SpawnCandidateSampler
    {
        private readonly ISpawnLocationQuery locations;
        private readonly IRandomSource random;
        private readonly int maxAttempts;

        public SpawnCandidateSampler(ISpawnLocationQuery locations, IRandomSource random = null, int maxAttempts = 8)
        {
            this.locations = locations ?? throw new ArgumentNullException(nameof(locations));
            this.random = random;
            if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            this.maxAttempts = maxAttempts;
        }

        public bool TrySample(SpawnContext context, out SpawnCandidate candidate)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.TargetPositions.Count == 0)
            {
                candidate = SpawnCandidate.Invalid;
                return false;
            }

            int count = context.TargetPositions.Count;
            int start = random == null ? 0 : random.NextInt(0, count);
            int attempts = maxAttempts;
            for (int i = 0; i < attempts; i++)
            {
                WorldPosition target = context.TargetPositions[(start + i) % count];
                WorldPosition sample = locations.SampleAround(target);
                if (!locations.IsValid(sample)) continue;
                candidate = new SpawnCandidate(sample, target, true);
                return true;
            }

            candidate = SpawnCandidate.Invalid;
            return false;
        }

        public SpawnCandidate TrySample(SpawnContext context)
        {
            return TrySample(context, out SpawnCandidate candidate) ? candidate : SpawnCandidate.Invalid;
        }

        public bool TrySample(SpawnContext context, out WorldPosition position)
        {
            if (TrySample(context, out SpawnCandidate candidate))
            {
                position = candidate.Position;
                return true;
            }

            position = default(WorldPosition);
            return false;
        }
    }
}
