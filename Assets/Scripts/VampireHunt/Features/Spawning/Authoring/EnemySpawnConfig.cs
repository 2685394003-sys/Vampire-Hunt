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
        [SerializeField, Min(0f)] private float firstSpawnDelay = 1f;
        [SerializeField, Min(0f)] private float minSpawnRadius = 4f;
        [SerializeField, Min(0f)] private float spawnRadius = 8f;
        [SerializeField, Min(0)] private int baseSpawnsPerInterval = 1;
        [SerializeField, Min(0f)] private float growthPerMinute = 0.25f;
        [SerializeField, Min(0)] private int maxSpawnsPerTick = 32;
        [SerializeField, Min(1)] private int maxSampleAttempts = 8;
        [SerializeField] private float spawnHeightOffset;
        [SerializeField, Min(0f)] private float minWaveSpawnSeparation;
        [SerializeField, Range(0f, 1f)] private float bossDirectionProbability;
        [SerializeField, Range(0f, 180f)] private float bossDirectionHalfAngle = 180f;
        [SerializeField] private bool pauseDuringBossTransition;
        [SerializeField] private bool stopWhenBossDies;
        [SerializeField, Min(0f)] private float resumeDelayAfterPhase;
        [SerializeField] private bool requireWalkable = true;
        [SerializeField] private LayerMask spawnBlockingLayers;
        [SerializeField, Min(0f)] private float spawnClearanceRadius;

        public string EnemyTypeId => enemyTypeId ?? string.Empty;
        public int MaxAlive => maxAlive;
        public int PoolPrewarm => poolPrewarm;
        public float SpawnInterval => spawnInterval;
        public float FirstSpawnDelay => firstSpawnDelay;
        public float MinSpawnRadius => minSpawnRadius;
        public float SpawnRadius => spawnRadius;
        public int BaseSpawnsPerInterval => baseSpawnsPerInterval;
        public float GrowthPerMinute => growthPerMinute;
        public int MaxSpawnsPerTick => maxSpawnsPerTick;
        public int MaxSampleAttempts => maxSampleAttempts;
        public float SpawnHeightOffset => spawnHeightOffset;
        public float MinWaveSpawnSeparation => minWaveSpawnSeparation;
        public float BossDirectionProbability => bossDirectionProbability;
        public float BossDirectionHalfAngle => bossDirectionHalfAngle;
        public bool PauseDuringBossTransition => pauseDuringBossTransition;
        public bool StopWhenBossDies => stopWhenBossDies;
        public float ResumeDelayAfterPhase => resumeDelayAfterPhase;
        public bool RequireWalkable => requireWalkable;
        public int SpawnBlockingMask => spawnBlockingLayers.value;
        public float SpawnClearanceRadius => spawnClearanceRadius;

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
                MaxSpawnsPerTick,
                FirstSpawnDelay,
                MinSpawnRadius,
                MaxSampleAttempts,
                SpawnHeightOffset,
                MinWaveSpawnSeparation,
                BossDirectionProbability,
                BossDirectionHalfAngle,
                PauseDuringBossTransition,
                StopWhenBossDies,
                ResumeDelayAfterPhase,
                RequireWalkable,
                SpawnBlockingMask,
                SpawnClearanceRadius);
        }

        public void Validate()
        {
            if (MaxAlive < 0) throw Invalid(nameof(MaxAlive), "must be non-negative");
            if (PoolPrewarm < 0) throw Invalid(nameof(PoolPrewarm), "must be non-negative");
            if (!IsFiniteNonNegative(SpawnInterval)) throw Invalid(nameof(SpawnInterval), "must be finite and non-negative");
            if (!IsFiniteNonNegative(FirstSpawnDelay)) throw Invalid(nameof(FirstSpawnDelay), "must be finite and non-negative");
            if (!IsFiniteNonNegative(MinSpawnRadius) || MinSpawnRadius > SpawnRadius)
                throw Invalid(nameof(MinSpawnRadius), "must be finite, non-negative and not exceed SpawnRadius");
            if (!IsFiniteNonNegative(SpawnRadius)) throw Invalid(nameof(SpawnRadius), "must be finite and non-negative");
            if (BaseSpawnsPerInterval < 0) throw Invalid(nameof(BaseSpawnsPerInterval), "must be non-negative");
            if (!IsFiniteNonNegative(GrowthPerMinute)) throw Invalid(nameof(GrowthPerMinute), "must be finite and non-negative");
            if (MaxSpawnsPerTick < 0) throw Invalid(nameof(MaxSpawnsPerTick), "must be non-negative");
            if (MaxSampleAttempts <= 0) throw Invalid(nameof(MaxSampleAttempts), "must be positive");
            if (float.IsNaN(SpawnHeightOffset) || float.IsInfinity(SpawnHeightOffset))
                throw Invalid(nameof(SpawnHeightOffset), "must be finite");
            if (!IsFiniteNonNegative(MinWaveSpawnSeparation)) throw Invalid(nameof(MinWaveSpawnSeparation), "must be finite and non-negative");
            if (!IsFiniteRange(BossDirectionProbability, 0f, 1f)) throw Invalid(nameof(BossDirectionProbability), "must be in [0, 1]");
            if (!IsFiniteRange(BossDirectionHalfAngle, 0f, 180f)) throw Invalid(nameof(BossDirectionHalfAngle), "must be in [0, 180]");
            if (!IsFiniteNonNegative(ResumeDelayAfterPhase)) throw Invalid(nameof(ResumeDelayAfterPhase), "must be finite and non-negative");
            if (!IsFiniteNonNegative(SpawnClearanceRadius)) throw Invalid(nameof(SpawnClearanceRadius), "must be finite and non-negative");
        }

        private void OnValidate()
        {
            maxAlive = Mathf.Max(0, maxAlive);
            poolPrewarm = Mathf.Max(0, poolPrewarm);
            spawnInterval = Mathf.Max(0f, spawnInterval);
            firstSpawnDelay = Mathf.Max(0f, firstSpawnDelay);
            minSpawnRadius = Mathf.Max(0f, minSpawnRadius);
            spawnRadius = Mathf.Max(0f, spawnRadius);
            minSpawnRadius = Mathf.Min(minSpawnRadius, spawnRadius);
            baseSpawnsPerInterval = Mathf.Max(0, baseSpawnsPerInterval);
            growthPerMinute = Mathf.Max(0f, growthPerMinute);
            maxSpawnsPerTick = Mathf.Max(0, maxSpawnsPerTick);
            maxSampleAttempts = Mathf.Max(1, maxSampleAttempts);
            minWaveSpawnSeparation = Mathf.Max(0f, minWaveSpawnSeparation);
            bossDirectionProbability = Mathf.Clamp01(bossDirectionProbability);
            bossDirectionHalfAngle = Mathf.Clamp(bossDirectionHalfAngle, 0f, 180f);
            resumeDelayAfterPhase = Mathf.Max(0f, resumeDelayAfterPhase);
            spawnClearanceRadius = Mathf.Max(0f, spawnClearanceRadius);
        }

        private static bool IsFiniteNonNegative(float value) =>
            value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFiniteRange(float value, float minimum, float maximum) =>
            value >= minimum && value <= maximum && !float.IsNaN(value) && !float.IsInfinity(value);

        private static InvalidOperationException Invalid(string property, string reason) =>
            new InvalidOperationException($"EnemySpawnConfig.{property} {reason}.");
    }
}
