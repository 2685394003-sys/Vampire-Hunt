using System;

namespace VampireHunt.Run
{
    /// <summary>
    /// Replication/read-model snapshot. Active clocks are represented by an
    /// authoritative server deadline instead of a remaining value sent per frame.
    /// </summary>
    public readonly struct RunSnapshot
    {
        public RunPhase Phase { get; }
        public double EndServerTime { get; }
        public double PausedRemainingSeconds { get; }
        public double DrainRate { get; }
        public bool IsPaused { get; }
        public uint Revision { get; }

        public RunSnapshot(
            RunPhase phase,
            double endServerTime,
            double pausedRemainingSeconds,
            double drainRate,
            bool isPaused,
            uint revision)
        {
            Phase = phase;
            EndServerTime = endServerTime;
            PausedRemainingSeconds = Math.Max(0d, pausedRemainingSeconds);
            DrainRate = Math.Max(0d, drainRate);
            IsPaused = isPaused;
            Revision = revision;
        }

        public double GetRemainingSeconds(double estimatedServerTime)
        {
            if (IsPaused || DrainRate <= 0d) return PausedRemainingSeconds;
            return Math.Max(0d, (EndServerTime - estimatedServerTime) * DrainRate);
        }
    }
}
