using VampireHunt.SharedKernel;

namespace VampireHunt.Contracts
{
    /// <summary>
    /// Small cross-module reward port. Enemy infrastructure grants a combat
    /// reward without depending on the player's concrete stats adapter.
    /// </summary>
    public interface IScarletRewardReceiver
    {
        bool TryGrantScarlet(float amount, EntityId sourceEntityId);
    }
}
