using System;
using System.Collections.Generic;

namespace VampireHunt.Economy
{
    public readonly struct UsableItemSlot
    {
        public int SlotIndex { get; }
        public uint ItemId { get; }
        public int Quantity { get; }

        public bool IsEmpty => ItemId == 0 || Quantity <= 0;

        public UsableItemSlot(int slotIndex, uint itemId, int quantity)
        {
            SlotIndex = slotIndex;
            ItemId = itemId;
            Quantity = Math.Max(0, quantity);
        }
    }

    public sealed class UsableItemInventory
    {
        private readonly UsableItemSlot[] m_Slots;

        public uint Revision { get; private set; }
        public int Capacity => m_Slots.Length;
        public int OccupiedSlotCount { get; private set; }

        public UsableItemInventory(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Slots = new UsableItemSlot[capacity];
            for (int i = 0; i < m_Slots.Length; i++) m_Slots[i] = EmptySlot(i);
        }

        public bool TryGetSlot(int slotIndex, out UsableItemSlot slot)
        {
            if (slotIndex < 0 || slotIndex >= m_Slots.Length)
            {
                slot = default;
                return false;
            }
            slot = m_Slots[slotIndex];
            return true;
        }

        public int GetTotalQuantity(uint itemId)
        {
            if (itemId == 0) return 0;
            int total = 0;
            for (int i = 0; i < m_Slots.Length; i++)
                if (m_Slots[i].ItemId == itemId) total += m_Slots[i].Quantity;
            return total;
        }

        public bool CanAdd(UsableItemDefinition definition, int quantity)
        {
            if (definition == null || quantity <= 0) return false;
            long free = 0;
            for (int i = 0; i < m_Slots.Length; i++)
            {
                UsableItemSlot slot = m_Slots[i];
                if (slot.IsEmpty) free += definition.MaxStackPerSlot;
                else if (slot.ItemId == definition.ItemId)
                    free += Math.Max(0, definition.MaxStackPerSlot - slot.Quantity);
                if (free >= quantity) return true;
            }
            return false;
        }

        public bool TryAdd(UsableItemDefinition definition, int quantity)
        {
            if (!CanAdd(definition, quantity)) return false;
            int remaining = quantity;

            for (int i = 0; i < m_Slots.Length && remaining > 0; i++)
            {
                UsableItemSlot slot = m_Slots[i];
                if (slot.IsEmpty || slot.ItemId != definition.ItemId ||
                    slot.Quantity >= definition.MaxStackPerSlot) continue;
                int added = Math.Min(remaining, definition.MaxStackPerSlot - slot.Quantity);
                m_Slots[i] = new UsableItemSlot(i, slot.ItemId, slot.Quantity + added);
                remaining -= added;
            }

            for (int i = 0; i < m_Slots.Length && remaining > 0; i++)
            {
                if (!m_Slots[i].IsEmpty) continue;
                int added = Math.Min(remaining, definition.MaxStackPerSlot);
                m_Slots[i] = new UsableItemSlot(i, definition.ItemId, added);
                OccupiedSlotCount++;
                remaining -= added;
            }

            Revision++;
            return remaining == 0;
        }

        public bool TryRemoveAt(int slotIndex, int quantity, out UsableItemSlot result)
        {
            result = default;
            if (quantity <= 0 || !TryGetSlot(slotIndex, out UsableItemSlot current) ||
                current.IsEmpty || current.Quantity < quantity) return false;

            int remaining = current.Quantity - quantity;
            if (remaining == 0)
            {
                m_Slots[slotIndex] = EmptySlot(slotIndex);
                OccupiedSlotCount--;
            }
            else
            {
                m_Slots[slotIndex] = new UsableItemSlot(slotIndex, current.ItemId, remaining);
            }

            Revision++;
            result = m_Slots[slotIndex];
            return true;
        }

        public bool TryRestoreSlot(
            int slotIndex,
            UsableItemDefinition definition,
            int quantity)
        {
            if (definition == null || slotIndex < 0 || slotIndex >= m_Slots.Length ||
                quantity <= 0 || quantity > definition.MaxStackPerSlot || !m_Slots[slotIndex].IsEmpty)
                return false;

            m_Slots[slotIndex] = new UsableItemSlot(slotIndex, definition.ItemId, quantity);
            OccupiedSlotCount++;
            Revision++;
            return true;
        }

        public void Capture(List<UsableItemSlot> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            output.AddRange(m_Slots);
        }

        public void Clear()
        {
            if (OccupiedSlotCount == 0) return;
            for (int i = 0; i < m_Slots.Length; i++) m_Slots[i] = EmptySlot(i);
            OccupiedSlotCount = 0;
            Revision++;
        }

        private static UsableItemSlot EmptySlot(int index) => new UsableItemSlot(index, 0, 0);
    }
}
