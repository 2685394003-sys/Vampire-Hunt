using VampireHunt.Core;

namespace VampireHunt.Enemies.Contracts
{
    public readonly struct DeathContext
    {
        public DeathContext(EntityId enemyId, EntityId killerId, WorldPosition position, double occurredAt)
        {
            EnemyId = enemyId;
            KillerId = killerId;
            Position = position;
            OccurredAt = occurredAt;
        }

        public EntityId EnemyId { get; }
        public EntityId KillerId { get; }
        public WorldPosition Position { get; }
        public double OccurredAt { get; }
    }
}
