using System;

namespace VampireHunt.Navigation.Domain
{
    /// <summary>Integer coordinate of a flow-field cell.</summary>
    public readonly struct CellIndex : IEquatable<CellIndex>
    {
        public CellIndex(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(CellIndex other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is CellIndex other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() => $"({X},{Y})";

        public static bool operator ==(CellIndex left, CellIndex right) => left.Equals(right);
        public static bool operator !=(CellIndex left, CellIndex right) => !left.Equals(right);
    }
}
