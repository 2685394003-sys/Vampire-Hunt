namespace VampireHunt.Contracts
{
    public enum AttributeModifierOperation : byte
    {
        Flat = 0,
        AdditivePercent = 1,
        Multiplicative = 2
    }

    /// <summary>One live contribution to a player's derived attribute value.</summary>
    public interface IAttributeModifier
    {
        int AttributeId { get; }
        AttributeModifierOperation Operation { get; }
        float Value { get; }
    }

    /// <summary>
    /// Registration and query boundary for derived player attributes. Resource mutations such as
    /// damage, healing and stamina consumption remain on their dedicated authoritative contracts.
    /// </summary>
    public interface IAttributeModifierTarget
    {
        bool RegisterAttributeModifier(IAttributeModifier modifier);
        bool UnregisterAttributeModifier(IAttributeModifier modifier);
        float ResolveAttributeValue(int attributeId, float baseValue);
        float GetFinalAttributeValue(int attributeId);
    }
}
