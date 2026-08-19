using System;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Navigation.Domain;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    /// <summary>
    /// Unity-world implementation of the engine-independent navigation port.
    /// A caller may inject a baked flow sampler; the fallback remains useful for
    /// small offline scenes and uses deterministic grid directions.
    /// </summary>
    public sealed class FlowFieldWorldAdapter : INavigationField
    {
        private readonly Bounds bounds;
        private readonly float cellSize;
        private readonly float clearanceRadius;
        private readonly int obstacleMask;
        private readonly int recoveryRadiusCells;
        private readonly Func<WorldPosition, WorldPosition, Direction> sampler;

        public FlowFieldWorldAdapter(
            Bounds bounds,
            float cellSize = 1f,
            int obstacleMask = Physics.DefaultRaycastLayers,
            float clearanceRadius = 0.2f,
            int recoveryRadiusCells = 8,
            Func<WorldPosition, WorldPosition, Direction> sampler = null)
        {
            if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            this.bounds = bounds;
            this.cellSize = cellSize;
            this.obstacleMask = obstacleMask;
            this.clearanceRadius = Mathf.Max(0f, clearanceRadius);
            this.recoveryRadiusCells = Mathf.Max(0, recoveryRadiusCells);
            this.sampler = sampler;
        }

        public Direction SampleDirection(WorldPosition position, WorldPosition target)
        {
            if (sampler != null) return sampler(position, target);
            Vector3 delta = ToVector3(target) - ToVector3(position);
            return new Direction(Mathf.RoundToInt(delta.x / cellSize), Mathf.RoundToInt(delta.z / cellSize));
        }

        public bool IsWalkable(WorldPosition position)
        {
            Vector3 point = ToVector3(position);
            return bounds.Contains(point) &&
                !Physics.CheckSphere(point, clearanceRadius, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        public WorldPosition TryFindRecovery(WorldPosition position)
        {
            if (IsWalkable(position)) return position;
            Vector3 start = ToVector3(position);
            for (int radius = 1; radius <= recoveryRadiusCells; radius++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        if (Mathf.Abs(x) != radius && Mathf.Abs(z) != radius) continue;
                        WorldPosition candidate = new(
                            start.x + x * cellSize,
                            start.y,
                            start.z + z * cellSize);
                        if (IsWalkable(candidate)) return candidate;
                    }
                }
            }
            return position;
        }

        public bool IsInsideBounds(WorldPosition position) => bounds.Contains(ToVector3(position));

        public bool IsPathClear(WorldPosition from, WorldPosition to)
        {
            Vector3 start = ToVector3(from);
            Vector3 end = ToVector3(to);
            if (!bounds.Contains(end)) return false;
            return !Physics.Linecast(start, end, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        private static Vector3 ToVector3(WorldPosition position) =>
            new(position.X, position.Y, position.Z);
    }
}
