using VampireHunt.Core;

namespace VampireHunt.Player.Contracts
{
    public sealed class PlayerDeathEvent : GameplayEventBase
    {
        public EntityId PlayerId { get; }
        public EntityId KillerId { get; }

        public PlayerDeathEvent(
            ulong eventId,
            double occurredAt,
            EntityId playerId,
            EntityId killerId)
            : base(eventId, occurredAt)
        {
            if (!playerId.IsValid) throw new System.ArgumentException("A valid player id is required.", nameof(playerId));
            PlayerId = playerId;
            KillerId = killerId;
        }

        public PlayerDeathEvent(EntityId playerId, EntityId killerId, double occurredAt = 0d)
            : this(0UL, occurredAt, playerId, killerId)
        {
        }
    }
}
