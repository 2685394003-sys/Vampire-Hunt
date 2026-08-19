using System;
using System.Globalization;

namespace VampireHunt.Core
{
    /// <summary>
    /// The identity of one logical lifetime of an entity. A default value is
    /// deliberately invalid; the server allocator is the only normal creator.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        public EntityId(ulong value)
        {
            if (value == 0UL)
                throw new ArgumentOutOfRangeException(nameof(value), "EntityId zero is reserved for an invalid id.");

            Value = value;
        }

        public static EntityId Invalid => default;

        public static bool TryCreate(ulong value, out EntityId id)
        {
            if (value == 0UL)
            {
                id = default;
                return false;
            }

            id = new EntityId(value);
            return true;
        }

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
        public override string ToString() => IsValid
            ? Value.ToString(CultureInfo.InvariantCulture)
            : "Invalid";

        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
        public static bool operator <(EntityId left, EntityId right) => left.Value < right.Value;
        public static bool operator >(EntityId left, EntityId right) => left.Value > right.Value;
        public static bool operator <=(EntityId left, EntityId right) => left.Value <= right.Value;
        public static bool operator >=(EntityId left, EntityId right) => left.Value >= right.Value;
    }

    /// <summary>
    /// Server-side monotonic allocator. It has no recycle or reset operation:
    /// an id remains retired forever once it has been returned.
    /// </summary>
    public sealed class EntityIdAllocator
    {
        private readonly object gate = new object();
        private ulong nextValue;

        public EntityIdAllocator(ulong firstValue = 1UL)
        {
            if (firstValue == 0UL)
                throw new ArgumentOutOfRangeException(nameof(firstValue), "The first entity id must be valid.");

            nextValue = firstValue;
        }

        public EntityId Allocate()
        {
            lock (gate)
            {
                if (nextValue == 0UL)
                    throw new InvalidOperationException("EntityId allocation exhausted the ulong range.");

                EntityId result = new EntityId(nextValue);
                nextValue = nextValue == ulong.MaxValue ? 0UL : nextValue + 1UL;
                return result;
            }
        }

        public EntityId Next() => Allocate();
    }

    /// <summary>Engine-independent world-space position value.</summary>
    public readonly struct WorldPosition : IEquatable<WorldPosition>
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public WorldPosition(float x, float y, float z)
        {
            ValidateFinite(x, nameof(x));
            ValidateFinite(y, nameof(y));
            ValidateFinite(z, nameof(z));
            X = x;
            Y = y;
            Z = z;
        }

        public static WorldPosition Origin => default;
        public float LengthSquared => X * X + Y * Y + Z * Z;
        public float Length => (float)Math.Sqrt(LengthSquared);
        public bool IsFinite => IsFiniteNumber(X) && IsFiniteNumber(Y) && IsFiniteNumber(Z);

        public float DistanceSquaredTo(WorldPosition other) => (this - other).LengthSquared;
        public float DistanceTo(WorldPosition other) => (float)Math.Sqrt(DistanceSquaredTo(other));

        public WorldPosition Normalized
        {
            get
            {
                float length = Length;
                return length <= 0.000001f ? Origin : this / length;
            }
        }

        public static WorldPosition Lerp(WorldPosition from, WorldPosition to, float t)
        {
            ValidateFinite(t, nameof(t));
            return new WorldPosition(
                from.X + (to.X - from.X) * t,
                from.Y + (to.Y - from.Y) * t,
                from.Z + (to.Z - from.Z) * t);
        }

        public bool Equals(WorldPosition other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is WorldPosition other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);

        public static WorldPosition operator +(WorldPosition left, WorldPosition right) =>
            new WorldPosition(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static WorldPosition operator -(WorldPosition left, WorldPosition right) =>
            new WorldPosition(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static WorldPosition operator *(WorldPosition value, float scale) =>
            new WorldPosition(value.X * scale, value.Y * scale, value.Z * scale);
        public static WorldPosition operator *(float scale, WorldPosition value) => value * scale;
        public static WorldPosition operator /(WorldPosition value, float scale)
        {
            ValidateFinite(scale, nameof(scale));
            if (Math.Abs(scale) <= 0.000001f)
                throw new DivideByZeroException();
            return new WorldPosition(value.X / scale, value.Y / scale, value.Z / scale);
        }

        public static bool operator ==(WorldPosition left, WorldPosition right) => left.Equals(right);
        public static bool operator !=(WorldPosition left, WorldPosition right) => !left.Equals(right);

        private static bool IsFiniteNumber(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static void ValidateFinite(float value, string parameterName)
        {
            if (!IsFiniteNumber(value))
                throw new ArgumentOutOfRangeException(parameterName, "World positions must contain finite values.");
        }
    }

    public interface IGameClock
    {
        double Now { get; }
        float DeltaTime { get; }
    }

    public interface IRandomSource
    {
        float NextFloat();
        int NextInt(int minInclusive, int maxExclusive);
    }

    public interface IGameplayEvent
    {
        ulong EventId { get; }
        double OccurredAt { get; }
    }

    public interface IGameplayEventSink
    {
        void Publish(IGameplayEvent @event);
    }

    /// <summary>Base for immutable gameplay events emitted by a domain service.</summary>
    public abstract class GameplayEventBase : IGameplayEvent
    {
        public ulong EventId { get; }
        public double OccurredAt { get; }

        protected GameplayEventBase(ulong eventId, double occurredAt)
        {
            if (double.IsNaN(occurredAt) || double.IsInfinity(occurredAt))
                throw new ArgumentOutOfRangeException(nameof(occurredAt));

            EventId = eventId;
            OccurredAt = occurredAt;
        }
    }

    /// <summary>Independent event sequence used by an authoritative application service.</summary>
    public sealed class GameplayEventIdAllocator
    {
        private readonly object gate = new object();
        private ulong nextValue = 1UL;

        public ulong Next()
        {
            lock (gate)
            {
                if (nextValue == 0UL)
                    throw new InvalidOperationException("Gameplay event id allocation exhausted the ulong range.");

                ulong result = nextValue;
                nextValue = nextValue == ulong.MaxValue ? 0UL : nextValue + 1UL;
                return result;
            }
        }
    }
}
