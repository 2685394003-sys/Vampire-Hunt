using System;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Spawning port for the authoritative EnemySpawnDirector. Registration is
    /// completed before the returned EntityId is exposed to the caller.
    /// </summary>
    public class NetworkSpawnAdapter : IEnemySpawner
    {
        private readonly NetworkEnemyPool pool;
        private readonly NetworkEntityRegistry registry;

        public NetworkSpawnAdapter(NetworkEnemyPool pool, NetworkEntityRegistry registry)
        {
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public int ActiveCount => pool.ActiveCount;

        public virtual EntityId Spawn(SpawnRequest request)
        {
            if (request.Spec == null) throw new ArgumentException("Spawn request requires a spec.", nameof(request));
            INetworkPooledEntity entity = pool.Acquire(request.Spec);
            EntityId id = entity.EntityId;
            if (entity.NetworkObjectId != 0UL)
            {
                if (!registry.Register(id, entity.NetworkObjectId))
                {
                    pool.Release(id);
                    throw new InvalidOperationException("Network entity handle is already registered.");
                }
            }
            else if (!registry.RegisterEntity(id))
            {
                pool.Release(id);
                throw new InvalidOperationException("Logical entity id is already registered.");
            }
            return id;
        }

        public virtual bool Despawn(EntityId entityId)
        {
            if (!pool.Release(entityId)) return false;
            registry.Unregister(entityId);
            return true;
        }
    }

    /// <summary>Named adapter kept for the Enemy module class diagram.</summary>
    public sealed class NetworkEnemySpawner : IEnemySpawner
    {
        private readonly NetworkSpawnAdapter adapter;

        public NetworkEnemySpawner(NetworkSpawnAdapter adapter)
        {
            this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public int ActiveCount => adapter.ActiveCount;
        public EntityId Spawn(SpawnRequest request) => adapter.Spawn(request);
        public bool Despawn(EntityId id) => adapter.Despawn(id);
    }
}
