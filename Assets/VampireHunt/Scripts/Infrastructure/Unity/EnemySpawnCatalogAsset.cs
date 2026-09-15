using System;
using UnityEngine;

namespace VampireHunt.Infrastructure.Unity
{
    [Serializable]
    public sealed class EnemySpawnEntryAsset
    {
        [SerializeField] private EnemyArchetypeAsset archetype;
        [Min(0f)] [SerializeField] private float weight = 1f;
        [Min(0f)] [SerializeField] private float minimumElapsedSeconds;
        [Min(1)] [SerializeField] private int maximumConcurrent = 20;

        public EnemyArchetypeAsset Archetype => archetype;
        public float Weight => Mathf.Max(0f, weight);
        public float MinimumElapsedSeconds => Mathf.Max(0f, minimumElapsedSeconds);
        public int MaximumConcurrent => Mathf.Max(1, maximumConcurrent);
    }

    [CreateAssetMenu(fileName = "EnemySpawnCatalog", menuName = "Vampire Hunt/Enemies/Spawn Catalog")]
    public sealed class EnemySpawnCatalogAsset : ScriptableObject
    {
        [SerializeField] private EnemySpawnEntryAsset[] entries = Array.Empty<EnemySpawnEntryAsset>();

        public EnemySpawnEntryAsset[] Entries => entries ?? Array.Empty<EnemySpawnEntryAsset>();

        private void OnValidate()
        {
            if (entries == null) entries = Array.Empty<EnemySpawnEntryAsset>();
        }
    }
}
