using System;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;

namespace VampireHunt.Enemies.Application
{
    public readonly struct EnemyDeathResult
    {
        public EnemyDeathResult(bool settled, EntityId enemyId, EntityId killerId, RewardGrant reward)
        {
            Settled = settled;
            EnemyId = enemyId;
            KillerId = killerId;
            Reward = reward;
        }

        public bool Settled { get; }
        public EntityId EnemyId { get; }
        public EntityId KillerId { get; }
        public RewardGrant Reward { get; }
    }

    /// <summary>
    /// Settles an enemy death exactly once. Reward delivery and the lifecycle
    /// event are independent of pooled presentation object lifetime.
    /// </summary>
    public sealed class EnemyDeathService
    {
        private readonly IEnemyRepository repository;
        private readonly IRewardService rewardService;
        private readonly IGameplayEventSink eventSink;
        private readonly IGameClock clock;
        private readonly GameplayEventIdAllocator eventIds;

        public EnemyDeathService(
            IEnemyRepository repository,
            IRewardService rewardService,
            IGameplayEventSink eventSink = null,
            IGameClock clock = null,
            GameplayEventIdAllocator eventIds = null)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.rewardService = rewardService ?? throw new ArgumentNullException(nameof(rewardService));
            this.eventSink = eventSink;
            this.clock = clock;
            this.eventIds = eventIds ?? new GameplayEventIdAllocator();
        }

        public EnemyDeathResult HandleDeath(EntityId enemyId, EntityId killerId)
        {
            if (!repository.TryGet(enemyId, out Domain.EnemyAggregate enemy) || enemy == null)
            {
                return new EnemyDeathResult(false, enemyId, killerId, null);
            }

            if (!enemy.TryConsumeDeath())
            {
                return new EnemyDeathResult(false, enemyId, killerId, null);
            }

            double now = clock == null ? 0d : clock.Now;
            DeathContext context = new DeathContext(enemyId, killerId, enemy.Position, now);
            RewardGrant reward = enemy.RewardPolicy.CreateReward(in context);
            if (killerId.IsValid && reward != null && !reward.IsEmpty)
            {
                rewardService.Grant(killerId, reward);
            }

            if (eventSink != null)
            {
                eventSink.Publish(new EnemyDeathEvent(eventIds.Next(), now, enemyId, killerId, enemy.Position));
            }

            return new EnemyDeathResult(true, enemyId, killerId, reward);
        }
    }
}
