using System;

namespace VampireHunt.Boss.Encounter
{
    public readonly struct BossEncounterSnapshot
    {
        public BossEncounterState State { get; }
        public int StageNumber { get; }
        public float GuardHealth { get; }
        public float MaxGuardHealth { get; }
        public float Health { get; }
        public float MaxHealth { get; }
        public bool HudVisible { get; }
        public uint Revision { get; }

        public BossEncounterSnapshot(BossEncounterState state, int stageNumber,
            float guardHealth, float maxGuardHealth, float health, float maxHealth,
            bool hudVisible, uint revision)
        {
            State = state;
            StageNumber = stageNumber;
            GuardHealth = guardHealth;
            MaxGuardHealth = maxGuardHealth;
            Health = health;
            MaxHealth = maxHealth;
            HudVisible = hudVisible;
            Revision = revision;
        }
    }

    /// <summary>
    /// Pure authoritative Boss encounter model. It knows no GameObject, UI, VFX or Netcode.
    /// </summary>
    public sealed class BossEncounterAggregate
    {
        private readonly BossEncounterRules m_Rules;
        private int m_StageIndex;

        public BossEncounterState State { get; private set; } = BossEncounterState.Dormant;
        public int StageNumber => m_StageIndex + 1;
        public float GuardHealth { get; private set; }
        public float MaxGuardHealth => CurrentRules.GuardHealth;
        public float Health { get; private set; }
        public float MaxHealth => CurrentRules.BattleHealth;
        public bool HudVisible { get; private set; }
        public uint Revision { get; private set; }
        public BossStageRules CurrentRules => m_Rules.Stages[m_StageIndex];

        public BossEncounterAggregate(BossEncounterRules rules)
        {
            m_Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            LoadStage(0);
        }

        public bool BeginRun()
        {
            if (State != BossEncounterState.Dormant) return false;
            SetState(BossEncounterState.RoamingIdle);
            return true;
        }

        public bool SetEngaged(bool visible = true)
        {
            if (HudVisible == visible) return false;
            HudVisible = visible;
            IncrementRevision();
            return true;
        }

        public bool SetRoamingEvade(bool evade)
        {
            if (State != BossEncounterState.RoamingIdle && State != BossEncounterState.RoamingEvade) return false;
            BossEncounterState next = evade ? BossEncounterState.RoamingEvade : BossEncounterState.RoamingIdle;
            if (State == next) return false;
            SetState(next);
            return true;
        }

        public BossDamageOutcome ApplyDamage(float amount, float attackerDistance)
        {
            amount = Math.Max(0f, amount);
            if (amount <= 0f || State == BossEncounterState.Dormant ||
                State == BossEncounterState.StaggerEffect || State == BossEncounterState.PhaseTransition ||
                State == BossEncounterState.Defeated) return BossDamageOutcome.Ignored;

            SetEngaged();
            if (State == BossEncounterState.RoamingIdle || State == BossEncounterState.RoamingEvade)
            {
                if (GuardHealth <= 0f) return TryBeginStagger(attackerDistance)
                    ? BossDamageOutcome.GuardBroken
                    : BossDamageOutcome.Ignored;
                GuardHealth = Math.Max(0f, GuardHealth - amount);
                IncrementRevision();
                if (GuardHealth > 0f) return BossDamageOutcome.GuardDamaged;
                TryBeginStagger(attackerDistance);
                return BossDamageOutcome.GuardBroken;
            }

            if (State == BossEncounterState.ExecutionWindow)
            {
                SetState(BossEncounterState.Battle);
                return BossDamageOutcome.ExecutionTriggered;
            }

            if (State != BossEncounterState.Battle) return BossDamageOutcome.Ignored;
            Health = Math.Max(0f, Health - amount);
            IncrementRevision();
            if (Health > 0f) return BossDamageOutcome.HealthDamaged;

            if (StageNumber >= m_Rules.Stages.Length)
            {
                SetState(BossEncounterState.Defeated);
                return BossDamageOutcome.BossDefeated;
            }

            SetState(BossEncounterState.PhaseTransition);
            return BossDamageOutcome.StageDefeated;
        }

        public bool TryBeginStagger(float nearestPlayerDistance)
        {
            if ((State != BossEncounterState.RoamingIdle && State != BossEncounterState.RoamingEvade) ||
                GuardHealth > 0f || nearestPlayerDistance > m_Rules.StaggerTriggerDistance) return false;
            SetState(BossEncounterState.StaggerEffect);
            return true;
        }

        public bool CompleteStaggerEffect()
        {
            if (State != BossEncounterState.StaggerEffect) return false;
            SetState(BossEncounterState.ExecutionWindow);
            return true;
        }

        public bool CompleteExecutionWindow()
        {
            if (State != BossEncounterState.ExecutionWindow) return false;
            SetState(BossEncounterState.Battle);
            return true;
        }

        public bool CompletePhaseTransition()
        {
            if (State != BossEncounterState.PhaseTransition || StageNumber >= m_Rules.Stages.Length) return false;
            LoadStage(m_StageIndex + 1);
            HudVisible = false;
            SetState(BossEncounterState.RoamingIdle);
            return true;
        }

        public BossEncounterSnapshot CaptureSnapshot() =>
            new BossEncounterSnapshot(State, StageNumber, GuardHealth, MaxGuardHealth,
                Health, MaxHealth, HudVisible, Revision);

        private void LoadStage(int stageIndex)
        {
            m_StageIndex = Math.Max(0, Math.Min(m_Rules.Stages.Length - 1, stageIndex));
            GuardHealth = CurrentRules.GuardHealth;
            Health = CurrentRules.BattleHealth;
            IncrementRevision();
        }

        private void SetState(BossEncounterState state)
        {
            if (State == state) return;
            State = state;
            IncrementRevision();
        }

        private void IncrementRevision()
        {
            unchecked { Revision++; }
        }
    }
}
