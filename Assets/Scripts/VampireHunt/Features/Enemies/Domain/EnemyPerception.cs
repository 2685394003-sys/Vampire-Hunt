using VampireHunt.Core;

namespace VampireHunt.Enemies.Domain
{
    /// <summary>Read-only result of a world/target query for one simulation tick.</summary>
    public readonly struct EnemyPerception
    {
        public EnemyPerception(
            EntityId selfId,
            WorldPosition selfPosition,
            EntityId targetId,
            WorldPosition targetPosition,
            float distance,
            bool hasLineOfTravel,
            bool targetIsAlive)
        {
            SelfId = selfId;
            SelfPosition = selfPosition;
            TargetId = targetId;
            TargetPosition = targetPosition;
            Distance = distance < 0f ? 0f : distance;
            HasLineOfTravel = hasLineOfTravel;
            TargetIsAlive = targetIsAlive;
        }

        public EntityId SelfId { get; }
        public WorldPosition SelfPosition { get; }
        public EntityId TargetId { get; }
        public WorldPosition TargetPosition { get; }
        public float Distance { get; }
        public bool HasLineOfTravel { get; }
        public bool TargetIsAlive { get; }
        public bool HasTarget => TargetId.IsValid && TargetIsAlive;

        public static EnemyPerception NoTarget(EntityId selfId, WorldPosition selfPosition) =>
            new EnemyPerception(selfId, selfPosition, default(EntityId), default(WorldPosition), 0f, false, false);
    }
}
