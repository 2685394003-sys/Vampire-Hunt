using System;
using VampireHunt.Abilities.Contracts;
using VampireHunt.Boss.Contracts;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Player.Contracts;
using VampireHunt.Presentation.Contracts;

namespace VampireHunt.Presentation.Runtime
{
    public sealed class AnimationEventPresenter : IDisposable
    {
        private readonly IAnimationDriver driver;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public AnimationEventPresenter(IGameplayEventStream events, IAnimationDriver driver)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
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
            if (@event is GameplayCueEvent cue)
            {
                driver.Play(new PresentationCue(
                    cue.TargetId,
                    cue.Position,
                    cue.Cue.Value,
                    PresentationCueKind.GameplayCue,
                    cue.EventId,
                    cue.OccurredAt));
                return;
            }

            if (@event is DamageConfirmedEvent damage)
            {
                if (damage.Result.AppliedDamage <= 0) return;
                driver.Play(new PresentationCue(
                    damage.TargetId,
                    damage.Result.HitPosition,
                    damage.Result.WasCritical ? "DamageCritical" : "Damage",
                    PresentationCueKind.Damage,
                    damage.EventId,
                    damage.OccurredAt,
                    damage.SourceId));
                return;
            }

            if (@event is EntityDiedEvent died)
            {
                driver.Play(new PresentationCue(died.EntityId, died.Position, "Death", PresentationCueKind.Death, died.EventId, died.OccurredAt, died.KillerId));
                return;
            }

            if (@event is EnemyDeathEvent enemyDeath)
            {
                driver.Play(new PresentationCue(enemyDeath.EnemyId, enemyDeath.Position, "Death", PresentationCueKind.Death, enemyDeath.EventId, enemyDeath.OccurredAt, enemyDeath.KillerId));
                return;
            }

            if (@event is PlayerDeathEvent playerDeath)
            {
                driver.Play(new PresentationCue(playerDeath.PlayerId, WorldPosition.Origin, "Death", PresentationCueKind.Death, playerDeath.EventId, playerDeath.OccurredAt, playerDeath.KillerId));
                return;
            }

            if (@event is BossDefeatedEvent bossDeath)
            {
                driver.Play(new PresentationCue(bossDeath.BossId, WorldPosition.Origin, "Death", PresentationCueKind.Death, bossDeath.EventId, bossDeath.OccurredAt, bossDeath.KillerId));
                return;
            }

            if (@event is BossAttackTelegraphEvent telegraph)
            {
                driver.Play(new PresentationCue(telegraph.BossId, telegraph.Origin, telegraph.Cue.Value, PresentationCueKind.BossTelegraph, telegraph.EventId, telegraph.OccurredAt));
                return;
            }

            if (@event is BossAttackCueEvent attack)
            {
                driver.Play(new PresentationCue(attack.BossId, WorldPosition.Origin, attack.Cue.Value, PresentationCueKind.BossAttack, attack.EventId, attack.OccurredAt));
                return;
            }

            if (@event is BossPhaseChangedEvent phase)
            {
                driver.Play(new PresentationCue(phase.BossId, WorldPosition.Origin, phase.Current.ToString(), PresentationCueKind.BossPhase, phase.EventId, phase.OccurredAt));
            }
        }
    }

    public sealed class StatusVfxPresenter : IDisposable
    {
        private readonly IVfxDriver driver;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public StatusVfxPresenter(IGameplayEventStream events, IVfxDriver driver)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            subscription = events.Subscribe< GameplayCueEvent >(HandleCue);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            subscription.Dispose();
        }

        private void HandleCue(GameplayCueEvent cue)
        {
            if (!tracker.Accept(cue)) return;
            if (cue.Phase == GameplayCuePhase.Removed)
                driver.Remove(cue.TargetId, cue.Cue.Value);
            else
                driver.Play(new PresentationCue(cue.TargetId, cue.Position, cue.Cue.Value, PresentationCueKind.GameplayCue, cue.EventId, cue.OccurredAt));
        }
    }

    public sealed class AudioCuePresenter : IDisposable
    {
        private readonly IAudioDriver driver;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public AudioCuePresenter(IGameplayEventStream events, IAudioDriver driver)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
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
            if (@event is GameplayCueEvent cue)
            {
                driver.Play(new PresentationCue(cue.TargetId, cue.Position, cue.Cue.Value, PresentationCueKind.GameplayCue, cue.EventId, cue.OccurredAt));
            }
            else if (@event is BossAttackCueEvent attack)
            {
                driver.Play(new PresentationCue(attack.BossId, WorldPosition.Origin, attack.Cue.Value, PresentationCueKind.BossAttack, attack.EventId, attack.OccurredAt));
            }
            else if (@event is BossPhaseChangedEvent phase)
            {
                driver.Play(new PresentationCue(phase.BossId, WorldPosition.Origin, phase.Current.ToString(), PresentationCueKind.BossPhase, phase.EventId, phase.OccurredAt));
            }
            else if (@event is PlayerDeathEvent playerDeath)
            {
                driver.Play(new PresentationCue(playerDeath.PlayerId, WorldPosition.Origin, "PlayerDeath", PresentationCueKind.Death, playerDeath.EventId, playerDeath.OccurredAt, playerDeath.KillerId));
            }
            else if (@event is EnemyDeathEvent enemyDeath)
            {
                driver.Play(new PresentationCue(enemyDeath.EnemyId, enemyDeath.Position, "EnemyDeath", PresentationCueKind.Death, enemyDeath.EventId, enemyDeath.OccurredAt, enemyDeath.KillerId));
            }
        }
    }

    public sealed class CameraCuePresenter : IDisposable
    {
        private readonly ICameraDriver driver;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public CameraCuePresenter(IGameplayEventStream events, ICameraDriver driver)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
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
            if (@event is BossAttackTelegraphEvent telegraph)
            {
                driver.Play(new PresentationCue(telegraph.BossId, telegraph.Target, telegraph.Cue.Value, PresentationCueKind.BossTelegraph, telegraph.EventId, telegraph.OccurredAt));
            }
            else if (@event is BossAttackCueEvent attack)
            {
                driver.Play(new PresentationCue(attack.BossId, WorldPosition.Origin, attack.Cue.Value, PresentationCueKind.BossAttack, attack.EventId, attack.OccurredAt));
            }
        }
    }
}
