using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Progression;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(
        fileName = "EnemyAffixCatalog",
        menuName = "Vampire Hunt/Progression/Enemy Affix Catalog")]
    public sealed class EnemyAffixCatalogAsset : ScriptableObject
    {
        [SerializeField] private EnemyAffixDefinitionAsset[] definitions;

        private EnemyAffixCatalog m_CachedCatalog;
        private readonly Dictionary<uint, EnemyAffixDefinitionAsset> m_AssetById =
            new Dictionary<uint, EnemyAffixDefinitionAsset>();

        public EnemyAffixCatalog CreateCatalog()
        {
            if (m_CachedCatalog != null) return m_CachedCatalog;
            var domainDefinitions = new List<EnemyAffixDefinition>();
            m_AssetById.Clear();
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    EnemyAffixDefinitionAsset asset = definitions[i];
                    if (asset == null) continue;
                    domainDefinitions.Add(asset.ToDomain());
                    m_AssetById[asset.AffixId] = asset;
                }
            }
            m_CachedCatalog = new EnemyAffixCatalog(domainDefinitions);
            return m_CachedCatalog;
        }

        public bool TryGetAsset(uint affixId, out EnemyAffixDefinitionAsset asset)
        {
            CreateCatalog();
            return m_AssetById.TryGetValue(affixId, out asset);
        }

        private void OnValidate()
        {
            m_CachedCatalog = null;
            m_AssetById.Clear();
        }
    }
}
