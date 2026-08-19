using VampireHunt.Core;

namespace VampireHunt.Spawning.Contracts
{
    public readonly struct SpawnCandidate
    {
        public SpawnCandidate(WorldPosition position, bool isValid = true)
            : this(position, position, isValid)
        {
        }

        public SpawnCandidate(WorldPosition position, WorldPosition target, bool isValid)
        {
            Position = position;
            Target = target;
            IsValid = isValid;
        }

        public WorldPosition Position { get; }
        public WorldPosition Target { get; }
        public bool IsValid { get; }

        public static SpawnCandidate Invalid => default(SpawnCandidate);
    }
}
