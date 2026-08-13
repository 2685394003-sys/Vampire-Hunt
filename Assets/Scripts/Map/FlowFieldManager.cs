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
    public Vector2Int nextCell;
    public bool hasLineOfSight;
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
                cells[x, z].nextCell = new Vector2Int(x, z);
                cells[x, z].hasLineOfSight = false;
            }
        }

        Vector2Int targetCell = WorldToGrid(targetPosition);
        if (!IsInGrid(targetCell.x, targetCell.y))
            return;

        // A player collider may share the obstacle mask. The target cell must be a
        // valid integration-field source, while the cached obstacle grid stays unchanged.
        cells[targetCell.x, targetCell.y].state = CellState.Walkable;
        cells[targetCell.x, targetCell.y].cost = 0;
        cells[targetCell.x, targetCell.y].nextCell = targetCell;
        cells[targetCell.x, targetCell.y].hasLineOfSight = true;

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

                Vector2Int current = new(x, z);
                Vector3 currentWorld = GridToWorld(current);
                Vector3 targetDirection = PlanarDirection(currentWorld, targetPosition);
                if (HasGridLineOfSight(cells, current, targetCell))
                {
                    cells[x, z].flowDirection = targetDirection;
                    cells[x, z].nextCell = targetCell;
                    cells[x, z].hasLineOfSight = true;
                    continue;
                }

                int bestTotalCost = int.MaxValue;
                float bestAlignment = float.NegativeInfinity;
                Vector2Int bestNext = current;
                foreach (Vector2Int offset in Neighbours)
                {
                    int nextX = x + offset.x;
                    int nextZ = z + offset.y;
                    if (!CanTraverse(cells, x, z, offset) ||
                        cells[nextX, nextZ].cost >= cells[x, z].cost)
                        continue;

                    int totalCost = cells[nextX, nextZ].cost + GetMoveCost(offset);
                    Vector3 candidateDirection = new(offset.x, 0f, offset.y);
                    candidateDirection.Normalize();
                    float alignment = Vector3.Dot(candidateDirection, targetDirection);
                    if (totalCost > bestTotalCost ||
                        (totalCost == bestTotalCost && alignment <= bestAlignment))
                        continue;

                    bestTotalCost = totalCost;
                    bestAlignment = alignment;
                    bestNext = new Vector2Int(nextX, nextZ);
                }

                if (bestNext != current)
                {
                    cells[x, z].nextCell = bestNext;
                    cells[x, z].flowDirection = PlanarDirection(
                        currentWorld,
                        GridToWorld(bestNext));
                }
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

    private bool HasGridLineOfSight(
        FlowCell[,] cells,
        Vector2Int from,
        Vector2Int to)
    {
        int x = from.x;
        int z = from.y;
        int deltaX = to.x - x;
        int deltaZ = to.y - z;
        int stepX = deltaX == 0 ? 0 : (deltaX > 0 ? 1 : -1);
        int stepZ = deltaZ == 0 ? 0 : (deltaZ > 0 ? 1 : -1);
        float tDeltaX = deltaX == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(deltaX);
        float tDeltaZ = deltaZ == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(deltaZ);
        float tMaxX = tDeltaX * 0.5f;
        float tMaxZ = tDeltaZ * 0.5f;

        while (x != to.x || z != to.y)
        {
            if (Mathf.Abs(tMaxX - tMaxZ) <= 0.0001f)
            {
                // Crossing a grid corner must not squeeze between two blocked cells.
                if (!IsWalkable(cells, x + stepX, z) ||
                    !IsWalkable(cells, x, z + stepZ))
                {
                    return false;
                }

                x += stepX;
                z += stepZ;
                tMaxX += tDeltaX;
                tMaxZ += tDeltaZ;
            }
            else if (tMaxX < tMaxZ)
            {
                x += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                z += stepZ;
                tMaxZ += tDeltaZ;
            }

            if (!IsWalkable(cells, x, z))
                return false;
        }

        return true;
    }

    private bool IsWalkable(FlowCell[,] cells, int x, int z) =>
        IsInGrid(x, z) && cells[x, z].state == CellState.Walkable;

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
        Vector3 origin = transform.position;
        return new Vector2Int(
            Mathf.FloorToInt((worldPosition.x - origin.x) / cellSize),
            Mathf.FloorToInt((worldPosition.z - origin.z) / cellSize));
    }

    public Vector3 GridToWorld(Vector2Int gridPosition)
    {
        Vector3 origin = transform.position;
        return new Vector3(
            origin.x + (gridPosition.x + 0.5f) * cellSize,
            origin.y,
            origin.z + (gridPosition.y + 0.5f) * cellSize);
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

        FlowCell flowCell = field.cells[cell.x, cell.y];
        if (flowCell.state == CellState.Walkable && flowCell.cost != int.MaxValue)
        {
            if (flowCell.hasLineOfSight)
                return PlanarDirection(enemyWorldPosition, target.position);

            if (flowCell.nextCell != cell)
                return PlanarDirection(enemyWorldPosition, GridToWorld(flowCell.nextCell));

            return flowCell.flowDirection;
        }

        // An enemy can briefly occupy a newly blocked cell after a rescan. Guide it
        // back to the nearest reachable cell instead of driving directly into the
        // target (and deeper into the obstacle).
        if (TryFindRecoveryCell(field.cells, cell, out Vector2Int recoveryCell))
            return PlanarDirection(enemyWorldPosition, GridToWorld(recoveryCell));

        return Vector3.zero;
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
