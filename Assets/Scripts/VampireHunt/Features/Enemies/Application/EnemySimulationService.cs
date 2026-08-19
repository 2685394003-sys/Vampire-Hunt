using System;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Enemies.Domain;

namespace VampireHunt.Enemies.Application
{
    /// <summary>
    /// One server simulation step: consume perception, decide, move, then
    /// execute a combat intent. It does not perform death rewards; that is a
    /// separate once-only service in the documented simulation order.
    /// </summary>
    internal sealed class EnemySimulationService
    {
        private readonly IEnemyRepository repository;
        private readonly IEnemyMotor motor;
        private readonly EnemyCombatService combat;
        private readonly IGameClock clock;

        public EnemySimulationService(
            IEnemyRepository repository,
            IEnemyMotor motor,
            EnemyCombatService combat,
            IGameClock clock)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.motor = motor ?? throw new ArgumentNullException(nameof(motor));
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public bool Tick(EntityId enemyId, in EnemyPerception perception)
        {
            if (!repository.TryGet(enemyId, out EnemyAggregate enemy) || enemy == null || !enemy.IsAlive)
            {
                return false;
            }

            EnemyIntent intent = enemy.Brain.Decide(in perception);
            enemy.SetTarget(intent.TargetId);
            enemy.SetState(intent.State);

            if (intent.ShouldMove) motor.Move(enemyId, intent);
            else if (!intent.ShouldAttack) motor.Stop(enemyId);

            if (!intent.ShouldAttack) return true;

            EnemyCombatContext context = new EnemyCombatContext(
                enemyId,
                intent.TargetId,
                perception.TargetPosition,
                intent.Distance,
                perception.TargetIsAlive,
                clock.Now,
                enemy.LastAttackAt);
            AttackIntent attack = enemy.CombatPolicy.CreateAttackIntent(in context);
            if (!attack.SourceId.IsValid) return true;

            EnemyAttackResult result = combat.Execute(enemyId, attack);
            if (result.Started) enemy.RecordAttack(clock.Now);
            return true;
        }

        public bool Tick(EntityId enemyId, EnemyPerception perception) => Tick(enemyId, in perception);

        public bool Tick(EntityId enemyId, float deltaTime, in EnemyPerception perception)
        {
            // Delta time is supplied by the server loop for API symmetry; all
            // cooldown decisions use IGameClock.Now to avoid divergent clocks.
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            return Tick(enemyId, in perception);
        }

        public bool Tick(EntityId enemyId, float perceptionRadius)
        {
            if (perceptionRadius < 0f) throw new ArgumentOutOfRangeException(nameof(perceptionRadius));
            if (!repository.TryGet(enemyId, out EnemyAggregate enemy) || enemy == null || !enemy.IsAlive)
            {
                return false;
            }

            EnemyPerception perception = enemy.Brain.Perceive(enemyId, enemy.Position, perceptionRadius);
            return Tick(enemyId, in perception);
        }
    }
}
