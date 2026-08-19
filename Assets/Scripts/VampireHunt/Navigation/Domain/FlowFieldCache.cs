using System;
using System.Collections.Generic;

namespace VampireHunt.Navigation.Domain
{
    /// <summary>Revision-aware cache for solved target fields.</summary>
    public sealed class FlowFieldCache
    {
        private readonly FlowGrid _grid;
        private readonly FlowFieldSolver _solver;
        private readonly int _capacity;
        private readonly Dictionary<CacheKey, FlowField> _fields = new Dictionary<CacheKey, FlowField>();
        private readonly Queue<CacheKey> _insertionOrder = new Queue<CacheKey>();

        public FlowFieldCache(FlowGrid grid, FlowFieldSolver solver = null, int capacity = 32)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _solver = solver ?? new FlowFieldSolver();
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int Count => _fields.Count;

        public FlowField GetOrBuild(CellIndex target, long obstacleRevision)
        {
            CacheKey key = new CacheKey(target, obstacleRevision);
            if (_fields.TryGetValue(key, out FlowField field)) return field;

            field = _solver.Solve(_grid, target);
            _fields[key] = field;
            _insertionOrder.Enqueue(key);
            TrimToCapacity();
            return field;
        }

        /// <summary>Removes cached fields from revisions other than revision.</summary>
        public void Invalidate(long revision)
        {
            List<CacheKey> stale = new List<CacheKey>();
            foreach (CacheKey key in _fields.Keys)
            {
                if (key.ObstacleRevision != revision) stale.Add(key);
            }

            for (int i = 0; i < stale.Count; i++) _fields.Remove(stale[i]);
            RebuildOrder();
        }

        public void Clear()
        {
            _fields.Clear();
            _insertionOrder.Clear();
        }

        private void TrimToCapacity()
        {
            while (_fields.Count > _capacity && _insertionOrder.Count > 0)
            {
                CacheKey oldest = _insertionOrder.Dequeue();
                _fields.Remove(oldest);
            }
        }

        private void RebuildOrder()
        {
            _insertionOrder.Clear();
            foreach (CacheKey key in _fields.Keys) _insertionOrder.Enqueue(key);
        }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public CacheKey(CellIndex target, long obstacleRevision)
            {
                Target = target;
                ObstacleRevision = obstacleRevision;
            }

            public CellIndex Target { get; }
            public long ObstacleRevision { get; }

            public bool Equals(CacheKey other) => Target == other.Target && ObstacleRevision == other.ObstacleRevision;
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Target, ObstacleRevision);
        }
    }
}
