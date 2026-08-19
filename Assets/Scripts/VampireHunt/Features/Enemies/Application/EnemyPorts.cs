using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Enemies.Domain;

namespace VampireHunt.Enemies.Application
{
    // The repository is an application-internal port because its return type
    // is the mutable EnemyAggregate. Cross-module adapters must use Contracts.
    internal interface IEnemyRepository
    {
        EnemyAggregate Get(EntityId enemyId);
        bool TryGet(EntityId enemyId, out EnemyAggregate enemy);
    }

    public interface IEnemyMotor
    {
        void Move(EntityId enemyId, EnemyIntent intent);
        void Knockback(EntityId enemyId, in KnockbackImpulse impulse);
        void Stop(EntityId enemyId);
    }

    public readonly struct EnemyProjectileRequest
    {
        public EnemyProjectileRequest(AttackIntent attack)
        {
            Attack = attack;
        }

        public AttackIntent Attack { get; }
        public EntityId SourceId => Attack.SourceId;
        public EntityId TargetId => Attack.TargetId;
        public WorldPosition HitPosition => Attack.HitPosition;
    }

    public interface IEnemyProjectileSpawner
    {
        void Spawn(EnemyProjectileRequest request);
    }
}
