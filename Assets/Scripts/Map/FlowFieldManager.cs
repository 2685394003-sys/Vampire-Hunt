using UnityEngine;
using System.Collections.Generic;

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

public class FlowFieldManager : MonoBehaviour
{
    [Header("网格设置 / Grid Settings")]
    public float cellSize = 1.2f;
    public int gridWidth = 120;
    public int gridHeight = 120;
    public Transform player;
    public LayerMask obstacleLayer;

    [Header("调试 / Debug")]
    public bool drawFlowArrows = true;
    public float arrowScale = 0.4f;

    private FlowCell[,] grid;
    private Vector2Int targetGridPos;
    private float refreshTimer;
    public float refreshInterval = 0.2f;

    // 4方向邻域
    private readonly Vector2Int[] neighbors =
    {
    new(-1,-1),new(0,-1),new(1,-1),
    new(-1,0),          new(1,0),
    new(-1,1), new(0,1),new(1,1)
    };


    void Awake()
    {
        grid = new FlowCell[gridWidth, gridHeight];
    }

    void Update()
    {
        refreshTimer += Time.deltaTime;
        if (refreshTimer >= refreshInterval)
        {
            refreshTimer = 0;
            UpdateFlowField();
        }
    }

    // 【重要】每次刷新流场都重新扫描障碍物，不再只初始化一次！
    void ScanObstacleGrid()
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                Vector3 worldPos = GridToWorld(new Vector2Int(x, z));
                // 缩小检测范围，修复判定过宽
                float checkExt = cellSize * 0.45f; // 覆盖格子 90%,减少边缘漏检
                bool hasObstacle = Physics.CheckBox(
                    worldPos + Vector3.up * 0.5f,
                    new Vector3(checkExt, 1, checkExt),
                    Quaternion.identity,
                    obstacleLayer);

                grid[x, z].state = hasObstacle ? CellState.Obstacle : CellState.Walkable;
                grid[x, z].cost = int.MaxValue;
                grid[x, z].flowDirection = Vector3.zero;
            }
        }
    }

    void UpdateFlowField()
    {
        ScanObstacleGrid(); // 每次重建先刷新障碍

        // 重置代价
        for (int x = 0; x < gridWidth; x++)
            for (int z = 0; z < gridHeight; z++)
                grid[x, z].cost = int.MaxValue;

        targetGridPos = WorldToGrid(player.position);
        if (!IsInGrid(targetGridPos.x, targetGridPos.y)) return;

        // 保护：玩家所在格子不能是障碍！防止BFS崩溃
        if (grid[targetGridPos.x, targetGridPos.y].state == CellState.Obstacle)
        {
            Debug.LogWarning("玩家所在格子被识别为障碍物，流场失效！");
            return;
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        grid[targetGridPos.x, targetGridPos.y].cost = 0;
        queue.Enqueue(targetGridPos);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int currX = current.x;
            int currZ = current.y;

            foreach (var offset in neighbors)
            {
                int nx = currX + offset.x;
                int nz = currZ + offset.y;
                if (!IsInGrid(nx, nz)) continue;
                var cell = grid[nx, nz];
                if (cell.state == CellState.Obstacle) continue;

                int newCost = grid[currX, currZ].cost + 1;
                if (newCost < grid[nx, nz].cost)
                {
                    grid[nx, nz].cost = newCost;
                    queue.Enqueue(new Vector2Int(nx, nz));
                }
            }
        }
        GenerateFlowDirections();
    }

    void GenerateFlowDirections()
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                if (grid[x, z].state == CellState.Obstacle || grid[x, z].cost == int.MaxValue)
                {
                    grid[x, z].flowDirection = Vector3.zero;
                    continue;
                }

                Vector3 bestDir = Vector3.zero;
                int minCost = grid[x, z].cost;

                foreach (var offset in neighbors)
                {
                    int nx = x + offset.x;
                    int nz = z + offset.y;
                    if (!IsInGrid(nx, nz)) continue;
                    if (grid[nx, nz].state == CellState.Obstacle) continue;

                    if (grid[nx, nz].cost < minCost)
                    {
                        minCost = grid[nx, nz].cost;
                        Vector3 neighborWorld = GridToWorld(new Vector2Int(nx, nz));
                        Vector3 currentWorld = GridToWorld(new Vector2Int(x, z));
                        bestDir = (neighborWorld - currentWorld).normalized;
                    }
                }
                grid[x, z].flowDirection = bestDir;
            }
        }
    }

    // ========== 坐标转换【修复Round边界跳变】 ==========
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt(worldPos.x / cellSize);
        int z = Mathf.FloorToInt(worldPos.z / cellSize);
        return new Vector2Int(x, z);
    }

    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        // 返回格子中心(不是左下角),让 CheckBox 和 Gizmos 都画在格子正中
        return new Vector3((gridPos.x + 0.5f) * cellSize, 0, (gridPos.y + 0.5f) * cellSize);
    }

    public bool IsInGrid(int x, int z)
    {
        return x >= 0 && x < gridWidth && z >= 0 && z < gridHeight;
    }

    // 给 LevelGenerator 用:查询某个格子当前是 Walkable 还是 Obstacle
    public CellState GetCellState(int x, int z)
    {
        if (!IsInGrid(x, z)) return CellState.Obstacle;
        return grid[x, z].state;
    }

    // 给 LevelGenerator 用:立即重建一次流场(不等 0.2s 自动刷新)
    public void ForceRescan()
    {
        UpdateFlowField();
    }

    public Vector3 GetFlowDirection(Vector3 enemyWorldPos)
    {
        var gridPos = WorldToGrid(enemyWorldPos);
        if (!IsInGrid(gridPos.x, gridPos.y))
        {
            return (player.position - enemyWorldPos).normalized;
        }
        Vector3 dir = grid[gridPos.x, gridPos.y].flowDirection;
        // 格子无有效流向时兜底直线追击
        if (dir.magnitude < 0.01f)
        {
            dir = (player.position - enemyWorldPos).normalized;
        }
        return dir;
    }

    // ========== Gizmos 绘制：网格+流向箭头 ==========
    void OnDrawGizmos()
    {
        if (grid == null) return;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                Vector3 center = GridToWorld(new Vector2Int(x, z));
                // 绘制格子方块
                Gizmos.color = grid[x, z].state == CellState.Obstacle ? Color.red : Color.green;
                Gizmos.DrawWireCube(center, Vector3.one * cellSize * 0.9f);

            }
        }
    }
}
