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
            CombatApplicationService combat = new(
                new CombatResolver(new FixedRandom()),
                new PlayerVitalsDirectory(vitals),
                clock: clock);
            DamageRequest request = new(EnemyId, PlayerId, 10);

            DamageResult first = combat.ApplyDamage(request);
            clock.Now = 11d;
            DamageResult afterWindow = combat.ApplyDamage(request);

            Assert.That(first.AppliedDamage, Is.EqualTo(10));
            Assert.That(afterWindow.AppliedDamage, Is.EqualTo(10),
                "Combat must evaluate PlayerVitals with the server clock instead of a constant timestamp.");
        }

        [Test]
        public void MovementValidation_RejectsAnInitialPoseWithAStaleClientTimestamp()
        {
            InMemoryRepository repository = new(CreatePlayer());
            InMemoryMovementState movement = new();
            RecordingMovementCorrector corrector = new();
            MovementValidationService validator = new(
                repository,
                movement,
                corrector,
                new OpenMovementWorld(),
                new FixedMovementClock(100d),
                new MovementValidationOptions(5f, maximumReportAge: 0.25d, maximumFutureSkew: 0.1d));

            MovementVerdict verdict = validator.Validate(
                PlayerId,
                new MovementPose(WorldPosition.Origin, new MoveVector(1f, 0f), 90d, 1u));

            Assert.That(verdict.Accepted, Is.False,
                "An old client timestamp must not seed the authoritative movement history.");
            Assert.That(verdict.Code, Is.EqualTo(MovementVerdictCode.InvalidPose));
        }

        [Test]
        public void MovementValidation_RejectsStaleReportsBeforeGrantingDistanceBudget()
        {
            InMemoryRepository repository = new(CreatePlayer());
            MovementPose previous = new(
                WorldPosition.Origin,
                new MoveVector(1f, 0f),
                reportedAt: 90d,
                sequence: 1u);
            InMemoryMovementState movement = new(previous);
            RecordingMovementCorrector corrector = new();
            MovementValidationService validator = new(
                repository,
                movement,
                corrector,
                new OpenMovementWorld(),
                new FixedMovementClock(100d),
                new MovementValidationOptions(5f, maximumReportAge: 0.25d, maximumFutureSkew: 0.1d));

            MovementVerdict verdict = validator.Validate(
                PlayerId,
                new MovementPose(
                    new WorldPosition(1f, 0f, 0f),
                    new MoveVector(1f, 0f),
                    reportedAt: 90.25d,
                    sequence: 2u));

            Assert.That(verdict.Accepted, Is.False,
                "A stale client delta must not create movement budget when no server time has elapsed.");
            Assert.That(verdict.Code, Is.EqualTo(MovementVerdictCode.InvalidPose));
            Assert.That(corrector.Corrections, Is.EqualTo(1));
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

        private sealed class FixedMovementClock : IMovementClock
        {
            public FixedMovementClock(double now) => Now = now;
            public double Now { get; }
        }

        private sealed class InMemoryMovementState : IMovementState
        {
            private bool hasPose;
            private MovementPose pose;

            public InMemoryMovementState() { }

            public InMemoryMovementState(MovementPose pose)
            {
                this.pose = pose;
                hasPose = true;
            }

            public bool TryGetLastAcceptedPose(EntityId playerId, out MovementPose result)
            {
                result = pose;
                return hasPose && playerId == PlayerId;
            }

            public void CommitAcceptedPose(EntityId playerId, MovementPose accepted)
            {
                if (playerId != PlayerId) return;
                pose = accepted;
                hasPose = true;
            }
        }

        private sealed class RecordingMovementCorrector : IMovementCorrector
        {
            public int Corrections { get; private set; }
            public void ForcePose(EntityId playerId, MovementPose pose) => Corrections++;
        }

        private sealed class OpenMovementWorld : IMovementWorldQuery
        {
            public bool IsInsideBounds(WorldPosition position) => true;
            public bool IsPathClear(WorldPosition from, WorldPosition to) => true;
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
