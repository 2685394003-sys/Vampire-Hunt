using System;
using System.Collections.Generic;
using System.Threading;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Presentation.Contracts;

namespace VampireHunt.Presentation.Runtime
{
    /// <summary>
    /// Ingress and fan-out for transient gameplay events. Subscribers are
    /// snapshotted before invocation so subscription changes during dispatch
    /// are safe; one faulty presenter cannot prevent the remaining presenters
    /// from receiving an authoritative event.
    /// </summary>
    public sealed class ClientGameplayEventDispatcher :
        IGameplayEventIngress,
        IGameplayEventStream,
        IDisposable
    {
        private readonly object gate = new object();
        private readonly List<Action<IGameplayEvent>> handlers = new List<Action<IGameplayEvent>>();
        private readonly IGameplayEventDispatchErrorSink errorSink;
        private bool disposed;
        private long dispatchCount;

        public ClientGameplayEventDispatcher(IGameplayEventDispatchErrorSink errorSink = null)
        {
            this.errorSink = errorSink;
        }

        public bool IsDisposed => Volatile.Read(ref disposed);
        public long DispatchCount => Interlocked.Read(ref dispatchCount);

        public IDisposable Subscribe(Action<IGameplayEvent> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (gate)
            {
                ThrowIfDisposed();
                handlers.Add(handler);
            }

            return new Subscription(this, handler);
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IGameplayEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return Subscribe(@event =>
            {
                if (@event is TEvent typed) handler(typed);
            });
        }

        public void Push(IGameplayEvent @event)
        {
            if (@event == null) return;
            Action<IGameplayEvent>[] snapshot;
            lock (gate)
            {
                if (disposed) return;
                snapshot = handlers.ToArray();
                dispatchCount++;
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i](@event);
                }
                catch (Exception exception)
                {
                    try
                    {
                        errorSink?.Report(@event, exception);
                    }
                    catch
                    {
                        // Error reporting is best-effort and must not break
                        // delivery to later presenters.
                    }
                }
            }
        }

        public void Publish(IGameplayEvent @event) => Push(@event);

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                handlers.Clear();
            }
        }

        private void Unsubscribe(Action<IGameplayEvent> handler)
        {
            lock (gate) handlers.Remove(handler);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ClientGameplayEventDispatcher));
        }

        private sealed class Subscription : IDisposable
        {
            private ClientGameplayEventDispatcher owner;
            private readonly Action<IGameplayEvent> handler;

            public Subscription(ClientGameplayEventDispatcher owner, Action<IGameplayEvent> handler)
            {
                this.owner = owner;
                this.handler = handler;
            }

            public void Dispose()
            {
                ClientGameplayEventDispatcher current = owner;
                if (current == null) return;
                owner = null;
                current.Unsubscribe(handler);
            }
        }
    }
}
