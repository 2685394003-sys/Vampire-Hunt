namespace VampireHunt.Contracts
{
    public enum CapacityChangePolicy : byte
    {
        PreserveRatio = 0,
        PreserveCurrent = 1,
        GrantIncreaseOnly = 2
    }

    public interface IResourceCapacityModifier
    {
        int ResourceId { get; }
        AttributeModifierOperation Operation { get; }
        float Value { get; }
        CapacityChangePolicy ChangePolicy { get; }
    }

    /// <summary>Server-owned registration boundary for Health/Stamina runtime capacities.</summary>
    public interface IResourceCapacityModifierTarget
    {
        bool RegisterCapacityModifier(IResourceCapacityModifier modifier);
        bool UpdateCapacityModifier(IResourceCapacityModifier modifier);
        bool UnregisterCapacityModifier(IResourceCapacityModifier modifier);
    }
}
