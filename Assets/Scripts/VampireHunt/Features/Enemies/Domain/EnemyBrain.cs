using System;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Navigation.Domain;

namespace VampireHunt.Enemies.Domain
{
    /// <summary>
    /// Deterministic decision policy. It observes a perception snapshot and
    /// returns an intent; it never writes a Transform or queries a scene.
    /// </summary>
    public sealed class EnemyBrain
    {
        private readonly INavigationField _navigation;
        private readonly float _attackRange;
        private readonly float _moveSpeed;
        private readonly float _stoppingDistance;
        private readonly ICombatTargetQuery _targetQuery;
        private EnemyState _state;

        public EnemyBrain(
            INavigationField navigation,
            float attackRange,
            float moveSpeed,
            float stoppingDistance = 0.05f,
            ICombatTargetQuery targetQuery = null)
        {
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            if (attackRange < 0f) throw new ArgumentOutOfRangeException(nameof(attackRange));
            if (moveSpeed < 0f) throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            if (stoppingDistance < 0f) throw new ArgumentOutOfRangeException(nameof(stoppingDistance));
            _attackRange = attackRange;
            _moveSpeed = moveSpeed;
            _stoppingDistance = stoppingDistance;
            _targetQuery = targetQuery;
            _state = EnemyState.Idle;
        }

        public EnemyState State => _state;
        public float MoveSpeed => _moveSpeed;

        /// <summary>
        /// Builds a perception snapshot from the shared combat target query.
        /// The query is optional so a caller can provide a batched perception
        /// snapshot instead; neither path performs scene lookups.
        /// </summary>
        public EnemyPerception Perceive(EntityId selfId, WorldPosition selfPosition, float radius)
        {
            if (_targetQuery == null || !selfId.IsValid || radius < 0f)
            {
                return EnemyPerception.NoTarget(selfId, selfPosition);
            }

            ICombatTarget target = _targetQuery.FindClosest(
                new TargetQuery(selfPosition, radius, selfId, true));
            if (target == null || !target.IsAlive)
            {
                return EnemyPerception.NoTarget(selfId, selfPosition);
            }

            float distance = selfPosition.DistanceTo(target.Position);
            bool hasLineOfTravel = _navigation.IsWalkable(selfPosition) && _navigation.IsWalkable(target.Position);
            return new EnemyPerception(
                selfId,
                selfPosition,
                target.Id,
                target.Position,
                distance,
                hasLineOfTravel,
                true);
        }

        public EnemyIntent Decide(in EnemyPerception perception)
        {
            if (!perception.HasTarget)
            {
                _state = EnemyState.Idle;
                return EnemyIntent.Idle(perception.SelfId);
            }

            if (!_navigation.IsWalkable(perception.SelfPosition))
            {
                _state = EnemyState.Recovering;
                Direction recoveryDirection = SampleRecoveryDirection(perception);
                return new EnemyIntent(
                    perception.SelfId,
                    perception.TargetId,
                    recoveryDirection,
                    perception.Distance,
                    !recoveryDirection.IsNone,
                    false,
                    _state);
            }

            bool canAttack = perception.Distance <= _attackRange && perception.HasLineOfTravel;
            if (canAttack)
            {
                _state = EnemyState.Attacking;
                return new EnemyIntent(
                    perception.SelfId,
                    perception.TargetId,
                    Direction.None,
                    perception.Distance,
                    false,
                    true,
                    _state);
            }

            Direction direction = _navigation.SampleDirection(perception.SelfPosition, perception.TargetPosition);
            _state = direction.IsNone || perception.Distance <= _stoppingDistance
                ? EnemyState.Idle
                : EnemyState.Chasing;
            return new EnemyIntent(
                perception.SelfId,
                perception.TargetId,
                direction,
                perception.Distance,
                !direction.IsNone && _state == EnemyState.Chasing,
                false,
                _state);
        }

        public EnemyIntent Decide(EnemyPerception perception) => Decide(in perception);

        public void Reset()
        {
            _state = EnemyState.Idle;
        }

        private Direction SampleRecoveryDirection(in EnemyPerception perception)
        {
            var recovery = _navigation.TryFindRecovery(perception.SelfPosition);
            // TryFindRecovery returns the input position when no safe cell can
            // be found.  Never ask the combat flow field for a direction in
            // that case: doing so would silently resume chasing while the
            // entity is still outside the walkable domain (or inside an
            // obstacle).  The same check also covers an adapter that returns
            // an out-of-bounds/blocked recovery candidate.
            if (recovery == perception.SelfPosition || !_navigation.IsWalkable(recovery))
            {
                return Direction.None;
            }

            // Recovery is its own phase.  The first sample must move from the
            // entity's current position toward the nearest walkable cell.  A
            // later Decide call observes IsWalkable(current) and only then
            // samples the combat target in the normal Chasing branch.
            return _navigation.SampleDirection(perception.SelfPosition, recovery);
        }
    }
}
