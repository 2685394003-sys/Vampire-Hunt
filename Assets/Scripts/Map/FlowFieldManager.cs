using System.Collections.Generic;
using UnityEngine;

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
public sealed class FlowFieldManager : MonoBehaviour
{
    [Header("网格设置 / Grid Settings")]
    public float cellSize = 1.2f;
    public int gridWidth = 120;
    public int gridHeight = 120;
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

    private readonly Dictionary<int, TargetFlowField> targetFields = new();
    private readonly List<PlayerNetworkState> alivePlayers = new(4);
    private readonly List<int> staleKeys = new();
    private CellState[,] obstacleGrid;
    private float refreshTimer;
    private float obstacleRescanTimer;
    private bool obstaclesDirty = true;

    private static readonly Vector2Int[] Neighbours =
    {
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1, 0),                new(1, 0),
        new(-1, 1),  new(0, 1),  new(1, 1)
    };

    private sealed class TargetFlowField
    {
        public Transform target;
        public FlowCell[,] cells;
    }

    private void Awake()
    {
        obstacleGrid = new CellState[gridWidth, gridHeight];
        ForceRescan();
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
    }

    private void RebuildPlayerFields()
    {
        NetworkPlayerRegistry.GetAlivePlayers(alivePlayers);
        if (alivePlayers.Count == 0 && player != null)
        {
            BuildOrRefreshField(player);
        }

        staleKeys.Clear();
        foreach (KeyValuePair<int, TargetFlowField> pair in targetFields)
            staleKeys.Add(pair.Key);

        foreach (PlayerNetworkState playerState in alivePlayers)
        {
            if (playerState == null)
                continue;
            Transform target = playerState.transform;
            BuildOrRefreshField(target);
            staleKeys.Remove(target.GetInstanceID());
        }

        if (player != null)
            staleKeys.Remove(player.GetInstanceID());

        foreach (int key in staleKeys)
            targetFields.Remove(key);
    }

    private void BuildOrRefreshField(Transform target)
    {
        if (target == null)
            return;

        int key = target.GetInstanceID();
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

        BuildField(field.cells, target.position);
    }

    private void BuildField(FlowCell[,] cells, Vector3 targetPosition)
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

        Vector2Int targetCell = WorldToGrid(targetPosition);
        if (!IsInGrid(targetCell.x, targetCell.y))
            return;

        // A player collider may share the obstacle mask. The target cell must be a
        // valid BFS source but the cached obstacle grid itself stays unchanged.
        cells[targetCell.x, targetCell.y].state = CellState.Walkable;
        cells[targetCell.x, targetCell.y].cost = 0;

        Queue<Vector2Int> queue = new();
        queue.Enqueue(targetCell);
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int nextCost = cells[current.x, current.y].cost + 1;
            foreach (Vector2Int offset in Neighbours)
            {
                int x = current.x + offset.x;
                int z = current.y + offset.y;
                if (!IsInGrid(x, z) || cells[x, z].state == CellState.Obstacle)
                    continue;
                if (nextCost >= cells[x, z].cost)
                    continue;
                cells[x, z].cost = nextCost;
                queue.Enqueue(new Vector2Int(x, z));
            }
        }

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                if (cells[x, z].state == CellState.Obstacle || cells[x, z].cost == int.MaxValue)
                    continue;

                int bestCost = cells[x, z].cost;
                Vector3 bestDirection = Vector3.zero;
                foreach (Vector2Int offset in Neighbours)
                {
                    int nextX = x + offset.x;
                    int nextZ = z + offset.y;
                    if (!IsInGrid(nextX, nextZ) || cells[nextX, nextZ].state == CellState.Obstacle)
                        continue;
                    if (cells[nextX, nextZ].cost >= bestCost)
                        continue;

                    bestCost = cells[nextX, nextZ].cost;
                    bestDirection = (GridToWorld(new Vector2Int(nextX, nextZ)) -
                                     GridToWorld(new Vector2Int(x, z))).normalized;
                }
                cells[x, z].flowDirection = bestDirection;
            }
        }
    }

    public Vector2Int WorldToGrid(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPosition.x / cellSize),
            Mathf.FloorToInt(worldPosition.z / cellSize));
    }

    public Vector3 GridToWorld(Vector2Int gridPosition)
    {
        return new Vector3(
            (gridPosition.x + 0.5f) * cellSize,
            0f,
            (gridPosition.y + 0.5f) * cellSize);
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

        int key = target.GetInstanceID();
        if (!targetFields.TryGetValue(key, out TargetFlowField field))
        {
            BuildOrRefreshField(target);
            targetFields.TryGetValue(key, out field);
        }

        Vector2Int cell = WorldToGrid(enemyWorldPosition);
        if (field == null || !IsInGrid(cell.x, cell.y))
            return PlanarDirection(enemyWorldPosition, target.position);

        Vector3 direction = field.cells[cell.x, cell.y].flowDirection;
        return direction.sqrMagnitude > 0.0001f
            ? direction
            : PlanarDirection(enemyWorldPosition, target.position);
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

    private void OnDrawGizmos()
    {
        if (obstacleGrid == null)
            return;
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                Gizmos.color = obstacleGrid[x, z] == CellState.Obstacle ? Color.red : Color.green;
                Gizmos.DrawWireCube(
                    GridToWorld(new Vector2Int(x, z)),
                    Vector3.one * cellSize * 0.9f);
            }
        }
    }
}
