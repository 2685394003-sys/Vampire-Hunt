using System;
using System.Collections.Generic;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using VampireHunt.Player.Domain;

namespace VampireHunt.Player.Application
{
    public sealed class PlayerProgressionService : IPlayerProgressionCommands
    {
        private readonly IPlayerRepository repository;

        public PlayerProgressionService(IPlayerRepository repository)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public bool GrantReward(EntityId playerId, RewardGrant reward)
        {
            if (!repository.TryGet(playerId, out PlayerAggregate player) || !player.IsAlive)
                return false;
            if (reward.Scarlet < 0 || reward.Coins < 0 || reward.Experience < 0 || reward.SharedScarlet < 0)
                return false;

            player.Progression.AddScarlet(reward.Scarlet);
            player.Progression.AddCoins(reward.Coins);
            player.Progression.AddExperience(reward.Experience);
            if (reward.SharedScarlet > 0) GrantSharedScarlet(reward.SharedScarlet);
            return true;
        }

        /// <summary>
        /// Splits shared Scarlet exactly among currently alive players. The first
        /// players in repository order receive the remainder, so no resource is lost.
        /// </summary>
        public int GrantSharedScarlet(int amount)
        {
            if (amount <= 0) return 0;
            List<PlayerAggregate> players = new();
            repository.GetAllAlive(players);
            players.RemoveAll(player => player == null || !player.IsAlive);
            if (players.Count == 0) return 0;

            int share = amount / players.Count;
            int remainder = amount % players.Count;
            for (int i = 0; i < players.Count; i++)
                players[i].Progression.AddScarlet(share + (i < remainder ? 1 : 0));
            return amount;
        }

        public int ShareScarlet(int amount) => GrantSharedScarlet(amount);
    }
}
