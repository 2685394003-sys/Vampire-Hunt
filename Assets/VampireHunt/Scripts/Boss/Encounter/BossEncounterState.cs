namespace VampireHunt.Boss.Encounter
{
    /// <summary>
    /// 遭遇状态机。踉跄（StaggerEffect）结束后直接进入 Boss 战（Battle），
    /// 中间不再有处决窗口（ExecutionWindow 已移除：破盾即开战，无需玩家贴近）。
    /// </summary>
    public enum BossEncounterState : byte
    {
        Dormant = 0,
        RoamingIdle = 1,
        RoamingEvade = 2,
        StaggerEffect = 3,
        Battle = 4,
        PhaseTransition = 5,
        Defeated = 6
    }

    public enum BossDamageOutcome : byte
    {
        Ignored = 0,
        GuardDamaged = 1,
        GuardBroken = 2,
        HealthDamaged = 3,
        StageDefeated = 4,
        BossDefeated = 5
    }
}
