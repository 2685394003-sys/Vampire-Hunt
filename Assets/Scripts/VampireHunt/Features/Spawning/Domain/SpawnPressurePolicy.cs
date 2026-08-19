using System;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Spawning.Domain
{
    /// <summary>
    /// Converts elapsed run time and encounter phase into a bounded spawn
    /// request. It never samples a position or instantiates an enemy.
    /// </summary>
    public sealed class SpawnPressurePolicy
    {
        private readonly EnemySpawnSpec spec;
        private readonly int phaseBonus;

        public SpawnPressurePolicy()
            : this(new EnemySpawnSpec("default", int.MaxValue, 0, 1f))
        {
        }

        public SpawnPressurePolicy(EnemySpawnSpec spec, int phaseBonus = 0)
        {
            this.spec = spec ?? throw new ArgumentNullException(nameof(spec));
            this.phaseBonus = Math.Max(0, phaseBonus);
        }

        public SpawnBudget CalculateBudget(double elapsedSeconds, int phase)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            phase = Math.Max(0, phase);
            double minutes = elapsedSeconds / 60d;
            double raw = spec.BaseSpawnsPerInterval + spec.GrowthPerMinute * minutes + phase * phaseBonus;
            int requested = raw >= int.MaxValue ? int.MaxValue : Math.Max(0, (int)Math.Floor(raw));
            requested = Math.Min(requested, spec.MaxSpawnsPerTick);
            return new SpawnBudget(requested, spec.MaxAlive, requested);
        }
    }
}
