using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Application;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Navigation.Domain;

namespace VampireHunt.Tests.Modules.Enemies
{
    public sealed class EnemyRuntimeControllerTests
    {
        [Test]
        public void LethalDamageSettlesRewardAndDeathEventExactlyOnce()
        {
            EntityIdAllocator ids = new EntityIdAllocator(10);
            RecordingReward rewards = new RecordingReward();
            RecordingEvents events = new RecordingEvents();
            EnemyRuntimeController runtime = new EnemyRuntimeController(
                new AlwaysWalkableNavigation(),
                ids,
                rewardService: rewards,
                eventSink: events);
            EnemySpec spec = new EnemySpec(
                "basic",
                5,
                1f,
                1,
                1f,
                0.5f,
                EnemyAttackType.Melee,
                new RewardGrant(3, 2));
            runtime.ResetForSpawn(spec);

            EntityId enemyId = runtime.Id;
            ResolvedDamage lethal = new ResolvedDamage(
                new EntityId(1),
                enemyId,
                5,
                false,
                new HitContext(WorldPosition.Origin));
            DamageResult damage = runtime.ApplyDamage(in lethal);

            Assert.That(damage.WasKilled, Is.True);
            Assert.That(runtime.SettleDeath(new EntityId(1)).Settled, Is.True);
            Assert.That(runtime.SettleDeath(new EntityId(1)).Settled, Is.False);
            Assert.That(rewards.Count, Is.EqualTo(1));
            Assert.That(events.Count, Is.EqualTo(1));
        }

        [Test]
        public void PublicRuntimeFacadeDelegatesPerceptionAndCombatIntent()
        {
            EntityIdAllocator ids = new EntityIdAllocator(30);
            EnemyRuntimeController runtime = new EnemyRuntimeController(
                new AlwaysWalkableNavigation(), ids);
            EnemySpec spec = new EnemySpec(
                "ranged",
                8,
                2f,
                4,
                3f,
                0.5f,
                EnemyAttackType.Ranged,
                new RewardGrant(0));
            runtime.ResetForSpawn(spec);

            EnemyPerceptionData perception = new EnemyPerceptionData(
                runtime.Id,
                WorldPosition.Origin,
                new EntityId(31),
                new WorldPosition(1f, 0f, 0f),
                1f,
                hasLineOfTravel: true,
                targetIsAlive: true);
            EnemyIntent intent = runtime.Decide(in perception);
            AttackIntent attack = runtime.CreateAttackIntent(new EnemyCombatContextData(
                runtime.Id,
                perception.TargetId,
                perception.TargetPosition,
                perception.Distance,
                targetIsAlive: true,
                now: 1d,
                lastAttackAt: double.NegativeInfinity));

            Assert.That(intent.State, Is.EqualTo(EnemyState.Attacking));
            Assert.That(attack.SourceId, Is.EqualTo(runtime.Id));
            Assert.That(attack.TargetId, Is.EqualTo(perception.TargetId));
            Assert.That(attack.BaseDamage, Is.EqualTo(spec.AttackDamage));
            Assert.That(attack.IsRanged, Is.True);
        }

        [Test]
        public void DespawnAndRespawnAllocateFreshEntityIdAndResetState()
        {
            EntityIdAllocator ids = new EntityIdAllocator(20);
            EnemyRuntimeController runtime = new EnemyRuntimeController(
                new AlwaysWalkableNavigation(), ids);
            EnemySpec spec = new EnemySpec(
                "basic", 4, 1f, 1, 1f, 0f, EnemyAttackType.Melee, new RewardGrant(0));

            runtime.ResetForSpawn(spec);
            EntityId first = runtime.Id;
            runtime.ResetForDespawn();
            runtime.ResetForSpawn(spec);

            Assert.That(runtime.Id, Is.Not.EqualTo(first));
            Assert.That(runtime.Id.Value, Is.GreaterThan(first.Value));
            Assert.That(runtime.Snapshot.Health, Is.EqualTo(spec.MaxHealth));
            Assert.That(runtime.Snapshot.IsAlive, Is.True);
        }

        [Test]
        public void Decide_UsesAFiniteRadiusForClosestTargetQuery()
        {
            RecordingTargetQuery targets = new RecordingTargetQuery();
            EnemyRuntimeController runtime = new EnemyRuntimeController(
                new AlwaysWalkableNavigation(),
                new EntityIdAllocator(40),
                targets);
            runtime.ResetForSpawn(new EnemySpec(
                "basic", 4, 1f, 1, 1f, 0f, EnemyAttackType.Melee, new RewardGrant(0)));

            Assert.DoesNotThrow(() => runtime.Decide());
            Assert.That(targets.LastRadius, Is.EqualTo(float.MaxValue));
        }

        private sealed class RecordingReward : IRewardService
        {
            public int Count { get; private set; }
            public void Grant(EntityId recipientId, RewardGrant reward) => Count++;
        }

        private sealed class RecordingEvents : IGameplayEventSink
        {
            public int Count { get; private set; }
            public void Publish(IGameplayEvent @event) => Count++;
        }

        private sealed class RecordingTargetQuery : ICombatTargetQuery
        {
            public float LastRadius { get; private set; }

            public ICombatTarget FindClosest(in TargetQuery query)
            {
                LastRadius = query.Radius;
                return null;
            }

            public int CollectInArea(in TargetQuery query, IList<ICombatTarget> buffer)
            {
                LastRadius = query.Radius;
                return 0;
            }
        }

        private sealed class AlwaysWalkableNavigation : INavigationField
        {
            public Direction SampleDirection(WorldPosition position, WorldPosition target) => Direction.None;
            public bool IsWalkable(WorldPosition position) => true;
            public WorldPosition TryFindRecovery(WorldPosition position) => position;
        }
    }
}
