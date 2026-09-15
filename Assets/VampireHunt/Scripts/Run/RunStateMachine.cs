namespace VampireHunt.Run
{
    /// <summary>Owns legal high-level run transitions without Unity dependencies.</summary>
    public sealed class RunStateMachine
    {
        public RunPhase CurrentPhase { get; private set; } = RunPhase.Lobby;

        public bool TryTransition(RunPhase nextPhase)
        {
            if (!CanTransition(nextPhase)) return false;
            CurrentPhase = nextPhase;
            return true;
        }

        public bool CanTransition(RunPhase nextPhase)
        {
            if (nextPhase == CurrentPhase) return false;

            switch (CurrentPhase)
            {
                case RunPhase.Lobby:
                    return nextPhase == RunPhase.Exploring;

                case RunPhase.Exploring:
                    return nextPhase == RunPhase.BossEncounter ||
                           nextPhase == RunPhase.Victory ||
                           nextPhase == RunPhase.Defeat;

                case RunPhase.BossEncounter:
                    return nextPhase == RunPhase.BossPhaseTransition ||
                           nextPhase == RunPhase.Victory ||
                           nextPhase == RunPhase.Defeat;

                case RunPhase.BossPhaseTransition:
                    return nextPhase == RunPhase.BossEncounter ||
                           nextPhase == RunPhase.Exploring ||
                           nextPhase == RunPhase.Victory ||
                           nextPhase == RunPhase.Defeat;

                case RunPhase.Victory:
                case RunPhase.Defeat:
                    return nextPhase == RunPhase.Lobby;

                default:
                    return false;
            }
        }
    }
}
