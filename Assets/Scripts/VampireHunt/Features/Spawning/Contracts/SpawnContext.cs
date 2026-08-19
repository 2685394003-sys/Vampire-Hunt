using System;
using System.Collections.Generic;
using VampireHunt.Core;

namespace VampireHunt.Spawning.Contracts
{
    /// <summary>Server snapshot consumed by the sole EnemySpawnDirector.</summary>
    public sealed class SpawnContext
    {
        private readonly WorldPosition[] _targets;
        private readonly IReadOnlyList<WorldPosition> _targetView;

        public SpawnContext(
            double elapsedSeconds,
            int phase,
            IReadOnlyList<WorldPosition> targetPositions,
            int activeCount = 0)
            : this(elapsedSeconds, 0f, phase, targetPositions, activeCount)
        {
        }

        public SpawnContext(
            double elapsedSeconds,
            float deltaTime,
            int phase,
            IReadOnlyList<WorldPosition> targetPositions,
            int activeCount = 0)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (activeCount < 0) throw new ArgumentOutOfRangeException(nameof(activeCount));
            ElapsedSeconds = elapsedSeconds;
            DeltaTime = deltaTime;
            Phase = phase < 0 ? 0 : phase;
            ActiveCount = activeCount;
            _targets = Copy(targetPositions);
            _targetView = Array.AsReadOnly(_targets);
        }

        public double ElapsedSeconds { get; }
        public float DeltaTime { get; }
        public int Phase { get; }
        public int ActiveCount { get; }
        public IReadOnlyList<WorldPosition> TargetPositions => _targetView;

        private static WorldPosition[] Copy(IReadOnlyList<WorldPosition> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<WorldPosition>();
            WorldPosition[] copy = new WorldPosition[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }
    }
}
