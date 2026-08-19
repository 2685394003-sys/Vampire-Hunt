using VampireHunt.Core;

namespace VampireHunt.Enemies.Contracts
{
    /// <summary>
    /// Authoritative one-shot event emitted after an enemy death has been
    /// settled. It is independent from view lifetime and therefore safe when
    /// the pooled enemy has already been returned.
    /// </summary>
    public readonly struct EnemyDeathEvent : IGameplayEvent
    {
        public EnemyDeathEvent(ulong eventId, double occurredAt, EntityId enemyId, EntityId killerId, WorldPosition position)
        {
            EventId = eventId;
            OccurredAt = occurredAt;
            EnemyId = enemyId;
            KillerId = killerId;
            Position = position;
        }

        public ulong EventId { get; }
        public double OccurredAt { get; }
        public EntityId EnemyId { get; }
        public EntityId KillerId { get; }
        public WorldPosition Position { get; }
    }
}
