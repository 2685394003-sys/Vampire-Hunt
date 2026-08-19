using System;
using VampireHunt.Boss.Contracts;
using VampireHunt.Boss.Domain;
using VampireHunt.Core;

namespace VampireHunt.Boss.Application
{
    public sealed class BossPhaseService
    {
        private readonly IBossRepository repository;
        private readonly IGameplayEventSink eventSink;
        private readonly IGameClock clock;
        private readonly GameplayEventIdAllocator eventIds;

        internal BossPhaseService(
            IBossRepository repository,
            IGameplayEventSink eventSink,
            IGameClock clock,
            GameplayEventIdAllocator eventIds)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.eventSink = eventSink;
            this.clock = clock;
            this.eventIds = eventIds ?? new GameplayEventIdAllocator();
        }

        public PhaseTransition EvaluateTransition(EntityId bossId)
        {
            BossAggregate boss = repository.Get(bossId);
            PhaseTransition transition = boss.Phases.Evaluate(
                boss.Vitals.MaxHealth > 0 ? (float)boss.Vitals.CurrentHealth / boss.Vitals.MaxHealth : 0f);
            if (transition.Changed)
            {
                eventSink?.Publish(new BossPhaseChangedEvent(
                    eventIds.Next(),
                    clock?.Now ?? 0d,
                    bossId,
                    transition.Previous,
                    transition.Current));
            }
            return transition;
        }
    }
}
