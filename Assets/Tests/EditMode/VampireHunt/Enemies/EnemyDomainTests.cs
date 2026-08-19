using NUnit.Framework;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Enemies.Domain;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Navigation.Domain;

namespace VampireHunt.Tests.Modules.Enemies
{
    public sealed class EnemyDomainTests
    {
        [Test]
        public void VitalsApplyDamageAndReportDeathExactlyOnce()
        {
            EntityId enemyId = new EntityId(2);
            EnemyVitals vitals = new EnemyVitals();
            vitals.Reset(enemyId, 10);
            ResolvedDamage damage = new ResolvedDamage(
                new EntityId(1),
                enemyId,
                10,
                false,
                new HitContext(new WorldPosition(1f, 0f, 1f)));

            DamageResult result = vitals.ApplyDamage(in damage);

            Assert.That(result.AppliedDamage, Is.EqualTo(10));
            Assert.That(result.WasKilled, Is.True);
            Assert.That(vitals.IsAlive, Is.False);
            Assert.That(vitals.ApplyDamage(in damage).AppliedDamage, Is.EqualTo(0));
        }

        [Test]
        public void AggregateResetRequiresNewValidEntityIdAndRestoresFullVitals()
        {
            EnemySpec spec = new EnemySpec("basic", 8, 2f, 2, 1.5f, 0.5f, EnemyAttackType.Melee, new RewardGrant(3, 1));
            EnemyAggregate enemy = new EnemyAggregate(new EntityId(3), spec, new AlwaysWalkableNavigation());
            ResolvedDamage damage = new ResolvedDamage(new EntityId(1), enemy.Id, 7, false);
            enemy.ApplyDamage(in damage);
            Assert.That(enemy.Vitals.CurrentHealth, Is.EqualTo(1));

            EntityId replacement = new EntityId(4);
            enemy.ResetForSpawn(replacement, spec);

            Assert.That(enemy.Id, Is.EqualTo(replacement));
            Assert.That(enemy.Vitals.CurrentHealth, Is.EqualTo(8));
            Assert.That(enemy.DeathHandled, Is.False);
        }

        private sealed class AlwaysWalkableNavigation : INavigationField
        {
            public Direction SampleDirection(WorldPosition position, WorldPosition target) => Direction.Up;
            public bool IsWalkable(WorldPosition position) => true;
            public WorldPosition TryFindRecovery(WorldPosition position) => position;
        }
    }
}
