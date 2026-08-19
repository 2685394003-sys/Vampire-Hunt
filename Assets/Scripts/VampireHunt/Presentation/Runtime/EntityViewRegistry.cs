using System;
using System.Collections.Generic;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;

namespace VampireHunt.Presentation.Runtime
{
    /// <summary>
    /// Client-only EntityId-to-view map. It never exposes or stores a domain
    /// aggregate; stale despawn notifications are harmless when a pooled view
    /// has already been replaced.
    /// </summary>
    public sealed class EntityViewRegistry : IEntityLifecycleEventSink, IDisposable
    {
        private readonly Dictionary<EntityId, object> views = new Dictionary<EntityId, object>();
        private readonly HashSet<EntityId> knownEntities = new HashSet<EntityId>();
        private bool disposed;

        public event Action<EntityId, object> ViewRegistered;
        public event Action<EntityId, object> ViewUnregistered;

        public int Count => views.Count;

        public bool Register(EntityId entityId, object view)
        {
            if (disposed || !entityId.IsValid || view == null) return false;
            object previous = null;
            if (views.TryGetValue(entityId, out previous) && ReferenceEquals(previous, view))
            {
                knownEntities.Add(entityId);
                return true;
            }

            views[entityId] = view;
            knownEntities.Add(entityId);
            if (previous != null) ViewUnregistered?.Invoke(entityId, previous);
            ViewRegistered?.Invoke(entityId, view);
            return true;
        }

        public bool TryGet(EntityId entityId, out object view)
        {
            if (disposed)
            {
                view = null;
                return false;
            }

            return views.TryGetValue(entityId, out view);
        }

        public bool TryGet<TView>(EntityId entityId, out TView view) where TView : class
        {
            if (TryGet(entityId, out object candidate) && candidate is TView typed)
            {
                view = typed;
                return true;
            }

            view = null;
            return false;
        }

        public bool Unregister(EntityId entityId, object expectedView = null)
        {
            if (disposed || !views.TryGetValue(entityId, out object current)) return false;
            if (expectedView != null && !ReferenceEquals(expectedView, current)) return false;
            views.Remove(entityId);
            ViewUnregistered?.Invoke(entityId, current);
            return true;
        }

        public bool ContainsEntity(EntityId entityId) => !disposed && knownEntities.Contains(entityId);

        public void EntitySpawned(EntityId id)
        {
            if (!disposed && id.IsValid) knownEntities.Add(id);
        }

        public void EntityDespawned(EntityId id)
        {
            if (disposed) return;
            Unregister(id);
            knownEntities.Remove(id);
        }

        public void Clear()
        {
            if (disposed) return;
            EntityId[] ids = new EntityId[views.Count];
            views.Keys.CopyTo(ids, 0);
            for (int i = 0; i < ids.Length; i++) Unregister(ids[i]);
            knownEntities.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            Clear();
            disposed = true;
            ViewRegistered = null;
            ViewUnregistered = null;
        }
    }
}
