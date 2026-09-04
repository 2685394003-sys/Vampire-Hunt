using NUnit.Framework;
using VampireHunt.Boss.Encounter;

namespace VampireHunt.Tests.Editor
{
    public sealed class BossEncounterTests
    {
        [Test]
        public void GuardUsesDamageValuesAndStaggerStartsWithoutProximity()
        {
            BossEncounterAggregate boss = CreateBoss();
            boss.BeginRun();

            Assert.AreEqual(BossDamageOutcome.GuardDamaged, boss.ApplyDamage(40f));
            Assert.AreEqual(60f, boss.GuardHealth);
            Assert.AreEqual(BossEncounterState.RoamingIdle, boss.State);
            // 破盾即踉跄：结算不再接收攻击者距离，玩家在任意距离都能触发
            Assert.AreEqual(BossDamageOutcome.GuardBroken, boss.ApplyDamage(60f));
            Assert.AreEqual(BossEncounterState.StaggerEffect, boss.State);
        }

        [Test]
        public void StaggerCannotStartWhileGuardIsIntact()
        {
            BossEncounterAggregate boss = CreateBoss();
            boss.BeginRun();

            Assert.IsFalse(boss.TryBeginStagger());
            boss.ApplyDamage(40f);
            Assert.IsFalse(boss.TryBeginStagger());
            Assert.AreEqual(BossEncounterState.RoamingIdle, boss.State);
        }

        [Test]
        public void StaggerEffectDamageIsIgnoredAndCompletesStraightIntoBattle()
        {
            BossEncounterAggregate boss = CreateBoss();
            boss.BeginRun();
            boss.ApplyDamage(100f);
            Assert.AreEqual(BossEncounterState.StaggerEffect, boss.State);

            // 踉跄表演期间 Boss 免伤
            Assert.AreEqual(BossDamageOutcome.Ignored, boss.ApplyDamage(50f));
            Assert.AreEqual(300f, boss.Health);

            // 踉跄结束直接进入 Boss 战，不再经过处决窗口
            Assert.IsTrue(boss.CompleteStaggerEffect());
            Assert.AreEqual(BossEncounterState.Battle, boss.State);
        }

        [Test]
        public void BattleDamageDefeatsStageAndTransitionLoadsNextGuardRow()
        {
            BossEncounterAggregate boss = CreateBoss();
            boss.BeginRun();
            boss.ApplyDamage(100f);
            boss.CompleteStaggerEffect();

            Assert.AreEqual(BossEncounterState.Battle, boss.State);
            Assert.AreEqual(BossDamageOutcome.StageDefeated, boss.ApplyDamage(300f));
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
            }));
        }
    }
}
