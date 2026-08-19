using System;
using VampireHunt.Core;

namespace VampireHunt.Player.Contracts
{
    /// <summary>Planar movement input sent by an owner for a server-checked Dash.</summary>
    public readonly struct MoveVector : IEquatable<MoveVector>
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public MoveVector(float x, float z)
            : this(x, 0f, z)
        {
        }

        public MoveVector(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float SqrMagnitude => X * X + Y * Y + Z * Z;
        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);
        public bool IsFinite =>
            !float.IsNaN(X) && !float.IsInfinity(X) &&
            !float.IsNaN(Y) && !float.IsInfinity(Y) &&
            !float.IsNaN(Z) && !float.IsInfinity(Z);
        public bool IsZero => SqrMagnitude <= 0.000001f;

        public MoveVector Normalized()
        {
            float magnitude = Magnitude;
            return magnitude <= 0.000001f
                ? default
                : new MoveVector(X / magnitude, Y / magnitude, Z / magnitude);
        }

        public MoveVector ClampMagnitude(float maximum)
        {
            if (maximum < 0f) maximum = 0f;
            float sqrMaximum = maximum * maximum;
            if (!IsFinite || SqrMagnitude <= sqrMaximum) return this;
            return Normalized() * maximum;
        }

        public static MoveVector operator +(MoveVector left, MoveVector right) =>
            new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        public static MoveVector operator -(MoveVector left, MoveVector right) =>
            new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static MoveVector operator *(MoveVector value, float scalar) =>
            new(value.X * scalar, value.Y * scalar, value.Z * scalar);

        public bool Equals(MoveVector other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj) => obj is MoveVector other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    public readonly struct DashCommand
    {
        public EntityId PlayerId { get; }
        public MoveVector Direction { get; }
        public uint Sequence { get; }

        public DashCommand(EntityId playerId, MoveVector direction, uint sequence)
        {
            PlayerId = playerId;
            Direction = direction;
            Sequence = sequence;
        }
    }

    public readonly struct AttackCommand
    {
        public EntityId PlayerId { get; }
        public WorldPosition AimAt { get; }
        public uint Sequence { get; }

        public AttackCommand(EntityId playerId, WorldPosition aimAt, uint sequence)
        {
            PlayerId = playerId;
            AimAt = aimAt;
            Sequence = sequence;
        }
    }

    /// <summary>Stable id of an authored blood pact. Empty ids are invalid.</summary>
    public readonly struct BloodPactId : IEquatable<BloodPactId>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public BloodPactId(string value)
        {
            Value = value?.Trim() ?? string.Empty;
        }

        public bool Equals(BloodPactId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is BloodPactId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value ?? string.Empty;
        public static implicit operator BloodPactId(string value) => new(value);
        public static implicit operator string(BloodPactId value) => value.Value;
        public static bool operator ==(BloodPactId left, BloodPactId right) => left.Equals(right);
        public static bool operator !=(BloodPactId left, BloodPactId right) => !left.Equals(right);
    }

    public readonly struct SelectBloodPactCommand
    {
        public EntityId PlayerId { get; }
        public BloodPactId Selection { get; }
        public uint OfferVersion { get; }
        public uint Sequence { get; }

        public SelectBloodPactCommand(
            EntityId playerId,
            BloodPactId selection,
            uint offerVersion,
            uint sequence = 0)
        {
            PlayerId = playerId;
            Selection = selection;
            OfferVersion = offerVersion;
            Sequence = sequence;
        }
    }

    public readonly struct BloodPactOption
    {
        public BloodPactId Id { get; }
        public int Cost { get; }
        public bool Repeatable { get; }
        public int MaximumStacks { get; }

        public BloodPactOption(
            BloodPactId id,
            int cost,
            bool repeatable = true,
            int maximumStacks = int.MaxValue)
        {
            Id = id;
            Cost = cost < 0 ? 0 : cost;
            Repeatable = repeatable;
            MaximumStacks = maximumStacks < 1 ? 1 : maximumStacks;
        }
    }

    public enum CommandResultStatus
    {
        Accepted = 0,
        Rejected = 1,
        Invalid = 2,
        Unauthorized = 3,
        NotFound = 4,
        Dead = 5,
        StaleSequence = 6,
        Cooldown = 7,
        InsufficientResource = 8,
        StaleOffer = 9
    }

    // Compatibility name used by command gateways that expose a *Code suffix.
    public enum CommandResultCode
    {
        Accepted = (int)CommandResultStatus.Accepted,
        Rejected = (int)CommandResultStatus.Rejected,
        Invalid = (int)CommandResultStatus.Invalid,
        Unauthorized = (int)CommandResultStatus.Unauthorized,
        NotFound = (int)CommandResultStatus.NotFound,
        Dead = (int)CommandResultStatus.Dead,
        StaleSequence = (int)CommandResultStatus.StaleSequence,
        Cooldown = (int)CommandResultStatus.Cooldown,
        InsufficientResource = (int)CommandResultStatus.InsufficientResource,
        StaleOffer = (int)CommandResultStatus.StaleOffer
    }

    /// <summary>Explicit outcome of a server command; callers never infer state from side effects.</summary>
    public readonly struct CommandResult : IEquatable<CommandResult>
    {
        public CommandResultStatus Status { get; }
        public CommandResultStatus Code => Status;
        public CommandResultCode ResultCode => (CommandResultCode)Status;
        public string Reason { get; }
        public uint Sequence { get; }
        public bool Accepted => Status == CommandResultStatus.Accepted;
        public bool Succeeded => Accepted;
        public bool Success => Accepted;

        public CommandResult(
            CommandResultStatus status,
            string reason = null,
            uint sequence = 0)
        {
            Status = status;
            Reason = reason ?? string.Empty;
            Sequence = sequence;
        }

        public static CommandResult Accept(uint sequence = 0) =>
            new(CommandResultStatus.Accepted, string.Empty, sequence);

        public static CommandResult Reject(
            CommandResultStatus status,
            string reason = null,
            uint sequence = 0) =>
            new(status == CommandResultStatus.Accepted ? CommandResultStatus.Rejected : status,
                reason,
                sequence);

        public bool Equals(CommandResult other) =>
            Status == other.Status && Sequence == other.Sequence &&
            string.Equals(Reason, other.Reason, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is CommandResult other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Status, Sequence, Reason);
    }

    public interface IPlayerCommandGateway
    {
        CommandResult SubmitDash(DashCommand command);
        CommandResult SubmitAttack(AttackCommand command);
        CommandResult SelectBloodPact(SelectBloodPactCommand command);
    }

    public interface IPlayerCommandHandler
    {
        CommandResult Handle(DashCommand command);
        CommandResult Handle(AttackCommand command);
        CommandResult Handle(SelectBloodPactCommand command);
    }

    public interface IPlayerReadModel
    {
        int Health { get; }
        int MaxHealth { get; }
        float Stamina { get; }
        float MaxStamina { get; }
        int Scarlet { get; }
        int Coins { get; }
        int Level { get; }
        int Experience { get; }
        bool IsAlive { get; }
    }

    /// <summary>
    /// Narrow read-only state used by the Blood Pact UI. The implementation
    /// can be a local application runtime or a replicated network projection.
    /// </summary>
    public interface IPlayerBloodPactReadModel
    {
        int BloodPactScarlet { get; }
        bool IsBloodPactPlayerAlive { get; }
        bool TryGetBloodPactOffer(out BloodPactOffer offer);
    }

    public interface IPlayerProgressionCommands
    {
        bool GrantReward(EntityId recipientId, RewardGrant reward);
        int GrantSharedScarlet(int amount);
    }
}
