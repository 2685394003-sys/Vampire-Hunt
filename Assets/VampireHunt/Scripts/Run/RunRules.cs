using System;

namespace VampireHunt.Run
{
    /// <summary>Validated immutable rules used to create a run.</summary>
    public sealed class RunRules
    {
        public double InitialDurationSeconds { get; }
        public int MinimumPlayersToStart { get; }

        public RunRules(double initialDurationSeconds, int minimumPlayersToStart)
        {
            if (double.IsNaN(initialDurationSeconds) ||
                double.IsInfinity(initialDurationSeconds) ||
                initialDurationSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(initialDurationSeconds));
            }

            if (minimumPlayersToStart < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumPlayersToStart));
            }

            InitialDurationSeconds = initialDurationSeconds;
            MinimumPlayersToStart = minimumPlayersToStart;
        }
    }
}
