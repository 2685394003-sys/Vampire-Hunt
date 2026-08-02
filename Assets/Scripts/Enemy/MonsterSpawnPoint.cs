using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 怪物刷怪点(MonsterSpawnPoint)。
/// 设计依据(思维导图):
/// - 每个刷怪点套用一个脚本,点与点之间互不协调;
/// - 刷新时间倒计时归 0 刷一波,每波数量="每波次怪物刷新数量";
/// - boss 死亡 / 转阶段时停止刷新,转阶段结束一段时间后再恢复;
/// - 生成点不进入玩家镜头(在镜头内则跳过本波);
/// - 生成点跟随玩家(挂在 Player 下的 shengchengfangxiang 子点上)。
/// </summary>
[DisallowMultipleComponent]
public class MonsterSpawnPoint : MonoBehaviour
{
    [Header("刷怪配置 (Spawn)")]
    [Tooltip("敌人预制体 (enemy prefab),需自带 FlowFieldEnemy/EnemyHealth 等组件")]
    public GameObject enemyPrefab;
    [Tooltip("每波次怪物刷新数量(本点)")]
    [Min(1)] public int monstersPerWave = 1;
    [Tooltip("刷新时间倒计时(秒),归 0 刷一波")]
    [Min(0.5f)] public float spawnInterval = 8f;
    [Tooltip("开局首次刷新延迟(秒);各点自动加随机抖动,避免完全同步")]
    [Min(0f)] public float firstSpawnDelay = 3f;
    [Tooltip("本点场上存活上限,达到后跳过本波(设计未设全局上限,按每点独立控制)")]
    [Min(1)] public int maxAlivePerPoint = 4;

    [Header("生成规则 (Rules)")]
    [Tooltip("生成点进入玩家镜头时跳过本波(设计:生成点不进镜头)")]
    public bool skipIfVisibleOnScreen = true;
    [Tooltip("格子被流场识别为障碍时跳过本波,防止怪卡进障碍")]
    public bool requireWalkableCell = true;
    [Tooltip("生成高度偏移,加在点位世界坐标 Y 上")]
    public float spawnHeightOffset = 0f;
    [Tooltip("生成的小怪统一挂到此父物体下;留空则自动创建 [SpawnedEnemies]")]
    public Transform spawnParent;

    [Header("Boss 联动 (留空自动查找)")]
    public BossHealth boss;
    [Tooltip("转阶段结束后,等待多少秒再恢复刷怪")]
    [Min(0f)] public float resumeDelayAfterPhase = 3f;

    [Header("Gizmo")]
    public float gizmoRadius = 1f;

    private float timer;                  // 刷新倒计时
    private float resumeBlockTimer;       // 转阶段结束后的恢复等待
    private bool wasBossInvulnerable;     // 上一帧 boss 是否处于转阶段无敌
    private bool bossEverFound;           // 是否曾找到过 boss
    private bool bossGone;                // boss 死亡或已销毁:永久停刷
    private readonly List<GameObject> alive = new List<GameObject>();
    private static Transform autoRoot;    // 自动创建的共享父物体
    private FlowFieldManager flowField;

    private void Start()
    {
        // 首次延迟 + 随机抖动,让各点节奏错开(设计上各点互不协调)
        timer = firstSpawnDelay + Random.Range(0f, spawnInterval * 0.3f);
        if (boss == null) boss = FindFirstObjectByType<BossHealth>();
        if (boss != null) bossEverFound = true;
        flowField = FindFirstObjectByType<FlowFieldManager>();
    }

