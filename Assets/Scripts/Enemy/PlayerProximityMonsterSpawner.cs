using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative monster spawner that samples positions around living players.
/// It does not rely on fixed spawn points. When a boss exists, a configurable share
/// of samples is drawn from a cone pointing from the selected player toward the boss.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerProximityMonsterSpawner : MonoBehaviour
{
    [Header("刷怪配置 / Spawn Config")]
    [Tooltip("留空时加载 Resources/GameBalance/MonsterSpawnConfig。")]
    [SerializeField] private MonsterSpawnConfig config;

    [Header("场景引用 / Scene References")]
    [Tooltip("留空时自动查找场景中的 BossHealth。")]
    public BossHealth boss;
    public MonsterSpawnConfig Config => config != null ? config : config = MonsterSpawnConfig.LoadDefault();

    public void SetConfig(MonsterSpawnConfig value) => config = value;

    private GameObject EnemyPrefab => Config != null ? Config.EnemyPrefab : null;
    [Tooltip("生成的怪物统一放在此父物体下；留空时自动创建 [SpawnedEnemies]（仅离线层级生效）。")]
    public Transform spawnParent;

    private readonly List<PlayerNetworkState> alivePlayers = new(4);
    private readonly List<GameObject> spawnedEnemies = new();
    private readonly List<Vector3> wavePositions = new();
    private static Transform autoRoot;
    private FlowFieldManager flowField;
    private Transform offlinePlayer;
    private float spawnTimer;
    private float bossFindTimer;
    private float resumeBlockTimer;
    private bool wasBossInvulnerable;
    private bool bossEverFound;
    private bool bossGone;
    private float elapsedSpawnTime;

    private void Awake()
    {
        flowField = FindFirstObjectByType<FlowFieldManager>();
        offlinePlayer = flowField != null ? flowField.player : null;
        spawnTimer = Config != null ? Config.FirstSpawnDelay : 3f;
        if (Config != null)
            NetworkSpawnUtility.ConfigurePool(EnemyPrefab, Config.PoolPrewarmCount, Config.MaxAlive);
        TryFindBoss();
    }

    private void Update()
    {
        if (!IsSpawnAuthorityReady() || bossGone)
            return;

        RefreshBossReference();
        if (ShouldPauseForBoss())
            return;

        elapsedSpawnTime += Time.deltaTime;
        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f)
            return;

        spawnTimer = Config != null ? Config.SpawnInterval : 4f;
        TrySpawnWave();
    }

    private void RefreshBossReference()
    {
        if (boss != null)
        {
            bossEverFound = true;
            return;
        }

        if (bossEverFound && Config != null && Config.StopWhenBossDies)
        {
            bossGone = true;
            return;
        }

        bossFindTimer -= Time.deltaTime;
        if (bossFindTimer <= 0f)
        {
            bossFindTimer = Config != null ? Config.BossFindRetryInterval : 0.5f;
            TryFindBoss();
        }
    }

    private void TryFindBoss()
    {
        if (boss != null)
            return;

        boss = FindFirstObjectByType<BossHealth>();
        if (boss == null)
        {
            foreach (BossHealth candidate in Resources.FindObjectsOfTypeAll<BossHealth>())
            {
                if (candidate != null && candidate.gameObject.scene.IsValid())
                {
                    boss = candidate;
                    break;
                }
            }
        }

        if (boss != null)
            bossEverFound = true;
    }

    private bool ShouldPauseForBoss()
    {
        if (boss == null)
            return false;

        if (boss.IsDead && Config != null && Config.StopWhenBossDies)
        {
            bossGone = true;
            return true;
        }

        if (Config != null && Config.PauseDuringBossTransition && boss.IsInvulnerable)
        {
            wasBossInvulnerable = true;
            return true;
        }

        if (wasBossInvulnerable)
        {
            wasBossInvulnerable = false;
            resumeBlockTimer = Config != null ? Config.ResumeDelayAfterPhase : 0f;
        }

        if (resumeBlockTimer <= 0f)
            return false;

        resumeBlockTimer -= Time.deltaTime;
        return true;
    }

    [ContextMenu("测试/立即刷一波")]
    public void TrySpawnWave()
    {
        if (!IsSpawnAuthorityReady())
            return;
        if (Config == null || EnemyPrefab == null)
        {
            Debug.LogWarning("[动态刷怪] 未配置 enemyPrefab，跳过本波。", this);
            return;
        }

        CleanupDestroyedEnemies();
        int activePooledEnemies = NetworkSpawnUtility.GetActivePooledCount(EnemyPrefab);
        int trackedActiveEnemies = 0;
        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            if (spawnedEnemies[i] != null && spawnedEnemies[i].activeInHierarchy)
                trackedActiveEnemies++;
        }
        int availableSlots = Config.MaxAlive - Mathf.Max(activePooledEnemies, trackedActiveEnemies);
        if (availableSlots <= 0)
            return;

        CollectPlayers();
        if (alivePlayers.Count == 0 && offlinePlayer == null)
            return;

        wavePositions.Clear();
        int growthSteps = Mathf.FloorToInt(elapsedSpawnTime / Config.WaveGrowthInterval);
        int scaledWaveSize = Mathf.Min(
            Config.MaxMonstersPerWave,
            Config.MonstersPerWave + growthSteps * Config.MonstersAddedPerGrowth);
        int count = Mathf.Min(scaledWaveSize, availableSlots);
        for (int i = 0; i < count; i++)
        {
            if (!TrySampleSpawnPosition(out Vector3 position, out Transform target))
                continue;

            Vector3 facing = target.position - position;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f)
                facing = Vector3.forward;

            GameObject instance = NetworkSpawnUtility.Spawn(
                EnemyPrefab,
                position,
                Quaternion.LookRotation(facing.normalized, Vector3.up),
                GetSpawnParent());
            if (instance == null)
                continue;

            spawnedEnemies.Add(instance);
            wavePositions.Add(position);
        }
    }

    private void CollectPlayers()
    {
        NetworkPlayerRegistry.GetAlivePlayers(alivePlayers);
        if (flowField == null)
            flowField = FindFirstObjectByType<FlowFieldManager>();
        if (offlinePlayer == null && flowField != null)
            offlinePlayer = flowField.player;
    }

    private bool TrySampleSpawnPosition(out Vector3 position, out Transform target)
    {
        target = GetRandomPlayerTransform();
        position = default;
        if (target == null)
            return false;

        for (int attempt = 0; attempt < Config.MaxSampleAttemptsPerMonster; attempt++)
        {
            Vector3 direction = SampleDirection(target.position);
            float radius = Mathf.Sqrt(Random.Range(
                Config.MinSpawnRadius * Config.MinSpawnRadius,
                Config.MaxSpawnRadius * Config.MaxSpawnRadius));
            Vector3 candidate = target.position + direction * radius;
            candidate.y = target.position.y + Config.SpawnHeightOffset;

            if (IsTooCloseToAnyPlayer(candidate))
                continue;
            if (!IsSeparatedFromCurrentWave(candidate))
                continue;
            if (Config.RequireWalkableCell && !IsWalkable(candidate))
                continue;
            if (Config.SpawnClearanceRadius > 0f && Physics.CheckSphere(
                    candidate + Vector3.up * Config.SpawnClearanceRadius,
                    Config.SpawnClearanceRadius,
                    Config.SpawnBlockingLayers,
                    QueryTriggerInteraction.Ignore))
                continue;

            position = candidate;
            return true;
        }

        return false;
    }

    private Transform GetRandomPlayerTransform()
    {
        if (alivePlayers.Count > 0)
            return alivePlayers[Random.Range(0, alivePlayers.Count)].transform;
        return offlinePlayer;
    }

    private Vector3 SampleDirection(Vector3 playerPosition)
    {
        bool useBossDirection = boss != null &&
                                !boss.IsDead &&
                                Random.value < Config.BossDirectionProbability;
        if (!useBossDirection)
            return Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

        Vector3 towardBoss = boss.transform.position - playerPosition;
        towardBoss.y = 0f;
        if (towardBoss.sqrMagnitude < 0.0001f)
            return Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

        float offset = Random.Range(-Config.BossDirectionHalfAngle, Config.BossDirectionHalfAngle);
        return Quaternion.Euler(0f, offset, 0f) * towardBoss.normalized;
    }

    private bool IsTooCloseToAnyPlayer(Vector3 position)
    {
        float minimumDistanceSquared = Config.MinSpawnRadius * Config.MinSpawnRadius;
        foreach (PlayerNetworkState player in alivePlayers)
        {
            if (player != null &&
                (player.transform.position - position).sqrMagnitude < minimumDistanceSquared)
                return true;
        }

        if (alivePlayers.Count == 0 && offlinePlayer != null &&
            (offlinePlayer.position - position).sqrMagnitude < minimumDistanceSquared)
            return true;

        return false;
    }

    private bool IsSeparatedFromCurrentWave(Vector3 position)
    {
        float minimumDistanceSquared = Config.MinWaveSpawnSeparation * Config.MinWaveSpawnSeparation;
        foreach (Vector3 other in wavePositions)
        {
            if ((other - position).sqrMagnitude < minimumDistanceSquared)
                return false;
        }
        return true;
    }

    private bool IsWalkable(Vector3 position)
    {
        if (flowField == null)
            flowField = FindFirstObjectByType<FlowFieldManager>();
        if (flowField == null)
            return true;

        Vector2Int cell = flowField.WorldToGrid(position);
        return flowField.IsInGrid(cell.x, cell.y) &&
               flowField.GetCellState(cell.x, cell.y) == CellState.Walkable;
    }

    private Transform GetSpawnParent()
    {
        if (spawnParent != null)
            return spawnParent;
        if (NetworkAuthority.IsNetworkActive)
            return null;
        if (autoRoot != null)
            return autoRoot;

        GameObject root = GameObject.Find("[SpawnedEnemies]");
        if (root == null)
            root = new GameObject("[SpawnedEnemies]");
        autoRoot = root.transform;
        return autoRoot;
    }

    private void CleanupDestroyedEnemies()
    {
        for (int i = spawnedEnemies.Count - 1; i >= 0; i--)
        {
            if (spawnedEnemies[i] == null || !spawnedEnemies[i].activeInHierarchy)
                spawnedEnemies.RemoveAt(i);
        }
    }

    private static bool IsSpawnAuthorityReady()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
            return true;
        return manager.IsListening && manager.IsServer;
    }

    private void OnDrawGizmosSelected()
    {
        MonsterSpawnConfig settings = Config;
        if (settings == null || !settings.DrawGizmos)
            return;

        FlowFieldManager manager = flowField != null
            ? flowField
            : FindFirstObjectByType<FlowFieldManager>();
        Transform fallback = manager != null ? manager.player : null;
        if (fallback == null)
            return;

        Gizmos.color = new Color(1f, 0.65f, 0f, 0.8f);
        DrawCircle(fallback.position, settings.MinSpawnRadius);
        Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.8f);
        DrawCircle(fallback.position, settings.MaxSpawnRadius);
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        const int segments = 48;
        Vector3 previous = center + Vector3.forward * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
