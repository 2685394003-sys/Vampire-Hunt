using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Navigation.Domain;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    /// <summary>
    /// Scene authoring and lifecycle shell for <see cref="FlowFieldWorldAdapter"/>.
    /// It owns Unity serialization and obstacle rescans while the Navigation
    /// domain remains the only flow-field solver.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlowFieldWorldAdapterBehaviour : MonoBehaviour, INavigationField
    {
        [Header("网格设置 / Grid Settings")]
        [SerializeField, Min(0.1f)] private float cellSize = 1.2f;
        [SerializeField, Min(1)] private int gridWidth = 120;
        [SerializeField, Min(1)] private int gridHeight = 120;
        [SerializeField, Tooltip("启动时根据指定地图根节点下的渲染器和碰撞体自动设置流场范围。")]
        private bool fitToMapBounds = true;
        [SerializeField, Tooltip("用于计算可玩区域的地图根节点。留空时继续使用手动网格设置。")]
        private Transform mapBoundsSource;
        [SerializeField, Min(0f)] private float mapBoundsPadding = 8f;
        [SerializeField, Min(32), Tooltip("自动适配时每条轴允许的最大格数；超出后会增大格子以限制重建开销。")]
        private int maxAutoCellsPerAxis = 256;
        [SerializeField] private LayerMask obstacleLayer;
        [SerializeField, Min(0f)] private float clearanceRadius = 0.2f;
        [SerializeField, Min(0)] private int recoveryRadiusCells = 8;

        [Header("更新 / Refresh")]
        [SerializeField, Min(0.02f)] private float refreshInterval = 0.2f;
        [SerializeField, Tooltip("仅在障碍物会移动且没有显式调用 MarkObstaclesDirty 时启用。")]
        private bool periodicallyRescanDynamicObstacles;
        [SerializeField, Min(0.2f)] private float dynamicObstacleRescanInterval = 1f;

        private FlowFieldWorldAdapter adapter;
        private float refreshTimer;
        private float obstacleRescanTimer;
        private bool obstaclesDirty;

        public long ObstacleRevision => EnsureAdapter().ObstacleRevision;
        public float EffectiveCellSize => EnsureAdapter().CellSize;

        private void Update()
        {
            // Navigation is server/offline simulation data.  Build lazily when a
            // composed runtime first samples the port so clients do not pay for
            // an unused full-scene physics scan at startup.
            if (adapter == null) return;

            refreshTimer += Time.deltaTime;
            obstacleRescanTimer += Time.deltaTime;

            if (periodicallyRescanDynamicObstacles &&
                obstacleRescanTimer >= dynamicObstacleRescanInterval)
            {
                obstaclesDirty = true;
            }

            if (!obstaclesDirty || refreshTimer < refreshInterval) return;
            RebuildAdapter();
        }

        public void MarkObstaclesDirty() => obstaclesDirty = true;

        [ContextMenu("重新扫描障碍 / Rescan Obstacles")]
        public void RebuildAdapter()
        {
            Bounds authoredBounds = CalculateAuthoredBounds();
            adapter = new FlowFieldWorldAdapter(
                authoredBounds,
                cellSize,
                obstacleLayer,
                clearanceRadius,
                recoveryRadiusCells,
                maxCellsPerAxis: maxAutoCellsPerAxis,
                alignOriginToCell: false);
            obstaclesDirty = false;
            refreshTimer = 0f;
            obstacleRescanTimer = 0f;
        }

        [ContextMenu("重新适配地图并扫描 / Refit Map Bounds And Rescan")]
        public void RefitMapBoundsAndRescan() => RebuildAdapter();

        public Direction SampleDirection(WorldPosition position, WorldPosition target) =>
            EnsureAdapter().SampleDirection(position, target);

        public bool IsWalkable(WorldPosition position) =>
            EnsureAdapter().IsWalkable(position);

        public WorldPosition TryFindRecovery(WorldPosition position) =>
            EnsureAdapter().TryFindRecovery(position);

        private FlowFieldWorldAdapter EnsureAdapter()
        {
            if (adapter == null) RebuildAdapter();
            return adapter;
        }

        private Bounds CalculateAuthoredBounds()
        {
            cellSize = Mathf.Max(0.1f, cellSize);
            gridWidth = Mathf.Max(1, gridWidth);
            gridHeight = Mathf.Max(1, gridHeight);
            maxAutoCellsPerAxis = Mathf.Max(32, maxAutoCellsPerAxis);

            Vector3 origin = transform.position;
            if (fitToMapBounds && mapBoundsSource != null &&
                TryCalculateMapBounds(mapBoundsSource, out Bounds mapBounds))
            {
                float minX = mapBounds.min.x - mapBoundsPadding;
                float maxX = mapBounds.max.x + mapBoundsPadding;
                float minZ = mapBounds.min.z - mapBoundsPadding;
                float maxZ = mapBounds.max.z + mapBoundsPadding;
                float spanX = Mathf.Max(0.1f, maxX - minX);
                float spanZ = Mathf.Max(0.1f, maxZ - minZ);
                cellSize = Mathf.Max(
                    cellSize,
                    spanX / maxAutoCellsPerAxis,
                    spanZ / maxAutoCellsPerAxis);
                origin = new Vector3(
                    Mathf.Floor(minX / cellSize) * cellSize,
                    transform.position.y,
                    Mathf.Floor(minZ / cellSize) * cellSize);
                gridWidth = Mathf.Max(1, Mathf.CeilToInt((maxX - origin.x) / cellSize));
                gridHeight = Mathf.Max(1, Mathf.CeilToInt((maxZ - origin.z) / cellSize));
            }

            Vector3 size = new(gridWidth * cellSize, 1f, gridHeight * cellSize);
            return new Bounds(origin + size * 0.5f, size);
        }

        private static bool TryCalculateMapBounds(Transform source, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer is ParticleSystemRenderer) continue;
                EncapsulateBounds(ref bounds, ref hasBounds, renderer.bounds);
            }

            foreach (Collider collider in source.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider is TerrainCollider) continue;
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

        private void OnValidate()
        {
            cellSize = Mathf.Max(0.1f, cellSize);
            gridWidth = Mathf.Max(1, gridWidth);
            gridHeight = Mathf.Max(1, gridHeight);
            mapBoundsPadding = Mathf.Max(0f, mapBoundsPadding);
            maxAutoCellsPerAxis = Mathf.Max(32, maxAutoCellsPerAxis);
            clearanceRadius = Mathf.Max(0f, clearanceRadius);
            recoveryRadiusCells = Mathf.Max(0, recoveryRadiusCells);
            refreshInterval = Mathf.Max(0.02f, refreshInterval);
            dynamicObstacleRescanInterval = Mathf.Max(0.2f, dynamicObstacleRescanInterval);
        }
    }
}
