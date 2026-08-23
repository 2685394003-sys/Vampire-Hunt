using System;

namespace VampireHunt.SharedKernel
{
    /// <summary>Stable runtime identity. Zero is reserved for no entity.</summary>
    [Serializable]
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public static readonly EntityId None = new EntityId(0);

        public ulong Value { get; }
        public bool IsNone => Value == 0;

        public EntityId(ulong value)
        {
            Value = value;
        }

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => IsNone ? "None" : Value.ToString();

        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }
}
