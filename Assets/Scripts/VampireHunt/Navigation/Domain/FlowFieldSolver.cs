using System;
using System.Collections.Generic;

namespace VampireHunt.Navigation.Domain
{
    /// <summary>
    /// Dijkstra flow-field solver. Each cell points to the cheapest legal
    /// cardinal or diagonal neighbour on the way to the target, while
    /// preserving unreachable regions instead of inventing a direction
    /// through an obstacle.
    /// </summary>
    public sealed class FlowFieldSolver
    {
        public FlowField Solve(FlowGrid grid, CellIndex target)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (!grid.IsInside(target) || !grid.GetCell(target).IsWalkable)
            {
                return FlowField.Empty(grid, target);
            }

            int[] costs = new int[grid.Count];
            Direction[] directions = new Direction[grid.Count];
            bool[] reachable = new bool[grid.Count];
            for (int i = 0; i < costs.Length; i++) costs[i] = int.MaxValue;

            CellPriorityQueue pending = new CellPriorityQueue();
            int targetLinear = grid.ToLinearIndex(target);
            costs[targetLinear] = 0;
            pending.Enqueue(target, 0);

            List<FlowTraversal> traversals = new List<FlowTraversal>(8);
            while (pending.Count > 0)
            {
                CellPriorityQueue.Entry currentEntry = pending.Dequeue();
                CellIndex current = currentEntry.Index;
                int currentCost = currentEntry.Priority;
                int currentLinear = grid.ToLinearIndex(current);
                if (currentCost != costs[currentLinear]) continue;

                reachable[currentLinear] = true;
                grid.GetTraversals(current, traversals);
                for (int i = 0; i < traversals.Count; i++)
                {
                    FlowTraversal traversal = traversals[i];
                    CellIndex neighbour = traversal.Index;
                    FlowCell neighbourCell = grid.GetCell(neighbour);
                    int movementCost = WeightedMovementCost(
                        traversal.MovementCost,
                        neighbourCell.Cost);
                    int nextCost = SaturatingAdd(currentCost, movementCost);
                    int neighbourLinear = grid.ToLinearIndex(neighbour);
                    if (nextCost >= costs[neighbourLinear]) continue;

                    costs[neighbourLinear] = nextCost;
                    pending.Enqueue(neighbour, nextCost);
                }
            }

            // Choose the cheapest reachable traversal for every cell. Ties
            // use FlowGrid.GetTraversals order for deterministic simulations.
            for (int i = 0; i < grid.Count; i++)
            {
                CellIndex index = grid.FromLinearIndex(i);
                if (!reachable[i] || index == target) continue;

                FlowCell currentCell = grid.GetCell(index);
                grid.GetTraversals(index, traversals);
                int bestCost = int.MaxValue;
                Direction bestDirection = Direction.None;
                for (int n = 0; n < traversals.Count; n++)
                {
                    FlowTraversal traversal = traversals[n];
                    CellIndex neighbour = traversal.Index;
                    int neighbourLinear = grid.ToLinearIndex(neighbour);
                    if (!reachable[neighbourLinear]) continue;
                    int candidateCost = SaturatingAdd(
                        costs[neighbourLinear],
                        WeightedMovementCost(traversal.MovementCost, currentCell.Cost));
                    if (candidateCost >= bestCost) continue;
                    bestCost = candidateCost;
                    bestDirection = new Direction(neighbour.X - index.X, neighbour.Y - index.Y);
                }

                directions[i] = bestDirection;
            }

            return new FlowField(grid, target, directions, reachable, costs);
        }

        private static int WeightedMovementCost(int movementCost, ushort cellCost)
        {
            long weighted = (long)Math.Max(1, movementCost) * Math.Max(1, (int)cellCost);
            return weighted >= int.MaxValue ? int.MaxValue : (int)weighted;
        }

        private static int SaturatingAdd(int left, int right)
        {
            if (left == int.MaxValue || right == int.MaxValue || left > int.MaxValue - right)
                return int.MaxValue;
            return left + right;
        }

        private sealed class CellPriorityQueue
        {
            private readonly List<Entry> _entries = new List<Entry>();

            public int Count => _entries.Count;

            public void Enqueue(CellIndex index, int priority)
            {
                Entry entry = new Entry(index, priority);
                _entries.Add(entry);
                int child = _entries.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (_entries[parent].Priority <= priority) break;
                    _entries[child] = _entries[parent];
                    child = parent;
                }

                _entries[child] = entry;
            }

            public Entry Dequeue()
            {
                Entry result = _entries[0];
                int last = _entries.Count - 1;
                Entry replacement = _entries[last];
                _entries.RemoveAt(last);
                if (_entries.Count == 0) return result;

                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1;
                    if (left >= _entries.Count) break;
                    int right = left + 1;
                    int child = right < _entries.Count && _entries[right].Priority < _entries[left].Priority
                        ? right
                        : left;
                    if (_entries[child].Priority >= replacement.Priority) break;
                    _entries[parent] = _entries[child];
                    parent = child;
                }

                _entries[parent] = replacement;
                return result;
            }

            public readonly struct Entry
            {
                public Entry(CellIndex index, int priority)
                {
                    Index = index;
                    Priority = priority;
                }

                public CellIndex Index { get; }
                public int Priority { get; }
            }
        }
    }
}
