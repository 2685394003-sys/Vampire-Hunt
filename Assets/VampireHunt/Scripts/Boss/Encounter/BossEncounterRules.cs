using System;

namespace VampireHunt.Boss.Encounter
{
    public sealed class BossStageRules
    {
        public float GuardHealth { get; }
        public float BattleHealth { get; }
        public int AbilityPhaseNumber { get; }

        public BossStageRules(float guardHealth, float battleHealth, int abilityPhaseNumber)
        {
            GuardHealth = Math.Max(1f, guardHealth);
            BattleHealth = Math.Max(1f, battleHealth);
            AbilityPhaseNumber = Math.Max(1, abilityPhaseNumber);
        }
    }

    public sealed class BossEncounterRules
    {
        public BossStageRules[] Stages { get; }

        public BossEncounterRules(BossStageRules[] stages)
        {
            if (stages == null || stages.Length != 3)
                throw new ArgumentException("A Boss encounter requires exactly three stage rule rows.", nameof(stages));
            Stages = new BossStageRules[stages.Length];
            for (int i = 0; i < stages.Length; i++)
                Stages[i] = stages[i] ?? throw new ArgumentException($"Boss stage {i + 1} is missing.", nameof(stages));
        }
    }
}
