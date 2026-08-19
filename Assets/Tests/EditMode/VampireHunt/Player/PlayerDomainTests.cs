using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Player.Application;
using VampireHunt.Player.Contracts;
using VampireHunt.Player.Domain;

namespace VampireHunt.Tests.Player
{
    public sealed class PlayerDomainTests
    {
        private static readonly EntityId PlayerId = new(1);
        private static readonly EntityId EnemyId = new(2);

        [Test]
        public void Vitals_IgnoreDamageDuringInvincibility_AndKillOnce()
        {
            PlayerVitals vitals = new(100);

            PlayerDamageOutcome first = vitals.ApplyDamage(30, 1d, 1d, EnemyId);
            PlayerDamageOutcome blocked = vitals.ApplyDamage(30, 1.5d, 1d, EnemyId);
            PlayerDamageOutcome second = vitals.ApplyDamage(80, 2.1d, 1d, EnemyId);
            PlayerDamageOutcome afterDeath = vitals.ApplyDamage(1, 3d, 1d, EnemyId);

            Assert.That(first.AppliedDamage, Is.EqualTo(30));
            Assert.That(blocked.AppliedDamage, Is.EqualTo(0));
            Assert.That(blocked.WasInvincible, Is.True);
            Assert.That(second.AppliedDamage, Is.EqualTo(70));
            Assert.That(second.WasKilled, Is.True);
            Assert.That(afterDeath.AppliedDamage, Is.EqualTo(0));
            Assert.That(vitals.IsAlive, Is.False);
        }

        [Test]
        public void Aggregate_RejectsStaleAndWrappedCommandSequences()
        {
            PlayerAggregate player = CreatePlayer();

            Assert.That(player.TryAcceptCommandSequence(uint.MaxValue), Is.True);
            Assert.That(player.TryAcceptCommandSequence(0u), Is.True);
            Assert.That(player.TryAcceptCommandSequence(uint.MaxValue), Is.False);
            Assert.That(player.LastCommandSequence, Is.EqualTo(0u));
        }

        [Test]
        public void BloodPactSelection_ConsumesOfferVersionOnlyOnce()
        {
            PlayerAggregate player = CreatePlayer();
            player.Progression.AddScarlet(20);
            InMemoryRepository repository = new(player);
            FakeCatalog catalog = new(new BloodPactOption(new BloodPactId("swift"), 10, true, 2));
            BloodPactOfferService service = new(repository, catalog, new FixedRandom(), 1);

            BloodPactOffer offer = service.CreateOffer(PlayerId);
            SelectionResult accepted = service.Select(PlayerId, new BloodPactId("swift"), offer.OfferVersion);
            SelectionResult replay = service.Select(PlayerId, new BloodPactId("swift"), offer.OfferVersion);

            Assert.That(accepted.Accepted, Is.True);
            Assert.That(accepted.Stacks, Is.EqualTo(1));
            Assert.That(replay.Reason, Is.EqualTo(BloodPactSelectionCode.StaleOffer));
            Assert.That(player.Progression.Scarlet, Is.EqualTo(10));
        }

        [Test]
        public void CombatService_DeduplicatesTargetIdsBeforeApplyingDamage()
        {
            PlayerAggregate player = CreatePlayer();
            FakeHitQuery query = new(new FakeTarget(EnemyId), new FakeTarget(EnemyId));
            CountingDamageResolver damage = new();
            PlayerCombatService service = new(query, damage, new FakePositionQuery(), 2f, 120f);

            AttackResult result = service.Attack(
                player,
                new AttackCommand(PlayerId, new WorldPosition(1f, 0f, 0f), 1),
                1d);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.TargetsHit, Is.EqualTo(1));
            Assert.That(damage.Calls, Is.EqualTo(1));
        }

        private static PlayerAggregate CreatePlayer()
        {
            Dictionary<PlayerStat, float> values = new()
            {
                [PlayerStat.BaseAttack] = 10f,
                [PlayerStat.InvincibleTime] = 1f
            };
            return new PlayerAggregate(
                PlayerId,
                new PlayerVitals(100),
                new PlayerRunStats(values),
                new PlayerCombatState(1d),
                new PlayerMobilityState(100f, 15f, 10f, 1d, 0.2d, 2d),
                new PlayerProgression(),
                new BloodPactLoadout());
        }

        private sealed class InMemoryRepository : IPlayerRepository
        {
            private readonly PlayerAggregate player;
            public InMemoryRepository(PlayerAggregate player) => this.player = player;
            public PlayerAggregate Get(EntityId playerId) => playerId == player.Id ? player : null;
            public bool TryGet(EntityId playerId, out PlayerAggregate result)
            {
                result = Get(playerId);
                return result != null;
            }
            public void GetAllAlive(ICollection<PlayerAggregate> buffer)
            {
                if (player.IsAlive) buffer.Add(player);
            }
        }

        private sealed class FakeCatalog : IBloodPactCatalog
        {
            private readonly BloodPactOption option;
            public FakeCatalog(BloodPactOption option) => this.option = option;
            public int Count => 1;
            public bool TryGet(BloodPactId id, out BloodPactOption result)
            {
                result = option;
                return id == option.Id;
            }
            public void CopyOptions(ICollection<BloodPactOption> buffer) => buffer.Add(option);
        }

        private sealed class FixedRandom : IPlayerRandom
        {
            public int NextInt(int minimumInclusive, int maximumExclusive) => minimumInclusive;
        }

        private sealed class FakeTarget : IMeleeHitTarget
        {
            public FakeTarget(EntityId id) => Id = id;
            public EntityId Id { get; }
            public bool IsAlive => true;
            public WorldPosition HitPosition => new(1f, 0f, 0f);
        }

        private sealed class FakeHitQuery : IMeleeHitQuery
        {
            private readonly IMeleeHitTarget[] targets;
            public FakeHitQuery(params IMeleeHitTarget[] targets) => this.targets = targets;
            public void CollectUniqueTargets(MeleeHitQuery query, ICollection<IMeleeHitTarget> buffer)
            {
                foreach (IMeleeHitTarget target in targets) buffer.Add(target);
            }
        }

        private sealed class FakePositionQuery : IPlayerPositionQuery
        {
            public bool TryGetPosition(EntityId playerId, out WorldPosition position)
            {
                position = WorldPosition.Origin;
                return playerId == PlayerId;
            }
        }

        private sealed class CountingDamageResolver : IPlayerDamageResolver
        {
            public int Calls { get; private set; }
            public DamageResult Apply(PlayerAttackRequest request)
            {
                Calls++;
                return new DamageResult(request.BaseDamage, request.BaseDamage, false, false, request.HitPosition);
            }
        }
    }
}
