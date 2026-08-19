using VampireHunt.Core;

namespace VampireHunt.Enemies.Domain
{
    public readonly struct EnemyCombatContext
    {
        public EnemyCombatContext(
            EntityId sourceId,
            EntityId targetId,
            WorldPosition hitPosition,
            float distance,
            bool targetIsAlive,
            double now,
            double lastAttackAt)
        {
            SourceId = sourceId;
            TargetId = targetId;
            HitPosition = hitPosition;
            Distance = distance;
            TargetIsAlive = targetIsAlive;
            Now = now;
            LastAttackAt = lastAttackAt;
        }

        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public WorldPosition HitPosition { get; }
        public float Distance { get; }
        public bool TargetIsAlive { get; }
        public double Now { get; }
        public double LastAttackAt { get; }
    }
}
