using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Core;
using VampireHunt.Spawning.Contracts;
using VampireHunt.Spawning.Domain;

namespace VampireHunt.Tests.Modules.Spawning
{
    public sealed class EnemySpawnDirectorTests
    {
        [Test]
        public void DirectorRespectsGateCapacityAndProducesUniqueSequence()
        {
            EnemySpawnSpec spec = new EnemySpawnSpec("basic", 2, 0, 0f, baseSpawnsPerInterval: 4, maxSpawnsPerTick: 4);
            SpawnPressurePolicy pressure = new SpawnPressurePolicy(spec);
            TestLocations locations = new TestLocations();
            SpawnCandidateSampler sampler = new SpawnCandidateSampler(locations);
            TestSpawner spawner = new TestSpawner(0);
            TestGate gate = new TestGate(true);
            EnemySpawnDirector director = new EnemySpawnDirector(spec, pressure, sampler, spawner, gate);
            SpawnContext context = new SpawnContext(0d, 0.1f, 0, new[] { new WorldPosition(10f, 0f, 10f) });

            SpawnTickResult first = director.Tick(context);
            SpawnTickResult second = director.Tick(context);

            Assert.That(first.SpawnedCount, Is.EqualTo(2));
            Assert.That(first.Budget.Available, Is.EqualTo(2));
            Assert.That(second.SpawnedCount, Is.EqualTo(0));
            Assert.That(spawner.Requests.Count, Is.EqualTo(2));

            gate.Open = false;
            Assert.That(director.Tick(context).GateOpen, Is.False);
        }

        private sealed class TestLocations : ISpawnLocationQuery
        {
            public bool IsValid(WorldPosition position) => position.X > 0f;
            public WorldPosition SampleAround(WorldPosition target) => target + new WorldPosition(1f, 0f, 0f);
        }

        private sealed class TestSpawner : IEnemySpawner
        {
            private readonly EntityIdAllocator ids = new EntityIdAllocator(100);
            public TestSpawner(int activeCount) { ActiveCount = activeCount; }
            public int ActiveCount { get; private set; }
            public List<SpawnRequest> Requests { get; } = new List<SpawnRequest>();
            public EntityId Spawn(SpawnRequest request)
            {
                Requests.Add(request);
                ActiveCount++;
                return ids.Allocate();
            }
        }

        private sealed class TestGate : ISpawnGate
        {
            public TestGate(bool open) { Open = open; }
            public bool Open { get; set; }
            public bool CanSpawn(SpawnContext context) => Open;
        }
    }
}
