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
            int maxSpawnsPerTick = 32,
            float firstSpawnDelay = -1f,
            float minSpawnRadius = -1f,
            int maxSampleAttempts = 8,
            float spawnHeightOffset = 0f,
            float minWaveSpawnSeparation = 0f,
            float bossDirectionProbability = 0f,
            float bossDirectionHalfAngle = 180f,
            bool pauseDuringBossTransition = false,
            bool stopWhenBossDies = false,
            float resumeDelayAfterPhase = 0f,
            bool requireWalkable = true,
            int spawnBlockingMask = 0,
            float spawnClearanceRadius = 0f)
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
            float resolvedFirstSpawnDelay = firstSpawnDelay < 0f ? spawnInterval : firstSpawnDelay;
            float resolvedMinSpawnRadius = minSpawnRadius < 0f ? spawnRadius * 0.5f : minSpawnRadius;
            if (!IsFiniteNonNegative(resolvedFirstSpawnDelay))
                throw new ArgumentOutOfRangeException(nameof(firstSpawnDelay));
            if (!IsFiniteNonNegative(resolvedMinSpawnRadius) || resolvedMinSpawnRadius > spawnRadius)
                throw new ArgumentOutOfRangeException(nameof(minSpawnRadius));
            if (maxSampleAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxSampleAttempts));
            if (float.IsNaN(spawnHeightOffset) || float.IsInfinity(spawnHeightOffset))
                throw new ArgumentOutOfRangeException(nameof(spawnHeightOffset));
            if (!IsFiniteNonNegative(minWaveSpawnSeparation))
                throw new ArgumentOutOfRangeException(nameof(minWaveSpawnSeparation));
            if (!IsFiniteRange(bossDirectionProbability, 0f, 1f))
                throw new ArgumentOutOfRangeException(nameof(bossDirectionProbability));
            if (!IsFiniteRange(bossDirectionHalfAngle, 0f, 180f))
                throw new ArgumentOutOfRangeException(nameof(bossDirectionHalfAngle));
            if (!IsFiniteNonNegative(resumeDelayAfterPhase))
                throw new ArgumentOutOfRangeException(nameof(resumeDelayAfterPhase));
            if (!IsFiniteNonNegative(spawnClearanceRadius))
                throw new ArgumentOutOfRangeException(nameof(spawnClearanceRadius));
            EnemyTypeId = enemyTypeId ?? string.Empty;
            MaxAlive = maxAlive;
            PoolPrewarm = poolPrewarm;
            SpawnInterval = spawnInterval;
            SpawnRadius = spawnRadius;
            BaseSpawnsPerInterval = baseSpawnsPerInterval;
            GrowthPerMinute = growthPerMinute;
            MaxSpawnsPerTick = maxSpawnsPerTick;
            FirstSpawnDelay = resolvedFirstSpawnDelay;
            MinSpawnRadius = resolvedMinSpawnRadius;
            MaxSampleAttempts = maxSampleAttempts;
            SpawnHeightOffset = spawnHeightOffset;
            MinWaveSpawnSeparation = minWaveSpawnSeparation;
            BossDirectionProbability = bossDirectionProbability;
            BossDirectionHalfAngle = bossDirectionHalfAngle;
            PauseDuringBossTransition = pauseDuringBossTransition;
            StopWhenBossDies = stopWhenBossDies;
            ResumeDelayAfterPhase = resumeDelayAfterPhase;
            RequireWalkable = requireWalkable;
            SpawnBlockingMask = spawnBlockingMask;
            SpawnClearanceRadius = spawnClearanceRadius;
        }

        public string EnemyTypeId { get; }
        public int MaxAlive { get; }
        public int PoolPrewarm { get; }
        public float SpawnInterval { get; }
        public float SpawnRadius { get; }
        public int BaseSpawnsPerInterval { get; }
        public float GrowthPerMinute { get; }
        public int MaxSpawnsPerTick { get; }
        public float FirstSpawnDelay { get; }
        public float MinSpawnRadius { get; }
        public int MaxSampleAttempts { get; }
        public float SpawnHeightOffset { get; }
        public float MinWaveSpawnSeparation { get; }
        public float BossDirectionProbability { get; }
        public float BossDirectionHalfAngle { get; }
        public bool PauseDuringBossTransition { get; }
        public bool StopWhenBossDies { get; }
        public float ResumeDelayAfterPhase { get; }
        public bool RequireWalkable { get; }
        public int SpawnBlockingMask { get; }
        public float SpawnClearanceRadius { get; }

        private static bool IsFiniteNonNegative(float value) =>
            value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFiniteRange(float value, float minimum, float maximum) =>
            value >= minimum && value <= maximum && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
