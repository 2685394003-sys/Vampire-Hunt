using System;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Infrastructure.Netcode.Contracts
{
    /// <summary>Sender-aware command endpoint implemented by the bootstrap.</summary>
    public interface IOwnedPlayerCommandEndpoint
    {
        CommandResult Route(ulong senderId, DashCommand command);
        CommandResult Route(ulong senderId, AttackCommand command);
        CommandResult Route(ulong senderId, SelectBloodPactCommand command);
    }

    /// <summary>
    /// Server-side ownership lookup used before a decoded RPC is handed to
    /// the command router.  Implementations bind a logical player id to the
    /// sender from the NGO receive context.
    /// </summary>
    public interface INetworkCommandOwnership
    {
        bool Owns(ulong senderId, EntityId playerId);
    }

    /// <summary>
    /// Small composition-root hook for a Player network shell.  Bootstrap
    /// supplies sender ownership without referencing the legacy global
    /// PlayerNetworkState type.
    /// </summary>
    public interface IPlayerPoseOwnershipBinding
    {
        void ConfigurePoseOwnership(INetworkCommandOwnership ownership);
    }

    public interface IEnemySimulationEndpoint
    {
        bool Tick(EntityId enemyId, float deltaTime);
    }

    public interface IBossSimulationEndpoint
    {
        void Tick(EntityId bossId, float deltaTime);
    }

    /// <summary>Transport abstraction; an NGO RPC wrapper can implement this.</summary>
    public interface INetworkCommandTransport
    {
        void Send(NetworkCommandEnvelope command);
    }

    public readonly struct NetworkEntityHandle : IEquatable<NetworkEntityHandle>
    {
        public NetworkEntityHandle(ulong networkObjectId)
        {
            if (networkObjectId == 0UL)
                throw new ArgumentOutOfRangeException(nameof(networkObjectId));
            NetworkObjectId = networkObjectId;
        }

        public ulong NetworkObjectId { get; }
        public bool Equals(NetworkEntityHandle other) => NetworkObjectId == other.NetworkObjectId;
        public override bool Equals(object obj) => obj is NetworkEntityHandle other && Equals(other);
        public override int GetHashCode() => NetworkObjectId.GetHashCode();
        public static bool operator ==(NetworkEntityHandle left, NetworkEntityHandle right) => left.Equals(right);
        public static bool operator !=(NetworkEntityHandle left, NetworkEntityHandle right) => !left.Equals(right);
    }

    public interface INetworkPooledEntity
    {
        EntityId EntityId { get; }
        ulong NetworkObjectId { get; }
        void ResetForSpawn(EntityId freshId, EnemySpawnSpec spec);
        void ResetForDespawn();
    }

    public interface INetworkPooledEntityFactory
    {
        INetworkPooledEntity Create(EnemySpawnSpec spec);
    }

    public interface INetworkSpawnTransport
    {
        void Despawn(INetworkPooledEntity entity);
    }

    public interface IGameplayEventTransport
    {
        void Send(GameplayEventEnvelope envelope);
    }

    /// <summary>
    /// Versioned event envelope.  The wire payload is a fixed NGO value; the
    /// domain event is reconstructed only at the ingress boundary.
    /// </summary>
    public readonly struct GameplayEventEnvelope
    {
        private readonly GameplayEventWire wire;

        public GameplayEventEnvelope(IGameplayEvent @event)
        {
            if (!GameplayEventWire.TryFrom(@event, out wire))
                throw new ArgumentNullException(nameof(@event), "The gameplay event cannot be encoded.");
        }

        public GameplayEventEnvelope(GameplayEventWire wire)
        {
            this.wire = wire;
        }

        public ulong EventId => wire.EventId;
        public double OccurredAt => wire.OccurredAtMilliseconds / 1000d;
        public ushort ProtocolVersion => wire.ProtocolVersion;
        public GameplayEventKind Kind => wire.Kind;
        public GameplayEventWire Wire => wire;
        public bool IsVersionSupported => wire.IsVersionSupported;

        public bool TryToDomain(out IGameplayEvent @event) => wire.TryToDomain(out @event);
    }

    /// <summary>
    /// Feature-independent sink used by the network event channel when a
    /// presentation or application ingress is not directly available.
    /// </summary>
    public interface IGameplayEventReceiver
    {
        void Receive(IGameplayEvent @event);
    }
}
