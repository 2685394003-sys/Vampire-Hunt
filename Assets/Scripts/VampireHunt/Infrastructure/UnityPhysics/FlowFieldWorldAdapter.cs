using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Navigation.Domain;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    /// <summary>
    /// Unity-world implementation of the engine-independent navigation port.
    ///
    /// Physics is used only to author a walkability snapshot. All target-field
    /// caching, recovery and route solving are delegated to the Navigation
    /// Domain, so this adapter cannot grow a second Unity-side solver.
    /// </summary>
    public sealed class FlowFieldWorldAdapter : INavigationField
    {
        private readonly Bounds bounds;
        private readonly int obstacleMask;
        private readonly float clearanceRadius;
        private readonly int recoveryRadiusCells;
        private readonly Func<WorldPosition, WorldPosition, Direction> sampler;
        private readonly float authoredCellSize;
        private readonly int maxCellsPerAxis;
        private readonly Vector3 gridOrigin;
        private FlowFieldNavigationField domainNavigation;
        private long obstacleRevision;

        public FlowFieldWorldAdapter(
            Bounds bounds,
            float cellSize = 1f,
            int obstacleMask = Physics.DefaultRaycastLayers,
            float clearanceRadius = 0.2f,
            int recoveryRadiusCells = 8,
            Func<WorldPosition, WorldPosition, Direction> sampler = null,
            int maxCellsPerAxis = 256,
            bool alignOriginToCell = true)
        {
            if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (maxCellsPerAxis <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCellsPerAxis));

            this.bounds = bounds;
            this.obstacleMask = obstacleMask;
            this.clearanceRadius = Mathf.Max(0f, clearanceRadius);
            this.recoveryRadiusCells = Mathf.Max(0, recoveryRadiusCells);
            this.sampler = sampler;
            this.maxCellsPerAxis = maxCellsPerAxis;

            float spanX = Mathf.Max(0.1f, bounds.size.x);
            float spanZ = Mathf.Max(0.1f, bounds.size.z);
            authoredCellSize = Mathf.Max(cellSize, spanX / maxCellsPerAxis, spanZ / maxCellsPerAxis);
            gridOrigin = alignOriginToCell
                ? new Vector3(
                    Mathf.Floor(bounds.min.x / authoredCellSize) * authoredCellSize,
                    bounds.min.y,
                    Mathf.Floor(bounds.min.z / authoredCellSize) * authoredCellSize)
                : bounds.min;
            Rebuild();
        }

        public long ObstacleRevision => obstacleRevision;
        public float CellSize => authoredCellSize;

        /// <summary>
        /// Rescans Unity obstacles and replaces the immutable Domain grid. The
        /// revision invalidates all target fields without mutating a solved one.
        /// </summary>
        public void Rebuild()
        {
            int width = Mathf.Max(1, Mathf.CeilToInt(
                (bounds.max.x - gridOrigin.x) / authoredCellSize));
            int height = Mathf.Max(1, Mathf.CeilToInt(
                (bounds.max.z - gridOrigin.z) / authoredCellSize));

            // Keep the grid bounded even when a large scene is supplied. The
            // effective cell size is coarsened in the constructor, so this only
            // protects malformed/degenerate Bounds values.
            width = Mathf.Clamp(width, 1, maxCellsPerAxis);
            height = Mathf.Clamp(height, 1, maxCellsPerAxis);

            List<FlowCell> cells = new(width * height);
            float checkRadius = Mathf.Max(clearanceRadius, authoredCellSize * 0.45f);
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    CellIndex index = new(x, z);
                    Vector3 point = ToWorld(index);
                    bool inside = ContainsHorizontal(point);
                    bool blocked = !inside || Physics.CheckSphere(
                        point + Vector3.up * 0.5f,
                        checkRadius,
                        obstacleMask,
                        QueryTriggerInteraction.Ignore);
                    cells.Add(blocked
                        ? FlowCell.Blocked(index)
                        : FlowCell.Walkable(index));
                }
            }

            obstacleRevision++;
            domainNavigation = new FlowFieldNavigationField(
                new FlowGrid(width, height, cells),
                new WorldPosition(gridOrigin.x, gridOrigin.y, gridOrigin.z),
                authoredCellSize,
                obstacleRevision);
        }

        public Direction SampleDirection(WorldPosition position, WorldPosition target)
        {
            if (sampler != null) return sampler(position, target);
            return domainNavigation.SampleDirection(position, target);
        }

        public bool IsWalkable(WorldPosition position)
        {
            return domainNavigation != null && domainNavigation.IsWalkable(position);
        }

        public WorldPosition TryFindRecovery(WorldPosition position)
        {
            if (domainNavigation == null) return position;
            return domainNavigation.TryFindRecovery(position);
        }

        public bool IsInsideBounds(WorldPosition position) => ContainsHorizontal(ToVector3(position));

        public bool IsPathClear(WorldPosition from, WorldPosition to)
        {
            Vector3 start = ToVector3(from);
            Vector3 end = ToVector3(to);
            if (!ContainsHorizontal(end)) return false;
            return !Physics.Linecast(start, end, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        private bool ContainsHorizontal(Vector3 position) =>
            position.x >= bounds.min.x && position.x <= bounds.max.x &&
            position.z >= bounds.min.z && position.z <= bounds.max.z;

        private Vector3 ToWorld(CellIndex index) => new(
            gridOrigin.x + (index.X + 0.5f) * authoredCellSize,
            gridOrigin.y,
            gridOrigin.z + (index.Y + 0.5f) * authoredCellSize);

        private static Vector3 ToVector3(WorldPosition position) =>
            new(position.X, position.Y, position.Z);
    }
}
