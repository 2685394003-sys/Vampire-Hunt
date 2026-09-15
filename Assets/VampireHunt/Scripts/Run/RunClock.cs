using System;

namespace VampireHunt.Run
{
    /// <summary>
    /// Pure server-time clock. It stores an anchored remaining value so rate
    /// changes, extensions and pauses do not accumulate frame-dependent drift.
    /// </summary>
    public sealed class RunClock
    {
        private double m_RemainingAtAnchorSeconds;
        private double m_AnchorServerTime;
        private double m_DrainRate = 1d;

        public bool IsPaused { get; private set; }
        public double DrainRate => m_DrainRate;

        public RunClock(double durationSeconds, double serverTime)
        {
            Reset(durationSeconds, serverTime);
        }

        public void Reset(double durationSeconds, double serverTime)
        {
            ValidateFinitePositive(durationSeconds, nameof(durationSeconds));
            ValidateFinite(serverTime, nameof(serverTime));

            m_RemainingAtAnchorSeconds = durationSeconds;
            m_AnchorServerTime = serverTime;
            m_DrainRate = 1d;
            IsPaused = false;
        }

        public double GetRemainingSeconds(double serverTime)
        {
            ValidateFinite(serverTime, nameof(serverTime));
            if (IsPaused) return m_RemainingAtAnchorSeconds;

            double elapsed = Math.Max(0d, serverTime - m_AnchorServerTime);
            return Math.Max(0d, m_RemainingAtAnchorSeconds - elapsed * m_DrainRate);
        }

        public double GetEndServerTime(double serverTime)
        {
            double remaining = GetRemainingSeconds(serverTime);
            return IsPaused || m_DrainRate <= 0d
                ? 0d
                : serverTime + remaining / m_DrainRate;
        }

        public bool Extend(double seconds, double serverTime)
        {
            if (!IsFinite(seconds) || seconds <= 0d) return false;
            Capture(serverTime);
            m_RemainingAtAnchorSeconds += seconds;
            return true;
        }

        public bool SetDrainRate(double drainRate, double serverTime)
        {
            if (!IsFinite(drainRate) || drainRate <= 0d) return false;
            if (Math.Abs(m_DrainRate - drainRate) < 0.000001d) return false;

            Capture(serverTime);
            m_DrainRate = drainRate;
            return true;
        }

        public bool Pause(double serverTime)
        {
            if (IsPaused) return false;
            Capture(serverTime);
            IsPaused = true;
            return true;
        }

        public bool Resume(double serverTime)
        {
            if (!IsPaused) return false;
            ValidateFinite(serverTime, nameof(serverTime));
            m_AnchorServerTime = serverTime;
            IsPaused = false;
            return true;
        }

        private void Capture(double serverTime)
        {
            m_RemainingAtAnchorSeconds = GetRemainingSeconds(serverTime);
            m_AnchorServerTime = serverTime;
        }

        private static void ValidateFinitePositive(double value, string parameterName)
        {
            if (!IsFinite(value) || value <= 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateFinite(double value, string parameterName)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
