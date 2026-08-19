using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VampireHunt.Boss.Application;
using VampireHunt.Boss.Contracts;
using VampireHunt.Boss.Domain;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Combat.Domain;
using VampireHunt.Core;

namespace VampireHunt.Tests.Boss
{
    public sealed class BossRuntimeTests
    {
        [Test]
        public void RuntimeFacade_DelegatesGuardRepairToDomain()
        {
            Harness harness = CreateHarness(CreateSpec(maxHealth: 100, guardIntegrity: 10));
            harness.Runtime.Start(EncounterMode.Battle);

            DamageRequest request = new(
                new EntityId(2), harness.Runtime.Id, 4,
                DamageFlags.NoCritical,
                new HitContext(WorldPosition.Origin));
            harness.Combat.ApplyDamage(in request);

            Assert.That(harness.Runtime.GuardIntegrity, Is.EqualTo(6));
            Assert.That(harness.Runtime.RestoreGuard(), Is.True);
            Assert.That(harness.Runtime.GuardIntegrity, Is.EqualTo(10));
        }

        [Test]
        public void RuntimeAttackSelection_UsesApplicationPlanWithoutPresentation()
        {
            Harness harness = CreateHarness(CreateSpec());
            harness.Runtime.Start(EncounterMode.Battle);
            IBossRuntimePort port = harness.Runtime;

            Assert.That(port.TryStartAttack(), Is.True);

            Assert.That(port.Snapshot.CurrentAttack, Is.EqualTo(BossAttackId.GuardSweep));

            // Every adapter is intentionally a no-op: no Animator, UI, Audio,
            // Camera, physics query, or projectile presenter is required.
            Assert.DoesNotThrow(() => port.Tick(0.3f));
            Assert.That(port.Snapshot.CurrentAttack, Is.EqualTo(BossAttackId.None));
            Assert.That(harness.Events.Events.OfType<BossAttackCueEvent>().Count(), Is.EqualTo(2));
        }

        [Test]
        public void RuntimeDeath_EmitsOneDefeatEventAcrossRepeatedTicks()
        {
            Harness harness = CreateHarness(CreateSpec(maxHealth: 10, guardIntegrity: 0));
            harness.Runtime.Start(EncounterMode.Battle);

            DamageRequest request = new(
                new EntityId(2), harness.Runtime.Id, 10,
                DamageFlags.NoCritical,
                new HitContext(WorldPosition.Origin));
            DamageResult damage = harness.Combat.ApplyDamage(in request);

            Assert.That(damage.WasKilled, Is.True);
            Assert.That(harness.Runtime.Snapshot.IsAlive, Is.False);
            harness.Runtime.Tick(0.1f);
            harness.Runtime.Tick(0.1f);

            Assert.That(harness.Runtime.Snapshot.Mode, Is.EqualTo(EncounterMode.Defeated));
            Assert.That(harness.Events.Events.OfType<BossDefeatedEvent>().Count(), Is.EqualTo(1));
        }

        private static Harness CreateHarness(BossSpec spec)
        {
            RecordingSink events = new();
            FixedClock clock = new();
            FixedRandom random = new();
            TestDirectory directory = new();
            CombatApplicationService combat = new(
                new CombatResolver(random), directory, events, clock);
            BossRuntime runtime = BossRuntimeFactory.Create(
                new EntityId(100), spec, random, combat,
                new NoopMotor(), new NoopWorldQuery(), new NoopProjectileSpawner(),
                new FixedWorldState(), events, clock);
            directory.Bind(runtime.Id, runtime.DamageReceiver);
            return new Harness(runtime, combat, events);
        }

        private static BossSpec CreateSpec(int maxHealth = 100, int guardIntegrity = 0)
        {
            PhaseSpecSet phases = new(new[]
            {
                new BossPhaseSpec(BossPhase.PhaseOne, 1f),
                new BossPhaseSpec(BossPhase.PhaseTwo, 0.6f),
                new BossPhaseSpec(BossPhase.PhaseThree, 0.2f)
            });
            BossAttackSpec attack = new(
                BossAttackId.GuardSweep,
                cooldown: 1f,
                weight: 1f,
                damage: 1,
                minimumPhase: BossPhase.PhaseOne,
                telegraphSeconds: 0f,
                activeSeconds: 0.2f,
                range: 4f,
                width: 2f,
                cue: new PresentationCueId("format1"));
            return new BossSpec(
                maxHealth, phases, new BossAttackSpecSet(new[] { attack }),
                guardIntegrity, guardDamageReduction: 0.5f,
                staggerSeconds: 4f, contractTriggerHealthRatio: 0.2f,
                contractSeconds: 0f, contractCountdownRate: 2f);
        }

        private readonly struct Harness
        {
            public BossRuntime Runtime { get; }
            public CombatApplicationService Combat { get; }
            public RecordingSink Events { get; }

            public Harness(BossRuntime runtime, CombatApplicationService combat, RecordingSink events)
            {
                Runtime = runtime;
                Combat = combat;
                Events = events;
            }
        }

        private sealed class FixedRandom : IRandomSource
        {
            public float NextFloat() => 0f;
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        }

        private sealed class FixedClock : IGameClock
        {
            public double Now => 0d;
            public float DeltaTime => 0.1f;
        }

        private sealed class RecordingSink : IGameplayEventSink
        {
            public List<IGameplayEvent> Events { get; } = new();
            public void Publish(IGameplayEvent @event)
            {
                if (@event != null) Events.Add(@event);
            }
        }

        private sealed class TestDirectory : ICombatEntityDirectory
        {
            private readonly Dictionary<EntityId, IDamageReceiver> damage = new();

            public void Bind(EntityId id, IDamageReceiver receiver)
            {
                damage[id] = receiver;
            }

            public IDamageReceiver TryGetDamageReceiver(EntityId id) =>
                damage.TryGetValue(id, out IDamageReceiver receiver) ? receiver : null;

            public IHealingReceiver TryGetHealingReceiver(EntityId id) => null;
            public IKnockbackReceiver TryGetKnockbackReceiver(EntityId id) => null;
        }

        private sealed class NoopMotor : IBossMotor
        {
            public void Execute(EntityId bossId, in MovementPlan plan) { }
        }

        private sealed class NoopWorldQuery : IAttackWorldQuery
        {
            public int CollectTargets(EntityId bossId, in DamageWindow window, IList<ICombatTarget> buffer)
            {
                buffer.Clear();
                return 0;
            }
        }

        private sealed class NoopProjectileSpawner : IProjectileSpawner
        {
            public void Spawn(EntityId bossId, BossAttackId attackId, in ProjectileRequest request) { }
        }

        private sealed class FixedWorldState : IBossWorldState
        {
            public bool TryGetAttackContext(
                EntityId bossId,
                BossPhase phase,
                double now,
                out BossAttackContext context)
            {
                context = new BossAttackContext(
                    bossId, phase, WorldPosition.Origin,
                    new WorldPosition(3f, 0f, 0f), now);
                return true;
            }
        }
    }
}
