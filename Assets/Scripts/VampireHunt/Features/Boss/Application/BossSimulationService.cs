using System;
using System.Collections.Generic;
using VampireHunt.Boss.Contracts;
using VampireHunt.Boss.Domain;
using VampireHunt.Core;

namespace VampireHunt.Boss.Application
{
    public sealed class BossSimulationService
    {
        private readonly IBossRepository repository;
        private readonly BossPhaseService phases;
        private readonly BossAttackService attacks;
        private readonly BossEncounterService encounter;
        private readonly Dictionary<EntityId, IEncounterMechanic> mechanics = new();

        internal BossSimulationService(
            IBossRepository repository,
            BossPhaseService phases,
            BossAttackService attacks,
            BossEncounterService encounter)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.phases = phases ?? throw new ArgumentNullException(nameof(phases));
            this.attacks = attacks ?? throw new ArgumentNullException(nameof(attacks));
            this.encounter = encounter ?? throw new ArgumentNullException(nameof(encounter));
        }

        public void Register(EntityId bossId)
        {
            BossAggregate boss = repository.Get(bossId);
            mechanics[bossId] = new ContractCountdownMechanic(
                boss.Spec.ContractTriggerHealthRatio,
                boss.Spec.ContractCountdownRate);
        }

        public void Tick(EntityId bossId, float deltaTime)
        {
            BossAggregate boss = repository.Get(bossId);
            float safeDelta = Math.Max(0f, deltaTime);
            encounter.Tick(bossId, safeDelta);
            if (!boss.Vitals.IsAlive || boss.Encounter.Mode == EncounterMode.Defeated) return;

            phases.EvaluateTransition(bossId);
            if (mechanics.TryGetValue(bossId, out IEncounterMechanic mechanic))
            {
                float ratio = (float)boss.Vitals.CurrentHealth / boss.Vitals.MaxHealth;
                mechanic.Tick(new BossMechanicContext(boss.Encounter, ratio, safeDelta));
            }

            if (boss.Attacks.IsExecuting)
                attacks.TickAttack(bossId, safeDelta);
            else
            {
                attacks.TickAttack(bossId, safeDelta);
                attacks.TryStartAttack(bossId);
            }
        }

        public AttackStartResult TryStartAttack(EntityId bossId) => attacks.TryStartAttack(bossId);

        public AttackStartResult TryStartAttack(EntityId bossId, BossAttackId attackId) =>
            attacks.TryStartAttack(bossId, attackId);

        public void CancelAttack(EntityId bossId) => attacks.CancelAttack(bossId);
    }
}
