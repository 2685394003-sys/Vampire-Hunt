using System;
using VampireHunt.Abilities.Domain;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Navigation.Contracts;

namespace VampireHunt.Enemies.Domain
{
    /// <summary>
    /// One logical enemy life. Reusing a pooled view requires ResetForSpawn
    /// with a newly allocated EntityId; the old id is never restored.
    /// </summary>
    // Aggregates are mutable domain state and must never be a cross-module
    // contract. Consumers outside this assembly receive EnemySnapshot and
    // command/result ports instead.
    internal sealed class EnemyAggregate
    {
        private bool _deathHandled;
        private EntityId _targetId;
        private WorldPosition _targetPosition;
        private WorldPosition _position;
        private double _lastAttackAt;
        private bool _hasSpawned;

        public EnemyAggregate(
            EntityId entityId,
            EnemySpec spec,
            INavigationField navigation,
            EntityIdAllocator entityIds)
            : this(entityId, spec, navigation, null, null, entityIds)
        {
        }

        public EnemyAggregate(
            EntityId entityId,
            EnemySpec spec,
            INavigationField navigation,
            ICombatTargetQuery targetQuery = null,
            Func<EntityId, GameplayAbilitySystem> abilityFactory = null,
            EntityIdAllocator entityIds = null)
        {
            if (!entityId.IsValid) throw new ArgumentException("An enemy aggregate requires a valid EntityId.", nameof(entityId));
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            BrainNavigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            BrainTargetQuery = targetQuery;
            AbilityFactory = abilityFactory;
            EntityIds = entityIds;
            Id = entityId;
            Vitals = new EnemyVitals();
            Abilities = abilityFactory == null ? null : abilityFactory(entityId);
            Brain = new EnemyBrain(navigation, spec.AttackRange, spec.MoveSpeed, targetQuery: targetQuery);
            CombatPolicy = new EnemyCombatPolicy(spec.AttackRange, spec.AttackCooldown, spec.AttackDamage, spec.AttackType);
            RewardPolicy = new EnemyRewardPolicy(spec.Reward);
            ResetForSpawn(entityId, spec);
        }

        public EntityId Id { get; private set; }
        public EnemyVitals Vitals { get; }
        public EnemyBrain Brain { get; private set; }
        public EnemyCombatPolicy CombatPolicy { get; private set; }
        public EnemyRewardPolicy RewardPolicy { get; }
        public GameplayAbilitySystem Abilities { get; private set; }
        public WorldPosition Position => _position;
        public EntityId TargetId => _targetId;
        public WorldPosition TargetPosition => _targetPosition;
        public double LastAttackAt => _lastAttackAt;
        public EnemyState State { get; private set; }
        public bool IsAlive => Vitals.IsAlive;
        public bool DeathHandled => _deathHandled;

        /// <summary>
        /// Convenience pool API. It can only be used when the composition
        /// root supplied the server's monotonic allocator; silently reusing
        /// the current id is intentionally impossible.
        /// </summary>
        public void ResetForSpawn(EnemySpec spec)
        {
            if (EntityIds == null)
                throw new InvalidOperationException("ResetForSpawn(spec) requires a server EntityIdAllocator.");
            ResetForSpawn(EntityIds.Allocate(), spec);
        }

        public void ResetForSpawn(EnemySpec spec, EntityId newEntityId) => ResetForSpawn(newEntityId, spec);

        public void ResetForSpawn(EntityId newEntityId, EnemySpec spec)
        {
            if (!newEntityId.IsValid) throw new ArgumentException("A pooled enemy requires a new valid EntityId.", nameof(newEntityId));
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (_hasSpawned && newEntityId == Id)
            {
                throw new InvalidOperationException("ResetForSpawn requires a fresh EntityId; pooled lifetimes never reuse ids.");
            }
            Id = newEntityId;
            Vitals.Reset(newEntityId, spec.MaxHealth);
            if (Abilities != null) Abilities.Clear();
            Abilities = AbilityFactory == null ? null : AbilityFactory(newEntityId);
            // Brain and combat policy are rebuilt from the immutable spec so
            // no cooldown or policy state leaks from the previous life.
            Brain.Reset();
            Brain = new EnemyBrain(BrainNavigation, spec.AttackRange, spec.MoveSpeed, targetQuery: BrainTargetQuery);
            CombatPolicy = new EnemyCombatPolicy(spec.AttackRange, spec.AttackCooldown, spec.AttackDamage, spec.AttackType);
            RewardPolicy.Reset(spec.Reward);
            _targetId = default(EntityId);
            _targetPosition = default(WorldPosition);
            _position = default(WorldPosition);
            _lastAttackAt = double.NegativeInfinity;
            _deathHandled = false;
            State = EnemyState.Idle;
            _hasSpawned = true;
        }

        public void SetPosition(WorldPosition position) => _position = position;

        public void SetTarget(EntityId targetId) => SetTarget(targetId, default(WorldPosition));

        public void SetTarget(EntityId targetId, WorldPosition targetPosition)
        {
            _targetId = targetId;
            _targetPosition = targetPosition;
        }

        public void SetState(EnemyState state)
        {
            State = state;
        }

        public void RecordAttack(double occurredAt)
        {
            _lastAttackAt = occurredAt;
            State = EnemyState.Attacking;
        }

        public DamageResult ApplyDamage(in ResolvedDamage damage)
        {
            DamageResult result = Vitals.ApplyDamage(damage);
            if (result.WasKilled) State = EnemyState.Dead;
            return result;
        }

        public bool TryConsumeDeath()
        {
            if (Vitals.IsAlive || _deathHandled) return false;
            _deathHandled = true;
            State = EnemyState.Dead;
            return true;
        }

        public EnemySnapshot CreateSnapshot() =>
            new EnemySnapshot(
                Id,
                Vitals.CurrentHealth,
                Vitals.MaxHealth,
                Vitals.IsAlive,
                State,
                Position,
                TargetId,
                TargetPosition,
                LastAttackAt);

        // Kept as an explicit port so ResetForSpawn never needs to discover a
        // navigation adapter from Unity. It is set by the constructor and
        // remains stable for all pooled lives.
        private INavigationField BrainNavigation { get; }
        private ICombatTargetQuery BrainTargetQuery { get; }
        private Func<EntityId, GameplayAbilitySystem> AbilityFactory { get; }
        private EntityIdAllocator EntityIds { get; }
    }
}
