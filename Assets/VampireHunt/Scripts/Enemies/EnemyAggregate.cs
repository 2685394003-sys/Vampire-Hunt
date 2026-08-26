using System;
using VampireHunt.Contracts;
using VampireHunt.SharedKernel;

namespace VampireHunt.Enemies
{
    public enum EnemyMovementIntent : byte
    {
        None = 0,
        Approach = 1,
        Orbit = 2,
        Retreat = 3
    }

    public readonly struct EnemyTickInput
    {
        public bool HasValidTarget { get; }
        public bool HasLineOfSight { get; }
        public float TargetDistance { get; }
        public double Time { get; }

        public EnemyTickInput(
            bool hasValidTarget,
            float targetDistance,
            double time,
            bool hasLineOfSight = true)
        {
            HasValidTarget = hasValidTarget;
            HasLineOfSight = hasLineOfSight;
            TargetDistance = Math.Max(0f, targetDistance);
            Time = time;
        }
    }

    public readonly struct EnemyTickResult
    {
        public bool StateChanged { get; }
        public EnemyMovementIntent MovementIntent { get; }
        public bool ShouldMove => MovementIntent != EnemyMovementIntent.None;
        public bool ShouldFaceTarget { get; }
        public bool ShouldCommitAttack { get; }

        public EnemyTickResult(
            bool stateChanged,
            EnemyMovementIntent movementIntent,
            bool shouldFaceTarget,
            bool shouldCommitAttack)
        {
            StateChanged = stateChanged;
            MovementIntent = movementIntent;
            ShouldFaceTarget = shouldFaceTarget;
            ShouldCommitAttack = shouldCommitAttack;
        }
    }

    public readonly struct EnemySnapshot
    {
        public EntityId EntityId { get; }
        public EnemyState State { get; }
        public float CurrentHealth { get; }
        public float MaxHealth { get; }
        public double StateEndTime { get; }
        public uint AttackSequence { get; }
        public uint Revision { get; }

        public EnemySnapshot(
            EntityId entityId,
            EnemyState state,
            float currentHealth,
            float maxHealth,
            double stateEndTime,
            uint attackSequence,
            uint revision)
        {
            EntityId = entityId;
            State = state;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            StateEndTime = stateEndTime;
            AttackSequence = attackSequence;
            Revision = revision;
        }
    }

    /// <summary>Authoritative runtime state for one enemy.</summary>
    public sealed class EnemyAggregate
    {
        private bool m_AttackCommitted;

        public EntityId Id { get; }
        public EnemyArchetypeDefinition Definition { get; }
        public EnemyRuntimeStats RuntimeStats { get; }
        public EntityId TargetId { get; private set; }
        public EnemyState State { get; private set; }
        public float CurrentHealth { get; private set; }
        public double StateEndTime { get; private set; }
        public uint AttackSequence { get; private set; }
        public uint Revision { get; private set; }
        public bool IsDead => State == EnemyState.Dead;

        public EnemyAggregate(
            EntityId id,
            EnemyArchetypeDefinition definition,
            EnemyRuntimeStats runtimeStats,
            double time)
        {
            if (id.IsNone) throw new ArgumentException("Enemy ID cannot be None.", nameof(id));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            RuntimeStats = runtimeStats ?? throw new ArgumentNullException(nameof(runtimeStats));

            Id = id;
            TargetId = EntityId.None;
            CurrentHealth = runtimeStats.MaxHealth;
            State = EnemyState.Spawning;
            StateEndTime = time + runtimeStats.SpawnDuration;
            Revision = 1;
        }

        public void SetTarget(EntityId targetId, double time)
        {
            if (IsDead || targetId.IsNone) return;
            TargetId = targetId;
            if (State == EnemyState.Seeking)
            {
                TransitionTo(EnemyState.Approaching, time, 0d);
            }
        }

        public void ClearTarget(double time)
        {
            TargetId = EntityId.None;
            if (!IsDead) TransitionTo(EnemyState.Seeking, time, 0d);
        }

