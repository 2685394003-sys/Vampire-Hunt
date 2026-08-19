using VampireHunt.Core;

namespace VampireHunt.Enemies.Contracts
{
    /// <summary>Output port for progression/currency integration.</summary>
    public interface IRewardService
    {
        void Grant(EntityId recipientId, RewardGrant reward);
    }
}
