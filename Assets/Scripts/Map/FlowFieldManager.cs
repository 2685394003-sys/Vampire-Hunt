using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Navigation.Domain;
using EntityId = VampireHunt.Core.EntityId;

public enum CellState
{
    Walkable,
    Obstacle
}

public struct FlowCell
{
    public CellState state;
    public int cost;
    public Vector3 flowDirection;
}

/// <summary>
/// Server-side multi-target flow fields. Physics obstacles are scanned only when
/// marked dirty; moving players rebuild inexpensive cost fields without doing
/// 72,000 Physics.CheckBox calls per second.
/// </summary>
public sealed class FlowFieldManager : MonoBehaviour, INavigationField
{
    [Header("网格设置 / Grid Settings")]
    public float cellSize = 1.2f;
    public int gridWidth = 120;
    public int gridHeight = 120;
    [Tooltip("启动时根据指定地图根节点下的渲染器和碰撞体自动设置流场范围。")]
    public bool fitToMapBounds = true;
    [Tooltip("用于计算可玩区域的地图根节点。留空时继续使用手动网格设置。")]
    public Transform mapBoundsSource;
    [Min(0f)] public float mapBoundsPadding = 8f;
    [Tooltip("自动适配时每条轴允许的最大格数；超出后会增大格子以限制重建开销。")]
    [Min(32)] public int maxAutoCellsPerAxis = 256;
    [Tooltip("Legacy/offline fallback. Network AI uses NetworkPlayerRegistry.")]
    public Transform player;
    public LayerMask obstacleLayer;

    [Header("更新 / Refresh")]
    [Min(0.02f)] public float refreshInterval = 0.2f;
    [Tooltip("Enable only when obstacles can move without notifying this manager.")]
    public bool periodicallyRescanDynamicObstacles;
    [Min(0.2f)] public float dynamicObstacleRescanInterval = 1f;

    [Header("调试 / Debug")]
    public bool drawFlowArrows = true;
    public float arrowScale = 0.4f;

    private readonly Dictionary<EntityId, TargetFlowField> targetFields = new();
    private readonly List<PlayerNetworkState> alivePlayers = new(4);
    private readonly List<EntityId> staleKeys = new();
    private CellState[,] obstacleGrid;
    private float refreshTimer;
    private float obstacleRescanTimer;
    private bool obstaclesDirty = true;
    private Vector3 gridOrigin;
    private int obstacleRevision;

    private const int StraightMoveCost = 10;
    private const int DiagonalMoveCost = 14;
    private const int RecoverySearchRadius = 3;

    private static readonly Vector2Int[] Neighbours =
    {
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
        new(-1, -1), new(1, -1), new(-1, 1), new(1, 1)
    };

    private readonly List<FrontierNode> frontier = new();

    private readonly struct FrontierNode
    {
        public readonly Vector2Int cell;
        public readonly int cost;

        public FrontierNode(Vector2Int cell, int cost)
        {
            this.cell = cell;
            this.cost = cost;
        }
    }

    private sealed class TargetFlowField
    {
        public Transform target;
        public FlowCell[,] cells;
        public Vector2Int targetCell;
        public int obstacleRevision;
        public bool initialized;
    }

    private void Awake()
    {
        ConfigureGridBounds();
        obstacleGrid = new CellState[gridWidth, gridHeight];
        ForceRescan();
    }

    private void ConfigureGridBounds()
    {
        gridOrigin = transform.position;
        cellSize = Mathf.Max(0.1f, cellSize);
        gridWidth = Mathf.Max(1, gridWidth);
        gridHeight = Mathf.Max(1, gridHeight);

        if (!fitToMapBounds || mapBoundsSource == null ||
            !TryCalculateMapBounds(mapBoundsSource, out Bounds mapBounds))
        {
            return;
        }

        float minX = mapBounds.min.x - mapBoundsPadding;
        float maxX = mapBounds.max.x + mapBoundsPadding;
        float minZ = mapBounds.min.z - mapBoundsPadding;
        float maxZ = mapBounds.max.z + mapBoundsPadding;
        float spanX = Mathf.Max(0.1f, maxX - minX);
        float spanZ = Mathf.Max(0.1f, maxZ - minZ);
        int maxCells = Mathf.Max(32, maxAutoCellsPerAxis);

        // Preserve authored resolution whenever possible. For exceptionally large
        // maps, coarsen the cells instead of allowing every target field to grow
        // without a bound and stall the server during its periodic rebuild.
        cellSize = Mathf.Max(cellSize, spanX / maxCells, spanZ / maxCells);
        gridOrigin = new Vector3(
            Mathf.Floor(minX / cellSize) * cellSize,
            transform.position.y,
            Mathf.Floor(minZ / cellSize) * cellSize);
        gridWidth = Mathf.Max(1, Mathf.CeilToInt((maxX - gridOrigin.x) / cellSize));
        gridHeight = Mathf.Max(1, Mathf.CeilToInt((maxZ - gridOrigin.z) / cellSize));
    }