        public EnemyTickResult Tick(in EnemyTickInput input)
        {
            if (IsDead) return default;

            EnemyState previous = State;
            bool shouldCommit = false;
            bool usesRangedTactics = Definition.CombatStyle == EnemyCombatStyle.RangedOrbit;

            if (State == EnemyState.Spawning)
            {
                if (input.Time >= StateEndTime)
                    TransitionTo(EnemyState.Seeking, input.Time, 0d);
            }
            else if (!input.HasValidTarget)
            {
                ClearTarget(input.Time);
            }
            else
            {
                switch (State)
                {
                    case EnemyState.Seeking:
                        TransitionTo(EnemyState.Approaching, input.Time, 0d);
                        break;
                    case EnemyState.Approaching:
                        if (usesRangedTactics && input.HasLineOfSight &&
                            input.TargetDistance <= Definition.PreferredRangeMax)
                        {
                            TransitionTo(EnemyState.Orbiting, input.Time, 0d);
                        }
                        else if (!usesRangedTactics && input.TargetDistance <= RuntimeStats.AttackRange)
                        {
                            TransitionTo(EnemyState.Telegraphing, input.Time, RuntimeStats.TelegraphDuration);
                        }
                        break;
                    case EnemyState.Orbiting:
                        if (!usesRangedTactics)
                        {
                            TransitionTo(EnemyState.Approaching, input.Time, 0d);
                        }
                        else if (!input.HasLineOfSight || input.TargetDistance > RuntimeStats.AttackBreakRange)
                        {
                            TransitionTo(EnemyState.Approaching, input.Time, 0d);
                        }
                        else if (input.TargetDistance >= Definition.PreferredRangeMin &&
                                 input.TargetDistance <= RuntimeStats.AttackRange)
                        {
                            TransitionTo(EnemyState.Telegraphing, input.Time, RuntimeStats.TelegraphDuration);
                        }
                        break;
                    case EnemyState.Telegraphing:
                        if (input.TargetDistance > RuntimeStats.AttackBreakRange ||
                            (usesRangedTactics && !input.HasLineOfSight))
                        {
                            TransitionTo(EnemyState.Approaching, input.Time, 0d);
                        }
                        else if (input.Time >= StateEndTime)
                        {
                            AttackSequence++;
                            m_AttackCommitted = false;
                            TransitionTo(EnemyState.Attacking, input.Time, RuntimeStats.ActiveDuration);
                            shouldCommit = true;
                        }
                        break;
                    case EnemyState.Attacking:
                        shouldCommit = !m_AttackCommitted;
                        if (input.Time >= StateEndTime)
                            TransitionTo(EnemyState.Recovering, input.Time, RuntimeStats.RecoveryDuration);
                        break;
                    case EnemyState.Recovering:
                        if (input.Time >= StateEndTime)
                            TransitionTo(
                                usesRangedTactics ? EnemyState.Orbiting : EnemyState.Approaching,
                                input.Time,
                                0d);
                        break;
                    case EnemyState.Stunned:
                        if (input.Time >= StateEndTime)
                            TransitionTo(EnemyState.Seeking, input.Time, 0d);
                        break;
                }
            }

            EnemyMovementIntent movementIntent = ResolveMovementIntent(input, usesRangedTactics);
            bool shouldFaceTarget = movementIntent != EnemyMovementIntent.None ||
                                    State == EnemyState.Telegraphing ||
                                    State == EnemyState.Attacking ||
                                    (usesRangedTactics && State == EnemyState.Recovering);
            return new EnemyTickResult(State != previous, movementIntent, shouldFaceTarget, shouldCommit);
        }

        public bool TryCommitAttack()
        {
            if (State != EnemyState.Attacking || m_AttackCommitted) return false;
            m_AttackCommitted = true;
            return true;
        }

        public bool ApplyDamage(in ResolvedDamage damage)
        {
            if (IsDead || damage.IsCancelled || damage.Amount <= 0f) return false;

            CurrentHealth = Math.Max(0f, CurrentHealth - damage.Amount);
            Revision++;
            if (CurrentHealth <= 0f)
            {
                TargetId = EntityId.None;
                TransitionTo(EnemyState.Dead, 0d, 0d);
            }

            return true;
        }

        public void DelayCurrentState(double duration)
        {
            if (IsDead || duration <= 0d || StateEndTime <= 0d) return;
            StateEndTime += duration;
            Revision++;
        }

        public EnemySnapshot CaptureSnapshot()
        {
            return new EnemySnapshot(
                Id,
                State,
                CurrentHealth,
                RuntimeStats.MaxHealth,
                StateEndTime,
                AttackSequence,
                Revision);
        }

        private void TransitionTo(EnemyState next, double time, double duration)
        {
            if (State == next && next != EnemyState.Dead) return;
            State = next;
            StateEndTime = duration > 0d ? time + duration : time;
            Revision++;
        }

        private EnemyMovementIntent ResolveMovementIntent(
            in EnemyTickInput input,
            bool usesRangedTactics)
        {
            if (!input.HasValidTarget) return EnemyMovementIntent.None;
            if (State == EnemyState.Approaching) return EnemyMovementIntent.Approach;
            if (!usesRangedTactics ||
                (State != EnemyState.Orbiting && State != EnemyState.Recovering))
                return EnemyMovementIntent.None;

            if (!input.HasLineOfSight || input.TargetDistance > Definition.PreferredRangeMax)
                return EnemyMovementIntent.Approach;
            if (input.TargetDistance < Definition.RetreatRange)
                return EnemyMovementIntent.Retreat;
            return EnemyMovementIntent.Orbit;
        }
    }
}
