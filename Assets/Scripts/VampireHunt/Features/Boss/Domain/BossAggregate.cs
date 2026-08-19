using System;
using VampireHunt.Boss.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Boss.Domain
{
    internal sealed class BossAggregate
    {
        public EntityId Id { get; }
        public BossSpec Spec { get; }
        public BossVitals Vitals { get; }
        public BossPhaseStateMachine Phases { get; }
        public BossEncounterState Encounter { get; }
        public BossAttackState Attacks { get; }
        public BossAttackSelector AttackSelector { get; }

        public BossAggregate(EntityId id, BossSpec spec, IRandomSource random)
        {
            if (!id.IsValid) throw new ArgumentException("A valid Boss id is required.", nameof(id));
            Id = id;
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            Encounter = new BossEncounterState(spec);
            Vitals = new BossVitals(spec.MaxHealth, Encounter);
            Phases = new BossPhaseStateMachine(spec.Phases);
            Attacks = new BossAttackState();
            AttackSelector = new BossAttackSelector(spec.Attacks, random);
        }

        public BossSnapshot CreateSnapshot() => new(
            Id,
            Vitals.CurrentHealth,
            Vitals.MaxHealth,
            Phases.CurrentPhase,
            Vitals.IsInvulnerable,
            Attacks.CurrentAttack,
            Encounter.Mode,
            Encounter.Stagger,
            Encounter.ContractSeconds);

        public void Start(EncounterMode mode = EncounterMode.Hunt) => Encounter.Start(mode);

        public void TickState(float deltaTime)
        {
            Encounter.Tick(deltaTime);
            Attacks.Tick(deltaTime);
        }

        public void ResetForEncounter(EncounterMode mode = EncounterMode.Hunt)
        {
            Vitals.Reset();
            Phases.Reset();
            Encounter.Reset();
            Attacks.Reset();
            Encounter.Start(mode);
        }
    }
}
