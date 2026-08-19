using System;
using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Abilities.Contracts;
using VampireHunt.Boss.Contracts;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Navigation.Domain;
using VampireHunt.Player.Contracts;
using VampireHunt.Presentation.CombatText;
using VampireHunt.Presentation.Contracts;
using VampireHunt.Presentation.Boss;
using VampireHunt.Presentation.Enemies;
using VampireHunt.Presentation.Player;
using VampireHunt.Presentation.Runtime;

namespace VampireHunt.Tests.Presentation
{
    public sealed class PresentationRuntimeTests
    {
        [Test]
        public void Dispatcher_IsolatesPresenterFailure_AndUnsubscribeWorks()
        {
            RecordingErrorSink errors = new RecordingErrorSink();
            ClientGameplayEventDispatcher dispatcher = new ClientGameplayEventDispatcher(errors);
            int delivered = 0;
            IDisposable throwing = dispatcher.Subscribe((IGameplayEvent _) => throw new InvalidOperationException("test"));
            IDisposable counting = dispatcher.Subscribe((IGameplayEvent _) => delivered++);

            dispatcher.Push(new TestEvent(11UL, 1d));

            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(errors.Count, Is.EqualTo(1));
            throwing.Dispose();
            dispatcher.Push(new TestEvent(12UL, 2d));
            Assert.That(delivered, Is.EqualTo(2));

            counting.Dispose();
            dispatcher.Push(new TestEvent(13UL, 3d));
            Assert.That(delivered, Is.EqualTo(2));
            Assert.That(dispatcher.DispatchCount, Is.EqualTo(3));
        }

        [Test]
        public void EntityViewRegistry_StaleViewCannotRemoveReplacement()
        {
            EntityViewRegistry registry = new EntityViewRegistry();
            EntityId id = new EntityId(9);
            object first = new object();
            object replacement = new object();

            Assert.That(registry.Register(id, first), Is.True);
            Assert.That(registry.Register(id, replacement), Is.True);
            Assert.That(registry.Unregister(id, first), Is.False);
            Assert.That(registry.TryGet(id, out object current), Is.True);
            Assert.That(current, Is.SameAs(replacement));

            registry.EntityDespawned(id);
            Assert.That(registry.TryGet(id, out _), Is.False);
            Assert.That(registry.ContainsEntity(id), Is.False);
        }

        [Test]
        public void ReadModelProjector_AtomicallyReplacesSnapshot_AndUsesFeatureSink()
        {
            PlayerReadModelProjector projector = new PlayerReadModelProjector();
            IPlayerStateSnapshotSink sink = projector;
            EntityId id = new EntityId(3);
            List<BloodPactStack> pacts = new List<BloodPactStack>
            {
                new BloodPactStack(new BloodPactId("swift"), 1)
            };
            PlayerSnapshot first = new PlayerSnapshot(
                id, 80, 100, true, false, 5f, 10f, 7, 2, 3, 40, 4, true, 9d, pacts);

            Assert.That(projector.HasSnapshot, Is.False);
            Assert.That(projector.Revision, Is.Zero);
            sink.Apply(first);
            Assert.That(projector.HasSnapshot, Is.True);
            Assert.That(projector.Revision, Is.EqualTo(1));
            Assert.That(projector.Health, Is.EqualTo(80));
            Assert.That(projector.Snapshot.BloodPacts.Count, Is.EqualTo(1));
            PlayerSnapshot exposed = projector.Snapshot;
            ((BloodPactStack[])exposed.BloodPacts)[0] = new BloodPactStack(new BloodPactId("mutated"), 99);
            Assert.That(projector.Snapshot.BloodPacts[0].Id, Is.EqualTo(new BloodPactId("swift")));

            sink.Apply(new PlayerSnapshot(
                id, 20, 100, true, false, 1f, 10f, 8, 2, 3, 41, 5, false, 0d));
            Assert.That(projector.Revision, Is.EqualTo(2));
            Assert.That(projector.Health, Is.EqualTo(20));
            Assert.That(projector.Snapshot.IsAttacking, Is.False);
        }

        [Test]
        public void EnemyAndBossProjectors_ImplementTheirFeatureSnapshotSinks()
        {
            EnemyReadModelProjector enemy = new EnemyReadModelProjector();
            IEnemyStateSnapshotSink enemySink = enemy;
            EntityId enemyId = new EntityId(4);
            enemySink.Apply(new EnemySnapshot(
                enemyId, 6, 10, true, EnemyState.Chasing,
                WorldPosition.Origin, new EntityId(1), 2d));

            BossReadModelProjector boss = new BossReadModelProjector();
            IBossStateSnapshotSink bossSink = boss;
            EntityId bossId = new EntityId(5);
            bossSink.Apply(new BossSnapshot(
                bossId, 90, 100, BossPhase.PhaseOne, false,
                BossAttackId.None, EncounterMode.Hunt, StaggerState.Guarded, 0f));

            Assert.That(enemy.Snapshot.Id, Is.EqualTo(enemyId));
            Assert.That(enemy.Health, Is.EqualTo(6));
            Assert.That(boss.Snapshot.Id, Is.EqualTo(bossId));
            Assert.That(boss.Health, Is.EqualTo(90));
        }

