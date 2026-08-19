using System;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using VampireHunt.Presentation.Contracts;
using VampireHunt.Presentation.Runtime;

namespace VampireHunt.Presentation.Player
{
    public readonly struct PlayerAnimationState
    {
        public PlayerAnimationState(EntityId playerId, bool isAlive, bool isAttacking, float healthRatio, float staminaRatio)
        {
            PlayerId = playerId;
            IsAlive = isAlive;
            IsAttacking = isAttacking;
            HealthRatio = healthRatio;
            StaminaRatio = staminaRatio;
        }

        public EntityId PlayerId { get; }
        public bool IsAlive { get; }
        public bool IsAttacking { get; }
        public float HealthRatio { get; }
        public float StaminaRatio { get; }
    }

    public interface IPlayerAnimationDriver
    {
        void Apply(PlayerAnimationState state);
    }

    public sealed class PlayerAnimatorPresenter
    {
        private readonly IPlayerAnimationDriver driver;

        public PlayerAnimatorPresenter(IPlayerAnimationDriver driver)
        {
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        public void Apply(IPlayerReadModel model)
        {
            if (model == null) return;
            float healthRatio = model.MaxHealth > 0 ? (float)model.Health / model.MaxHealth : 0f;
            float staminaRatio = model.MaxStamina > 0f ? model.Stamina / model.MaxStamina : 0f;
            driver.Apply(new PlayerAnimationState(default(EntityId), model.IsAlive, false, Clamp01(healthRatio), Clamp01(staminaRatio)));
        }

        public void Apply(PlayerSnapshot snapshot)
        {
            float healthRatio = snapshot.MaxHealth > 0 ? (float)snapshot.Health / snapshot.MaxHealth : 0f;
            float staminaRatio = snapshot.MaxStamina > 0f ? snapshot.Stamina / snapshot.MaxStamina : 0f;
            driver.Apply(new PlayerAnimationState(snapshot.Id, snapshot.IsAlive, snapshot.IsAttacking, Clamp01(healthRatio), Clamp01(staminaRatio)));
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }

    public sealed class PlayerVfxPresenter : IDisposable
    {
        private readonly IVfxDriver driver;
        private readonly EntityId playerId;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public PlayerVfxPresenter(IGameplayEventStream events, IVfxDriver driver, EntityId playerId = default(EntityId))
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            this.playerId = playerId;
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
            if (@event is DamageConfirmedEvent damage)
            {
                if (damage.Result.AppliedDamage <= 0 || !Matches(damage.TargetId)) return;
                driver.Play(new PresentationCue(damage.TargetId, damage.Result.HitPosition, damage.Result.WasCritical ? "PlayerCriticalHit" : "PlayerHit", PresentationCueKind.Player, damage.EventId, damage.OccurredAt, damage.SourceId));
            }
            else if (@event is PlayerDeathEvent death && Matches(death.PlayerId))
            {
                driver.Play(new PresentationCue(death.PlayerId, WorldPosition.Origin, "PlayerDeath", PresentationCueKind.Death, death.EventId, death.OccurredAt, death.KillerId));
            }
        }

        private bool Matches(EntityId id) => !playerId.IsValid || playerId == id;
    }

    public sealed class PlayerAudioPresenter : IDisposable
    {
        private readonly IAudioDriver driver;
        private readonly EntityId playerId;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public PlayerAudioPresenter(IGameplayEventStream events, IAudioDriver driver, EntityId playerId = default(EntityId))
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            this.playerId = playerId;
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
            if (@event is DamageConfirmedEvent damage && damage.Result.AppliedDamage > 0 && Matches(damage.TargetId))
            {
                driver.Play(new PresentationCue(damage.TargetId, damage.Result.HitPosition, damage.Result.WasCritical ? "PlayerCriticalHit" : "PlayerHit", PresentationCueKind.Player, damage.EventId, damage.OccurredAt, damage.SourceId));
            }
            else if (@event is PlayerDeathEvent death && Matches(death.PlayerId))
            {
                driver.Play(new PresentationCue(death.PlayerId, WorldPosition.Origin, "PlayerDeath", PresentationCueKind.Death, death.EventId, death.OccurredAt, death.KillerId));
            }
        }

        private bool Matches(EntityId id) => !playerId.IsValid || playerId == id;
    }
}