    private void Update()
    {
        if (bossGone) return; // boss 已死:永久停止刷新

        // ---- boss 联动 ----
        if (boss == null)
        {
            if (bossEverFound)
            {
                // 曾找到过、现在没了 = boss 物体已销毁,按死亡处理
                bossGone = true;
                return;
            }
            // 从未找到过(比如测试场景没 boss):再找一次,找不到就正常刷
            boss = FindFirstObjectByType<BossHealth>();
            if (boss != null) bossEverFound = true;
        }
        else
        {
            if (boss.IsDead)
            {
                bossGone = true;
                return;
            }
            // 转阶段期间 boss 处于无敌(见 BossConfig.invulnerableDuringPhaseChange)
            bool transitioning = boss.IsInvulnerable;
            if (transitioning)
            {
                wasBossInvulnerable = true;
                return; // 转阶段中:暂停刷新,倒计时冻结
            }
            if (wasBossInvulnerable)
            {
                wasBossInvulnerable = false;
                resumeBlockTimer = resumeDelayAfterPhase; // 转阶段刚结束,进入恢复等待
            }
            if (resumeBlockTimer > 0f)
            {
                resumeBlockTimer -= Time.deltaTime;
                return;
            }
        }

        // ---- 刷新倒计时 ----
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = spawnInterval;

        TrySpawnWave();
    }

    private void TrySpawnWave()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("[刷怪点] 未配置敌人预制体 (enemyPrefab),跳过本波。", this);
            return;
        }

        CleanupDead();
        if (alive.Count >= maxAlivePerPoint) return; // 达到本点存活上限

        Vector3 pos = transform.position + Vector3.up * spawnHeightOffset;

        if (skipIfVisibleOnScreen && IsOnScreen(pos)) return; // 镜头内不刷
        if (requireWalkableCell && !IsWalkable(pos)) return;  // 障碍格不刷

        int count = Mathf.Min(monstersPerWave, maxAlivePerPoint - alive.Count);
        for (int i = 0; i < count; i++)
        {
            // 出生即面向玩家(水平方向)
            Vector3 face = Vector3.forward;
            if (flowField != null && flowField.player != null)
            {
                face = flowField.player.position - pos;
                face.y = 0f;
                if (face.sqrMagnitude < 0.001f) face = Vector3.forward;
            }
            GameObject go = Instantiate(enemyPrefab, pos,
                Quaternion.LookRotation(face.normalized, Vector3.up), GetSpawnParent());
            alive.Add(go);
        }
    }

    private bool IsOnScreen(Vector3 worldPos)
    {
        Camera cam = Camera.main;
        if (cam == null) return false; // 找不到相机时不拦截
        Vector3 v = cam.WorldToViewportPoint(worldPos);
        return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
    }

    private bool IsWalkable(Vector3 worldPos)
    {
        if (flowField == null) flowField = FindFirstObjectByType<FlowFieldManager>();
        if (flowField == null) return true; // 没有流场就不拦截
        Vector2Int g = flowField.WorldToGrid(worldPos);
        return flowField.GetCellState(g.x, g.y) != CellState.Obstacle;
    }

    private Transform GetSpawnParent()
    {
        if (spawnParent != null) return spawnParent;
        if (autoRoot == null)
        {
            GameObject root = GameObject.Find("[SpawnedEnemies]");
            if (root == null) root = new GameObject("[SpawnedEnemies]");
            autoRoot = root.transform;
        }
        return autoRoot;
    }

    private void CleanupDead()
    {
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            if (alive[i] == null) alive.RemoveAt(i);
        }
    }

    [ContextMenu("测试/立即刷一波")]
    private void DebugSpawnNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[刷怪点] 请先进入 Play 模式再测试。", this);
            return;
        }
        TrySpawnWave();
    }

    private void OnDrawGizmos()
    {
        // 橙色线框球 = 刷怪点位置,黄线 = 指向玩家(编辑时始终可见)
        Gizmos.color = new Color(1f, 0.55f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);
        if (flowField == null) flowField = FindFirstObjectByType<FlowFieldManager>();
        if (flowField != null && flowField.player != null)
        {
            Gizmos.color = new Color(1f, 0.9f, 0f, 0.5f);
            Gizmos.DrawLine(transform.position, flowField.player.position);
        }
    }
}
