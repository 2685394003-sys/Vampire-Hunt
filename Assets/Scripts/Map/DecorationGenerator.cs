using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 装饰性地面贴片生成器 / Decorative ground tile generator (grass / road)
/// - 不参与寻路:强制移除所有 collider、强制 layer 0
///   Does NOT participate in pathfinding:all colliders removed,forced to layer 0
/// - 在 LevelGenerator 之后跑(协程等一帧),避开障碍格
///   Runs AFTER LevelGenerator (waits one frame),avoids obstacle cells
/// - 支持 prefab 模式或材质回退模式
///   Supports prefab mode or material fallback mode (flattened Cube)
/// </summary>
public class DecorationGenerator : MonoBehaviour
{
    public enum RoadPattern { Scatter, RandomWalk, Cross }

    [Header("草地设置 / Grass settings")]
    [Tooltip("草地 prefab 池,随机选一个;留空则用 grassMaterial 创建简单贴片 / Grass prefabs (random pick);leave empty to use grassMaterial fallback")]
    public GameObject[] grassPrefabs;
    [Tooltip("prefab 池为空时,用这个材质创建简单贴片(推荐 floor_ground_grass.mat) / Fallback material if prefabs empty (recommend floor_ground_grass.mat)")]
    public Material grassMaterial;
    [Tooltip("草地数量 / Grass tile count")]
    public int grassCount = 50;

    [Header("道路设置 / Road settings")]
    [Tooltip("道路 prefab 池;留空则用 roadMaterial 创建简单贴片 / Road prefabs;leave empty to use roadMaterial fallback")]
    public GameObject[] roadPrefabs;
    [Tooltip("prefab 池为空时,用这个材质(推荐 floor_ground_dirt.mat) / Fallback material (recommend floor_ground_dirt.mat)")]
    public Material roadMaterial;
    [Tooltip("道路数量(仅 Scatter/RandomWalk 模式生效) / Road tile count (only for Scatter/RandomWalk)")]
    public int roadCount = 30;
    [Tooltip("Scatter=纯随机;RandomWalk=蛇形小路;Cross=横竖两条主路 / Scatter=random;RandomWalk=snake path;Cross=horizontal+vertical main roads")]
    public RoadPattern roadPattern = RoadPattern.RandomWalk;

    [Header("通用 / General")]
    [Tooltip("0=每次随机;非 0=固定种子 / 0=random each run;non-zero=fixed seed")]
    public int seed = 0;
    [Tooltip("地板可生成范围(世界坐标矩形,米) / Floor bounds (world XZ rectangle,meters)")]
    public Vector2 floorMin = new Vector2(2f, 2f);
    public Vector2 floorMax = new Vector2(48f, 93f);
    [Tooltip("地板表面 Y 高度(0=自动 raycast 检测) / Floor surface Y (0=auto-detect via raycast)")]
    public float floorYOverride = 0f;
    [Range(0.5f, 1.5f)]
    [Tooltip("贴片占格子大小比例(1.0=刚好填满一格) / Tile size ratio (1.0=fills one cell)")]
    public float tileSizeRatio = 1.0f;

    [Header("调试 / Debug")]
    [Tooltip("在 Scene 窗口画出可生成区域 / Draw generation area in Scene view")]
    public bool drawGizmos = true;
    [Tooltip("可生成区域颜色 / Generation area color")]
    public Color gizmoColor = new Color(0f, 1f, 0.5f, 1f);  // 青绿色 cyan-green

    [Header("引用 / References")]
    public FlowFieldManager flowField;
    [Tooltip("生成的装饰物挂在这个物体下;留空则挂在自己下面 / Spawned decorations become children of this;leave empty to parent to self")]
    public Transform decorationParent;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();

    IEnumerator Start()
    {
        // 等一帧,确保 LevelGenerator 的 Start 已经跑完(障碍已放+流场已重扫)
        yield return null;
        Generate();
    }

    [ContextMenu("重新生成 / Regenerate")]
    public void Regenerate()
    {
        Clear();
        Generate();
    }

