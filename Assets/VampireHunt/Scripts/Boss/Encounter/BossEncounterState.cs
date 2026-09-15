namespace VampireHunt.Boss.Encounter
{
    public enum BossEncounterState : byte
    {
        Dormant = 0,
        RoamingIdle = 1,
        RoamingEvade = 2,
        StaggerEffect = 3,
        ExecutionWindow = 4,
        Battle = 5,
        PhaseTransition = 6,
        Defeated = 7
    }

    public enum BossDamageOutcome : byte
    {
        Ignored = 0,
        GuardDamaged = 1,
        GuardBroken = 2,
        ExecutionTriggered = 3,
        HealthDamaged = 4,
        StageDefeated = 5,
        BossDefeated = 6
    }
}