    private static bool TryCalculateMapBounds(Transform source, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer)
                continue;
            EncapsulateBounds(ref bounds, ref hasBounds, renderer.bounds);
        }

        Collider[] colliders = source.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            if (collider == null || collider is TerrainCollider)
                continue;
            EncapsulateBounds(ref bounds, ref hasBounds, collider.bounds);
        }

        return hasBounds;
    }

    private static void EncapsulateBounds(
        ref Bounds combined,
        ref bool hasBounds,
        Bounds candidate)
    {
        if (!hasBounds)
        {
            combined = candidate;
            hasBounds = true;
            return;
        }

        combined.Encapsulate(candidate);
    }

    private void Update()
    {
        if (!NetworkAuthority.IsServerOrOffline())
            return;

        refreshTimer += Time.deltaTime;
        obstacleRescanTimer += Time.deltaTime;

        if (periodicallyRescanDynamicObstacles &&
            obstacleRescanTimer >= dynamicObstacleRescanInterval)
        {
            obstaclesDirty = true;
        }

        if (refreshTimer < refreshInterval)
            return;

        refreshTimer = 0f;
        if (obstaclesDirty)
            ScanObstacleGrid();
        RebuildPlayerFields();
    }

    public void MarkObstaclesDirty()
    {
        obstaclesDirty = true;
    }

    private void ScanObstacleGrid()
    {
        EnsureGridSize();
        float checkExtent = cellSize * 0.45f;
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                Vector3 worldPosition = GridToWorld(new Vector2Int(x, z));
                bool blocked = Physics.CheckBox(
                    worldPosition + Vector3.up * 0.5f,
                    new Vector3(checkExtent, 1f, checkExtent),
                    Quaternion.identity,
                    obstacleLayer);
                obstacleGrid[x, z] = blocked ? CellState.Obstacle : CellState.Walkable;
            }
        }

        obstaclesDirty = false;
        obstacleRescanTimer = 0f;
        obstacleRevision++;
    }

    private void RebuildPlayerFields()
    {
        NetworkPlayerRegistry.GetAlivePlayers(alivePlayers);
        if (alivePlayers.Count == 0 && player != null)
        {
            BuildOrRefreshField(player);
        }

        staleKeys.Clear();
        foreach (KeyValuePair<EntityId, TargetFlowField> pair in targetFields)
            staleKeys.Add(pair.Key);

        foreach (PlayerNetworkState playerState in alivePlayers)
        {
            if (playerState == null)
                continue;
            Transform target = playerState.transform;
            BuildOrRefreshField(target);
            staleKeys.Remove(EnemyLegacyEntityIds.Resolve(target.gameObject));
        }

        if (player != null)
            staleKeys.Remove(EnemyLegacyEntityIds.Resolve(player.gameObject));

        foreach (EntityId key in staleKeys)
            targetFields.Remove(key);
    }

    private void BuildOrRefreshField(Transform target)
    {
        if (target == null)
            return;

        EntityId key = EnemyLegacyEntityIds.Resolve(target.gameObject);
        Vector2Int targetCell = WorldToGrid(target.position);
        if (!targetFields.TryGetValue(key, out TargetFlowField field) ||
            field.cells.GetLength(0) != gridWidth ||
            field.cells.GetLength(1) != gridHeight)
        {
            field = new TargetFlowField
            {
                target = target,
                cells = new FlowCell[gridWidth, gridHeight]
            };
            targetFields[key] = field;
        }

        if (field.initialized &&
            field.targetCell == targetCell &&
            field.obstacleRevision == obstacleRevision)
        {
            return;
        }

        BuildField(field.cells, targetCell);
        field.targetCell = targetCell;
        field.obstacleRevision = obstacleRevision;
        field.initialized = true;
    }

    private void BuildField(FlowCell[,] cells, Vector2Int targetCell)
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                cells[x, z].state = obstacleGrid[x, z];
                cells[x, z].cost = int.MaxValue;
                cells[x, z].flowDirection = Vector3.zero;
            }
        }

        if (!IsInGrid(targetCell.x, targetCell.y))
            return;

        // A player collider may share the obstacle mask. The target cell must be a
        // valid integration-field source, while the cached obstacle grid stays unchanged.
        cells[targetCell.x, targetCell.y].state = CellState.Walkable;
        cells[targetCell.x, targetCell.y].cost = 0;

        frontier.Clear();
        PushFrontier(new FrontierNode(targetCell, 0));
        while (frontier.Count > 0)
        {
            FrontierNode node = PopFrontier();
            Vector2Int current = node.cell;
            if (node.cost != cells[current.x, current.y].cost)
                continue;

            foreach (Vector2Int offset in Neighbours)
            {
                int x = current.x + offset.x;
                int z = current.y + offset.y;
                if (!CanTraverse(cells, current.x, current.y, offset))
                    continue;

                int nextCost = node.cost + GetMoveCost(offset);
                if (nextCost >= cells[x, z].cost)
                    continue;

                cells[x, z].cost = nextCost;
                PushFrontier(new FrontierNode(new Vector2Int(x, z), nextCost));
            }
        }

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                if (cells[x, z].state == CellState.Obstacle || cells[x, z].cost == int.MaxValue)
                    continue;

                Vector3 weightedDirection = Vector3.zero;
                foreach (Vector2Int offset in Neighbours)
                {
                    int nextX = x + offset.x;
                    int nextZ = z + offset.y;
                    if (!CanTraverse(cells, x, z, offset))
                        continue;

                    int moveCost = GetMoveCost(offset);
                    if (cells[nextX, nextZ].cost > cells[x, z].cost - moveCost)
                        continue;

                    Vector3 candidateDirection = new(offset.x, 0f, offset.y);
                    candidateDirection.Normalize();
                    weightedDirection += candidateDirection * moveCost;
                }

                cells[x, z].flowDirection = weightedDirection.sqrMagnitude > 0.0001f
                    ? weightedDirection.normalized
                    : Vector3.zero;
            }
        }
    }

    private static int GetMoveCost(Vector2Int offset) =>
        offset.x != 0 && offset.y != 0 ? DiagonalMoveCost : StraightMoveCost;

    private bool CanTraverse(
        FlowCell[,] cells,
        int fromX,
        int fromZ,
        Vector2Int offset)
    {
        int toX = fromX + offset.x;
        int toZ = fromZ + offset.y;
        if (!IsInGrid(toX, toZ) || cells[toX, toZ].state == CellState.Obstacle)
            return false;

        if (offset.x == 0 || offset.y == 0)
            return true;

        // A diagonal is valid only when both side cells are clear. This keeps an
        // enemy capsule from taking a mathematical shortcut through a solid corner.
        return IsInGrid(fromX + offset.x, fromZ) &&
               IsInGrid(fromX, fromZ + offset.y) &&
               cells[fromX + offset.x, fromZ].state == CellState.Walkable &&
               cells[fromX, fromZ + offset.y].state == CellState.Walkable;
    }

    private void PushFrontier(FrontierNode node)
    {
        int index = frontier.Count;
        frontier.Add(node);
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (frontier[parent].cost <= node.cost)
                break;
            frontier[index] = frontier[parent];
            index = parent;
        }
        frontier[index] = node;
    }

    private FrontierNode PopFrontier()
    {
        FrontierNode root = frontier[0];
        int lastIndex = frontier.Count - 1;
        FrontierNode last = frontier[lastIndex];
        frontier.RemoveAt(lastIndex);
        if (lastIndex == 0)
            return root;

        int index = 0;
        while (true)
        {
            int left = index * 2 + 1;
            if (left >= frontier.Count)
                break;
            int right = left + 1;
            int child = right < frontier.Count && frontier[right].cost < frontier[left].cost
                ? right
                : left;
            if (frontier[child].cost >= last.cost)
                break;
            frontier[index] = frontier[child];
            index = child;
        }
        frontier[index] = last;
        return root;
    }

    public Vector2Int WorldToGrid(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.FloorToInt((worldPosition.x - gridOrigin.x) / cellSize),
            Mathf.FloorToInt((worldPosition.z - gridOrigin.z) / cellSize));
    }

    public Vector3 GridToWorld(Vector2Int gridPosition)
    {
        return new Vector3(
            gridOrigin.x + (gridPosition.x + 0.5f) * cellSize,
            gridOrigin.y,
            gridOrigin.z + (gridPosition.y + 0.5f) * cellSize);
    }

    public bool IsInGrid(int x, int z)
    {
        return x >= 0 && x < gridWidth && z >= 0 && z < gridHeight;
    }

    public CellState GetCellState(int x, int z)
    {
        EnsureGridSize();
        return IsInGrid(x, z) ? obstacleGrid[x, z] : CellState.Obstacle;
    }

    public void ForceRescan()
    {
        if (!NetworkAuthority.IsServerOrOffline())
            return;
        ScanObstacleGrid();
        RebuildPlayerFields();
    }

    [ContextMenu("重新适配地图并扫描 / Refit Map Bounds And Rescan")]
    public void RefitMapBoundsAndRescan()
    {
        if (!NetworkAuthority.IsServerOrOffline())
            return;

        ConfigureGridBounds();
        obstacleGrid = new CellState[gridWidth, gridHeight];
        targetFields.Clear();
        obstaclesDirty = true;
        ForceRescan();
    }

    public Vector3 GetFlowDirection(Vector3 enemyWorldPosition)
    {
        PlayerNetworkState closest = NetworkPlayerRegistry.GetClosestAlive(enemyWorldPosition);
        Transform target = closest != null ? closest.transform : player;
        return GetFlowDirection(enemyWorldPosition, target);
    }

    public Vector3 GetFlowDirection(Vector3 enemyWorldPosition, Transform target)
    {
        if (target == null)
            return Vector3.zero;

        EntityId key = EnemyLegacyEntityIds.Resolve(target.gameObject);
        if (!targetFields.TryGetValue(key, out TargetFlowField field))
        {
            BuildOrRefreshField(target);
            targetFields.TryGetValue(key, out field);
        }

        Vector2Int cell = WorldToGrid(enemyWorldPosition);
        if (field == null || !IsInGrid(cell.x, cell.y))
            return PlanarDirection(enemyWorldPosition, target.position);

        FlowCell flowCell = field.cells[cell.x, cell.y];
        if (flowCell.state == CellState.Walkable && flowCell.cost != int.MaxValue)
        {
            return flowCell.flowDirection.sqrMagnitude > 0.0001f
                ? flowCell.flowDirection
                : PlanarDirection(enemyWorldPosition, target.position);
        }

        // An enemy can briefly occupy a newly blocked cell after a rescan. Guide it
        // back to the nearest reachable cell instead of driving directly into the
        // target (and deeper into the obstacle).
        if (TryFindRecoveryCell(field.cells, cell, out Vector2Int recoveryCell))
            return PlanarDirection(enemyWorldPosition, GridToWorld(recoveryCell));

        return Vector3.zero;
    }

    Direction INavigationField.SampleDirection(WorldPosition position, WorldPosition targetPosition)
    {
        Vector3 origin = new(position.X, position.Y, position.Z);
        Vector3 flow = GetFlowDirection(origin);
        if (flow.sqrMagnitude < 0.0001f)
        {
            Vector3 targetWorld = new(targetPosition.X, targetPosition.Y, targetPosition.Z);
            flow = PlanarDirection(origin, targetWorld);
        }

        return flow.sqrMagnitude < 0.0001f
            ? Direction.None
            : new Direction(Mathf.RoundToInt(flow.x), Mathf.RoundToInt(flow.z));
    }

    bool INavigationField.IsWalkable(WorldPosition position)
    {
        Vector2Int cell = WorldToGrid(new Vector3(position.X, position.Y, position.Z));
        return IsInGrid(cell.x, cell.y) && GetCellState(cell.x, cell.y) == CellState.Walkable;
    }

    WorldPosition INavigationField.TryFindRecovery(WorldPosition position)
    {
        if (((INavigationField)this).IsWalkable(position)) return position;

        Vector2Int origin = WorldToGrid(new Vector3(position.X, position.Y, position.Z));
        for (int radius = 1; radius <= RecoverySearchRadius; radius++)
        {
            for (int x = origin.x - radius; x <= origin.x + radius; x++)
            {
                for (int z = origin.y - radius; z <= origin.y + radius; z++)
                {
                    if (Mathf.Max(Mathf.Abs(x - origin.x), Mathf.Abs(z - origin.y)) != radius ||
                        !IsInGrid(x, z) ||
                        GetCellState(x, z) != CellState.Walkable)
                    {
                        continue;
                    }

                    Vector3 recovery = GridToWorld(new Vector2Int(x, z));
                    return new WorldPosition(recovery.x, position.Y, recovery.z);
                }
            }
        }

        return position;
    }

    private bool TryFindRecoveryCell(
        FlowCell[,] cells,
        Vector2Int origin,
        out Vector2Int recoveryCell)
    {
        recoveryCell = origin;
        float bestDistance = float.PositiveInfinity;
        for (int radius = 1; radius <= RecoverySearchRadius; radius++)
        {
            bool foundAtRadius = false;
            for (int x = origin.x - radius; x <= origin.x + radius; x++)
            {
                for (int z = origin.y - radius; z <= origin.y + radius; z++)
                {
                    if (Mathf.Max(Mathf.Abs(x - origin.x), Mathf.Abs(z - origin.y)) != radius ||
                        !IsInGrid(x, z) ||
                        cells[x, z].state == CellState.Obstacle ||
                        cells[x, z].cost == int.MaxValue)
                    {
                        continue;
                    }

                    float distance = (GridToWorld(new Vector2Int(x, z)) -
                                      GridToWorld(origin)).sqrMagnitude;
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    recoveryCell = new Vector2Int(x, z);
                    foundAtRadius = true;
                }
            }

            if (foundAtRadius)
                return true;
        }

        return false;
    }

    private void EnsureGridSize()
    {
        if (obstacleGrid == null ||
            obstacleGrid.GetLength(0) != gridWidth ||
            obstacleGrid.GetLength(1) != gridHeight)
        {
            obstacleGrid = new CellState[gridWidth, gridHeight];
            obstaclesDirty = true;
        }
    }

    private static Vector3 PlanarDirection(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private void OnDrawGizmosSelected()
    {
        DrawConfiguredBoundsPreview();
        if (!drawFlowArrows)
            return;

        if (obstacleGrid == null)
            return;

        const float MaxDebugCells = 2000f;
        int stride = Mathf.Max(
            1,
            Mathf.CeilToInt(Mathf.Sqrt(gridWidth * (float)gridHeight / MaxDebugCells)));
        for (int x = 0; x < gridWidth; x += stride)
        {
            for (int z = 0; z < gridHeight; z += stride)
            {
                Gizmos.color = obstacleGrid[x, z] == CellState.Obstacle ? Color.red : Color.green;
                Gizmos.DrawWireCube(
                    GridToWorld(new Vector2Int(x, z)),
                    Vector3.one * cellSize * stride * 0.9f);
            }
        }
    }

    private void DrawConfiguredBoundsPreview()
    {
        if (!fitToMapBounds || mapBoundsSource == null ||
            !TryCalculateMapBounds(mapBoundsSource, out Bounds mapBounds))
        {
            return;
        }

        mapBounds.Expand(new Vector3(mapBoundsPadding * 2f, 0f, mapBoundsPadding * 2f));
        Vector3 center = new(mapBounds.center.x, transform.position.y, mapBounds.center.z);
        Vector3 size = new(mapBounds.size.x, 0.1f, mapBounds.size.z);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(center, size);
    }

    private void OnValidate()
    {
        cellSize = Mathf.Max(0.1f, cellSize);
        gridWidth = Mathf.Max(1, gridWidth);
        gridHeight = Mathf.Max(1, gridHeight);
        mapBoundsPadding = Mathf.Max(0f, mapBoundsPadding);
        maxAutoCellsPerAxis = Mathf.Max(32, maxAutoCellsPerAxis);
    }
}
