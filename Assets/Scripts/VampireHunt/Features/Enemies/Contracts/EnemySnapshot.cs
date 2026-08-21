using VampireHunt.Core;

namespace VampireHunt.Enemies.Contracts
{
    /// <summary>Read-only state projection produced by an EnemyAggregate.</summary>
    public readonly struct EnemySnapshot
    {
        public EnemySnapshot(
            EntityId id,
            int health,
            int maxHealth,
            bool isAlive,
            EnemyState state,
            WorldPosition position,
            EntityId targetId,
            double lastAttackAt)
            : this(
                id,
                health,
                maxHealth,
                isAlive,
                state,
                position,
                targetId,
                default(WorldPosition),
                lastAttackAt)
        {
        }

        public EnemySnapshot(
            EntityId id,
            int health,
            int maxHealth,
            bool isAlive,
            EnemyState state,
            WorldPosition position,
            EntityId targetId,
            WorldPosition targetPosition,
            double lastAttackAt)
        {
            Id = id;
            Health = health;
            MaxHealth = maxHealth;
            IsAlive = isAlive;
            State = state;
            Position = position;
            TargetId = targetId;
            TargetPosition = targetPosition;
            LastAttackAt = lastAttackAt;
        }

        public EntityId Id { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public bool IsAlive { get; }
        public EnemyState State { get; }
        public WorldPosition Position { get; }
        public EntityId TargetId { get; }
        public WorldPosition TargetPosition { get; }
        public double LastAttackAt { get; }
    }

    public interface IEnemyReadModel
    {
        int Health { get; }
        bool IsAlive { get; }
        EnemyState State { get; }
        WorldPosition Position { get; }
    }
}
