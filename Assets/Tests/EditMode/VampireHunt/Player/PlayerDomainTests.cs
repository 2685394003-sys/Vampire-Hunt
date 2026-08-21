using System;
using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Combat.Domain;
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
        public void CombatApplication_UsesAuthoritativeTimeForPlayerInvincibility()
        {
            PlayerVitals vitals = new(100, invincibilityDuration: 0.8d);
            MutableGameClock clock = new() { Now = 10d };
            RecordingCombatSink sink = new();
            CombatApplicationService combat = new(
                new CombatResolver(new FixedRandom()),
                new PlayerVitalsDirectory(vitals),
                eventSink: sink,
                clock: clock);
            DamageRequest request = new(EnemyId, PlayerId, 10);

            DamageResult first = combat.ApplyDamage(request);
            clock.Now = 10.4d;
            DamageResult blocked = combat.ApplyDamage(request);

            Assert.That(first.AppliedDamage, Is.EqualTo(10));
            Assert.That(blocked.AppliedDamage, Is.Zero);
            Assert.That(sink.Events, Has.Count.EqualTo(1),
                "An invincibility-blocked hit must not emit a zero-damage presentation event.");

            clock.Now = 11d;
            DamageResult afterWindow = combat.ApplyDamage(request);

            Assert.That(afterWindow.AppliedDamage, Is.EqualTo(10),
                "Combat must evaluate PlayerVitals with the server clock instead of a constant timestamp.");
            Assert.That(sink.Events, Has.Count.EqualTo(2));
        }

        [Test]
        public void CombatApplication_RejectsTimeDependentReceiverWithoutAuthoritativeClock()
        {
            PlayerVitals vitals = new(100, invincibilityDuration: 0.8d);
            CombatApplicationService combat = new(
                new CombatResolver(new FixedRandom()),
                new PlayerVitalsDirectory(vitals));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => combat.ApplyDamage(new DamageRequest(EnemyId, PlayerId, 10)));

            Assert.That(error.Message, Does.Contain("authoritative game clock"));
            Assert.That(vitals.CurrentHealth, Is.EqualTo(100),
                "A missing clock must fail before mutating a time-dependent receiver.");
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
        public void BloodPactSelection_ChargesThePriceAdvertisedByTheOffer()
        {
            PlayerAggregate player = CreatePlayer();
            player.Progression.AddScarlet(100);
            BloodPactId cheapId = new("cheap");
            BloodPactId expensiveId = new("expensive");
            FakeCatalog catalog = new(
                new BloodPactOption(cheapId, 10),
                new BloodPactOption(expensiveId, 25));
            BloodPactOfferService service = new(
                new InMemoryRepository(player),
                catalog,
                new FixedRandom(),
                choicesPerOffer: 2);

            BloodPactOffer offer = service.CreateOffer(PlayerId);
            SelectionResult result = service.Select(PlayerId, expensiveId, offer.OfferVersion);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.RemainingScarlet, Is.EqualTo(100 - offer.Cost),
                "The single price exposed by BloodPactOffer must match the price charged for every offered choice.");
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

        [Test]
        public void RuntimeEndpoint_DelegatesDamageWithoutApplyingASecondHealthWrite()
        {
            CountingDamageResolver damage = new();
            IPlayerRuntimePort runtime = PlayerRuntimeEndpointFactory.Create(
                PlayerId,
                CreateSpec(),
                new PlayerRuntimeValues(15f, 10f, 5f, 2f, 0.2f, 2f, 110f),
                damageResolver: damage,
                hitQuery: new FakeHitQuery(),
                positions: new FakePositionQuery());

            PlayerDamageResult result = runtime.ApplyDamage(
                new PlayerDamageCommand(EnemyId, PlayerId, 12, WorldPosition.Origin));

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.AppliedDamage, Is.EqualTo(12));
            Assert.That(damage.Calls, Is.EqualTo(1));
            Assert.That(runtime.Health, Is.EqualTo(100),
                "The Player endpoint delegates mutation to Combat; it must not subtract health a second time.");

            PlayerDamageResult wrongTarget = runtime.ApplyDamage(
                new PlayerDamageCommand(EnemyId, EnemyId, 12, WorldPosition.Origin));
            Assert.That(wrongTarget.Accepted, Is.False);
            Assert.That(damage.Calls, Is.EqualTo(1));
        }

        [Test]
        public void RuntimeEndpoint_UsesDomainVitalsForHealingWithoutASecondHealthStore()
        {
            IPlayerRuntimePort runtime = PlayerRuntimeEndpointFactory.Create(
                PlayerId,
                CreateSpec(),
                new PlayerRuntimeValues(15f, 10f, 5f, 2f, 0.2f, 2f, 110f));

            IAuthoritativeDamageReceiver receiver = (IAuthoritativeDamageReceiver)runtime;
            receiver.ApplyDamage(
                new ResolvedDamage(EnemyId, PlayerId, 20, false, new HitContext(WorldPosition.Origin)),
                1d);
            int applied = runtime.ApplyHealing(20);

            Assert.That(applied, Is.EqualTo(20));
            Assert.That(runtime.Health, Is.EqualTo(100),
                "Healing is owned by PlayerVitals through the combat capability, not a duplicate adapter field.");
        }

        [Test]
        public void RuntimeEndpoint_SubmitAttackBuildsCombatAgainstItsOwnRepository()
        {
            CountingDamageResolver damage = new();
            IPlayerRuntimePort runtime = PlayerRuntimeEndpointFactory.Create(
                PlayerId,
                CreateSpec(),
                damageResolver: damage,
                hitQuery: new FakeHitQuery(new FakeTarget(EnemyId)),
                positions: new FakePositionQuery());

            CommandResult result = runtime.SubmitAttack(
                new AttackCommand(PlayerId, new WorldPosition(1f, 0f, 0f), 1u));

            Assert.That(result.Accepted, Is.True,
                "Supplying the query/resolver ports must let the endpoint construct its own combat service.");
            Assert.That(damage.Calls, Is.EqualTo(1),
                "The command path must reach the hit query and damage resolver exactly once.");
        }

        [Test]
        public void RuntimeEndpoint_SubmitPoseRecordsVerticalPositionWithoutValidation()
        {
            RecordingPoseSink poses = new();
            IPlayerRuntimePort runtime = PlayerRuntimeEndpointFactory.Create(
                PlayerId,
                CreateSpec(),
                poseSink: poses);
            MovementPose fallingPose = new(
                new WorldPosition(250f, -30f, -400f),
                new MoveVector(1f, 0f),
                reportedAt: -999d,
                sequence: 77u);

            runtime.SubmitPose(fallingPose);

            Assert.That(poses.LastPlayerId, Is.EqualTo(PlayerId));
            Assert.That(poses.LastPose.Position, Is.EqualTo(fallingPose.Position),
                "Owner poses, including gravity-driven Y movement, must be stored without speed or bounds correction.");
        }

        private static PlayerSpec CreateSpec()
        {
            Dictionary<PlayerStat, float> values = new()
            {
                [PlayerStat.BaseAttack] = 10f,
                [PlayerStat.InvincibleTime] = 0.8f,
                [PlayerStat.AttackRange] = 2f
            };
            return new PlayerSpec(100, 100f, 15f, 10f, 1d, 0.2d, 2d, 1d, values);
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
            private readonly BloodPactOption[] options;
            public FakeCatalog(params BloodPactOption[] options) => this.options = options;
            public int Count => options.Length;
            public bool TryGet(BloodPactId id, out BloodPactOption result)
            {
                for (int i = 0; i < options.Length; i++)
                {
                    if (id != options[i].Id) continue;
                    result = options[i];
                    return true;
                }
                result = default;
                return false;
            }
            public void CopyOptions(ICollection<BloodPactOption> buffer)
            {
                for (int i = 0; i < options.Length; i++) buffer.Add(options[i]);
            }
        }

        private sealed class FixedRandom : IPlayerRandom, IRandomSource
        {
            public int NextInt(int minimumInclusive, int maximumExclusive) => minimumInclusive;
            public float NextFloat() => 0f;
        }

        private sealed class MutableGameClock : IGameClock
        {
            public double Now { get; set; }
            public float DeltaTime { get; set; }
        }

        private sealed class PlayerVitalsDirectory : ICombatEntityDirectory
        {
            private readonly PlayerVitals vitals;
            public PlayerVitalsDirectory(PlayerVitals vitals) => this.vitals = vitals;
            public IDamageReceiver TryGetDamageReceiver(EntityId id) => id == PlayerId ? vitals : null;
            public IHealingReceiver TryGetHealingReceiver(EntityId id) => id == PlayerId ? vitals : null;
            public IKnockbackReceiver TryGetKnockbackReceiver(EntityId id) => null;
        }

        private sealed class RecordingPoseSink : IPlayerPoseSink
        {
            public EntityId LastPlayerId { get; private set; }
            public MovementPose LastPose { get; private set; }

            public void SetPose(EntityId playerId, MovementPose pose)
            {
                LastPlayerId = playerId;
                LastPose = pose;
            }
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

        private sealed class RecordingCombatSink : IGameplayEventSink
        {
            public readonly List<IGameplayEvent> Events = new();
            public void Publish(IGameplayEvent @event) => Events.Add(@event);
        }
    }
}