        [Test]
        public void CombatTextPresenter_DeduplicatesEvents_AndRecyclesAtHardCapacity()
        {
            ClientGameplayEventDispatcher dispatcher = new ClientGameplayEventDispatcher();
            InMemoryCombatTextPool pool = new InMemoryCombatTextPool(2);
            CombatTextPresenter presenter = new CombatTextPresenter(dispatcher, pool);
            EntityId source = new EntityId(1);
            EntityId target = new EntityId(2);
            WorldPosition hit = new WorldPosition(2f, 0f, 4f);

            dispatcher.Push(new DamageConfirmedEvent(
                21UL,
                1d,
                source,
                target,
                new DamageResult(5, 5, false, false, hit)));
            dispatcher.Push(new DamageConfirmedEvent(
                22UL,
                2d,
                source,
                target,
                new DamageResult(6, 6, true, false, hit)));
            dispatcher.Push(new DamageConfirmedEvent(
                21UL,
                1d,
                source,
                target,
                new DamageResult(5, 5, false, false, hit)));

            Assert.That(presenter.ActiveCount, Is.EqualTo(2));
            Assert.That(presenter.DroppedCount, Is.Zero);
            Assert.That(presenter.Capacity, Is.EqualTo(2));
            presenter.Dispose();
            Assert.That(pool.ActiveCount, Is.Zero);
        }

        [Test]
        public void AnimationPresenter_CanBeRemovedWithoutStoppingEventIngress()
        {
            ClientGameplayEventDispatcher dispatcher = new ClientGameplayEventDispatcher();
            RecordingAnimationDriver driver = new RecordingAnimationDriver();
            AnimationEventPresenter presenter = new AnimationEventPresenter(dispatcher, driver);
            DamageConfirmedEvent first = new DamageConfirmedEvent(
                30UL,
                1d,
                new EntityId(1),
                new EntityId(2),
                new DamageResult(4, 4, false, false, WorldPosition.Origin));

            dispatcher.Push(first);
            dispatcher.Push(first);
            Assert.That(driver.Cues, Has.Count.EqualTo(1));
            presenter.Dispose();
            dispatcher.Push(new DamageConfirmedEvent(
                31UL,
                2d,
                new EntityId(1),
                new EntityId(2),
                new DamageResult(4, 4, false, false, WorldPosition.Origin)));

            Assert.That(driver.Cues, Has.Count.EqualTo(1));
            Assert.That(dispatcher.DispatchCount, Is.EqualTo(3));
        }

        [Test]
        public void FlowFieldDebugPresenter_OnlyEmitsDebugCommand()
        {
            RecordingFlowDriver driver = new RecordingFlowDriver();
            FlowFieldDebugPresenter presenter = new FlowFieldDebugPresenter(new TestNavigation(), driver);
            WorldPosition position = new WorldPosition(1f, 0f, 2f);

            presenter.Draw(position, WorldPosition.Origin);

            Assert.That(driver.Commands, Has.Count.EqualTo(1));
            Assert.That(driver.Commands[0].Direction.X, Is.EqualTo(1));
            Assert.That(driver.Commands[0].Direction.Y, Is.EqualTo(-1));
            Assert.That(driver.Commands[0].IsWalkable, Is.True);
        }

        private sealed class TestEvent : GameplayEventBase
        {
            public TestEvent(ulong eventId, double occurredAt)
                : base(eventId, occurredAt)
            {
            }
        }

        private sealed class RecordingErrorSink : IGameplayEventDispatchErrorSink
        {
            public int Count { get; private set; }
            public void Report(IGameplayEvent @event, Exception exception) => Count++;
        }

        private sealed class RecordingAnimationDriver : IAnimationDriver
        {
            public List<PresentationCue> Cues { get; } = new List<PresentationCue>();
            public void Play(PresentationCue cue) => Cues.Add(cue);
        }

        private sealed class RecordingFlowDriver : IFlowFieldDebugDriver
        {
            public List<FlowFieldDebugCommand> Commands { get; } = new List<FlowFieldDebugCommand>();
            public void Draw(FlowFieldDebugCommand command) => Commands.Add(command);
        }

        private sealed class TestNavigation : INavigationField
        {
            public Direction SampleDirection(WorldPosition position, WorldPosition target) => new Direction(1, -1);
            public bool IsWalkable(WorldPosition position) => true;
            public WorldPosition TryFindRecovery(WorldPosition position) => position;
        }
    }
}
