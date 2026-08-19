using VampireHunt.Core;
using VampireHunt.Navigation.Domain;

namespace VampireHunt.Enemies.Contracts
{
    /// <summary>Decision output from EnemyBrain; it contains no movement side effects.</summary>
    public readonly struct EnemyIntent
    {
        public EnemyIntent(
            EntityId enemyId,
            EntityId targetId,
            Direction direction,
            float distance,
            bool shouldMove,
            bool shouldAttack,
            EnemyState state)
        {
            EnemyId = enemyId;
            TargetId = targetId;
            Direction = direction;
            Distance = distance;
            ShouldMove = shouldMove;
            ShouldAttack = shouldAttack;
            State = state;
        }

        public EntityId EnemyId { get; }
        public EntityId TargetId { get; }
        public Direction Direction { get; }
        public float Distance { get; }
        public bool HasTarget => TargetId.IsValid;
        public bool ShouldMove { get; }
        public bool ShouldAttack { get; }
        public EnemyState State { get; }

        public static EnemyIntent Idle(EntityId enemyId) =>
            new EnemyIntent(enemyId, default(EntityId), Direction.None, 0f, false, false, EnemyState.Idle);
    }
}
