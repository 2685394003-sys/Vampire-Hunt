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
    [Header("刷怪配置 / Spawn")]
    [Tooltip("需要包含 FlowFieldEnemy、EnemyHealth 和 NetworkObject。")]
    public GameObject enemyPrefab;
    [Min(1)] public int monstersPerWave = 2;
    [Min(0.1f)] public float spawnInterval = 4f;
    [Min(0f)] public float firstSpawnDelay = 3f;
    [Min(1)] public int maxAlive = 30;

    [Header("玩家周围范围 / Player Ring")]
    [Tooltip("生成位置到所有存活玩家的最小距离。")]
    [Min(0f)] public float minSpawnRadius = 15f;
    [Tooltip("生成位置到本次选中玩家的最大距离。")]
    [Min(0f)] public float maxSpawnRadius = 25f;
    [Min(1)] public int maxSampleAttemptsPerMonster = 16;
    [Min(0f)] public float spawnHeightOffset;
    [Tooltip("同一波怪物之间的最小出生间距。")]
    [Min(0f)] public float minWaveSpawnSeparation = 1f;

    [Header("Boss 方向偏置 / Boss Direction Bias")]
    [Tooltip("有 Boss 时，从 Boss 方向扇区采样的概率；剩余概率在整圈均匀采样。")]
    [Range(0f, 1f)] public float bossDirectionProbability = 0.7f;
    [Tooltip("Boss 方向扇区的半角。例如 55 表示 Boss 方向左右各 55 度。")]
    [Range(0f, 180f)] public float bossDirectionHalfAngle = 55f;
    [Tooltip("留空时自动查找场景中的 BossHealth。")]
    public BossHealth boss;
    [Min(0.1f)] public float bossFindRetryInterval = 0.5f;
    public bool pauseDuringBossTransition = true;
    public bool stopWhenBossDies = true;
    [Min(0f)] public float resumeDelayAfterPhase = 3f;

    [Header("有效位置 / Validation")]
    public bool requireWalkableCell = true;
    [Tooltip("用于避免出生位置与障碍、玩家、Boss 或已有怪物重叠。默认检测 Obstacle、Player、Enemy。")]
    public LayerMask spawnBlockingLayers = (1 << 3) | (1 << 6) | (1 << 7);
    [Min(0f)] public float spawnClearanceRadius = 0.6f;
    [Tooltip("生成的怪物统一放在此父物体下；留空时自动创建 [SpawnedEnemies]（仅离线层级生效）。")]
    public Transform spawnParent;

    [Header("调试 / Debug")]
    public bool drawGizmos = true;

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

    private void Awake()
    {
        NormalizeSettings();
        flowField = FindFirstObjectByType<FlowFieldManager>();
        offlinePlayer = flowField != null ? flowField.player : null;
        spawnTimer = firstSpawnDelay;
        TryFindBoss();
    }

    private void OnValidate()
    {
        NormalizeSettings();
    }

    private void Update()
    {
        if (!IsSpawnAuthorityReady() || bossGone)
            return;

        RefreshBossReference();
        if (ShouldPauseForBoss())
            return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f)
            return;

        spawnTimer = spawnInterval;
        TrySpawnWave();
    }

    private void NormalizeSettings()
    {
        minSpawnRadius = Mathf.Max(0f, minSpawnRadius);
        maxSpawnRadius = Mathf.Max(minSpawnRadius, maxSpawnRadius);
        spawnInterval = Mathf.Max(0.1f, spawnInterval);
        maxSampleAttemptsPerMonster = Mathf.Max(1, maxSampleAttemptsPerMonster);
    }

    private void RefreshBossReference()
    {
        if (boss != null)
        {
            bossEverFound = true;
            return;
        }

        if (bossEverFound && stopWhenBossDies)
        {
            bossGone = true;
            return;
        }

        bossFindTimer -= Time.deltaTime;
        if (bossFindTimer <= 0f)
        {
            bossFindTimer = bossFindRetryInterval;
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

        if (boss.IsDead && stopWhenBossDies)
        {
            bossGone = true;
            return true;
        }

        if (pauseDuringBossTransition && boss.IsInvulnerable)
        {
            wasBossInvulnerable = true;
            return true;
        }

        if (wasBossInvulnerable)
        {
            wasBossInvulnerable = false;
            resumeBlockTimer = resumeDelayAfterPhase;
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
        if (enemyPrefab == null)
        {
            Debug.LogWarning("[动态刷怪] 未配置 enemyPrefab，跳过本波。", this);
            return;
        }

        CleanupDestroyedEnemies();
        int availableSlots = maxAlive - spawnedEnemies.Count;
        if (availableSlots <= 0)
            return;

        CollectPlayers();
        if (alivePlayers.Count == 0 && offlinePlayer == null)
            return;

        wavePositions.Clear();
        int count = Mathf.Min(monstersPerWave, availableSlots);
        for (int i = 0; i < count; i++)
        {
            if (!TrySampleSpawnPosition(out Vector3 position, out Transform target))
                continue;

            Vector3 facing = target.position - position;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f)
                facing = Vector3.forward;

            GameObject instance = NetworkSpawnUtility.Spawn(
                enemyPrefab,
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

        for (int attempt = 0; attempt < maxSampleAttemptsPerMonster; attempt++)
        {
            Vector3 direction = SampleDirection(target.position);
            float radius = Mathf.Sqrt(Random.Range(
                minSpawnRadius * minSpawnRadius,
                maxSpawnRadius * maxSpawnRadius));
            Vector3 candidate = target.position + direction * radius;
            candidate.y = target.position.y + spawnHeightOffset;

            if (IsTooCloseToAnyPlayer(candidate))
                continue;
            if (!IsSeparatedFromCurrentWave(candidate))
                continue;
            if (requireWalkableCell && !IsWalkable(candidate))
                continue;
            if (spawnClearanceRadius > 0f && Physics.CheckSphere(
                    candidate + Vector3.up * spawnClearanceRadius,
                    spawnClearanceRadius,
                    spawnBlockingLayers,
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
                                Random.value < bossDirectionProbability;
        if (!useBossDirection)
            return Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

        Vector3 towardBoss = boss.transform.position - playerPosition;
        towardBoss.y = 0f;
        if (towardBoss.sqrMagnitude < 0.0001f)
            return Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

        float offset = Random.Range(-bossDirectionHalfAngle, bossDirectionHalfAngle);
        return Quaternion.Euler(0f, offset, 0f) * towardBoss.normalized;
    }

    private bool IsTooCloseToAnyPlayer(Vector3 position)
    {
        float minimumDistanceSquared = minSpawnRadius * minSpawnRadius;
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
        float minimumDistanceSquared = minWaveSpawnSeparation * minWaveSpawnSeparation;
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
            if (spawnedEnemies[i] == null)
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
        if (!drawGizmos)
            return;

        FlowFieldManager manager = flowField != null
            ? flowField
            : FindFirstObjectByType<FlowFieldManager>();
        Transform fallback = manager != null ? manager.player : null;
        if (fallback == null)
            return;

        Gizmos.color = new Color(1f, 0.65f, 0f, 0.8f);
        DrawCircle(fallback.position, minSpawnRadius);
        Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.8f);
        DrawCircle(fallback.position, maxSpawnRadius);
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
