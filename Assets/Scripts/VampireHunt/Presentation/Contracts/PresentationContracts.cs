using System;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Presentation.Contracts
{
    /// <summary>
    /// Client-side stream for transient authoritative events. It is separate
    /// from snapshot sinks: events trigger presentation, snapshots remain the
    /// source of truth for persistent state.
    /// </summary>
    public interface IGameplayEventStream
    {
        IDisposable Subscribe(Action<IGameplayEvent> handler);
        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IGameplayEvent;
    }

    public interface IGameplayEventDispatchErrorSink
    {
        void Report(IGameplayEvent @event, Exception exception);
    }

    public enum PresentationCueKind
    {
        Generic = 0,
        Damage = 1,
        Healing = 2,
        Death = 3,
        GameplayCue = 4,
        BossPhase = 5,
        BossTelegraph = 6,
        BossAttack = 7,
        Player = 8,
        Enemy = 9,
        Boss = 10
    }

    public readonly struct PresentationCue
    {
        public PresentationCue(
            EntityId entityId,
            WorldPosition position,
            string cue,
            PresentationCueKind kind,
            ulong eventId,
            double occurredAt,
            EntityId sourceId = default(EntityId))
        {
            EntityId = entityId;
            Position = position;
            Cue = cue ?? string.Empty;
            Kind = kind;
            EventId = eventId;
            OccurredAt = occurredAt;
            SourceId = sourceId;
        }

        public EntityId EntityId { get; }
        public EntityId SourceId { get; }
        public WorldPosition Position { get; }
        public string Cue { get; }
        public PresentationCueKind Kind { get; }
        public ulong EventId { get; }
        public double OccurredAt { get; }
    }

    public interface IAnimationDriver
    {
        void Play(PresentationCue cue);
    }

    public interface IVfxDriver
    {
        void Play(PresentationCue cue);
        void Remove(EntityId entityId, string cue);
    }

    public interface IAudioDriver
    {
        void Play(PresentationCue cue);
    }

    public interface ICameraDriver
    {
        void Play(PresentationCue cue);
    }

    public readonly struct HurtFlashCommand
    {
        public HurtFlashCommand(EntityId targetId, int appliedDamage, WorldPosition position, ulong eventId, double occurredAt)
        {
            TargetId = targetId;
            AppliedDamage = Math.Max(0, appliedDamage);
            Position = position;
            EventId = eventId;
            OccurredAt = occurredAt;
        }

        public EntityId TargetId { get; }
        public int AppliedDamage { get; }
        public WorldPosition Position { get; }
        public ulong EventId { get; }
        public double OccurredAt { get; }
    }

    public interface IHurtFlashDriver
    {
        void Flash(HurtFlashCommand command);
    }

    public readonly struct CombatTextCommand
    {
        public CombatTextCommand(
            EntityId sourceId,
            EntityId targetId,
            int amount,
            bool isCritical,
            bool isHealing,
            WorldPosition position,
            DamageFlags flags,
            ulong eventId,
            double occurredAt)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Amount = Math.Max(0, amount);
            IsCritical = isCritical;
            IsHealing = isHealing;
            Position = position;
            Flags = flags;
            EventId = eventId;
            OccurredAt = occurredAt;
        }

        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public int Amount { get; }
        public bool IsCritical { get; }
        public bool IsHealing { get; }
        public WorldPosition Position { get; }
        public DamageFlags Flags { get; }
        public ulong EventId { get; }
        public double OccurredAt { get; }
    }

    public interface ICombatTextHandle
    {
        bool IsActive { get; }
        void Show(CombatTextCommand command);
        void Reset();
    }

    public interface ICombatTextPool
    {
        int Capacity { get; }
        int ActiveCount { get; }
        bool TryRent(out ICombatTextHandle handle);
        void Return(ICombatTextHandle handle);
    }

    public readonly struct PresentationDirection
    {
        public PresentationDirection(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
        public bool IsNone => X == 0 && Y == 0;
    }

    public readonly struct FlowFieldDebugCommand
    {
        public FlowFieldDebugCommand(WorldPosition position, PresentationDirection direction, bool isWalkable)
        {
            Position = position;
            Direction = direction;
            IsWalkable = isWalkable;
        }

        public WorldPosition Position { get; }
        public PresentationDirection Direction { get; }
        public bool IsWalkable { get; }
    }

    public interface IFlowFieldDebugDriver
    {
        void Draw(FlowFieldDebugCommand command);
    }
}
