using System;
using VampireHunt.Enemies.Contracts;

namespace VampireHunt.Enemies.Domain
{
    public sealed class EnemyRewardPolicy
    {
        private RewardGrant _reward;

        public EnemyRewardPolicy(RewardGrant reward)
        {
            _reward = reward ?? throw new ArgumentNullException(nameof(reward));
        }

        public RewardGrant CreateReward(in DeathContext deathContext)
        {
            // The context is intentionally accepted so future reward rules can
            // use killer/type without coupling the policy to Player concrete types.
            return _reward;
        }

        public void Reset(RewardGrant reward)
        {
            _reward = reward ?? throw new ArgumentNullException(nameof(reward));
        }
    }
}
