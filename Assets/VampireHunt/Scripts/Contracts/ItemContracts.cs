namespace VampireHunt.Contracts
{
    using VampireHunt.Economy;

    /// <summary>
    /// Server-side inventory mutation boundary used by rewards, pickups and future shop transactions.
    /// Callers submit stable definition IDs and never mutate replicated collections directly.
    /// </summary>
    public interface IPlayerItemInventory
    {
        bool TryGrantUsable(uint itemId, int quantity);
        bool TryConsumeUsable(int slotIndex, int quantity);
        bool TryGrantAccessory(uint accessoryId, int quantity);
        bool TryRemoveAccessory(uint accessoryId, int quantity);
    }

    /// <summary>Server-side view used by the consumable application service.</summary>
    public interface IUsableItemUseInventory
    {
        bool TryGetUsableForUse(int slotIndex, out UsableItemDefinition definition);
        bool TryConsumeUsable(int slotIndex, int quantity);
    }
}
