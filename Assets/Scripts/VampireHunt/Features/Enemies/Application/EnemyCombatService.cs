using System;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;

namespace VampireHunt.Enemies.Application
{
    public readonly struct EnemyAttackResult
    {
        public EnemyAttackResult(bool started, bool projectile, DamageResult damage)
        {
            Started = started;
            IsProjectile = projectile;
            Damage = damage;
        }

        public bool Started { get; }
        public bool IsProjectile { get; }
        public DamageResult Damage { get; }
    }

    /// <summary>
    /// Converts enemy attack intent into the shared Combat application entry
    /// point. Ranged intent crosses a projectile port; the projectile's
    /// eventual hit must call CombatApplicationService on the server.
    /// </summary>
    public sealed class EnemyCombatService
    {
        private readonly CombatApplicationService combat;
        private readonly IEnemyProjectileSpawner projectileSpawner;

        public EnemyCombatService(
            CombatApplicationService combatApplicationService,
            IEnemyProjectileSpawner projectileSpawner = null)
        {
            combat = combatApplicationService ?? throw new ArgumentNullException(nameof(combatApplicationService));
            this.projectileSpawner = projectileSpawner;
        }

        public EnemyAttackResult Execute(EntityId enemyId, AttackIntent intent)
        {
            if (!enemyId.IsValid || intent.SourceId != enemyId || !intent.TargetId.IsValid || intent.BaseDamage < 0)
            {
                return new EnemyAttackResult(false, false, DamageResult.NoDamage(Math.Max(0, intent.BaseDamage), intent.HitPosition));
            }

            if (intent.IsRanged)
            {
                if (projectileSpawner == null) return new EnemyAttackResult(false, true, DamageResult.NoDamage(intent.BaseDamage, intent.HitPosition));
                projectileSpawner.Spawn(new EnemyProjectileRequest(intent));
                return new EnemyAttackResult(true, true, DamageResult.NoDamage(intent.BaseDamage, intent.HitPosition));
            }

            DamageRequest request = new DamageRequest(
                intent.SourceId,
                intent.TargetId,
                intent.BaseDamage,
                intent.Flags,
                new HitContext(intent.HitPosition, intent.DamageTag));
            DamageResult result = combat.ApplyDamage(in request);
            return new EnemyAttackResult(true, false, result);
        }
    }
}
