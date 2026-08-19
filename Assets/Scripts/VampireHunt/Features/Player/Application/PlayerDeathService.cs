using System;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using VampireHunt.Player.Domain;

namespace VampireHunt.Player.Application
{
    /// <summary>One-shot death orchestration; Combat only reports the lethal result.</summary>
    public sealed class PlayerDeathService
    {
        private readonly IPlayerRepository repository;
        private readonly IPlayerDeathSink events;

        internal PlayerDeathService(IPlayerRepository repository, IPlayerDeathSink events = null)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.events = events;
        }

        public bool HandleDeath(EntityId playerId, EntityId killerId = default, double occurredAt = 0d)
        {
            if (!repository.TryGet(playerId, out PlayerAggregate player)) return false;
            if (!player.TryMarkDeathHandled()) return false;
            events?.Publish(new PlayerDeathEvent(playerId, killerId, occurredAt));
            return true;
        }
    }
}
