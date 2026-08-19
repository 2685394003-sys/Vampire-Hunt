using System;
using VampireHunt.Core;
using EnemyRewardGrant = VampireHunt.Enemies.Contracts.RewardGrant;
using EnemyRewardItem = VampireHunt.Enemies.Contracts.RewardItem;
using PlayerRewardGrant = VampireHunt.Player.Contracts.RewardGrant;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Converts an enemy reward payload into the Player progression command
    /// without making the Enemies module depend on Player.
    /// </summary>
    public sealed class PlayerRewardAdapter : VampireHunt.Enemies.Contracts.IRewardService
    {
        private readonly VampireHunt.Player.Contracts.IPlayerProgressionCommands progression;
        private readonly IRewardItemMapper itemMapper;

        public PlayerRewardAdapter(
            VampireHunt.Player.Contracts.IPlayerProgressionCommands progression,
            IRewardItemMapper itemMapper = null)
        {
            this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
            this.itemMapper = itemMapper;
        }

        public void Grant(EntityId recipientId, EnemyRewardGrant reward)
        {
            if (!recipientId.IsValid) throw new ArgumentException("A valid reward recipient is required.", nameof(recipientId));
            if (reward == null) throw new ArgumentNullException(nameof(reward));

            int scarlet = reward.Scarlet;
            int coins = reward.Coins;
            int experience = reward.Experience;
            int sharedScarlet = 0;

            for (int i = 0; i < reward.Items.Count; i++)
            {
                EnemyRewardItem item = reward.Items[i];
                if (itemMapper == null)
                {
                    throw new InvalidOperationException(
                        $"Reward item kind {item.Kind} cannot be converted without an {nameof(IRewardItemMapper)}.");
                }

                if (!itemMapper.TryMap(item, out RewardItemValue mapped))
                {
                    throw new InvalidOperationException($"Reward item kind {item.Kind} was rejected by the mapper.");
                }

                try
                {
                    scarlet = checked(scarlet + mapped.Scarlet);
                    coins = checked(coins + mapped.Coins);
                    experience = checked(experience + mapped.Experience);
                    sharedScarlet = checked(sharedScarlet + mapped.SharedScarlet);
                }
                catch (OverflowException exception)
                {
                    throw new InvalidOperationException("Mapped reward values exceed the progression range.", exception);
                }
            }

            // IRewardService is intentionally fire-and-forget. The progression
            // port remains responsible for acceptance rules such as player life
            // state or a server-side currency cap.
            progression.GrantReward(
                recipientId,
                new PlayerRewardGrant(recipientId, scarlet, coins, experience, sharedScarlet));
        }

        /// <summary>Forwards a shared Scarlet request to the Player progression port.</summary>
        public int GrantSharedScarlet(int amount) => progression.GrantSharedScarlet(amount);
    }

    /// <summary>
    /// Explicit conversion boundary for reward item kinds that are authored by
    /// a feature outside the shared scalar reward fields.
    /// </summary>
    public interface IRewardItemMapper
    {
        bool TryMap(EnemyRewardItem item, out RewardItemValue value);
    }

    public readonly struct RewardItemValue
    {
        public RewardItemValue(int scarlet, int coins = 0, int experience = 0, int sharedScarlet = 0)
        {
            if (scarlet < 0) throw new ArgumentOutOfRangeException(nameof(scarlet));
            if (coins < 0) throw new ArgumentOutOfRangeException(nameof(coins));
            if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
            if (sharedScarlet < 0) throw new ArgumentOutOfRangeException(nameof(sharedScarlet));
            Scarlet = scarlet;
            Coins = coins;
            Experience = experience;
            SharedScarlet = sharedScarlet;
        }

        public int Scarlet { get; }
        public int Coins { get; }
        public int Experience { get; }
        public int SharedScarlet { get; }
    }
}
