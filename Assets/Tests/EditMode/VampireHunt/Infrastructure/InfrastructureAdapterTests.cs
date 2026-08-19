using System;
using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Player.Contracts;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Tests.Infrastructure
{
    public sealed class InfrastructureAdapterTests
    {
        [Test]
        public void RegistryRetiresLogicalIdAndReleasesNetworkHandle()
        {
            NetworkEntityRegistry registry = new();
            EntityId id = new(11UL);

            Assert.That(registry.Register(id, 77UL), Is.True);
            Assert.That(registry.TryGetEntityId(77UL, out EntityId mapped), Is.True);
            Assert.That(mapped, Is.EqualTo(id));
            Assert.That(registry.Unregister(id, out ulong handle), Is.True);
            Assert.That(handle, Is.EqualTo(77UL));
            Assert.That(registry.TryGetEntityId(77UL, out _), Is.False);
            Assert.That(registry.Register(id, 78UL), Is.False);
            Assert.That(registry.IsRetired(id), Is.True);
        }

        [Test]
        public void PoolAllocatesFreshIdAndResetsOnReturn()
        {
            FakePoolFactory factory = new();
            NetworkObjectPool pool = new(factory, new EntityIdAllocator(100UL), 4);
            EnemySpawnSpec spec = new EnemySpawnSpec("bat", 4, 1, 1f);

            INetworkPooledEntity first = pool.Acquire(spec);
            EntityId firstId = first.EntityId;
            Assert.That(firstId.Value, Is.EqualTo(100UL));
            Assert.That(factory.Last.ResetCount, Is.EqualTo(1));
            Assert.That(pool.Release(firstId), Is.True);
            Assert.That(factory.Last.DespawnResetCount, Is.EqualTo(1));

            INetworkPooledEntity second = pool.Acquire(spec);
            Assert.That(second.EntityId.Value, Is.EqualTo(101UL));
            Assert.That(second, Is.SameAs(first));
            Assert.That(factory.Last.ResetCount, Is.EqualTo(2));
        }

        [Test]
        public void EventChannelDeduplicatesByEventIdWithoutDependingOnSnapshotOrder()
        {
            GameplayEventReplicator replicator = new();
            RecordingIngress ingress = new();
            IGameplayEvent eventA = new TestEvent(10UL, 2d);
            IGameplayEvent eventB = new TestEvent(11UL, 1d);
            Assert.That(replicator.Receive(new GameplayEventEnvelope(eventB), ingress), Is.True);
            Assert.That(replicator.Receive(new GameplayEventEnvelope(eventA), ingress), Is.True);
            Assert.That(replicator.Receive(new GameplayEventEnvelope(eventA), ingress), Is.False);
            Assert.That(ingress.Events.Count, Is.EqualTo(2));
            Assert.That(ingress.Events[0].EventId, Is.EqualTo(11UL));
            Assert.That(ingress.Events[1].EventId, Is.EqualTo(10UL));
        }

        [Test]
        public void EnemyDtoQuantizesAndRoundTripsPosition()
        {
            EntityId id = new(3UL);
            EnemySnapshot snapshot = new(
                id, 12, 20, true, EnemyState.Chasing,
                new WorldPosition(1.234f, 0f, -2.345f), default, 4.5d);
            EnemyStateDto dto = EnemyStateDto.From(snapshot, 1U, true, EnemyReplicationTier.Near);

            Assert.That(dto.PositionX, Is.EqualTo(123));
            Assert.That(dto.PositionZ, Is.EqualTo(-235));
            Assert.That(dto.ToSnapshot().Position.X, Is.EqualTo(1.23f).Within(0.005f));
            Assert.That(dto.ToSnapshot().Position.Z, Is.EqualTo(-2.35f).Within(0.005f));
        }

        [Test]
        public void FarEnemyReplication_GivesEveryEntityOnePeriodicSlotPerInterval()
        {
            EnemyReplicationPolicy policy = new(
                nearDistance: 10f,
                midDistance: 20f,
                hiddenDistance: 100f,
                midIntervalTicks: 2,
                farIntervalTicks: 4);
            EnemyStateReplicator replicator = new(policy);
            EnemySnapshot[] snapshots = new EnemySnapshot[4];
            int[] periodicSends = new int[snapshots.Length];

            for (int i = 0; i < snapshots.Length; i++)
            {
                snapshots[i] = new EnemySnapshot(
                    new EntityId((ulong)(i + 1)),
                    10,
                    10,
                    true,
                    EnemyState.Idle,
                    new WorldPosition(i, 0f, 0f),
                    default,
                    0d);
                Assert.That(replicator.Capture(snapshots[i], 50f, 0d, out _), Is.True);
            }

            for (int serverTick = 1; serverTick <= policy.FarIntervalTicks; serverTick++)
            {
                for (int i = 0; i < snapshots.Length; i++)
                {
                    if (replicator.Capture(snapshots[i], 50f, serverTick, out _))
                        periodicSends[i]++;
                }
            }

            CollectionAssert.AreEqual(
                new[] { 1, 1, 1, 1 },
                periodicSends,
                "Replication cadence must be tracked per entity or per server frame, not by capture order.");
        }

        private sealed class RecordingIngress : IGameplayEventIngress
        {
            public readonly List<IGameplayEvent> Events = new();
            public void Push(IGameplayEvent @event) => Events.Add(@event);
        }

        private sealed class TestEvent : GameplayEventBase
        {
            public TestEvent(ulong id, double at) : base(id, at) { }
        }

        private sealed class FakePoolFactory : INetworkPooledEntityFactory
        {
            public FakePooledEntity Last { get; private set; }
            public INetworkPooledEntity Create(EnemySpawnSpec spec)
            {
                Last ??= new FakePooledEntity();
                return Last;
            }
        }

        private sealed class FakePooledEntity : INetworkPooledEntity
        {
            public EntityId EntityId { get; private set; }
            public ulong NetworkObjectId => 0UL;
            public int ResetCount { get; private set; }
            public int DespawnResetCount { get; private set; }
            public void ResetForSpawn(EntityId freshId, EnemySpawnSpec spec)
            {
                EntityId = freshId;
                ResetCount++;
            }
            public void ResetForDespawn()
            {
                EntityId = default;
                DespawnResetCount++;
            }
        }
    }
}
