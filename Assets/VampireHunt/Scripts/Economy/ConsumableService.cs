using System;
using System.Collections.Generic;
using VampireHunt.Contracts;

namespace VampireHunt.Economy
{
    public enum ItemUseFailureReason : byte
    {
        None = 0,
        InvalidSlot = 1,
        SlotUnavailable = 2,
        NoConfiguredEffects = 3,
        Cooldown = 4,
        EffectUnavailable = 5,
        EffectFailed = 6,
        ConsumptionFailed = 7,
        ActionBlocked = 8
    }

    public readonly struct ItemUseResult
    {
        public int SlotIndex { get; }
        public uint ItemId { get; }
        public bool Success { get; }
        public ItemUseFailureReason FailureReason { get; }
        public double NextReadyTime { get; }

        public ItemUseResult(
            int slotIndex,
            uint itemId,
            bool success,
            ItemUseFailureReason failureReason,
            double nextReadyTime)
        {
            SlotIndex = slotIndex;
            ItemId = itemId;
            Success = success;
            FailureReason = failureReason;
            NextReadyTime = nextReadyTime;
        }

        public static ItemUseResult Failed(
            int slotIndex,
            uint itemId,
            ItemUseFailureReason reason,
            double nextReadyTime = 0d) =>
            new ItemUseResult(slotIndex, itemId, false, reason, nextReadyTime);
    }

    public interface IUsableItemEffectExecutor
    {
        bool HasConfiguredEffects(uint itemId);
        bool CanApplyAny(uint itemId);
        bool TryApplyConfiguredEffects(uint itemId);
    }

    /// <summary>
    /// Synchronous server-side use transaction. Effects are validated before execution;
    /// successful consumables are removed and cooldown starts only after the effect commits.
    /// </summary>
    public sealed class ConsumableService
    {
        private readonly IUsableItemUseInventory m_Inventory;
        private readonly IUsableItemEffectExecutor m_Effects;
        private readonly Dictionary<uint, double> m_NextReadyByItemId =
            new Dictionary<uint, double>();

        public ConsumableService(
            IUsableItemUseInventory inventory,
            IUsableItemEffectExecutor effects)
        {
            m_Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            m_Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        }

        public ItemUseResult TryUse(int slotIndex, double serverTime)
        {
            if (slotIndex < 0 || double.IsNaN(serverTime) || double.IsInfinity(serverTime))
                return ItemUseResult.Failed(slotIndex, 0, ItemUseFailureReason.InvalidSlot);

            if (!m_Inventory.TryGetUsableForUse(slotIndex, out UsableItemDefinition definition))
                return ItemUseResult.Failed(slotIndex, 0, ItemUseFailureReason.SlotUnavailable);

            if (!m_Effects.HasConfiguredEffects(definition.ItemId))
                return ItemUseResult.Failed(
                    slotIndex, definition.ItemId, ItemUseFailureReason.NoConfiguredEffects);

            double nextReady = GetNextReadyTime(definition.ItemId);
            if (serverTime < nextReady)
                return ItemUseResult.Failed(
                    slotIndex, definition.ItemId, ItemUseFailureReason.Cooldown, nextReady);

            if (!m_Effects.CanApplyAny(definition.ItemId))
                return ItemUseResult.Failed(
                    slotIndex, definition.ItemId, ItemUseFailureReason.EffectUnavailable);

            if (!m_Effects.TryApplyConfiguredEffects(definition.ItemId))
                return ItemUseResult.Failed(
                    slotIndex, definition.ItemId, ItemUseFailureReason.EffectFailed);

            if (definition.ConsumptionPolicy == UseConsumptionPolicy.ConsumeOnSuccess &&
                !m_Inventory.TryConsumeUsable(slotIndex, 1))
                return ItemUseResult.Failed(
                    slotIndex, definition.ItemId, ItemUseFailureReason.ConsumptionFailed);

            nextReady = serverTime + definition.CooldownSeconds;
            if (definition.CooldownSeconds > 0f) m_NextReadyByItemId[definition.ItemId] = nextReady;
            else m_NextReadyByItemId.Remove(definition.ItemId);

            return new ItemUseResult(
                slotIndex,
                definition.ItemId,
                true,
                ItemUseFailureReason.None,
                nextReady);
        }

        public double GetNextReadyTime(uint itemId) =>
            itemId != 0 && m_NextReadyByItemId.TryGetValue(itemId, out double time) ? time : 0d;

        public void ClearCooldowns() => m_NextReadyByItemId.Clear();
    }
}
