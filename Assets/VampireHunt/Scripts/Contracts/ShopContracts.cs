namespace VampireHunt.Contracts
{
    public interface ICurrencyWallet
    {
        int Balance { get; }
        bool CanAfford(int amount);
        bool TrySpend(int amount);
        void Credit(int amount);
    }

    /// <summary>Implemented by the owner player bridge and consumed by world interaction adapters.</summary>
    public interface IShopInteractionGateway
    {
        bool OpenShop(uint shopId, ulong shopInstanceId);
    }
}
