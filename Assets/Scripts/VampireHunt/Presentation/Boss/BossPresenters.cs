using System;
using VampireHunt.Boss.Contracts;
using VampireHunt.Core;
using VampireHunt.Presentation.Contracts;
using VampireHunt.Presentation.Runtime;

namespace VampireHunt.Presentation.Boss
{
    public readonly struct BossAnimationState
    {
        public BossAnimationState(BossSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public BossSnapshot Snapshot { get; }
    }

    public interface IBossAnimationDriver
    {
        void Apply(BossAnimationState state);
    }

    public sealed class BossAnimatorPresenter
    {
        private readonly IBossAnimationDriver driver;

        public BossAnimatorPresenter(IBossAnimationDriver driver)
        {
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        public void Apply(IBossReadModel model)
        {
            if (model == null) return;
            driver.Apply(new BossAnimationState(new BossSnapshot(
                default(EntityId),
                model.Health,
                model.MaxHealth,
                model.Phase,
                model.IsInvulnerable,
                model.CurrentAttack,
                model.Mode,
                model.Stagger,
                model.ContractSeconds)));
        }

        public void Apply(BossSnapshot snapshot) => driver.Apply(new BossAnimationState(snapshot));
    }

    public readonly struct BossTelegraphCommand
    {
        public BossTelegraphCommand(
            EntityId bossId,
            BossAttackId attackId,
            WorldPosition origin,
            WorldPosition target,
            float duration,
            PresentationCueId cue,
            ulong eventId,
            double occurredAt)
        {
            BossId = bossId;
            AttackId = attackId;
            Origin = origin;
            Target = target;
            Duration = duration;
            Cue = cue;
            EventId = eventId;
            OccurredAt = occurredAt;
        }

        public EntityId BossId { get; }
        public BossAttackId AttackId { get; }
        public WorldPosition Origin { get; }
        public WorldPosition Target { get; }
        public float Duration { get; }
        public PresentationCueId Cue { get; }
        public ulong EventId { get; }
        public double OccurredAt { get; }
    }

    public interface IBossTelegraphDriver
    {
        void Show(BossTelegraphCommand command);
    }

    public sealed class BossTelegraphPresenter : IDisposable
    {
        private readonly IBossTelegraphDriver driver;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public BossTelegraphPresenter(IGameplayEventStream events, IBossTelegraphDriver driver)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            subscription = events.Subscribe<BossAttackTelegraphEvent>(HandleTelegraph);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            subscription.Dispose();
        }

        private void HandleTelegraph(BossAttackTelegraphEvent telegraph)
        {
            if (!tracker.Accept(telegraph)) return;
            driver.Show(new BossTelegraphCommand(
                telegraph.BossId,
                telegraph.AttackId,
                telegraph.Origin,
                telegraph.Target,
                telegraph.Duration,
                telegraph.Cue,
                telegraph.EventId,
                telegraph.OccurredAt));
        }
    }

    public sealed class BossVfxPresenter : IDisposable
    {
        private readonly IVfxDriver driver;
        private readonly EntityId bossId;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public BossVfxPresenter(IGameplayEventStream events, IVfxDriver driver, EntityId bossId = default(EntityId))
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            this.bossId = bossId;
            subscription = events.Subscribe(HandleEvent);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            subscription.Dispose();
        }

        private void HandleEvent(IGameplayEvent @event)
        {
            if (!tracker.Accept(@event)) return;
            if (@event is BossAttackTelegraphEvent telegraph && Matches(telegraph.BossId))
            {
                driver.Play(new PresentationCue(telegraph.BossId, telegraph.Origin, telegraph.Cue.Value, PresentationCueKind.BossTelegraph, telegraph.EventId, telegraph.OccurredAt));
            }
            else if (@event is BossAttackCueEvent attack && Matches(attack.BossId))
            {
                driver.Play(new PresentationCue(attack.BossId, WorldPosition.Origin, attack.Cue.Value, PresentationCueKind.BossAttack, attack.EventId, attack.OccurredAt));
            }
            else if (@event is BossPhaseChangedEvent phase && Matches(phase.BossId))
            {
                driver.Play(new PresentationCue(phase.BossId, WorldPosition.Origin, phase.Current.ToString(), PresentationCueKind.BossPhase, phase.EventId, phase.OccurredAt));
            }
            else if (@event is BossDefeatedEvent defeated && Matches(defeated.BossId))
            {
                driver.Play(new PresentationCue(defeated.BossId, WorldPosition.Origin, "BossDefeated", PresentationCueKind.Death, defeated.EventId, defeated.OccurredAt, defeated.KillerId));
            }
        }

        private bool Matches(EntityId id) => !bossId.IsValid || bossId == id;
    }

    public sealed class BossAudioPresenter : IDisposable
    {
        private readonly IAudioDriver driver;
        private readonly EntityId bossId;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public BossAudioPresenter(IGameplayEventStream events, IAudioDriver driver, EntityId bossId = default(EntityId))
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            this.bossId = bossId;
            subscription = events.Subscribe(HandleEvent);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            subscription.Dispose();
        }

        private void HandleEvent(IGameplayEvent @event)
        {
            if (!tracker.Accept(@event)) return;
            if (@event is BossAttackCueEvent attack && Matches(attack.BossId))
            {
                driver.Play(new PresentationCue(attack.BossId, WorldPosition.Origin, attack.Cue.Value, PresentationCueKind.BossAttack, attack.EventId, attack.OccurredAt));
            }
            else if (@event is BossPhaseChangedEvent phase && Matches(phase.BossId))
            {
                driver.Play(new PresentationCue(phase.BossId, WorldPosition.Origin, phase.Current.ToString(), PresentationCueKind.BossPhase, phase.EventId, phase.OccurredAt));
            }
            else if (@event is BossDefeatedEvent defeated && Matches(defeated.BossId))
            {
                driver.Play(new PresentationCue(defeated.BossId, WorldPosition.Origin, "BossDefeated", PresentationCueKind.Death, defeated.EventId, defeated.OccurredAt, defeated.KillerId));
            }
        }

        private bool Matches(EntityId id) => !bossId.IsValid || bossId == id;
    }

    public readonly struct BossDebugState
    {
        public BossDebugState(BossSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public BossSnapshot Snapshot { get; }
    }

    public interface IBossDebugDriver
    {
        void Render(BossDebugState state);
    }

    public sealed class BossDebugPresenter
    {
        private readonly IBossDebugDriver driver;

        public BossDebugPresenter(IBossDebugDriver driver)
        {
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        public void Apply(IBossReadModel model)
        {
            if (model == null) return;
            driver.Render(new BossDebugState(new BossSnapshot(
                default(EntityId),
                model.Health,
                model.MaxHealth,
                model.Phase,
                model.IsInvulnerable,
                model.CurrentAttack,
                model.Mode,
                model.Stagger,
                model.ContractSeconds)));
        }

        public void Apply(BossSnapshot snapshot) => driver.Render(new BossDebugState(snapshot));
    }
}
