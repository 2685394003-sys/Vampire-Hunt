using System;

namespace VampireHunt.Navigation.Domain
{
    /// <summary>
    /// A grid direction. It deliberately uses integer components so a flow
    /// field is deterministic on every server and test runner.
    /// </summary>
    public readonly struct Direction : IEquatable<Direction>
    {
        public Direction(int x, int y)
        {
            X = Math.Sign(x);
            Y = Math.Sign(y);
        }

        public int X { get; }
        public int Y { get; }

        public bool IsNone => X == 0 && Y == 0;

        public static Direction None => new Direction(0, 0);
        public static Direction Up => new Direction(0, 1);
        public static Direction Down => new Direction(0, -1);
        public static Direction Left => new Direction(-1, 0);
        public static Direction Right => new Direction(1, 0);

        public bool Equals(Direction other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is Direction other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() => $"({X},{Y})";

        public static bool operator ==(Direction left, Direction right) => left.Equals(right);
        public static bool operator !=(Direction left, Direction right) => !left.Equals(right);
    }
}
