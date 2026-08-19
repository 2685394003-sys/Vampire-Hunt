using System;
using System.Collections.Generic;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Infrastructure.Netcode.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Transient event channel. It deliberately has no reference to a state
    /// replicator: snapshots are truth and events are independently deduped
    /// triggers, so either channel may arrive first.
    /// </summary>
    public sealed class GameplayEventReplicator : IGameplayEventSink, IGameplayEventIngress
    {
        private readonly Queue<GameplayEventEnvelope> outgoing = new();
        private readonly HashSet<ulong> receivedIds = new();
        private readonly IGameplayEventIngress ingress;

        public GameplayEventReplicator(IGameplayEventIngress ingress = null)
        {
            this.ingress = ingress;
        }

        public int PendingCount => outgoing.Count;

        public void Publish(IGameplayEvent @event)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            outgoing.Enqueue(new GameplayEventEnvelope(@event));
        }

        public void Push(IGameplayEvent @event)
        {
            Receive(new GameplayEventEnvelope(@event));
        }

        public bool TryDequeue(out GameplayEventEnvelope envelope)
        {
            if (outgoing.Count == 0)
            {
                envelope = default;
                return false;
            }
            envelope = outgoing.Dequeue();
            return true;
        }

        public int Flush(IGameplayEventTransport transport)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            int sent = 0;
            while (TryDequeue(out GameplayEventEnvelope envelope))
            {
                transport.Send(envelope);
                sent++;
            }
            return sent;
        }

        public bool Receive(GameplayEventEnvelope envelope)
        {
            return Receive(envelope, ingress);
        }

        public bool Receive(GameplayEventEnvelope envelope, IGameplayEventIngress target)
        {
            if (target == null || envelope.Event == null) return false;
            // EventId zero is used by local/domain events that have not been
            // assigned a transport id; those must not all collapse into one.
            if (envelope.EventId != 0UL && !receivedIds.Add(envelope.EventId)) return false;
            target.Push(envelope.Event);
            return true;
        }

        public void Receive(IGameplayEvent @event, IGameplayEventReceiver target)
        {
            if (@event == null || target == null) return;
            if (@event.EventId != 0UL && !receivedIds.Add(@event.EventId)) return;
            target.Receive(@event);
        }

        public void Reset()
        {
            outgoing.Clear();
            receivedIds.Clear();
        }
    }
}
