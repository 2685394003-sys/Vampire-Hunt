using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Boss.Contracts;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Spawning.Contracts;
using EnemyRewardGrant = VampireHunt.Enemies.Contracts.RewardGrant;
using IPlayerProgressionCommands = VampireHunt.Player.Contracts.IPlayerProgressionCommands;

namespace VampireHunt.Tests.Infrastructure.Integration
{
    public sealed class IntegrationAdapterTests
    {
        [Test]
        public void PlayerRewardAdapter_ForwardsScalarRewardAndSharedScarlet()
        {
            FakeProgression progression = new();
            PlayerRewardAdapter adapter = new(progression);
            EntityId recipient = new(11UL);

            adapter.Grant(recipient, new EnemyRewardGrant(7, 5, 3));
            int shared = adapter.GrantSharedScarlet(5);

            Assert.That(progression.LastReward.RecipientId, Is.EqualTo(recipient));
            Assert.That(progression.LastReward.Scarlet, Is.EqualTo(7));
            Assert.That(progression.LastReward.Coins, Is.EqualTo(5));
            Assert.That(progression.LastReward.Experience, Is.EqualTo(3));
            Assert.That(shared, Is.EqualTo(5));
        }

        [Test]
        public void TargetQueryAdapter_UsesRadiusAliveExclusionAndDeterministicDistance()
        {
            CombatTargetQueryAdapter adapter = new();
            FakeTarget nearest = new(new EntityId(2UL), new WorldPosition(1f, 0f, 0f), true);
            FakeTarget farther = new(new EntityId(1UL), new WorldPosition(2f, 0f, 0f), true);
            FakeTarget dead = new(new EntityId(3UL), new WorldPosition(0.5f, 0f, 0f), false);
            adapter.Register(farther);
            adapter.Register(nearest);
            adapter.Register(dead);

            TargetQuery query = new(WorldPosition.Origin, 2.1f, nearest.Id, true);
            List<ICombatTarget> found = new();
            int count = adapter.CollectInArea(in query, found);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(found[0], Is.SameAs(farther));
            Assert.That(adapter.FindClosest(in query), Is.SameAs(farther));
        }

        [Test]
        public void EntityDirectory_ReplacingRegistrationRemovesStaleCapabilities()
        {
            LifecycleRecorder lifecycle = new();
            CombatEntityDirectoryAdapter directory = new(lifecycle);
            EntityId id = new(9UL);
            FakeDamage damage = new();
            FakeHealing healing = new();
            FakeKnockback knockback = new();

            directory.Register(id, damage, healing, null);
            directory.Register(id, null, null, knockback);

            Assert.That(directory.Count, Is.EqualTo(1));
            Assert.That(directory.TryGetDamageReceiver(id), Is.Null);
            Assert.That(directory.TryGetHealingReceiver(id), Is.Null);
            Assert.That(directory.TryGetKnockbackReceiver(id), Is.SameAs(knockback));
            Assert.That(lifecycle.Spawned, Is.EqualTo(1));

            Assert.That(directory.Unregister(id), Is.True);
            Assert.That(directory.TryGetKnockbackReceiver(id), Is.Null);
            Assert.That(lifecycle.Despawned, Is.EqualTo(1));
        }

        [Test]
        public void BossSpawnGate_BlocksOnlyWhileAuthoritativeEncounterIsActive()
        {
            FakeEncounter encounter = new();
            BossSpawnGateAdapter gate = new(encounter);
            SpawnContext context = new(1d, 0, new List<WorldPosition>());

            encounter.Active = true;
            Assert.That(gate.CanSpawn(context), Is.False);
            encounter.Active = false;
            Assert.That(gate.CanSpawn(context), Is.True);
        }

        private sealed class FakeProgression : IPlayerProgressionCommands
        {
            public VampireHunt.Player.Contracts.RewardGrant LastReward { get; private set; }
            public bool GrantReward(EntityId recipientId, VampireHunt.Player.Contracts.RewardGrant reward)
            {
                LastReward = reward;
                return true;
            }

            public int GrantSharedScarlet(int amount) => amount;
        }

        private sealed class FakeTarget : ICombatTarget
        {
            public FakeTarget(EntityId id, WorldPosition position, bool isAlive)
            {
                Id = id;
                Position = position;
                IsAlive = isAlive;
            }

            public EntityId Id { get; }
            public bool IsAlive { get; }
            public WorldPosition Position { get; }
        }

        private sealed class FakeDamage : IDamageReceiver
        {
            public DamageResult ApplyDamage(in ResolvedDamage damage) => DamageResult.NoDamage(damage.FinalDamage, damage.Hit.Position);
        }

        private sealed class FakeHealing : IHealingReceiver
        {
            public int ApplyHealing(int amount) => amount;
        }

        private sealed class FakeKnockback : IKnockbackReceiver
        {
            public void ApplyKnockback(in KnockbackImpulse impulse) { }
        }

        private sealed class LifecycleRecorder : IEntityLifecycleEventSink
        {
            public int Spawned { get; private set; }
            public int Despawned { get; private set; }
            public void EntitySpawned(EntityId id) => Spawned++;
            public void EntityDespawned(EntityId id) => Despawned++;
        }

        private sealed class FakeEncounter : IBossEncounterQuery
        {
            public EncounterMode Mode => Active ? EncounterMode.Battle : EncounterMode.Inactive;
            public bool IsEncounterActive => Active;
            public bool Active { get; set; }
        }
    }
}
