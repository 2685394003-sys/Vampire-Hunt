using System;
using System.Collections.Generic;

namespace VampireHunt.Economy
{
    public readonly struct AccessoryStack
    {
        public uint AccessoryId { get; }
        public int Stacks { get; }

        public AccessoryStack(uint accessoryId, int stacks)
        {
            AccessoryId = accessoryId;
            Stacks = Math.Max(0, stacks);
        }
    }

    public sealed class AccessoryInventory
    {
        private readonly Dictionary<uint, int> m_Stacks = new Dictionary<uint, int>();

        public uint Revision { get; private set; }
        public int DistinctCount => m_Stacks.Count;

        public int GetStacks(uint accessoryId) =>
            m_Stacks.TryGetValue(accessoryId, out int stacks) ? stacks : 0;

        public bool CanAdd(AccessoryDefinition definition, int quantity = 1)
        {
            if (definition == null || quantity <= 0) return false;
            int current = GetStacks(definition.ItemId);
            return current <= definition.MaxStacks - quantity;
        }

        public bool TryAddOrStack(
            AccessoryDefinition definition,
            int quantity,
            out AccessoryStack result)
        {
            result = default;
            if (!CanAdd(definition, quantity)) return false;
            int next = GetStacks(definition.ItemId) + quantity;
            m_Stacks[definition.ItemId] = next;
            Revision++;
            result = new AccessoryStack(definition.ItemId, next);
            return true;
        }

        public bool TryRemove(uint accessoryId, int quantity, out AccessoryStack result)
        {
            result = default;
            if (accessoryId == 0 || quantity <= 0 || !m_Stacks.TryGetValue(accessoryId, out int current) ||
                current < quantity) return false;

            int next = current - quantity;
            if (next == 0) m_Stacks.Remove(accessoryId);
            else m_Stacks[accessoryId] = next;
            Revision++;
            result = new AccessoryStack(accessoryId, next);
            return true;
        }

        public bool TryRestore(AccessoryDefinition definition, int stacks)
        {
            if (definition == null || stacks <= 0 || stacks > definition.MaxStacks) return false;
            m_Stacks[definition.ItemId] = stacks;
            Revision++;
            return true;
        }

        public void Capture(List<AccessoryStack> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            foreach (KeyValuePair<uint, int> pair in m_Stacks)
                output.Add(new AccessoryStack(pair.Key, pair.Value));
            output.Sort((left, right) => left.AccessoryId.CompareTo(right.AccessoryId));
        }

        public void Clear()
        {
            if (m_Stacks.Count == 0) return;
            m_Stacks.Clear();
            Revision++;
        }
    }
}
