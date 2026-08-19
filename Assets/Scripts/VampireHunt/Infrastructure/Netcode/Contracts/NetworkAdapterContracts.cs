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

    public interface IMovementPoseEndpoint
    {
        MovementVerdict Validate(EntityId playerId, MovementPose pose);
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

    public enum NetworkCommandKind
    {
        Dash = 0,
        Attack = 1,
        SelectBloodPact = 2
    }

    /// <summary>
    /// A transport-neutral command envelope. The strongly typed command is
    /// retained so offline and server tests cannot silently lose fields.
    /// </summary>
    public readonly struct NetworkCommandEnvelope
    {
        private readonly object payload;

        private NetworkCommandEnvelope(
            ulong senderId,
            NetworkCommandKind kind,
            object payload,
            uint sequence)
        {
            SenderId = senderId;
            Kind = kind;
            this.payload = payload;
            Sequence = sequence;
        }

        public ulong SenderId { get; }
        public NetworkCommandKind Kind { get; }
        public uint Sequence { get; }
        public DashCommand Dash => (DashCommand)payload;
        public AttackCommand Attack => (AttackCommand)payload;
        public SelectBloodPactCommand SelectBloodPact => (SelectBloodPactCommand)payload;

        public static NetworkCommandEnvelope From(ulong senderId, DashCommand command) =>
            new NetworkCommandEnvelope(senderId, NetworkCommandKind.Dash, command, command.Sequence);

        public static NetworkCommandEnvelope From(ulong senderId, AttackCommand command) =>
            new NetworkCommandEnvelope(senderId, NetworkCommandKind.Attack, command, command.Sequence);

        public static NetworkCommandEnvelope From(ulong senderId, SelectBloodPactCommand command) =>
            new NetworkCommandEnvelope(senderId, NetworkCommandKind.SelectBloodPact, command, command.Sequence);
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

    public readonly struct GameplayEventEnvelope
    {
        public GameplayEventEnvelope(IGameplayEvent @event)
        {
            Event = @event ?? throw new ArgumentNullException(nameof(@event));
            EventId = @event.EventId;
            OccurredAt = @event.OccurredAt;
        }

        public ulong EventId { get; }
        public double OccurredAt { get; }
        public IGameplayEvent Event { get; }
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
