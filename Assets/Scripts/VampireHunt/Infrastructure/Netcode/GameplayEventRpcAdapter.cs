using Unity.Netcode;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Infrastructure.Netcode.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>Reliable server event transport with explicit fail-closed decode.</summary>
    public sealed class GameplayEventRpcAdapter : NetworkBehaviour, IGameplayEventTransport
    {
        private GameplayEventReplicator replicator;
        private IGameplayEventIngress ingress;

        public bool IsConfigured => replicator != null && ingress != null;

        public void Configure(GameplayEventReplicator eventReplicator, IGameplayEventIngress eventIngress)
        {
            replicator = eventReplicator ?? throw new System.ArgumentNullException(nameof(eventReplicator));
            ingress = eventIngress ?? throw new System.ArgumentNullException(nameof(eventIngress));
        }

        public bool Broadcast(GameplayEventEnvelope envelope)
        {
            if (!IsServer || !IsConfigured || !envelope.IsVersionSupported || !envelope.Wire.IsFeatureEvent) return false;
            PublishEventRpc(envelope.Wire);
            return true;
        }

        public void Send(GameplayEventEnvelope envelope)
        {
            Broadcast(envelope);
        }

        private void LateUpdate()
        {
            if (IsServer && replicator != null)
                replicator.Flush(this);
        }

        [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Reliable)]
        private void PublishEventRpc(GameplayEventWire wire)
        {
            if (!wire.IsFeatureEvent || replicator == null || ingress == null) return;
            if (!wire.TryToDomain(out IGameplayEvent @event)) return;
            replicator.Receive(new GameplayEventEnvelope(wire), ingress);
        }
    }
}
