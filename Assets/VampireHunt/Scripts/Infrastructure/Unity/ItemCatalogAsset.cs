using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Economy;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "Vampire Hunt/Items/Item Catalog")]
    public sealed class ItemCatalogAsset : ScriptableObject
    {
        [SerializeField] private ItemDefinitionAsset[] definitions;

        private ItemCatalog m_CachedCatalog;
        private readonly Dictionary<uint, ItemDefinitionAsset> m_AssetById =
            new Dictionary<uint, ItemDefinitionAsset>();

        public ItemCatalog CreateCatalog()
        {
            if (m_CachedCatalog != null) return m_CachedCatalog;
            var domainDefinitions = new List<ItemDefinition>();
            m_AssetById.Clear();
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    ItemDefinitionAsset asset = definitions[i];
                    if (asset == null) continue;
                    domainDefinitions.Add(asset.ToDomain());
                    m_AssetById.Add(asset.ItemId, asset);
                }
            }
            m_CachedCatalog = new ItemCatalog(domainDefinitions);
            return m_CachedCatalog;
        }

        public bool TryGetAsset(uint itemId, out ItemDefinitionAsset asset)
        {
            CreateCatalog();
            return m_AssetById.TryGetValue(itemId, out asset);
        }

        public bool TryGetUsableAsset(uint itemId, out UsableItemDefinitionAsset asset)
        {
            asset = null;
            return TryGetAsset(itemId, out ItemDefinitionAsset item) &&
                   (asset = item as UsableItemDefinitionAsset) != null;
        }

        public bool TryGetAccessoryAsset(uint itemId, out AccessoryDefinitionAsset asset)
        {
            asset = null;
            return TryGetAsset(itemId, out ItemDefinitionAsset item) &&
                   (asset = item as AccessoryDefinitionAsset) != null;
        }

        private void OnValidate()
        {
            m_CachedCatalog = null;
            m_AssetById.Clear();
        }
    }
}
