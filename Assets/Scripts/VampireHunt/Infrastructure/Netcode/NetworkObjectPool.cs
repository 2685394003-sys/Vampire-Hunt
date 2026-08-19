using System;
using System.Collections.Generic;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Transport-neutral pool. Every acquire gets a fresh monotonic EntityId;
    /// pooled objects are reset before they can be observed by simulation.
    /// </summary>
    public sealed class NetworkObjectPool
    {
        private readonly INetworkPooledEntityFactory factory;
        private readonly EntityIdAllocator ids;
        private readonly Dictionary<string, Stack<INetworkPooledEntity>> idle = new(StringComparer.Ordinal);
        private readonly Dictionary<EntityId, INetworkPooledEntity> active = new();
        private readonly Dictionary<EntityId, string> activeKeys = new();
        private readonly int maxRetainedPerType;

        public NetworkObjectPool(
            INetworkPooledEntityFactory factory,
            EntityIdAllocator ids = null,
            int maxRetainedPerType = 64)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.ids = ids ?? new EntityIdAllocator();
            if (maxRetainedPerType < 0) throw new ArgumentOutOfRangeException(nameof(maxRetainedPerType));
            this.maxRetainedPerType = maxRetainedPerType;
        }

        public int ActiveCount => active.Count;
        public int RetainedCount
        {
            get
            {
                int count = 0;
                foreach (Stack<INetworkPooledEntity> stack in idle.Values) count += stack.Count;
                return count;
            }
        }

        public INetworkPooledEntity Acquire(EnemySpawnSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            string key = spec.EnemyTypeId ?? string.Empty;
            if (!idle.TryGetValue(key, out Stack<INetworkPooledEntity> stack))
            {
                stack = new Stack<INetworkPooledEntity>();
                idle.Add(key, stack);
            }

            INetworkPooledEntity entity = stack.Count > 0 ? stack.Pop() : factory.Create(spec);
            if (entity == null) throw new InvalidOperationException("Network pool factory returned null.");
            EntityId id = ids.Allocate();
            entity.ResetForSpawn(id, spec);
            active.Add(id, entity);
            activeKeys.Add(id, key);
            return entity;
        }

        public bool Release(EntityId id)
        {
            if (!active.TryGetValue(id, out INetworkPooledEntity entity)) return false;
            active.Remove(id);
            entity.ResetForDespawn();
            string key = activeKeys.TryGetValue(id, out string retainedKey)
                ? retainedKey
                : (entity is INetworkPooledType typed ? typed.EnemyTypeId ?? string.Empty : string.Empty);
            activeKeys.Remove(id);
            if (!idle.TryGetValue(key, out Stack<INetworkPooledEntity> stack))
            {
                stack = new Stack<INetworkPooledEntity>();
                idle.Add(key, stack);
            }
            if (stack.Count < maxRetainedPerType) stack.Push(entity);
            return true;
        }

        public bool TryGet(EntityId id, out INetworkPooledEntity entity) => active.TryGetValue(id, out entity);

        public void Prewarm(EnemySpawnSpec spec, int count)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            string key = spec.EnemyTypeId ?? string.Empty;
            if (!idle.TryGetValue(key, out Stack<INetworkPooledEntity> stack))
            {
                stack = new Stack<INetworkPooledEntity>();
                idle.Add(key, stack);
            }
            int desired = Math.Min(count, maxRetainedPerType);
            while (stack.Count < desired)
            {
                INetworkPooledEntity entity = factory.Create(spec);
                if (entity == null) throw new InvalidOperationException("Network pool factory returned null.");
                entity.ResetForDespawn();
                stack.Push(entity);
            }
        }

        public void Clear()
        {
            foreach (INetworkPooledEntity entity in active.Values) entity.ResetForDespawn();
            active.Clear();
            activeKeys.Clear();
            idle.Clear();
        }
    }

    /// <summary>Optional type hint used to retain objects in per-enemy pools.</summary>
    public interface INetworkPooledType
    {
        string EnemyTypeId { get; }
    }

    public sealed class NetworkEnemyPool
    {
        private readonly NetworkObjectPool pool;

        public NetworkEnemyPool(NetworkObjectPool pool)
        {
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        public int ActiveCount => pool.ActiveCount;
        public int RetainedCount => pool.RetainedCount;
        public INetworkPooledEntity Acquire(EnemySpawnSpec spec) => pool.Acquire(spec);
        public bool Release(EntityId id) => pool.Release(id);
        public bool TryGet(EntityId id, out INetworkPooledEntity entity) => pool.TryGet(id, out entity);
        public void Prewarm(EnemySpawnSpec spec, int count) => pool.Prewarm(spec, count);
    }
}
