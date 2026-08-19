using System;
using VampireHunt.Core;
using VampireHunt.Navigation.Contracts;

namespace VampireHunt.Navigation.Domain
{
    /// <summary>
    /// Pure world-to-grid navigation implementation useful for offline
    /// simulation and tests. A Unity adapter can use the same port while
    /// providing scene-derived grid data.
    /// </summary>
    public sealed class FlowFieldNavigationField : INavigationField
    {
        private readonly FlowGrid _grid;
        private readonly FlowFieldCache _cache;
        private readonly float _cellSize;
        private readonly WorldPosition _origin;
        private long _obstacleRevision;

        public FlowFieldNavigationField(
            FlowGrid grid,
            WorldPosition origin,
            float cellSize,
            long obstacleRevision = 0,
            FlowFieldCache cache = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            _origin = origin;
            _cellSize = cellSize;
            _obstacleRevision = obstacleRevision;
            _cache = cache ?? new FlowFieldCache(grid, capacity: 32);
        }

        public long ObstacleRevision => _obstacleRevision;

        public void SetObstacleRevision(long revision)
        {
            if (revision == _obstacleRevision) return;
            _obstacleRevision = revision;
            _cache.Invalidate(revision);
        }

        public Direction SampleDirection(WorldPosition position, WorldPosition target)
        {
            CellIndex current = ToCell(position);
            CellIndex targetCell = ToCell(target);
            if (!_grid.TryGetCell(targetCell, out FlowCell targetData) || !targetData.IsWalkable)
            {
                if (!_grid.TryFindNearestWalkable(targetCell, Math.Max(_grid.Width, _grid.Height), out targetCell))
                {
                    return Direction.None;
                }
            }

            FlowField field = _cache.GetOrBuild(targetCell, _obstacleRevision);
            if (field.TryGetDirection(current, out Direction direction)) return direction;

            if (!_grid.TryFindNearestWalkable(current, Math.Max(_grid.Width, _grid.Height), out CellIndex recovery))
            {
                return Direction.None;
            }

            return field.GetDirection(recovery);
        }

        public bool IsWalkable(WorldPosition position)
        {
            return _grid.TryGetCell(ToCell(position), out FlowCell cell) && cell.IsWalkable;
        }

        public WorldPosition TryFindRecovery(WorldPosition position)
        {
            CellIndex current = ToCell(position);
            if (_grid.TryGetCell(current, out FlowCell cell) && cell.IsWalkable) return position;
            if (!_grid.TryFindNearestWalkable(current, Math.Max(_grid.Width, _grid.Height), out CellIndex recovery))
            {
                return position;
            }

            return ToWorld(recovery, position.Y);
        }

        public CellIndex ToCell(WorldPosition position)
        {
            int x = (int)Math.Floor((position.X - _origin.X) / _cellSize);
            int y = (int)Math.Floor((position.Z - _origin.Z) / _cellSize);
            return new CellIndex(x, y);
        }

        public WorldPosition ToWorld(CellIndex index, float y = 0f)
        {
            return new WorldPosition(
                _origin.X + (index.X + 0.5f) * _cellSize,
                y,
                _origin.Z + (index.Y + 0.5f) * _cellSize);
        }
    }
}
