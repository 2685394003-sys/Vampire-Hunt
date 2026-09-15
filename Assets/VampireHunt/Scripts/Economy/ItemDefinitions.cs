using System;
using VampireHunt.Effects;

namespace VampireHunt.Economy
{
    public enum ItemKind : byte
    {
        Usable = 1,
        Accessory = 2
    }

    public enum UseConsumptionPolicy : byte
    {
        ConsumeOnSuccess = 1,
        RetainOnUse = 2
    }

    public enum AccessoryStackPolicy : byte
    {
        Unique = 1,
        Limited = 2,
        Unlimited = 3
    }

    public abstract class ItemDefinition
    {
        public uint ItemId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public ItemKind Kind { get; }

        protected ItemDefinition(uint itemId, string displayName, string description, ItemKind kind)
        {
            if (itemId == 0) throw new ArgumentOutOfRangeException(nameof(itemId));
            ItemId = itemId;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            Kind = kind;
        }
    }

    public sealed class UsableItemDefinition : ItemDefinition
    {
        public UseConsumptionPolicy ConsumptionPolicy { get; }
        public int MaxStackPerSlot { get; }
        public float CooldownSeconds { get; }

        public UsableItemDefinition(
            uint itemId,
            string displayName,
            string description,
            UseConsumptionPolicy consumptionPolicy,
            int maxStackPerSlot,
            float cooldownSeconds)
            : base(itemId, displayName, description, ItemKind.Usable)
        {
            ConsumptionPolicy = consumptionPolicy;
            MaxStackPerSlot = consumptionPolicy == UseConsumptionPolicy.RetainOnUse
                ? 1
                : Math.Max(1, maxStackPerSlot);
            CooldownSeconds = Math.Max(0f, cooldownSeconds);
        }
    }

    public sealed class AccessoryDefinition : ItemDefinition
    {
        public AccessoryStackPolicy StackPolicy { get; }
        public int MaxStacks { get; }
        public EffectDefinition RuntimeEffects { get; }

        public AccessoryDefinition(
            uint itemId,
            string displayName,
            string description,
            AccessoryStackPolicy stackPolicy,
            int maxStacks,
            IEffectModuleDescriptor[] effectModules)
            : base(itemId, displayName, description, ItemKind.Accessory)
        {
            StackPolicy = stackPolicy;
            MaxStacks = stackPolicy == AccessoryStackPolicy.Unique
                ? 1
                : stackPolicy == AccessoryStackPolicy.Unlimited
                    ? int.MaxValue
                    : Math.Max(1, maxStacks);
            RuntimeEffects = new EffectDefinition(
                EffectSourceKind.Equipment,
                itemId,
                effectModules ?? Array.Empty<IEffectModuleDescriptor>());
        }
    }
}
