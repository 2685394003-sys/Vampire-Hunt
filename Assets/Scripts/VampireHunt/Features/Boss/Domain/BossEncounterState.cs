using System;
using VampireHunt.Boss.Contracts;

namespace VampireHunt.Boss.Domain
{
    internal sealed class BossEncounterState
    {
        private readonly int maximumGuardIntegrity;
        private readonly float guardDamageReduction;
        private readonly float initialContractSeconds;

        public EncounterMode Mode { get; private set; }
        public StaggerState Stagger { get; private set; }
        public float ContractSeconds { get; private set; }
        public int GuardIntegrity { get; private set; }
        public float VulnerableSecondsRemaining { get; private set; }

        public BossEncounterState(BossSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            maximumGuardIntegrity = spec.GuardIntegrity;
            guardDamageReduction = spec.GuardDamageReduction;
            initialContractSeconds = spec.ContractSeconds;
            Reset();
        }

        public void Start(EncounterMode mode = EncounterMode.Hunt)
        {
            if (mode is EncounterMode.Inactive or EncounterMode.Defeated)
                throw new ArgumentOutOfRangeException(nameof(mode));
            Mode = mode;
        }

        public void SetMode(EncounterMode mode)
        {
            if (Mode == EncounterMode.Defeated && mode != EncounterMode.Defeated)
                return;
            Mode = mode;
        }

        public int ResolveIncomingDamage(int incomingDamage)
        {
            if (incomingDamage <= 0 || Mode == EncounterMode.Defeated)
                return 0;
            if (Stagger != StaggerState.Guarded || maximumGuardIntegrity <= 0)
                return incomingDamage;

            GuardIntegrity = Math.Max(0, GuardIntegrity - incomingDamage);
            if (GuardIntegrity == 0)
            {
                Stagger = StaggerState.Telegraph;
                return incomingDamage;
            }

            float healthFraction = 1f - guardDamageReduction;
            return Math.Max(0, (int)Math.Ceiling(incomingDamage * healthFraction));
        }

        public bool BeginVulnerableWindow(float seconds)
        {
            if (Stagger != StaggerState.Telegraph || seconds <= 0f)
                return false;
            Stagger = StaggerState.Vulnerable;
            VulnerableSecondsRemaining = seconds;
            return true;
        }

        public bool ExecuteStagger()
        {
            if (Stagger != StaggerState.Vulnerable)
                return false;
            Stagger = StaggerState.Executed;
            VulnerableSecondsRemaining = 0f;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || Stagger != StaggerState.Vulnerable)
                return;
            VulnerableSecondsRemaining = Math.Max(0f, VulnerableSecondsRemaining - deltaTime);
            if (VulnerableSecondsRemaining <= 0f)
                RestoreGuard();
        }

        public float ConsumeContractTime(float amount)
        {
            if (amount <= 0f || ContractSeconds <= 0f)
                return ContractSeconds;
            ContractSeconds = Math.Max(0f, ContractSeconds - amount);
            return ContractSeconds;
        }

        public void Defeat()
        {
            Mode = EncounterMode.Defeated;
            VulnerableSecondsRemaining = 0f;
        }

        public void Reset()
        {
            Mode = EncounterMode.Inactive;
            Stagger = maximumGuardIntegrity > 0 ? StaggerState.Guarded : StaggerState.Vulnerable;
            GuardIntegrity = maximumGuardIntegrity;
            VulnerableSecondsRemaining = 0f;
            ContractSeconds = initialContractSeconds;
        }

        private void RestoreGuard()
        {
            GuardIntegrity = maximumGuardIntegrity;
            Stagger = maximumGuardIntegrity > 0 ? StaggerState.Guarded : StaggerState.Vulnerable;
            VulnerableSecondsRemaining = 0f;
        }
    }
}
