using System;
using UnityEngine;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Spawning.Authoring
{
    /// <summary>
    /// Spawn authoring values. ConfigCatalog is the only bootstrap entry point
    /// that turns this Unity asset into an immutable EnemySpawnSpec.
    /// </summary>
    [CreateAssetMenu(
        fileName = "EnemySpawnConfig",
        menuName = "Vampire Hunt/Spawning/Enemy Spawn Config",
        order = 3)]
    public sealed class EnemySpawnConfig : ScriptableObject
    {
        [SerializeField] private string enemyTypeId = "default";
        [SerializeField, Min(0)] private int maxAlive = 100;
        [SerializeField, Min(0)] private int poolPrewarm;
        [SerializeField, Min(0f)] private float spawnInterval = 1f;
        [SerializeField, Min(0f)] private float spawnRadius = 8f;
        [SerializeField, Min(0)] private int baseSpawnsPerInterval = 1;
        [SerializeField, Min(0f)] private float growthPerMinute = 0.25f;
        [SerializeField, Min(0)] private int maxSpawnsPerTick = 32;

        public string EnemyTypeId => enemyTypeId ?? string.Empty;
        public int MaxAlive => maxAlive;
        public int PoolPrewarm => poolPrewarm;
        public float SpawnInterval => spawnInterval;
        public float SpawnRadius => spawnRadius;
        public int BaseSpawnsPerInterval => baseSpawnsPerInterval;
        public float GrowthPerMinute => growthPerMinute;
        public int MaxSpawnsPerTick => maxSpawnsPerTick;

        public EnemySpawnSpec CreateSpec()
        {
            Validate();
            return new EnemySpawnSpec(
                EnemyTypeId,
                MaxAlive,
                PoolPrewarm,
                SpawnInterval,
                SpawnRadius,
                BaseSpawnsPerInterval,
                GrowthPerMinute,
                MaxSpawnsPerTick);
        }

        public void Validate()
        {
            if (MaxAlive < 0) throw Invalid(nameof(MaxAlive), "must be non-negative");
            if (PoolPrewarm < 0) throw Invalid(nameof(PoolPrewarm), "must be non-negative");
            if (!IsFiniteNonNegative(SpawnInterval)) throw Invalid(nameof(SpawnInterval), "must be finite and non-negative");
            if (!IsFiniteNonNegative(SpawnRadius)) throw Invalid(nameof(SpawnRadius), "must be finite and non-negative");
            if (BaseSpawnsPerInterval < 0) throw Invalid(nameof(BaseSpawnsPerInterval), "must be non-negative");
            if (!IsFiniteNonNegative(GrowthPerMinute)) throw Invalid(nameof(GrowthPerMinute), "must be finite and non-negative");
            if (MaxSpawnsPerTick < 0) throw Invalid(nameof(MaxSpawnsPerTick), "must be non-negative");
        }

        private void OnValidate()
        {
            maxAlive = Mathf.Max(0, maxAlive);
            poolPrewarm = Mathf.Max(0, poolPrewarm);
            spawnInterval = Mathf.Max(0f, spawnInterval);
            spawnRadius = Mathf.Max(0f, spawnRadius);
            baseSpawnsPerInterval = Mathf.Max(0, baseSpawnsPerInterval);
            growthPerMinute = Mathf.Max(0f, growthPerMinute);
            maxSpawnsPerTick = Mathf.Max(0, maxSpawnsPerTick);
        }

        private static bool IsFiniteNonNegative(float value) =>
            value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static InvalidOperationException Invalid(string property, string reason) =>
            new InvalidOperationException($"EnemySpawnConfig.{property} {reason}.");
    }
}
