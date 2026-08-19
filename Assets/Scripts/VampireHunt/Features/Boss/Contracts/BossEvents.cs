using System;
using VampireHunt.Core;

namespace VampireHunt.Boss.Contracts
{
    public enum BossAttackCuePhase
    {
        Telegraph = 0,
        Started = 1,
        Completed = 2,
        Cancelled = 3
    }

    public sealed class BossPhaseChangedEvent : GameplayEventBase
    {
        public EntityId BossId { get; }
        public BossPhase Previous { get; }
        public BossPhase Current { get; }

        public BossPhaseChangedEvent(
            ulong eventId,
            double occurredAt,
            EntityId bossId,
            BossPhase previous,
            BossPhase current)
            : base(eventId, occurredAt)
        {
            if (!bossId.IsValid) throw new ArgumentException("A valid Boss id is required.", nameof(bossId));
            BossId = bossId;
            Previous = previous;
            Current = current;
        }
    }

    public sealed class BossAttackTelegraphEvent : GameplayEventBase
    {
        public EntityId BossId { get; }
        public BossAttackId AttackId { get; }
        public WorldPosition Origin { get; }
        public WorldPosition Target { get; }
        public float Duration { get; }
        public PresentationCueId Cue { get; }

        public BossAttackTelegraphEvent(
            ulong eventId,
            double occurredAt,
            EntityId bossId,
            BossAttackId attackId,
            WorldPosition origin,
            WorldPosition target,
            float duration,
            PresentationCueId cue)
            : base(eventId, occurredAt)
        {
            if (!bossId.IsValid) throw new ArgumentException("A valid Boss id is required.", nameof(bossId));
            if (attackId == BossAttackId.None) throw new ArgumentOutOfRangeException(nameof(attackId));
            BossId = bossId;
            AttackId = attackId;
            Origin = origin;
            Target = target;
            Duration = Math.Max(0f, duration);
            Cue = cue;
        }
    }

    public sealed class BossAttackCueEvent : GameplayEventBase
    {
        public EntityId BossId { get; }
        public BossAttackId AttackId { get; }
        public BossAttackCuePhase Phase { get; }
        public PresentationCueId Cue { get; }

        public BossAttackCueEvent(
            ulong eventId,
            double occurredAt,
            EntityId bossId,
            BossAttackId attackId,
            BossAttackCuePhase phase,
            PresentationCueId cue)
            : base(eventId, occurredAt)
        {
            if (!bossId.IsValid) throw new ArgumentException("A valid Boss id is required.", nameof(bossId));
            if (attackId == BossAttackId.None) throw new ArgumentOutOfRangeException(nameof(attackId));
            BossId = bossId;
            AttackId = attackId;
            Phase = phase;
            Cue = cue;
        }
    }

    public sealed class BossDefeatedEvent : GameplayEventBase
    {
        public EntityId BossId { get; }
        public EntityId KillerId { get; }

        public BossDefeatedEvent(
            ulong eventId,
            double occurredAt,
            EntityId bossId,
            EntityId killerId)
            : base(eventId, occurredAt)
        {
            if (!bossId.IsValid) throw new ArgumentException("A valid Boss id is required.", nameof(bossId));
            BossId = bossId;
            KillerId = killerId;
        }
    }
}
