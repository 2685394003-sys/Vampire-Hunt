using System;
using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Bootstrap;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Netcode.Contracts;

namespace VampireHunt.Tests.Netcode
{
    public sealed class NetworkTopologyContractTests
    {
        [Test]
        public void HostAndOneClient_ReceiveConfirmedDamageExactlyOnce()
        {
            RuntimeGameplayEventHub serverHub = new();
            GameplayEventReplicator hostReplicator = new();
            GameplayEventReplicator remoteReplicator = new();
            RecordingIngress hostPresentation = new();
            RecordingIngress remotePresentation = new();
            using IDisposable outbound = serverHub.AddOutbound(hostReplicator);
            using IDisposable local = serverHub.AddLocalIngress(hostPresentation);
            MulticastEventTransport transport = new(
                (hostReplicator, hostPresentation),
                (remoteReplicator, remotePresentation));

            serverHub.Publish(CreateDamageEvent(101UL));
            Assert.That(hostReplicator.Flush(transport), Is.EqualTo(1));

            Assert.That(hostPresentation.Events, Has.Count.EqualTo(1));
            Assert.That(remotePresentation.Events, Has.Count.EqualTo(1));
            Assert.That(hostPresentation.Events[0].EventId, Is.EqualTo(101UL));
            Assert.That(remotePresentation.Events[0].EventId, Is.EqualTo(101UL));
        }

        [Test]
        public void DedicatedServerAndTwoClients_ReceiveTheSameConfirmedDamage()
        {
            RuntimeGameplayEventHub serverHub = new();
            GameplayEventReplicator serverReplicator = new();
            GameplayEventReplicator firstClient = new();
            GameplayEventReplicator secondClient = new();
            RecordingIngress firstPresentation = new();
            RecordingIngress secondPresentation = new();
            using IDisposable outbound = serverHub.AddOutbound(serverReplicator);
            MulticastEventTransport transport = new(
                (firstClient, firstPresentation),
                (secondClient, secondPresentation));

            serverHub.Publish(CreateDamageEvent(202UL));
            Assert.That(serverReplicator.Flush(transport), Is.EqualTo(1));

            Assert.That(firstPresentation.Events, Has.Count.EqualTo(1));
            Assert.That(secondPresentation.Events, Has.Count.EqualTo(1));
            Assert.That(firstPresentation.Events[0].EventId, Is.EqualTo(202UL));
            Assert.That(secondPresentation.Events[0].EventId, Is.EqualTo(202UL));
        }

        private static DamageConfirmedEvent CreateDamageEvent(ulong eventId)
        {
            EntityId source = new(1UL);
            EntityId target = new(2UL);
            return new DamageConfirmedEvent(
                eventId,
                1d,
                source,
                target,
                new DamageResult(7, 7, false, false, WorldPosition.Origin));
        }

        private sealed class RecordingIngress : IGameplayEventIngress
        {
            public readonly List<IGameplayEvent> Events = new();
            public void Push(IGameplayEvent @event) => Events.Add(@event);
        }

        private sealed class MulticastEventTransport : IGameplayEventTransport
        {
            private readonly (GameplayEventReplicator Replicator, IGameplayEventIngress Ingress)[] clients;

            public MulticastEventTransport(
                params (GameplayEventReplicator Replicator, IGameplayEventIngress Ingress)[] clients)
            {
                this.clients = clients ?? Array.Empty<(GameplayEventReplicator, IGameplayEventIngress)>();
            }

            public void Send(GameplayEventEnvelope envelope)
            {
                for (int i = 0; i < clients.Length; i++)
                    clients[i].Replicator.Receive(envelope, clients[i].Ingress);
            }
        }
    }
}
