using System;
using System.Collections.Generic;

namespace VampireHunt.Economy
{
    public sealed class ItemCatalog
    {
        private readonly Dictionary<uint, ItemDefinition> m_ById =
            new Dictionary<uint, ItemDefinition>();
        private readonly List<ItemDefinition> m_All = new List<ItemDefinition>();

        public IReadOnlyList<ItemDefinition> All => m_All;

        public ItemCatalog(IEnumerable<ItemDefinition> definitions)
        {
            if (definitions == null) return;
            foreach (ItemDefinition definition in definitions)
            {
                if (definition == null) continue;
                if (m_ById.ContainsKey(definition.ItemId))
                    throw new InvalidOperationException($"Duplicate ItemId {definition.ItemId}.");
                m_ById.Add(definition.ItemId, definition);
                m_All.Add(definition);
            }
            m_All.Sort((left, right) => left.ItemId.CompareTo(right.ItemId));
        }

        public bool TryGet(uint itemId, out ItemDefinition definition) =>
            m_ById.TryGetValue(itemId, out definition);

        public bool TryGetUsable(uint itemId, out UsableItemDefinition definition)
        {
            definition = null;
            return m_ById.TryGetValue(itemId, out ItemDefinition item) &&
                   (definition = item as UsableItemDefinition) != null;
        }

        public bool TryGetAccessory(uint itemId, out AccessoryDefinition definition)
        {
            definition = null;
            return m_ById.TryGetValue(itemId, out ItemDefinition item) &&
                   (definition = item as AccessoryDefinition) != null;
        }
    }
}
