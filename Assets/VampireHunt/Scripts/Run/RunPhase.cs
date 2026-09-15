namespace VampireHunt.Run
{
    /// <summary>Authoritative high-level phase of one VampireHunt run.</summary>
    public enum RunPhase : byte
    {
        Lobby = 0,
        Exploring = 1,
        BossEncounter = 2,
        BossPhaseTransition = 3,
        Victory = 4,
        Defeat = 5
    }

    public static class RunPhaseExtensions
    {
        public static bool IsTerminal(this RunPhase phase)
        {
            return phase == RunPhase.Victory || phase == RunPhase.Defeat;
        }

        public static bool ConsumesGlobalClock(this RunPhase phase)
        {
            return phase == RunPhase.Exploring ||
                   phase == RunPhase.BossEncounter ||
                   phase == RunPhase.BossPhaseTransition;
        }
    }
}
