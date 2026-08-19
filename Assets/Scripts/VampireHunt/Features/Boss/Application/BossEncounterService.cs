using System;
using System.Collections.Generic;
using VampireHunt.Boss.Contracts;
using VampireHunt.Boss.Domain;
using VampireHunt.Core;

namespace VampireHunt.Boss.Application
{
    public sealed class BossEncounterService : IBossEncounterQuery
    {
        private readonly IBossRepository repository;
        private readonly BossAttackService attacks;
        private readonly IBossEncounterConsequences consequences;
        private readonly IGameplayEventSink eventSink;
        private readonly IGameClock clock;
        private readonly GameplayEventIdAllocator eventIds;
        private readonly HashSet<EntityId> settledDefeats = new();

        public EncounterMode Mode
        {
            get
            {
                EncounterMode result = EncounterMode.Inactive;
                foreach (BossAggregate boss in repository.GetAll())
                {
                    if (boss.Encounter.Mode == EncounterMode.Battle) return EncounterMode.Battle;
                    if (boss.Encounter.Mode == EncounterMode.Hunt) result = EncounterMode.Hunt;
                }
                return result;
            }
        }

        public bool IsEncounterActive => Mode is EncounterMode.Hunt or EncounterMode.Battle;

        internal BossEncounterService(
            IBossRepository repository,
            BossAttackService attacks,
            IBossEncounterConsequences consequences,
            IGameplayEventSink eventSink,
            IGameClock clock,
            GameplayEventIdAllocator eventIds)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.attacks = attacks ?? throw new ArgumentNullException(nameof(attacks));
            this.consequences = consequences;
            this.eventSink = eventSink;
            this.clock = clock;
            this.eventIds = eventIds ?? new GameplayEventIdAllocator();
        }

        public bool HandleDefeat(EntityId bossId)
        {
            BossAggregate boss = repository.Get(bossId);
            if (boss.Vitals.IsAlive || !settledDefeats.Add(bossId)) return false;

            attacks.Cancel(bossId);
            boss.Encounter.Defeat();
            EntityId killerId = boss.Vitals.LastDamageSourceId;
            consequences?.OnBossDefeated(bossId, killerId);
            eventSink?.Publish(new BossDefeatedEvent(
                eventIds.Next(), clock?.Now ?? 0d, bossId, killerId));
            return true;
        }

        public void Tick(EntityId bossId, float deltaTime)
        {
            BossAggregate boss = repository.Get(bossId);
            boss.Encounter.Tick(Math.Max(0f, deltaTime));
            if (!boss.Vitals.IsAlive) HandleDefeat(bossId);
        }

        internal void Reset(EntityId bossId) => settledDefeats.Remove(bossId);
    }
}