    [ContextMenu("清空所有生成的装饰 / Clear all spawned decorations")]
    public void Clear()
    {
        foreach (var go in spawned)
        {
            if (go == null) continue;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        spawned.Clear();
        occupiedCells.Clear();
    }

    public void Generate()
    {
        if (flowField == null) flowField = FindObjectOfType<FlowFieldManager>();
        if (flowField == null)
        {
            Debug.LogError("[DecorationGenerator] 找不到 FlowFieldManager / FlowFieldManager not found");
            return;
        }
        if (decorationParent == null) decorationParent = transform;

        // 先 raycast 找地板 Y(只算一次,后续放置都用它)
        float floorY = GetFloorY();

        int actualSeed = (seed != 0) ? seed : Random.Range(int.MinValue, int.MaxValue);
        Random.State oldState = Random.state;
        Random.InitState(actualSeed);

        int grassPlaced = PlaceGrass(grassCount, floorY);
        int roadPlaced = PlaceRoad(roadCount, floorY);

        Random.state = oldState;
        Debug.Log("[DecorationGenerator] 草地 " + grassPlaced + "/" + grassCount +
                  ",道路 " + roadPlaced + "/" + roadCount +
                  ",seed=" + actualSeed + ",floorY=" + floorY);
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
            Debug.Log("[DecorationGenerator] 自动检测地板 Y = " + hit.point.y + " / auto-detected floor Y");
            return hit.point.y;
        }
        Debug.LogWarning("[DecorationGenerator] 找不到地板,使用默认 Y=0 / Floor not found,using default Y=0");
        return 0f;
    }

    int PlaceGrass(int count, float floorY)
    {
        return PlaceScatter(grassPrefabs, grassMaterial, count, floorY);
    }

    int PlaceRoad(int count, float floorY)
    {
        switch (roadPattern)
        {
            case RoadPattern.Scatter:
                return PlaceScatter(roadPrefabs, roadMaterial, count, floorY);
            case RoadPattern.RandomWalk:
                return PlaceRandomWalk(roadPrefabs, roadMaterial, count, floorY);
            case RoadPattern.Cross:
                return PlaceCross(roadPrefabs, roadMaterial, floorY);
            default:
                return 0;
        }
    }

    int PlaceScatter(GameObject[] prefabs, Material mat, int count, float floorY)
    {
        int placed = 0, attempts = 0;
        int maxAttempts = count * 100;
        while (placed < count && attempts < maxAttempts)
        {
            attempts++;
            Vector2Int cell = RandomCellInFloor();
            if (!CanPlace(cell)) continue;
            DoPlace(cell, prefabs, mat, floorY);
            placed++;
        }
        return placed;
    }

