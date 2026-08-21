using NUnit.Framework;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Netcode.Contracts;

namespace VampireHunt.Tests.Netcode
{
    public sealed class NetworkStateReplicatorContractTests
    {
        [Test]
        public void PeriodicEnemySnapshotGetsNewSequenceAndIsAccepted()
        {
            EnemyReplicationPolicy policy = new(
                nearDistance: 10f,
                midDistance: 20f,
                hiddenDistance: 100f,
                midIntervalTicks: 2,
                farIntervalTicks: 2);
            EnemyStateReplicator sender = new(policy);
            EnemyStateReplicator receiver = new(policy);
            RecordingSink sink = new();
            EnemySnapshot snapshot = new(
                new EntityId(7UL),
                10,
                10,
                true,
                EnemyState.Idle,
                new WorldPosition(5f, 0f, 5f),
                default,
                0d);

            Assert.That(sender.Capture(snapshot, 50f, 0d, out var first), Is.True);
            Assert.That(receiver.ApplyNetworkState(first, sink), Is.True);

            Assert.That(sender.Capture(snapshot, 50f, 1d, out _), Is.False);
            EnemySnapshot moved = new(
                snapshot.Id,
                snapshot.Health,
                snapshot.MaxHealth,
                snapshot.IsAlive,
                snapshot.State,
                new WorldPosition(7f, 0f, 5f),
                snapshot.TargetId,
                snapshot.LastAttackAt);
            Assert.That(sender.Capture(moved, 50f, 1d, out _), Is.False);
            Assert.That(sender.Capture(moved, 50f, 2d, out var periodic), Is.True);
            Assert.That(periodic.Dirty, Is.True);
            Assert.That(periodic.Sequence, Is.Not.EqualTo(first.Sequence));
            Assert.That(receiver.ApplyNetworkState(periodic, sink), Is.True);
            Assert.That(periodic.ToSnapshot().Position.X, Is.EqualTo(7f).Within(0.01f));
            Assert.That(sink.Count, Is.EqualTo(2));
        }

        private sealed class RecordingSink : IEnemyStateSnapshotSink
        {
            public int Count { get; private set; }

            public void Apply(EnemySnapshot snapshot)
            {
                Count++;
            }
        }
    }
}
