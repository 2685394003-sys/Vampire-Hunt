using UnityEngine;

/// <summary>
/// Immutable authoring data for the server-authoritative proximity spawner.
/// Runtime pressure is derived from elapsed time and never mutates this asset.
/// </summary>
[CreateAssetMenu(
    fileName = "MonsterSpawnConfig",
    menuName = "Vampire Hunt/Balance/Monster Spawn Config",
    order = 20)]
public sealed class MonsterSpawnConfig : ScriptableObject
{
    private const string DefaultResourcePath = "GameBalance/MonsterSpawnConfig";

    [Header("刷怪配置 / Spawn")]
    [SerializeField, Tooltip("需要包含 FlowFieldEnemy、EnemyHealth 和 NetworkObject。")]
    private GameObject enemyPrefab;
    [SerializeField, Min(1)] private int monstersPerWave = 2;
    [SerializeField, Min(0.1f)] private float spawnInterval = 4f;
    [SerializeField, Min(0f)] private float firstSpawnDelay = 3f;
    [SerializeField, Min(1)] private int maxAlive = 300;

    [Header("随时间增压 / Time Scaling")]
    [SerializeField, Tooltip("每经过多少秒增加一次每波刷怪量。"), Min(1f)]
    private float waveGrowthInterval = 60f;
    [SerializeField, Tooltip("每次成长增加的怪物数量。"), Min(1)]
    private int monstersAddedPerGrowth = 1;
    [SerializeField, Tooltip("成长后的单波数量上限。"), Min(1)]
    private int maxMonstersPerWave = 20;

    [Header("对象池 / Pool")]
    [SerializeField, Tooltip("开局预先创建的敌人数量，减少第一轮高峰卡顿。"), Min(0)]
    private int poolPrewarmCount = 32;

    [Header("玩家周围范围 / Player Ring")]
    [SerializeField, Tooltip("生成位置到所有存活玩家的最小距离。"), Min(0f)]
    private float minSpawnRadius = 15f;
    [SerializeField, Tooltip("生成位置到本次选中玩家的最大距离。"), Min(0f)]
    private float maxSpawnRadius = 25f;
    [SerializeField, Min(1)] private int maxSampleAttemptsPerMonster = 16;
    [SerializeField] private float spawnHeightOffset;
    [SerializeField, Tooltip("同一波怪物之间的最小出生间距。"), Min(0f)]
    private float minWaveSpawnSeparation = 1f;

    [Header("Boss 方向偏置 / Boss Direction Bias")]
    [SerializeField, Tooltip("有 Boss 时，从 Boss 方向扇区采样的概率。"), Range(0f, 1f)]
    private float bossDirectionProbability = 0.7f;
    [SerializeField, Tooltip("Boss 方向扇区的左右半角。"), Range(0f, 180f)]
    private float bossDirectionHalfAngle = 55f;
    [SerializeField, Min(0.1f)] private float bossFindRetryInterval = 0.5f;
    [SerializeField] private bool pauseDuringBossTransition = true;
    [SerializeField] private bool stopWhenBossDies = true;
    [SerializeField, Min(0f)] private float resumeDelayAfterPhase = 3f;

    [Header("有效位置 / Validation")]
    [SerializeField] private bool requireWalkableCell = true;
    [SerializeField, Tooltip("避免出生位置与障碍、玩家、Boss 或已有怪物重叠。")]
    private LayerMask spawnBlockingLayers = (1 << 3) | (1 << 6) | (1 << 7);
    [SerializeField, Min(0f)] private float spawnClearanceRadius = 0.6f;
    [SerializeField] private bool drawGizmos = true;

    public GameObject EnemyPrefab => enemyPrefab;
    public int MonstersPerWave => monstersPerWave;
    public float SpawnInterval => spawnInterval;
    public float FirstSpawnDelay => firstSpawnDelay;
    public int MaxAlive => maxAlive;
    public float WaveGrowthInterval => waveGrowthInterval;
    public int MonstersAddedPerGrowth => monstersAddedPerGrowth;
    public int MaxMonstersPerWave => maxMonstersPerWave;
    public int PoolPrewarmCount => poolPrewarmCount;
    public float MinSpawnRadius => minSpawnRadius;
    public float MaxSpawnRadius => maxSpawnRadius;
    public int MaxSampleAttemptsPerMonster => maxSampleAttemptsPerMonster;
    public float SpawnHeightOffset => spawnHeightOffset;
    public float MinWaveSpawnSeparation => minWaveSpawnSeparation;
    public float BossDirectionProbability => bossDirectionProbability;
    public float BossDirectionHalfAngle => bossDirectionHalfAngle;
    public float BossFindRetryInterval => bossFindRetryInterval;
    public bool PauseDuringBossTransition => pauseDuringBossTransition;
    public bool StopWhenBossDies => stopWhenBossDies;
    public float ResumeDelayAfterPhase => resumeDelayAfterPhase;
    public bool RequireWalkableCell => requireWalkableCell;
    public LayerMask SpawnBlockingLayers => spawnBlockingLayers;
    public float SpawnClearanceRadius => spawnClearanceRadius;
    public bool DrawGizmos => drawGizmos;

    public static MonsterSpawnConfig LoadDefault() =>
        Resources.Load<MonsterSpawnConfig>(DefaultResourcePath);

    private void OnValidate()
    {
        monstersPerWave = Mathf.Max(1, monstersPerWave);
        spawnInterval = Mathf.Max(0.1f, spawnInterval);
        firstSpawnDelay = Mathf.Max(0f, firstSpawnDelay);
        maxAlive = Mathf.Max(1, maxAlive);
        waveGrowthInterval = Mathf.Max(1f, waveGrowthInterval);
        monstersAddedPerGrowth = Mathf.Max(1, monstersAddedPerGrowth);
        maxMonstersPerWave = Mathf.Max(monstersPerWave, maxMonstersPerWave);
        poolPrewarmCount = Mathf.Clamp(poolPrewarmCount, 0, maxAlive);
        minSpawnRadius = Mathf.Max(0f, minSpawnRadius);
        maxSpawnRadius = Mathf.Max(minSpawnRadius, maxSpawnRadius);
        maxSampleAttemptsPerMonster = Mathf.Max(1, maxSampleAttemptsPerMonster);
        minWaveSpawnSeparation = Mathf.Max(0f, minWaveSpawnSeparation);
        bossFindRetryInterval = Mathf.Max(0.1f, bossFindRetryInterval);
        resumeDelayAfterPhase = Mathf.Max(0f, resumeDelayAfterPhase);
        spawnClearanceRadius = Mathf.Max(0f, spawnClearanceRadius);
    }
}
