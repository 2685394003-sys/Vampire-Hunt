using System;

namespace VampireHunt.Navigation.Domain
{
    /// <summary>
    /// Immutable result of a flow-field solve. Directions for blocked and
    /// unreachable cells are Direction.None; consumers must check CanReach
    /// before treating a direction as a valid route.
    /// </summary>
    public sealed class FlowField
    {
        private readonly Direction[] _directions;
        private readonly bool[] _reachable;
        private readonly int[] _costs;

        internal FlowField(FlowGrid grid, CellIndex target, Direction[] directions, bool[] reachable, int[] costs)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            Target = target;
            _directions = directions ?? throw new ArgumentNullException(nameof(directions));
            _reachable = reachable ?? throw new ArgumentNullException(nameof(reachable));
            _costs = costs ?? throw new ArgumentNullException(nameof(costs));
            if (_directions.Length != grid.Count || _reachable.Length != grid.Count || _costs.Length != grid.Count)
            {
                throw new ArgumentException("Flow field arrays must match the grid size.");
            }
        }

        public FlowGrid Grid { get; }
        public CellIndex Target { get; }

        public Direction GetDirection(CellIndex index)
        {
            return Grid.IsInside(index) ? _directions[Grid.ToLinearIndex(index)] : Direction.None;
        }

        public bool TryGetDirection(CellIndex index, out Direction direction)
        {
            if (!Grid.IsInside(index))
            {
                direction = Direction.None;
                return false;
            }

            int linear = Grid.ToLinearIndex(index);
            direction = _directions[linear];
            return _reachable[linear];
        }

        public bool CanReach(CellIndex index) =>
            Grid.IsInside(index) && _reachable[Grid.ToLinearIndex(index)];

        public int CostToTarget(CellIndex index)
        {
            if (!Grid.IsInside(index)) return int.MaxValue;
            return _costs[Grid.ToLinearIndex(index)];
        }

        internal static FlowField Empty(FlowGrid grid, CellIndex target)
        {
            Direction[] directions = new Direction[grid.Count];
            bool[] reachable = new bool[grid.Count];
            int[] costs = new int[grid.Count];
            for (int i = 0; i < costs.Length; i++) costs[i] = int.MaxValue;
            return new FlowField(grid, target, directions, reachable, costs);
        }
    }
}
