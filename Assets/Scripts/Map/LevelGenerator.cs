using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 运行时随机障碍生成器 / Runtime random obstacle generator
/// 配合 FlowFieldManager / Works with FlowFieldManager:
/// 1. 在地板范围内随机选格子,严格"一格一物"
///    Randomly picks cells within floor bounds,strictly one obstacle per cell
/// 2. 每放一个,用 FlowFieldManager 立即重扫,BFS 验证 4 角落仍能连通到玩家
///    After each placement,force flow field rescan and BFS-verify 4 corners are still reachable from player
/// 3. 不通就回滚,换另一个格子重试
///    If not reachable,rollback and try another cell
/// </summary>
public class LevelGenerator : MonoBehaviour
{
    [Header("障碍 prefab 池 / Obstacle prefabs (random pick one each time)")]
    public GameObject[] obstaclePrefabs;

    [Header("生成数量 / Obstacle count")]
    public int obstacleCount = 30;

    [Header("随机种子 / Seed (0=random each run;non-zero=fixed for debug)")]
    public int seed = 0;

    [Header("地板可生成范围 / Floor bounds (world XZ rectangle,in meters)")]
    public Vector2 floorMin = new Vector2(2f, 2f);
    public Vector2 floorMax = new Vector2(48f, 93f);

    [Header("地板表面 Y 高度 / Floor surface Y (0=auto-detect via raycast)")]
    public float floorYOverride = 0f;

    [Header("玩家安全半径 / Player safe radius (cells)")]
    [Tooltip("玩家所在格周围多少格内不放障碍,防止开局被围 / No obstacles within N cells of player to avoid spawn-trap")]
    public int playerSafeRadius = 3;

    [Header("强制缩放到一格 / Force fit to one cell")]
    public bool fitToOneCell = true;
    [Range(0.5f, 1f)]
    [Tooltip("障碍物最大宽度 = cellSize × fitRatio / Obstacle max width = cellSize × fitRatio")]
    public float fitRatio = 0.9f;

    [Header("随机 Y 轴旋转 / Random Y rotation (visual variety)")]
    public bool randomYRotation = true;

    [Header("调试 / Debug")]
    [Tooltip("在 Scene 窗口画出可生成区域 / Draw generation area in Scene view")]
    public bool drawGizmos = true;
    [Tooltip("可生成区域颜色 / Generation area color")]
    public Color gizmoColor = new Color(1f, 0.6f, 0f, 1f);  // 橙色 orange

    [Header("引用 / References")]
    public FlowFieldManager flowField;
    public Transform player;
    [Tooltip("生成的障碍物会作为这个物体的子物体;留空则挂在自己下面 / Spawned obstacles become children of this;leave empty to parent to self")]
    public Transform obstacleParent;

    private readonly List<GameObject> spawned = new List<GameObject>();

    void Start()
    {
        Generate();
    }

    [ContextMenu("重新生成 / Regenerate")]
    public void Regenerate()
    {
        Clear();
        Generate();
    }

