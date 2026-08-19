using UnityEngine;

/// <summary>
/// Legacy spawn-point shell.  It retains all serialized fields/GUIDs, but no
/// longer owns a timer, capacity, boss gate, or prefab instantiation path.
/// Calls are forwarded to the scene's single EnemySpawnDirectorAdapter.
/// </summary>
[DisallowMultipleComponent]
public class MonsterSpawnPoint : MonoBehaviour
{
    [Header("刷怪配置 (Spawn)")]
    public GameObject enemyPrefab;
    [Min(1)] public int monstersPerWave = 1;
    [Min(0.5f)] public float spawnInterval = 8f;
    [Min(0f)] public float firstSpawnDelay = 3f;
    [Min(1)] public int maxAlivePerPoint = 4;

    [Header("生成规则 (Rules)")]
    public bool skipIfVisibleOnScreen = true;
    [Min(0f)] public float minSpawnDistanceFromPlayers = 12f;
    public bool requireWalkableCell = true;
    public float spawnHeightOffset;
    public Transform spawnParent;

    [Header("Boss 联动 (留空自动查找)")]
    public BossHealth boss;
    [Min(0f)] public float resumeDelayAfterPhase = 3f;

    [Header("Gizmo")]
    public float gizmoRadius = 1f;

    private EnemySpawnDirectorAdapter director;

    private void Start()
    {
        director = EnemySpawnDirectorAdapter.GetOrCreate(null, spawnParent, boss);
        // This object is now only a compatibility endpoint. The shared
        // director's own Update is the sole authoritative tick.
        enabled = false;
    }

    [ContextMenu("测试/立即刷一波")]
    private void DebugSpawnNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[刷怪点] 请先进入 Play 模式再测试。", this);
            return;
        }

        director ??= EnemySpawnDirectorAdapter.GetOrCreate(null, spawnParent, boss);
        if (NetworkAuthority.IsServerOrOffline()) director.SpawnNow();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.55f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);
    }
}
