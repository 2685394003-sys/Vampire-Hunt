using VampireHunt.Boss.Contracts;

namespace VampireHunt.Boss.Domain
{
    public readonly struct PhaseTransition
    {
        public BossPhase Previous { get; }
        public BossPhase Current { get; }
        public bool Changed => Previous != Current;

        public PhaseTransition(BossPhase previous, BossPhase current)
        {
            Previous = previous;
            Current = current;
        }
    }

    internal sealed class BossPhaseStateMachine
    {
        private readonly PhaseSpecSet phases;

        public BossPhase CurrentPhase { get; private set; }

        public BossPhaseStateMachine(PhaseSpecSet phases)
        {
            this.phases = phases;
            CurrentPhase = phases.Resolve(1f);
        }

        public PhaseTransition Evaluate(float healthRatio)
        {
            BossPhase previous = CurrentPhase;
            BossPhase candidate = phases.Resolve(healthRatio);
            if ((int)candidate > (int)CurrentPhase)
                CurrentPhase = candidate;
            return new PhaseTransition(previous, CurrentPhase);
        }

        public void Reset() => CurrentPhase = phases.Resolve(1f);
    }
}
