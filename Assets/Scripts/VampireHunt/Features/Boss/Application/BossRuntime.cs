using System;
using VampireHunt.Boss.Contracts;
using VampireHunt.Boss.Domain;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Boss.Application
{
    /// <summary>
    /// Public composition facade for one authoritative Boss lifetime. Concrete
    /// adapters receive this facade instead of writable aggregate internals.
    /// </summary>
    public sealed class BossRuntime
    {
        private readonly BossAggregate aggregate;
        private readonly BossSimulationService simulation;
        private readonly BossEncounterService encounter;

        public EntityId Id => aggregate.Id;
        public BossSnapshot Snapshot => aggregate.CreateSnapshot();
        public IDamageReceiver DamageReceiver => aggregate.Vitals;
        public IBossEncounterQuery EncounterQuery => encounter;

        internal BossRuntime(
            BossAggregate aggregate,
            BossSimulationService simulation,
            BossEncounterService encounter)
        {
            this.aggregate = aggregate;
            this.simulation = simulation;
            this.encounter = encounter;
        }

        public void Start(EncounterMode mode = EncounterMode.Hunt) => aggregate.Start(mode);
        public void Tick(float deltaTime) => simulation.Tick(Id, deltaTime);
        public void SetInvulnerable(bool value) => aggregate.Vitals.SetInvulnerable(value);
        public void SetEncounterMode(EncounterMode mode) => aggregate.Encounter.SetMode(mode);
        public bool BeginStagger(float seconds) => aggregate.Encounter.BeginVulnerableWindow(seconds);
        public bool ExecuteStagger() => aggregate.Encounter.ExecuteStagger();
    }

    public static class BossRuntimeFactory
    {
        public static BossRuntime Create(
            EntityId id,
            BossSpec spec,
            IRandomSource random,
            CombatApplicationService combat,
            IBossMotor motor,
            IAttackWorldQuery attackWorldQuery,
            IProjectileSpawner projectileSpawner,
            IBossWorldState worldState,
            IGameplayEventSink eventSink = null,
            IGameClock clock = null,
            IBossEncounterConsequences consequences = null)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (random == null) throw new ArgumentNullException(nameof(random));
            InMemoryBossRepository repository = new();
            BossAggregate aggregate = new(id, spec, random);
            repository.Add(aggregate);
            GameplayEventIdAllocator eventIds = new();
            BossPhaseService phaseService = new(repository, eventSink, clock, eventIds);
            BossAttackService attackService = new(
                repository, motor, attackWorldQuery, projectileSpawner, worldState,
                combat, eventSink, clock, eventIds);
            BossEncounterService encounterService = new(
                repository, attackService, consequences, eventSink, clock, eventIds);
            BossSimulationService simulation = new(
                repository, phaseService, attackService, encounterService);
            simulation.Register(id);
            return new BossRuntime(aggregate, simulation, encounterService);
        }
    }
}
