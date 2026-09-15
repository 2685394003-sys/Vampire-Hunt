namespace VampireHunt.Contracts
{
    /// <summary>Engine-independent snapshot of an entity's current world-space aim.</summary>
    public readonly struct AimSnapshot
    {
        public Float3 WorldPoint { get; }
        public Float3 Direction { get; }

        public AimSnapshot(Float3 worldPoint, Float3 direction)
        {
            WorldPoint = worldPoint;
            Direction = direction;
        }
    }

    /// <summary>Supplies combat abilities with aim data without coupling them to a camera or input device.</summary>
    public interface ICombatAimSource
    {
        bool TryGetAim(out AimSnapshot snapshot);
    }
}
