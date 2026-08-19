using System;
using System.Collections.Generic;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Narrow registration port used by Unity/network adapters to project their
    /// local entity registry into the combat query contract.
    /// </summary>
    public interface ICombatTargetIndex
    {
        int Count { get; }
        void Register(ICombatTarget target);
        bool Unregister(EntityId id);
        bool TryGet(EntityId id, out ICombatTarget target);
        int Collect(in TargetQuery query, IList<ICombatTarget> buffer);
        void Clear();
    }

    /// <summary>
    /// Deterministic in-memory spatial index. It deliberately uses the domain
    /// position value instead of Unity Physics so the adapter is server-safe
    /// and testable in EditMode.
    /// </summary>
    public sealed class InMemoryCombatTargetIndex : ICombatTargetIndex
    {
        private readonly Dictionary<EntityId, ICombatTarget> targets = new();

        public int Count => targets.Count;

        public void Register(ICombatTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!target.Id.IsValid) throw new ArgumentException("A valid target id is required.", nameof(target));
            targets[target.Id] = target;
        }

        public bool Unregister(EntityId id) => id.IsValid && targets.Remove(id);

        public bool TryGet(EntityId id, out ICombatTarget target)
        {
            if (!id.IsValid)
            {
                target = null;
                return false;
            }
            return targets.TryGetValue(id, out target);
        }

        public int Collect(in TargetQuery query, IList<ICombatTarget> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Clear();
            float radiusSquared = query.Radius * query.Radius;
            List<TargetDistance> matches = new();

            foreach (KeyValuePair<EntityId, ICombatTarget> pair in targets)
            {
                ICombatTarget target = pair.Value;
                if (target == null || pair.Key == query.ExcludedId) continue;
                if (query.RequireAlive && !target.IsAlive) continue;

                float distanceSquared = query.Origin.DistanceSquaredTo(target.Position);
                if (distanceSquared <= radiusSquared)
                    matches.Add(new TargetDistance(target, distanceSquared));
            }

            matches.Sort(TargetDistanceComparer.Instance);
            for (int i = 0; i < matches.Count; i++) buffer.Add(matches[i].Target);
            return matches.Count;
        }

        public void Clear() => targets.Clear();

        private readonly struct TargetDistance
        {
            public TargetDistance(ICombatTarget target, float distanceSquared)
            {
                Target = target;
                DistanceSquared = distanceSquared;
            }

            public ICombatTarget Target { get; }
            public float DistanceSquared { get; }
        }

        private sealed class TargetDistanceComparer : IComparer<TargetDistance>
        {
            public static readonly TargetDistanceComparer Instance = new();

            public int Compare(TargetDistance left, TargetDistance right)
            {
                int distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
                return distance != 0 ? distance : left.Target.Id.Value.CompareTo(right.Target.Id.Value);
            }
        }
    }

    /// <summary>Combat query port backed by an injectable entity index.</summary>
    public sealed class CombatTargetQueryAdapter : ICombatTargetQuery
    {
        private readonly ICombatTargetIndex index;

        public CombatTargetQueryAdapter()
            : this(new InMemoryCombatTargetIndex())
        {
        }

        public CombatTargetQueryAdapter(ICombatTargetIndex index)
        {
            this.index = index ?? throw new ArgumentNullException(nameof(index));
        }

        public ICombatTargetIndex Index => index;

        public void Register(ICombatTarget target) => index.Register(target);
        public bool Unregister(EntityId id) => index.Unregister(id);
        public bool TryGet(EntityId id, out ICombatTarget target) => index.TryGet(id, out target);
        public void Clear() => index.Clear();

        public ICombatTarget FindClosest(in TargetQuery query)
        {
            List<ICombatTarget> matches = new();
            index.Collect(in query, matches);
            return matches.Count == 0 ? null : matches[0];
        }

        public int CollectInArea(in TargetQuery query, IList<ICombatTarget> buffer) =>
            index.Collect(in query, buffer);
    }
}