    [ContextMenu("清空所有生成的障碍 / Clear all spawned obstacles")]
    public void Clear()
    {
        foreach (var go in spawned)
        {
            if (go == null) continue;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        spawned.Clear();
    }

    public void Generate()
    {
        if (obstaclePrefabs == null || obstaclePrefabs.Length == 0)
        {
            Debug.LogWarning("[LevelGenerator] obstaclePrefabs 为空,跳过生成 / obstaclePrefabs empty,skipping");
            return;
        }
        if (flowField == null) flowField = FindObjectOfType<FlowFieldManager>();
        if (flowField == null)
        {
            Debug.LogError("[LevelGenerator] 场景里找不到 FlowFieldManager / FlowFieldManager not found in scene");
            return;
        }
        if (player == null) player = flowField.player;
        if (player == null)
        {
            Debug.LogError("[LevelGenerator] player 未指定 / player not assigned");
            return;
        }
        if (obstacleParent == null) obstacleParent = transform;

        // 先 raycast 找地板 Y(只算一次,后续放置都用它)
        float floorY = GetFloorY();

        int actualSeed = (seed != 0) ? seed : Random.Range(int.MinValue, int.MaxValue);
        Random.State oldState = Random.state;
        Random.InitState(actualSeed);

        Vector2Int playerCell = flowField.WorldToGrid(player.position);
        float cellSize = flowField.cellSize;

        int placed = 0, attempts = 0;
        int maxAttempts = obstacleCount * 100;

        while (placed < obstacleCount && attempts < maxAttempts)
        {
            attempts++;

            float worldX = Random.Range(floorMin.x, floorMax.x);
            float worldZ = Random.Range(floorMin.y, floorMax.y);
            Vector2Int cell = flowField.WorldToGrid(new Vector3(worldX, 0, worldZ));

            if (Mathf.Abs(cell.x - playerCell.x) <= playerSafeRadius &&
                Mathf.Abs(cell.y - playerCell.y) <= playerSafeRadius)
                continue;

            if (flowField.GetCellState(cell.x, cell.y) == CellState.Obstacle)
                continue;

            Vector3 cellCenter = flowField.GridToWorld(cell);
            cellCenter.y = floorY;
            GameObject temp = CreateTempObstacle(cellCenter, cellSize);

            flowField.ForceRescan();
            bool ok = ValidateConnectivity(playerCell);

            var tempCol = temp.GetComponent<Collider>();
            if (tempCol != null) tempCol.enabled = false;
            if (Application.isPlaying) Destroy(temp); else DestroyImmediate(temp);

            if (!ok)
            {
                flowField.ForceRescan();
                continue;
            }

            GameObject prefab = obstaclePrefabs[Random.Range(0, obstaclePrefabs.Length)];
            Quaternion rot = randomYRotation
                ? Quaternion.Euler(0, Random.Range(0f, 360f), 0)
                : prefab.transform.rotation;
            GameObject go = Instantiate(prefab, cellCenter, rot, obstacleParent);

            if (go.GetComponentInChildren<Collider>() == null)
            {
                Debug.LogWarning("[LevelGenerator] prefab '" + prefab.name + "' 没有 Collider / has no Collider");
            }

            SetLayerRecursively(go, 3);
            if (fitToOneCell) FitToCell(go, cellSize);

            spawned.Add(go);
            placed++;
        }

        flowField.ForceRescan();
        Random.state = oldState;
        Debug.Log("[LevelGenerator] 放置 " + placed + "/" + obstacleCount +
                  " 障碍 / placed,seed=" + actualSeed + ",attempts=" + attempts +
                  ",floorY=" + floorY);
    }

    // 从地板中心高空往下打射线,命中点就是地板表面 Y
    // 用 layerMask 排除 layer 3 (obstacle),避免射线打到障碍物
    float GetFloorY()
    {
        if (floorYOverride != 0f) return floorYOverride;
        Vector3 center = new Vector3((floorMin.x + floorMax.x) / 2f, 50f, (floorMin.y + floorMax.y) / 2f);
        int mask = ~(1 << 3);
        if (Physics.Raycast(center, Vector3.down, out RaycastHit hit, 100f, mask))
        {
            Debug.Log("[LevelGenerator] 自动检测地板 Y = " + hit.point.y + " / auto-detected floor Y");
            return hit.point.y;
        }
        Debug.LogWarning("[LevelGenerator] 找不到地板,使用默认 Y=0 / Floor not found,using default Y=0");
        return 0f;
    }

    GameObject CreateTempObstacle(Vector3 position, float cellSize)
    {
        GameObject go = new GameObject("__temp_obstacle__");
        go.transform.position = position;
        go.layer = 3;
        var col = go.AddComponent<BoxCollider>();
        col.size = Vector3.one * cellSize * 0.9f;
        return go;
    }

    void FitToCell(GameObject go, float cellSize)
    {
        Collider col = go.GetComponentInChildren<Collider>();
        if (col == null) return;
        Vector3 worldSize = col.bounds.size;
        float maxXZ = Mathf.Max(worldSize.x, worldSize.z);
        float target = cellSize * fitRatio;
        if (maxXZ > target && maxXZ > 0.001f)
        {
            float k = target / maxXZ;
            go.transform.localScale *= k;
        }
    }

    void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    bool ValidateConnectivity(Vector2Int playerCell)
    {
        if (flowField.GetCellState(playerCell.x, playerCell.y) == CellState.Obstacle)
            return false;

        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(playerCell);
        visited.Add(playerCell);

        Vector2Int[] neighbors = {
            new Vector2Int(-1, 0), new Vector2Int(1, 0),
            new Vector2Int(0, -1), new Vector2Int(0, 1),
        };

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var off in neighbors)
            {
                Vector2Int next = cur + off;
                if (visited.Contains(next)) continue;
                if (!flowField.IsInGrid(next.x, next.y)) continue;
                if (flowField.GetCellState(next.x, next.y) == CellState.Obstacle) continue;
                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        Vector2Int[] corners = {
            flowField.WorldToGrid(new Vector3(floorMin.x + 1f, 0, floorMin.y + 1f)),
            flowField.WorldToGrid(new Vector3(floorMax.x - 1f, 0, floorMin.y + 1f)),
            flowField.WorldToGrid(new Vector3(floorMin.x + 1f, 0, floorMax.y - 1f)),
            flowField.WorldToGrid(new Vector3(floorMax.x - 1f, 0, floorMax.y - 1f)),
        };
        foreach (var c in corners)
        {
            if (!visited.Contains(c)) return false;
        }
        return true;
    }

    // ===== Gizmos 可视化 / Gizmos visualization =====
    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // 外框:橙色 3D 立方体(高一些,跟装饰的扁框区分)
        Vector3 center = new Vector3(
            (floorMin.x + floorMax.x) * 0.5f,
            0.5f,
            (floorMin.y + floorMax.y) * 0.5f
        );
        Vector3 size = new Vector3(
            floorMax.x - floorMin.x,
            1f,
            floorMax.y - floorMin.y
        );
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(center, size);

        // 4 个角球标记
        float markerSize = 0.5f;
        Gizmos.DrawSphere(new Vector3(floorMin.x, 0.5f, floorMin.y), markerSize);
        Gizmos.DrawSphere(new Vector3(floorMax.x, 0.5f, floorMin.y), markerSize);
        Gizmos.DrawSphere(new Vector3(floorMin.x, 0.5f, floorMax.y), markerSize);
        Gizmos.DrawSphere(new Vector3(floorMax.x, 0.5f, floorMax.y), markerSize);

        // 玩家安全区(红色):以玩家为中心,半径 = playerSafeRadius × cellSize
        if (player != null)
        {
            float cs = (flowField != null) ? flowField.cellSize : 1.2f;
            float r = playerSafeRadius * cs;
            Gizmos.color = new Color(1f, 0f, 0f, 1f);
            Gizmos.DrawWireCube(player.position, new Vector3(r * 2f, 0.5f, r * 2f));
        }
    }
}
