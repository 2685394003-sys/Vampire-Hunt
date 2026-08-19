using System;
using System.Collections.Generic;

namespace VampireHunt.Navigation.Domain
{
    /// <summary>
    /// Dijkstra flow-field solver. Each cell points to the cheapest cardinal
    /// neighbour on the way to the target, while preserving unreachable
    /// regions instead of inventing a direction through an obstacle.
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

            List<CellIndex> neighbours = new List<CellIndex>(4);
            while (pending.Count > 0)
            {
                CellPriorityQueue.Entry currentEntry = pending.Dequeue();
                CellIndex current = currentEntry.Index;
                int currentCost = currentEntry.Priority;
                int currentLinear = grid.ToLinearIndex(current);
                if (currentCost != costs[currentLinear]) continue;

                reachable[currentLinear] = true;
                grid.GetNeighbors(current, neighbours);
                for (int i = 0; i < neighbours.Count; i++)
                {
                    CellIndex neighbour = neighbours[i];
                    FlowCell neighbourCell = grid.GetCell(neighbour);
                    if (!neighbourCell.IsWalkable) continue;

                    int nextCost = currentCost + Math.Max(1, (int)neighbourCell.Cost);
                    int neighbourLinear = grid.ToLinearIndex(neighbour);
                    if (nextCost >= costs[neighbourLinear]) continue;

                    costs[neighbourLinear] = nextCost;
                    pending.Enqueue(neighbour, nextCost);
                }
            }

            // Choose the cheapest reachable neighbour for every cell. Ties
            // use the same order as FlowGrid.GetNeighbors for deterministic
            // server/client simulations.
            for (int i = 0; i < grid.Count; i++)
            {
                CellIndex index = grid.FromLinearIndex(i);
                if (!reachable[i] || index == target) continue;

                grid.GetNeighbors(index, neighbours);
                int bestCost = int.MaxValue;
                Direction bestDirection = Direction.None;
                for (int n = 0; n < neighbours.Count; n++)
                {
                    CellIndex neighbour = neighbours[n];
                    int neighbourLinear = grid.ToLinearIndex(neighbour);
                    if (!reachable[neighbourLinear] || costs[neighbourLinear] >= bestCost) continue;
                    bestCost = costs[neighbourLinear];
                    bestDirection = new Direction(neighbour.X - index.X, neighbour.Y - index.Y);
                }

                directions[i] = bestDirection;
            }

            return new FlowField(grid, target, directions, reachable, costs);
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
