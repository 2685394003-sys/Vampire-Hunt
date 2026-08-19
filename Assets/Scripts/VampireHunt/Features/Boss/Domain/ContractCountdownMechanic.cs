using System;

namespace VampireHunt.Boss.Domain
{
    internal readonly struct BossMechanicContext
    {
        public BossEncounterState Encounter { get; }
        public float HealthRatio { get; }
        public float DeltaTime { get; }

        internal BossMechanicContext(BossEncounterState encounter, float healthRatio, float deltaTime)
        {
            Encounter = encounter;
            HealthRatio = healthRatio;
            DeltaTime = Math.Max(0f, deltaTime);
        }
    }

    public readonly struct MechanicResult
    {
        public bool TriggeredThisTick { get; }
        public bool IsActive { get; }
        public bool Expired { get; }
        public float RemainingSeconds { get; }

        public MechanicResult(bool triggeredThisTick, bool isActive, bool expired, float remainingSeconds)
        {
            TriggeredThisTick = triggeredThisTick;
            IsActive = isActive;
            Expired = expired;
            RemainingSeconds = Math.Max(0f, remainingSeconds);
        }
    }

    internal interface IEncounterMechanic
    {
        MechanicResult Tick(BossMechanicContext context);
        void Reset();
    }

    internal sealed class ContractCountdownMechanic : IEncounterMechanic
    {
        private readonly float triggerHealthRatio;
        private readonly float countdownRate;
        private bool triggered;

        public ContractCountdownMechanic(float triggerHealthRatio, float countdownRate)
        {
            if (triggerHealthRatio <= 0f || triggerHealthRatio >= 1f)
                throw new ArgumentOutOfRangeException(nameof(triggerHealthRatio));
            if (countdownRate <= 0f || float.IsNaN(countdownRate))
                throw new ArgumentOutOfRangeException(nameof(countdownRate));
            this.triggerHealthRatio = triggerHealthRatio;
            this.countdownRate = countdownRate;
        }

        public MechanicResult Tick(BossMechanicContext context)
        {
            if (context.Encounter == null)
                return default;
            bool activatedNow = !triggered && context.HealthRatio <= triggerHealthRatio;
            triggered |= activatedNow;
            if (!triggered)
                return new MechanicResult(false, false, false, context.Encounter.ContractSeconds);

            float remaining = context.Encounter.ConsumeContractTime(context.DeltaTime * countdownRate);
            return new MechanicResult(activatedNow, remaining > 0f, remaining <= 0f, remaining);
        }

        public void Reset() => triggered = false;
    }
}
