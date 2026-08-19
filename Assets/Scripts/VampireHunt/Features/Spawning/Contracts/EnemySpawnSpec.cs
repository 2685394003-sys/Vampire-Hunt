using System;

namespace VampireHunt.Spawning.Contracts
{
    /// <summary>Immutable run-time spawn tuning produced by an Authoring factory.</summary>
    public sealed class EnemySpawnSpec
    {
        public EnemySpawnSpec(int maxAlive, int poolPrewarm, float spawnInterval)
            : this(string.Empty, maxAlive, poolPrewarm, spawnInterval)
        {
        }

        public EnemySpawnSpec(
            string enemyTypeId,
            int maxAlive,
            int poolPrewarm,
            float spawnInterval,
            float spawnRadius = 8f,
            int baseSpawnsPerInterval = 1,
            float growthPerMinute = 0.25f,
            int maxSpawnsPerTick = 32)
        {
            if (maxAlive < 0) throw new ArgumentOutOfRangeException(nameof(maxAlive));
            if (poolPrewarm < 0) throw new ArgumentOutOfRangeException(nameof(poolPrewarm));
            if (spawnInterval < 0f || float.IsNaN(spawnInterval) || float.IsInfinity(spawnInterval))
                throw new ArgumentOutOfRangeException(nameof(spawnInterval));
            if (spawnRadius < 0f || float.IsNaN(spawnRadius) || float.IsInfinity(spawnRadius))
                throw new ArgumentOutOfRangeException(nameof(spawnRadius));
            if (baseSpawnsPerInterval < 0) throw new ArgumentOutOfRangeException(nameof(baseSpawnsPerInterval));
            if (growthPerMinute < 0f || float.IsNaN(growthPerMinute) || float.IsInfinity(growthPerMinute))
                throw new ArgumentOutOfRangeException(nameof(growthPerMinute));
            if (maxSpawnsPerTick < 0) throw new ArgumentOutOfRangeException(nameof(maxSpawnsPerTick));
            EnemyTypeId = enemyTypeId ?? string.Empty;
            MaxAlive = maxAlive;
            PoolPrewarm = poolPrewarm;
            SpawnInterval = spawnInterval;
            SpawnRadius = spawnRadius;
            BaseSpawnsPerInterval = baseSpawnsPerInterval;
            GrowthPerMinute = growthPerMinute;
            MaxSpawnsPerTick = maxSpawnsPerTick;
        }

        public string EnemyTypeId { get; }
        public int MaxAlive { get; }
        public int PoolPrewarm { get; }
        public float SpawnInterval { get; }
        public float SpawnRadius { get; }
        public int BaseSpawnsPerInterval { get; }
        public float GrowthPerMinute { get; }
        public int MaxSpawnsPerTick { get; }
    }
}
