using System;
using System.Collections.Generic;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Infrastructure.Netcode.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Owns the lifetime mapping between the logical EntityId and an NGO
    /// NetworkObjectId. The two identifiers are never conflated and an EntityId
    /// is retired after despawn, even when the view is pooled.
    /// </summary>
    public sealed class NetworkEntityRegistry : IEntityLifecycleEventSink
    {
        private readonly object gate = new object();
        private readonly Dictionary<EntityId, ulong> entityToNetwork = new();
        private readonly Dictionary<ulong, EntityId> networkToEntity = new();
        private readonly HashSet<EntityId> pendingEntities = new();
        private readonly HashSet<EntityId> retiredEntities = new();

        public int ActiveCount
        {
            get { lock (gate) return entityToNetwork.Count + pendingEntities.Count; }
        }

        public int MappedCount
        {
            get { lock (gate) return entityToNetwork.Count; }
        }

        public bool Register(EntityId entityId, NetworkEntityHandle handle) =>
            Register(entityId, handle.NetworkObjectId);

        public bool Register(EntityId entityId, ulong networkObjectId)
        {
            if (!entityId.IsValid || networkObjectId == 0UL) return false;
            lock (gate)
            {
                if (retiredEntities.Contains(entityId)) return false;
                if (entityToNetwork.TryGetValue(entityId, out ulong existing))
                    return existing == networkObjectId;
                if (networkToEntity.TryGetValue(networkObjectId, out EntityId mapped))
                    return mapped == entityId;

                pendingEntities.Remove(entityId);
                entityToNetwork.Add(entityId, networkObjectId);
                networkToEntity.Add(networkObjectId, entityId);
                return true;
            }
        }

        /// <summary>Registers an offline/local entity without inventing an NGO handle.</summary>
        public bool RegisterEntity(EntityId entityId)
        {
            if (!entityId.IsValid) return false;
            lock (gate)
            {
                if (retiredEntities.Contains(entityId) || entityToNetwork.ContainsKey(entityId)) return false;
                pendingEntities.Add(entityId);
                return true;
            }
        }

        public bool TryGetNetworkObjectId(EntityId entityId, out ulong networkObjectId)
        {
            lock (gate) return entityToNetwork.TryGetValue(entityId, out networkObjectId);
        }

        public bool TryGetEntityId(ulong networkObjectId, out EntityId entityId)
        {
            if (networkObjectId == 0UL)
            {
                entityId = default;
                return false;
            }
            lock (gate) return networkToEntity.TryGetValue(networkObjectId, out entityId);
        }

        public bool IsActive(EntityId entityId)
        {
            lock (gate) return entityToNetwork.ContainsKey(entityId) || pendingEntities.Contains(entityId);
        }

        public bool IsRetired(EntityId entityId)
        {
            lock (gate) return retiredEntities.Contains(entityId);
        }

        public bool Unregister(EntityId entityId, out ulong networkObjectId)
        {
            lock (gate)
            {
                bool found = entityToNetwork.TryGetValue(entityId, out networkObjectId);
                if (found)
                {
                    entityToNetwork.Remove(entityId);
                    networkToEntity.Remove(networkObjectId);
                }

                bool pending = pendingEntities.Remove(entityId);
                if (found || pending) retiredEntities.Add(entityId);
                return found || pending;
            }
        }

        public bool Unregister(EntityId entityId)
        {
            return Unregister(entityId, out _);
        }

        public void EntitySpawned(EntityId id)
        {
            RegisterEntity(id);
        }

        public void EntityDespawned(EntityId id)
        {
            Unregister(id);
        }

        public void Clear()
        {
            lock (gate)
            {
                foreach (EntityId id in entityToNetwork.Keys) retiredEntities.Add(id);
                foreach (EntityId id in pendingEntities) retiredEntities.Add(id);
                entityToNetwork.Clear();
                networkToEntity.Clear();
                pendingEntities.Clear();
            }
        }
    }
}
