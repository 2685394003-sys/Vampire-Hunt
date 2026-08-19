using System;
using System.Collections.Generic;

namespace VampireHunt.Navigation.Domain
{
    /// <summary>
    /// Compact immutable-topology grid used by the solver. The cell array is
    /// copied on construction so callers cannot mutate a solved field by
    /// retaining their input list.
    /// </summary>
    public sealed class FlowGrid
    {
        private readonly FlowCell[] _cells;

        public FlowGrid(int width, int height, IReadOnlyList<FlowCell> cells)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (cells.Count != width * height)
            {
                throw new ArgumentException("The cell count must equal width * height.", nameof(cells));
            }

            Width = width;
            Height = height;
            _cells = new FlowCell[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                CellIndex expected = FromLinearIndex(i);
                FlowCell cell = cells[i];
                _cells[i] = cell.Index == expected
                    ? cell
                    : new FlowCell(expected, cell.IsWalkable, cell.Cost);
            }
        }

        public FlowGrid(int width, int height, bool defaultWalkable = true, ushort defaultCost = 1)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            _cells = new FlowCell[width * height];
            for (int i = 0; i < _cells.Length; i++)
            {
                CellIndex index = FromLinearIndex(i);
                _cells[i] = new FlowCell(index, defaultWalkable, defaultCost);
            }
        }

        public int Width { get; }
        public int Height { get; }
        public int Count => _cells.Length;

        public bool IsInside(CellIndex index) =>
            index.X >= 0 && index.X < Width && index.Y >= 0 && index.Y < Height;

        public FlowCell GetCell(CellIndex index)
        {
            if (!IsInside(index)) throw new ArgumentOutOfRangeException(nameof(index));
            return _cells[ToLinearIndex(index)];
        }

        public bool TryGetCell(CellIndex index, out FlowCell cell)
        {
            if (!IsInside(index))
            {
                cell = default(FlowCell);
                return false;
            }

            cell = _cells[ToLinearIndex(index)];
            return true;
        }

        public CellIndex FromLinearIndex(int linearIndex)
        {
            if (linearIndex < 0 || linearIndex >= Width * Height)
            {
                throw new ArgumentOutOfRangeException(nameof(linearIndex));
            }

            return new CellIndex(linearIndex % Width, linearIndex / Width);
        }

        public int ToLinearIndex(CellIndex index)
        {
            if (!IsInside(index)) throw new ArgumentOutOfRangeException(nameof(index));
            return index.Y * Width + index.X;
        }

        /// <summary>
        /// Writes the four cardinal neighbours in deterministic order. The
        /// buffer is cleared first and is never retained by the grid.
        /// </summary>
        public void GetNeighbors(CellIndex index, IList<CellIndex> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (!IsInside(index)) throw new ArgumentOutOfRangeException(nameof(index));

            buffer.Clear();
            AddIfInside(new CellIndex(index.X, index.Y + 1), buffer);
            AddIfInside(new CellIndex(index.X + 1, index.Y), buffer);
            AddIfInside(new CellIndex(index.X, index.Y - 1), buffer);
            AddIfInside(new CellIndex(index.X - 1, index.Y), buffer);
        }

        public IReadOnlyList<CellIndex> GetNeighbors(CellIndex index)
        {
            List<CellIndex> result = new List<CellIndex>(4);
            GetNeighbors(index, result);
            return result;
        }

        /// <summary>
        /// Finds the nearest walkable cell with a bounded breadth-first search.
        /// This is the deterministic recovery path used when an entity is
        /// pushed onto an obstacle or outside the playable boundary.
        /// </summary>
        public bool TryFindNearestWalkable(CellIndex origin, int maxRadius, out CellIndex result)
        {
            if (maxRadius < 0) throw new ArgumentOutOfRangeException(nameof(maxRadius));
            if (!IsInside(origin))
            {
                origin = Clamp(origin);
            }

            FlowCell originCell = GetCell(origin);
            if (originCell.IsWalkable)
            {
                result = origin;
                return true;
            }

            Queue<CellIndex> queue = new Queue<CellIndex>();
            HashSet<CellIndex> visited = new HashSet<CellIndex>();
            Dictionary<CellIndex, int> distances = new Dictionary<CellIndex, int>();
            queue.Enqueue(origin);
            visited.Add(origin);
            distances[origin] = 0;

            while (queue.Count > 0)
            {
                CellIndex current = queue.Dequeue();
                int distance = distances[current];
                if (distance >= maxRadius) continue;

                List<CellIndex> neighbours = new List<CellIndex>(4);
                GetNeighbors(current, neighbours);
                for (int i = 0; i < neighbours.Count; i++)
                {
                    CellIndex neighbour = neighbours[i];
                    if (!visited.Add(neighbour)) continue;
                    int nextDistance = distance + 1;
                    distances[neighbour] = nextDistance;
                    FlowCell cell = GetCell(neighbour);
                    if (cell.IsWalkable)
                    {
                        result = neighbour;
                        return true;
                    }

                    queue.Enqueue(neighbour);
                }
            }

            result = default(CellIndex);
            return false;
        }

        public CellIndex Clamp(CellIndex index) =>
            new CellIndex(Math.Min(Math.Max(index.X, 0), Width - 1), Math.Min(Math.Max(index.Y, 0), Height - 1));

        private void AddIfInside(CellIndex index, IList<CellIndex> buffer)
        {
            if (IsInside(index)) buffer.Add(index);
        }
    }
}
