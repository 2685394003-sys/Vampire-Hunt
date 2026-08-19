using System;
using VampireHunt.Boss.Contracts;
using VampireHunt.Boss.Domain;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Combat.Domain;
using VampireHunt.Core;

namespace VampireHunt.Boss.Application
{
    /// <summary>
    /// Public composition facade for one authoritative Boss lifetime. Concrete
    /// adapters receive this facade instead of writable aggregate internals.
    /// </summary>
    public sealed class BossRuntime : IBossRuntimePort
    {
        private readonly BossAggregate aggregate;
        private readonly BossSimulationService simulation;
        private readonly BossEncounterService encounter;
        private readonly CombatApplicationService combat;
        private readonly KnockbackResolver knockback = new();
        private readonly BossRuntimeEventHub eventHub;

        public EntityId Id => aggregate.Id;
        public EntityId BossId => Id;
        public BossSnapshot Snapshot => aggregate.CreateSnapshot();
        public IDamageReceiver DamageReceiver => aggregate.Vitals;
        public IBossEncounterQuery EncounterQuery => encounter;
        public int GuardIntegrity => aggregate.Encounter.GuardIntegrity;
        public int MaxGuardIntegrity => aggregate.Encounter.MaxGuardIntegrity;
        public int Health => Snapshot.Health;
        public int MaxHealth => Snapshot.MaxHealth;
        public BossPhase Phase => Snapshot.Phase;
        public bool IsInvulnerable => Snapshot.IsInvulnerable;
        public BossAttackId CurrentAttack => Snapshot.CurrentAttack;
        public EncounterMode Mode => Snapshot.Mode;
        public bool IsEncounterActive => encounter.IsEncounterActive;
        public StaggerState Stagger => Snapshot.Stagger;
        public float ContractSeconds => Snapshot.ContractSeconds;

        public event Action<IGameplayEvent> GameplayEventProduced
        {
            add => eventHub.Published += value;
            remove => eventHub.Published -= value;
        }

        internal BossRuntime(
            BossAggregate aggregate,
            BossSimulationService simulation,
            BossEncounterService encounter,
            CombatApplicationService combat,
            BossRuntimeEventHub eventHub)
        {
            this.aggregate = aggregate;
            this.simulation = simulation;
            this.encounter = encounter;
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
        }

        public void Start(EncounterMode mode = EncounterMode.Hunt) => aggregate.Start(mode);
        public void Tick(float deltaTime) => simulation.Tick(Id, deltaTime);
        public AttackStartResult TryStartAttack() => simulation.TryStartAttack(Id);
        public AttackStartResult TryStartAttack(BossAttackId attackId) => simulation.TryStartAttack(Id, attackId);
        bool IBossRuntimePort.TryStartAttack(BossAttackId attackId) =>
            TryStartAttack(attackId).Started;
        public void CancelAttack() => simulation.CancelAttack(Id);
        public void SetInvulnerable(bool value) => aggregate.Vitals.SetInvulnerable(value);
        public void SetEncounterMode(EncounterMode mode) => aggregate.Encounter.SetMode(mode);
        public bool BeginStagger(float seconds) => aggregate.Encounter.BeginVulnerableWindow(seconds);
        public bool ExecuteStagger() => aggregate.Encounter.ExecuteStagger();
        public bool RestoreGuard() => aggregate.Encounter.RestoreGuard();
        public DamageResult ApplyDamage(in DamageRequest request) => combat.ApplyDamage(in request);
        public KnockbackImpulse ApplyKnockback(in KnockbackRequest request) =>
            combat.ApplyKnockback(in request, knockback);
    }

    public static class BossRuntimeFactory
    {
        public static BossRuntime Create(
            BossSpec spec,
            IRandomSource random,
            CombatApplicationService combat,
            in BossRuntimeDependencies dependencies,
            IGameplayEventSink eventSink = null,
            IGameClock clock = null,
            IBossEncounterConsequences consequences = null)
        {
            if (!dependencies.IsValid)
                throw new ArgumentException("Boss runtime dependencies are incomplete.", nameof(dependencies));

            return Create(
                dependencies.BossId,
                spec,
                random,
                combat,
                dependencies.Motor,
                dependencies.AttackWorldQuery,
                dependencies.ProjectileSpawner,
                dependencies.WorldState,
                eventSink,
                clock,
                consequences);
        }

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
            BossRuntimeEventHub eventHub = new(eventSink);
            BossPhaseService phaseService = new(repository, eventHub, clock, eventIds);
            BossAttackService attackService = new(
                repository, motor, attackWorldQuery, projectileSpawner, worldState,
                combat, eventHub, clock, eventIds);
            BossEncounterService encounterService = new(
                repository, attackService, consequences, eventHub, clock, eventIds);
            BossSimulationService simulation = new(
                repository, phaseService, attackService, encounterService);
            simulation.Register(id);
            return new BossRuntime(aggregate, simulation, encounterService, combat, eventHub);
        }
    }

    internal sealed class BossRuntimeEventHub : IGameplayEventSink
    {
        private readonly IGameplayEventSink downstream;

        public event Action<IGameplayEvent> Published;

        public BossRuntimeEventHub(IGameplayEventSink downstream) => this.downstream = downstream;

        public void Publish(IGameplayEvent @event)
        {
            if (@event == null) return;
            downstream?.Publish(@event);
            Published?.Invoke(@event);
        }
    }
}
