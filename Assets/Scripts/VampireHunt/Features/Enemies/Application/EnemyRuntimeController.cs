using System;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Enemies.Domain;
using VampireHunt.Navigation.Contracts;

namespace VampireHunt.Enemies.Application
{
    /// <summary>
    /// Public application facade used by legacy Unity/NGO views during the
    /// incremental migration.  Mutable state remains in EnemyAggregate; this
    /// type only exposes Contracts and keeps pool/death transitions explicit.
    /// </summary>
    public sealed class EnemyRuntimeController : IEnemyRuntime, IDamageReceiver, IHealingReceiver
    {
        private readonly INavigationField navigation;
        private readonly ICombatTargetQuery targetQuery;
        private readonly EntityIdAllocator entityIds;
        private readonly IRewardService rewardService;
        private readonly IGameplayEventSink eventSink;
        private readonly IGameClock clock;
        private readonly GameplayEventIdAllocator eventIds;
        private readonly IEnemyRepository repository;
        private readonly EnemyDeathService deathService;
        private EnemyAggregate aggregate;

        public EnemyRuntimeController(
            INavigationField navigation,
            EntityIdAllocator entityIds = null,
            ICombatTargetQuery targetQuery = null,
            IRewardService rewardService = null,
            IGameplayEventSink eventSink = null,
            IGameClock clock = null,
            GameplayEventIdAllocator eventIds = null)
        {
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            this.entityIds = entityIds ?? new EntityIdAllocator();
            this.targetQuery = targetQuery;
            this.rewardService = rewardService ?? new NoopRewardService();
            this.eventSink = eventSink;
            this.clock = clock;
            this.eventIds = eventIds ?? new GameplayEventIdAllocator();
            repository = new SingleEnemyRepository(this);
            deathService = new EnemyDeathService(repository, this.rewardService, eventSink, clock, this.eventIds);
        }

        public EntityId Id => aggregate == null ? EntityId.Invalid : aggregate.Id;
        public bool IsSpawned => aggregate != null;
        public float MoveSpeed => aggregate == null ? 0f : aggregate.Brain.MoveSpeed;
        public EnemySnapshot Snapshot => aggregate == null
            ? default(EnemySnapshot)
            : aggregate.CreateSnapshot();
        public IDamageReceiver DamageReceiver => aggregate == null ? null : aggregate.Vitals;
        public IHealingReceiver HealingReceiver => aggregate == null ? null : aggregate.Vitals;

        public void ResetForSpawn(EnemySpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            ResetForSpawn(entityIds.Allocate(), spec);
        }

        public void ResetForSpawn(EntityId freshId, EnemySpec spec)
        {
            if (!freshId.IsValid) throw new ArgumentException("A fresh EntityId is required.", nameof(freshId));
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            if (aggregate == null)
            {
                aggregate = new EnemyAggregate(freshId, spec, navigation, targetQuery, null, entityIds);
                return;
            }

            aggregate.ResetForSpawn(freshId, spec);
        }

        public void ResetForDespawn()
        {
            // The old logical id is deliberately not retained as an active
            // life.  ResetForSpawn allocates or accepts a completely new id.
            aggregate = null;
        }

        public DamageResult ApplyDamage(in ResolvedDamage damage)
        {
            if (aggregate == null || !aggregate.IsAlive || damage.TargetId != aggregate.Id)
            {
                return DamageResult.NoDamage(Math.Max(0, damage.FinalDamage), damage.Hit.Position);
            }

            return aggregate.ApplyDamage(in damage);
        }

        public int ApplyHealing(int amount)
        {
            return aggregate == null ? 0 : aggregate.Vitals.ApplyHealing(amount);
        }

        public EnemyIntent Decide(in EnemyPerceptionData perception)
        {
            if (aggregate == null || !aggregate.IsAlive)
            {
                return EnemyIntent.Idle(perception.SelfId);
            }

            EnemyPerception domainPerception = new EnemyPerception(
                perception.SelfId,
                perception.SelfPosition,
                perception.TargetId,
                perception.TargetPosition,
                perception.Distance,
                perception.HasLineOfTravel,
                perception.TargetIsAlive);
            EnemyIntent intent = aggregate.Brain.Decide(in domainPerception);
            aggregate.SetTarget(intent.TargetId);
            aggregate.SetState(intent.State);
            return intent;
        }

        public AttackIntent CreateAttackIntent(in EnemyCombatContextData context)
        {
            if (aggregate == null || !aggregate.IsAlive || context.SourceId != aggregate.Id)
            {
                return default(AttackIntent);
            }

            EnemyCombatContext internalContext = new EnemyCombatContext(
                context.SourceId,
                context.TargetId,
                context.HitPosition,
                context.Distance,
                context.TargetIsAlive,
                context.Now,
                context.LastAttackAt);
            return aggregate.CombatPolicy.CreateAttackIntent(in internalContext);
        }

        public EnemyDeathResultData SettleDeath(EntityId killerId)
        {
            EnemyDeathResult result = deathService.HandleDeath(Id, killerId);
            return new EnemyDeathResultData(result.Settled, result.EnemyId, result.KillerId, result.Reward);
        }

        public void SetPosition(WorldPosition position)
        {
            aggregate?.SetPosition(position);
        }

        public void SetState(EnemyState state)
        {
            aggregate?.SetState(state);
        }

        public void RecordAttack(double occurredAt)
        {
            aggregate?.RecordAttack(occurredAt);
        }

        public DamageResult ApplyDamage(in ResolvedDamage damage, double authoritativeNow)
        {
            // Enemy vitals currently have no time-dependent invulnerability
            // window.  Keep the overload so the adapter can be registered as
            // an authoritative Combat receiver without inventing a timestamp.
            return ApplyDamage(in damage);
        }

        private sealed class SingleEnemyRepository : IEnemyRepository
        {
            private readonly EnemyRuntimeController owner;

            public SingleEnemyRepository(EnemyRuntimeController owner)
            {
                this.owner = owner;
            }

            public EnemyAggregate Get(EntityId enemyId)
            {
                return TryGet(enemyId, out EnemyAggregate enemy)
                    ? enemy
                    : throw new InvalidOperationException("Enemy lifetime is not active.");
            }

            public bool TryGet(EntityId enemyId, out EnemyAggregate enemy)
            {
                enemy = owner.aggregate;
                return enemy != null && enemy.Id == enemyId;
            }
        }

        private sealed class NoopRewardService : IRewardService
        {
            public void Grant(EntityId recipientId, RewardGrant reward)
            {
                // Compatibility views can run without a Player composition;
                // the configured Integration adapter remains authoritative.
            }
        }
    }
}
