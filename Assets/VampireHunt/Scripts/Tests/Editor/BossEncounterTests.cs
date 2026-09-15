using NUnit.Framework;
using VampireHunt.Boss.Encounter;

namespace VampireHunt.Tests.Editor
{
    public sealed class BossEncounterTests
    {
        [Test]
        public void GuardUsesDamageValuesAndRequiresNearbyPlayerToStartStagger()
        {
            BossEncounterAggregate boss = CreateBoss();
            boss.BeginRun();

            Assert.AreEqual(BossDamageOutcome.GuardDamaged, boss.ApplyDamage(40f, 2f));
            Assert.AreEqual(60f, boss.GuardHealth);
            Assert.AreEqual(BossDamageOutcome.GuardBroken, boss.ApplyDamage(60f, 20f));
            Assert.AreEqual(BossEncounterState.RoamingIdle, boss.State);
            Assert.IsTrue(boss.TryBeginStagger(4f));
            Assert.AreEqual(BossEncounterState.StaggerEffect, boss.State);
        }

        [Test]
        public void ExecutionStartsBattleAndStageTransitionLoadsNextGuardRow()
        {
            BossEncounterAggregate boss = CreateBoss();
            boss.BeginRun();
            boss.ApplyDamage(100f, 2f);
            boss.CompleteStaggerEffect();

            Assert.AreEqual(BossDamageOutcome.ExecutionTriggered, boss.ApplyDamage(1f, 2f));
            Assert.AreEqual(BossEncounterState.Battle, boss.State);
            Assert.AreEqual(BossDamageOutcome.StageDefeated, boss.ApplyDamage(300f, 2f));
            Assert.IsTrue(boss.CompletePhaseTransition());
            Assert.AreEqual(2, boss.StageNumber);
            Assert.AreEqual(180f, boss.GuardHealth);
            Assert.AreEqual(450f, boss.Health);
            Assert.AreEqual(BossEncounterState.RoamingIdle, boss.State);
        }

        private static BossEncounterAggregate CreateBoss()
        {
            return new BossEncounterAggregate(new BossEncounterRules(new[]
            {
                new BossStageRules(100f, 300f, 1),
                new BossStageRules(180f, 450f, 2),
                new BossStageRules(280f, 650f, 3)
            }, 6f));
        }
    }
}