    int PlaceRandomWalk(GameObject[] prefabs, Material mat, int steps, float floorY)
    {
        Vector2Int cur = RandomCellInFloor();
        int placed = 0, attempts = 0;
        int maxAttempts = steps * 30;
        Vector2Int[] dirs = {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        while (placed < steps && attempts < maxAttempts)
        {
            attempts++;
            if (CanPlace(cur))
            {
                DoPlace(cur, prefabs, mat, floorY);
                placed++;
            }
            cur += dirs[Random.Range(0, dirs.Length)];
            if (!IsInFloor(cur))
            {
                cur = RandomCellInFloor();
            }
        }
        return placed;
    }

    int PlaceCross(GameObject[] prefabs, Material mat, float floorY)
    {
        int placed = 0;
        int midZ = flowField.WorldToGrid(new Vector3(0, 0, (floorMin.y + floorMax.y) * 0.5f)).y;
        int midX = flowField.WorldToGrid(new Vector3((floorMin.x + floorMax.x) * 0.5f, 0, 0)).x;

        Vector2Int startH = flowField.WorldToGrid(new Vector3(floorMin.x, 0, 0));
        Vector2Int endH = flowField.WorldToGrid(new Vector3(floorMax.x, 0, 0));
        Vector2Int startV = flowField.WorldToGrid(new Vector3(0, 0, floorMin.y));
        Vector2Int endV = flowField.WorldToGrid(new Vector3(0, 0, floorMax.y));

        for (int x = startH.x; x <= endH.x; x++)
        {
            Vector2Int cell = new Vector2Int(x, midZ);
            if (CanPlace(cell)) { DoPlace(cell, prefabs, mat, floorY); placed++; }
        }
        for (int z = startV.y; z <= endV.y; z++)
        {
            Vector2Int cell = new Vector2Int(midX, z);
            if (CanPlace(cell)) { DoPlace(cell, prefabs, mat, floorY); placed++; }
        }
        return placed;
    }

    Vector2Int RandomCellInFloor()
    {
        float worldX = Random.Range(floorMin.x, floorMax.x);
        float worldZ = Random.Range(floorMin.y, floorMax.y);
        return flowField.WorldToGrid(new Vector3(worldX, 0, worldZ));
    }

    bool IsInFloor(Vector2Int cell)
    {
        Vector3 world = flowField.GridToWorld(cell);
        return world.x >= floorMin.x && world.x <= floorMax.x &&
               world.z >= floorMin.y && world.z <= floorMax.y;
    }

    bool CanPlace(Vector2Int cell)
    {
        if (!flowField.IsInGrid(cell.x, cell.y)) return false;
        if (flowField.GetCellState(cell.x, cell.y) == CellState.Obstacle) return false;
        if (occupiedCells.Contains(cell)) return false;
        return true;
    }

    void DoPlace(Vector2Int cell, GameObject[] prefabs, Material mat, float floorY)
    {
        occupiedCells.Add(cell);
        Vector3 cellCenter = flowField.GridToWorld(cell);
        Quaternion rot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        bool hasPrefabs = (prefabs != null && prefabs.Length > 0);
        GameObject go;

        if (hasPrefabs)
        {
            GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
            // 稍微抬高避免 z-fighting(0.05 是经验值,prefab 轴心在底部的可以改 0)
            cellCenter.y = floorY + 0.05f;
            go = Instantiate(prefab, cellCenter, rot, decorationParent);
        }
        else if (mat != null)
        {
            // Cube 压扁做贴片:localScale.y=0.04,半高 0.02,加 0.005 防 z-fighting
            cellCenter.y = floorY + 0.025f;
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = cellCenter;
            go.transform.rotation = rot;
            float s = flowField.cellSize * tileSizeRatio;
            go.transform.localScale = new Vector3(s, 0.04f, s);
            go.GetComponent<Renderer>().material = mat;
            if (decorationParent != null) go.transform.SetParent(decorationParent, true);
        }
        else
        {
            Debug.LogWarning("[DecorationGenerator] 既没 prefab 也没 material / No prefab or material");
            return;
        }

        // 关键:移除所有 collider,确保不参与流场
        foreach (var col in go.GetComponentsInChildren<Collider>())
        {
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        }
        // 强制非障碍 layer(layer 0 = Default)
        SetLayerRecursively(go, 0);

        // prefab 模式:缩放到一格大小
        if (hasPrefabs)
        {
            FitToTile(go, flowField.cellSize * tileSizeRatio);
        }

        spawned.Add(go);
    }

    void FitToTile(GameObject go, float targetSize)
    {
        // 装饰没 collider,用 Renderer.bounds 测
        Renderer r = go.GetComponentInChildren<Renderer>();
        if (r == null) return;
        Vector3 worldSize = r.bounds.size;
        float maxXZ = Mathf.Max(worldSize.x, worldSize.z);
        if (maxXZ > 0.001f)
        {
            float k = targetSize / maxXZ;
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

    // ===== Gizmos 可视化 / Gizmos visualization =====
    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // 外框:青色扁平立方体(贴地,跟障碍的高框区分)
        Vector3 center = new Vector3(
            (floorMin.x + floorMax.x) * 0.5f,
            0.15f,
            (floorMin.y + floorMax.y) * 0.5f
        );
        Vector3 size = new Vector3(
            floorMax.x - floorMin.x,
            0.3f,
            floorMax.y - floorMin.y
        );
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(center, size);

        // 4 个角球标记
        float markerSize = 0.4f;
        Gizmos.DrawSphere(new Vector3(floorMin.x, 0.15f, floorMin.y), markerSize);
        Gizmos.DrawSphere(new Vector3(floorMax.x, 0.15f, floorMin.y), markerSize);
        Gizmos.DrawSphere(new Vector3(floorMin.x, 0.15f, floorMax.y), markerSize);
        Gizmos.DrawSphere(new Vector3(floorMax.x, 0.15f, floorMax.y), markerSize);
    }
}
