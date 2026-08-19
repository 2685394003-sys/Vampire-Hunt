using System;
using System.Collections.Generic;
using VampireHunt.Core;

namespace VampireHunt.Presentation.Runtime
{
    internal sealed class PresentationEventTracker
    {
        private readonly int capacity;
        private readonly HashSet<ulong> seen = new HashSet<ulong>();
        private readonly Queue<ulong> order = new Queue<ulong>();

        public PresentationEventTracker(int capacity = 512)
        {
            this.capacity = Math.Max(1, capacity);
        }

        public bool Accept(IGameplayEvent @event)
        {
            if (@event == null || @event.EventId == 0UL) return true;
            if (!seen.Add(@event.EventId)) return false;
            order.Enqueue(@event.EventId);
            while (order.Count > capacity)
            {
                seen.Remove(order.Dequeue());
            }

            return true;
        }
    }

    internal sealed class SubscriptionGroup : IDisposable
    {
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();
        private bool disposed;

        public void Add(IDisposable subscription)
        {
            if (subscription == null) return;
            if (disposed)
            {
                subscription.Dispose();
                return;
            }

            subscriptions.Add(subscription);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = subscriptions.Count - 1; i >= 0; i--)
            {
                try { subscriptions[i].Dispose(); }
                catch { }
            }

            subscriptions.Clear();
        }
    }
}
