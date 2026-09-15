namespace VampireHunt.Run
{
    /// <summary>
    /// Server-side application service for one run. Future spawn, boss and reward
    /// modules call this service through the GameManager integration boundary.
    /// </summary>
    public sealed class RunApplicationService
    {
        private readonly RunRules m_Rules;
        private readonly RunClock m_Clock;
        private readonly RunStateMachine m_StateMachine = new RunStateMachine();
        private uint m_Revision;

        public RunPhase Phase => m_StateMachine.CurrentPhase;
        public uint Revision => m_Revision;

        public RunApplicationService(RunRules rules, double serverTime)
        {
            m_Rules = rules;
            m_Clock = new RunClock(rules.InitialDurationSeconds, serverTime);
        }

        public bool TryStartRun(double serverTime)
        {
            if (!m_StateMachine.TryTransition(RunPhase.Exploring)) return false;
            m_Clock.Reset(m_Rules.InitialDurationSeconds, serverTime);
            IncrementRevision();
            return true;
        }

        public bool TryTransition(RunPhase nextPhase, double serverTime)
        {
            if (!m_StateMachine.TryTransition(nextPhase)) return false;

            if (nextPhase.IsTerminal())
            {
                m_Clock.Pause(serverTime);
            }
            else if (nextPhase == RunPhase.Lobby)
            {
                m_Clock.Reset(m_Rules.InitialDurationSeconds, serverTime);
            }

            IncrementRevision();
            return true;
        }

        public bool Tick(double serverTime)
        {
            if (!Phase.ConsumesGlobalClock()) return false;
            if (m_Clock.GetRemainingSeconds(serverTime) > 0d) return false;

            return TryTransition(RunPhase.Defeat, serverTime);
        }

        public bool TryExtendClock(double seconds, double serverTime)
        {
            if (Phase.IsTerminal() || Phase == RunPhase.Lobby) return false;
            if (!m_Clock.Extend(seconds, serverTime)) return false;
            IncrementRevision();
            return true;
        }

        public bool TrySetDrainRate(double drainRate, double serverTime)
        {
            if (!Phase.ConsumesGlobalClock()) return false;
            if (!m_Clock.SetDrainRate(drainRate, serverTime)) return false;
            IncrementRevision();
            return true;
        }

        public bool TryPauseClock(double serverTime)
        {
            if (!Phase.ConsumesGlobalClock()) return false;
            if (!m_Clock.Pause(serverTime)) return false;
            IncrementRevision();
            return true;
        }

        public bool TryResumeClock(double serverTime)
        {
            if (!Phase.ConsumesGlobalClock()) return false;
            if (!m_Clock.Resume(serverTime)) return false;
            IncrementRevision();
            return true;
        }

        public RunSnapshot CaptureSnapshot(double serverTime)
        {
            double remaining = m_Clock.GetRemainingSeconds(serverTime);
            return new RunSnapshot(
                Phase,
                m_Clock.GetEndServerTime(serverTime),
                remaining,
                m_Clock.DrainRate,
                m_Clock.IsPaused,
                m_Revision);
        }

        private void IncrementRevision()
        {
            unchecked
            {
                m_Revision++;
            }
        }
    }
}
