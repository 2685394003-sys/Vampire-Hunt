using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Progression;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "PactCatalog", menuName = "Vampire Hunt/Progression/Pact Catalog")]
    public sealed class PactCatalogAsset : ScriptableObject
    {
        [SerializeField] private PactDefinitionAsset[] definitions;

        private PactCatalog m_CachedCatalog;
        private readonly Dictionary<uint, PactDefinitionAsset> m_AssetById =
            new Dictionary<uint, PactDefinitionAsset>();

        public PactCatalog CreateCatalog()
        {
            if (m_CachedCatalog != null) return m_CachedCatalog;
            var domainDefinitions = new List<PactDefinition>();
            m_AssetById.Clear();
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    PactDefinitionAsset asset = definitions[i];
                    if (asset == null) continue;
                    domainDefinitions.Add(asset.ToDomain());
                    m_AssetById[asset.PactId] = asset;
                }
            }
            m_CachedCatalog = new PactCatalog(domainDefinitions);
            return m_CachedCatalog;
        }

        public bool TryGetAsset(uint pactId, out PactDefinitionAsset asset)
        {
            CreateCatalog();
            return m_AssetById.TryGetValue(pactId, out asset);
        }

        private void OnValidate()
        {
            m_CachedCatalog = null;
            m_AssetById.Clear();
        }
    }
}
