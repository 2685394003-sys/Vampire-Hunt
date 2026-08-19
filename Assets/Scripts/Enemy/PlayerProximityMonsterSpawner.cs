using UnityEngine;

/// <summary>
/// Legacy proximity-spawner shell.  It preserves scene/Inspector fields and
/// the TrySpawnWave entry point, while all pressure, sampling, global caps,
/// pool lifecycle and EntityId allocation are delegated to the one
/// EnemySpawnDirectorAdapter.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerProximityMonsterSpawner : MonoBehaviour
{
    [Header("刷怪配置 / Spawn Config")]
    [Tooltip("留空时加载 Resources/GameBalance/MonsterSpawnConfig。")]
    [SerializeField] private MonsterSpawnConfig config;

    [Header("场景引用 / Scene References")]
    public BossHealth boss;
    public MonsterSpawnConfig Config => config != null ? config : config = MonsterSpawnConfig.LoadDefault();
    public void SetConfig(MonsterSpawnConfig value)
    {
        config = value;
        director = EnemySpawnDirectorAdapter.GetOrCreate(config, spawnParent, boss);
    }

    [Tooltip("生成的怪物统一放在此父物体下；留空时由共享刷怪适配器处理。")]
    public Transform spawnParent;

    private EnemySpawnDirectorAdapter director;

    private void Awake()
    {
        director = EnemySpawnDirectorAdapter.GetOrCreate(Config, spawnParent, boss);
        // The shared adapter is the only object with an authoritative Update.
        enabled = false;
    }

    [ContextMenu("测试/立即刷一波")]
    public void TrySpawnWave()
    {
        director ??= EnemySpawnDirectorAdapter.GetOrCreate(Config, spawnParent, boss);
        if (NetworkAuthority.IsServerOrOffline()) director.SpawnNow();
    }

    private void OnDrawGizmosSelected()
    {
        MonsterSpawnConfig settings = Config;
        if (settings == null) return;

        FlowFieldManager manager = FindFirstObjectByType<FlowFieldManager>();
        Transform target = manager != null ? manager.player : null;
        if (target == null) return;

        Gizmos.color = new Color(1f, 0.65f, 0f, 0.8f);
        DrawCircle(target.position, settings.MinSpawnRadius);
        Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.8f);
        DrawCircle(target.position, settings.MaxSpawnRadius);
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
