using System;
using System.Collections.Generic;
using VampireHunt.Abilities.Contracts;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Presentation.Contracts;
using VampireHunt.Presentation.Runtime;

namespace VampireHunt.Presentation.Enemies
{
    public readonly struct EnemyAnimationState
    {
        public EnemyAnimationState(EntityId enemyId, EnemyState state, bool isAlive, float healthRatio)
        {
            EnemyId = enemyId;
            State = state;
            IsAlive = isAlive;
            HealthRatio = healthRatio;
        }

        public EntityId EnemyId { get; }
        public EnemyState State { get; }
        public bool IsAlive { get; }
        public float HealthRatio { get; }
    }

    public interface IEnemyAnimationDriver
    {
        void Apply(EnemyAnimationState state);
    }

    public sealed class EnemyAnimationPresenter
    {
        private readonly IEnemyAnimationDriver driver;

        public EnemyAnimationPresenter(IEnemyAnimationDriver driver)
        {
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        public void Apply(IEnemyReadModel model)
        {
            if (model == null) return;
            driver.Apply(new EnemyAnimationState(default(EntityId), model.State, model.IsAlive, model.IsAlive ? 1f : 0f));
        }

        public void Apply(EnemySnapshot snapshot)
        {
            float ratio = snapshot.MaxHealth > 0 ? (float)snapshot.Health / snapshot.MaxHealth : 0f;
            driver.Apply(new EnemyAnimationState(snapshot.Id, snapshot.State, snapshot.IsAlive, Clamp01(ratio)));
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }

    public sealed class EnemyStatusVfxPresenter : IDisposable
    {
        private readonly IVfxDriver driver;
        private readonly EntityId enemyId;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public EnemyStatusVfxPresenter(IGameplayEventStream events, IVfxDriver driver, EntityId enemyId = default(EntityId))
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            this.enemyId = enemyId;
            subscription = events.Subscribe<GameplayCueEvent>(HandleCue);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            subscription.Dispose();
        }

        private void HandleCue(GameplayCueEvent cue)
        {
            if (!tracker.Accept(cue) || !Matches(cue.TargetId)) return;
            if (cue.Phase == VampireHunt.Abilities.Contracts.GameplayCuePhase.Removed)
                driver.Remove(cue.TargetId, cue.Cue.Value);
            else
                driver.Play(new PresentationCue(cue.TargetId, cue.Position, cue.Cue.Value, PresentationCueKind.Enemy, cue.EventId, cue.OccurredAt));
        }

        private bool Matches(EntityId id) => !enemyId.IsValid || enemyId == id;
    }

    public sealed class EnemyHurtFlashPresenter : IDisposable
    {
        private readonly IHurtFlashDriver driver;
        private readonly EntityId enemyId;
        private readonly PresentationEventTracker tracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public EnemyHurtFlashPresenter(IGameplayEventStream events, IHurtFlashDriver driver, EntityId enemyId = default(EntityId))
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            this.enemyId = enemyId;
            subscription = events.Subscribe<DamageConfirmedEvent>(HandleDamage);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            subscription.Dispose();
        }

        private void HandleDamage(DamageConfirmedEvent damage)
        {
            if (!tracker.Accept(damage) || damage.Result.AppliedDamage <= 0 || !Matches(damage.TargetId)) return;
            driver.Flash(new HurtFlashCommand(damage.TargetId, damage.Result.AppliedDamage, damage.Result.HitPosition, damage.EventId, damage.OccurredAt));
        }

        private bool Matches(EntityId id) => !enemyId.IsValid || enemyId == id;
    }

    public sealed class FlowFieldDebugPresenter
    {
        private readonly INavigationField navigation;
        private readonly IFlowFieldDebugDriver driver;

        public FlowFieldDebugPresenter(INavigationField navigation, IFlowFieldDebugDriver driver)
        {
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        public void Draw(WorldPosition position, WorldPosition target)
        {
            var direction = navigation.SampleDirection(position, target);
            driver.Draw(new FlowFieldDebugCommand(
                position,
                new PresentationDirection(direction.X, direction.Y),
                navigation.IsWalkable(position)));
        }

        public void Draw(IEnumerable<WorldPosition> positions, WorldPosition target)
        {
            if (positions == null) return;
            foreach (WorldPosition position in positions) Draw(position, target);
        }
    }
}
