using System;
using VampireHunt.Abilities.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Abilities.Domain
{
    /// <summary>Mutable runtime state for one effect on one target.</summary>
    public sealed class ActiveGameplayEffect
    {
        private float timeUntilPeriod;

        public GameplayEffectSpec Spec { get; }
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public float RemainingTime { get; private set; }
        public int StackCount { get; private set; }
        public bool IsExpired => Spec.IsInstant ||
            (Spec.Duration == DurationPolicy.Duration && RemainingTime <= 0f);
        public GameplayEffectContext Context { get; private set; }

        public ActiveGameplayEffect(GameplayEffectSpec spec, EntityId sourceId)
            : this(spec, sourceId, EntityId.Invalid) { }

        public ActiveGameplayEffect(GameplayEffectSpec spec, EntityId sourceId, EntityId targetId)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            if (!sourceId.IsValid) throw new ArgumentException("A valid source id is required.", nameof(sourceId));
            SourceId = sourceId;
            TargetId = targetId;
            StackCount = 1;
            RemainingTime = InitialRemainingTime(spec);
            timeUntilPeriod = spec.PeriodSeconds;
        }

        public bool Tick(float deltaTime)
        {
            ValidateDelta(deltaTime);
            if (Spec.Duration == DurationPolicy.Duration && RemainingTime > 0f)
            {
                RemainingTime -= deltaTime;
                if (RemainingTime < 0f) RemainingTime = 0f;
            }
            return IsExpired;
        }

        public void Refresh()
        {
            RemainingTime = InitialRemainingTime(Spec);
            timeUntilPeriod = Spec.PeriodSeconds;
        }

        public bool AddStack()
        {
            if (StackCount >= Spec.MaxStacks) return false;
            StackCount++;
            return true;
        }

        internal void SetContext(GameplayEffectContext context) => Context = context;

        internal int ConsumePeriods(float deltaTime)
        {
            ValidateDelta(deltaTime);
            if (Spec.PeriodSeconds <= 0f || deltaTime <= 0f) return 0;

            timeUntilPeriod -= deltaTime;
            int executions = 0;
            const int maxCatchUp = 8;
            while (timeUntilPeriod <= 0f && executions < maxCatchUp)
            {
                executions++;
                timeUntilPeriod += Spec.PeriodSeconds;
            }

            // Drop an excessive backlog deterministically rather than executing
            // an unbounded number of effects in one server tick.
            if (timeUntilPeriod <= 0f)
                timeUntilPeriod = Spec.PeriodSeconds;
            return executions;
        }

        private static float InitialRemainingTime(GameplayEffectSpec spec)
        {
            switch (spec.Duration)
            {
                case DurationPolicy.Instant: return 0f;
                case DurationPolicy.Duration: return spec.DurationSeconds;
                case DurationPolicy.Infinite: return float.PositiveInfinity;
                default: throw new ArgumentOutOfRangeException(nameof(spec));
            }
        }

        private static void ValidateDelta(float deltaTime)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
        }
    }
}
