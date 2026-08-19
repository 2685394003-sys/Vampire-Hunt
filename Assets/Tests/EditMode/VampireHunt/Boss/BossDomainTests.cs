using NUnit.Framework;
using VampireHunt.Boss.Contracts;
using VampireHunt.Boss.Domain;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Tests.Boss
{
    public sealed class BossDomainTests
    {
        [Test]
        public void PhaseStateMachine_OnlyMovesForwardAcrossConfiguredThresholds()
        {
            BossSpec spec = CreateSpec();
            BossPhaseStateMachine state = new(spec.Phases);

            Assert.That(state.CurrentPhase, Is.EqualTo(BossPhase.PhaseOne));
            Assert.That(state.Evaluate(0.6f).Current, Is.EqualTo(BossPhase.PhaseTwo));
            Assert.That(state.Evaluate(0.2f).Current, Is.EqualTo(BossPhase.PhaseThree));
            Assert.That(state.Evaluate(0.9f).Current, Is.EqualTo(BossPhase.PhaseThree));
        }

        [Test]
        public void Guard_MitigatesUntilBroken_ThenDamageIsUnreduced()
        {
            BossSpec spec = CreateSpec(guardIntegrity: 15, guardReduction: 0.5f);
            BossEncounterState encounter = new(spec);
            encounter.Start(EncounterMode.Battle);

            Assert.That(encounter.ResolveIncomingDamage(10), Is.EqualTo(5));
            Assert.That(encounter.GuardIntegrity, Is.EqualTo(5));
            Assert.That(encounter.ResolveIncomingDamage(5), Is.EqualTo(5));
            Assert.That(encounter.Stagger, Is.EqualTo(StaggerState.Telegraph));
            Assert.That(encounter.ResolveIncomingDamage(10), Is.EqualTo(10));
        }

        [Test]
        public void Vitals_ReturnActualOverkillDamage_AndRespectInvulnerability()
        {
            BossSpec spec = CreateSpec(maxHealth: 20, guardIntegrity: 0);
            BossEncounterState encounter = new(spec);
            encounter.Start(EncounterMode.Battle);
            BossVitals vitals = new(20, encounter);
            ResolvedDamage first = new(
                new EntityId(1), new EntityId(2), 7, false,
                new HitContext(new WorldPosition(1f, 0f, 1f)));

            vitals.SetInvulnerable(true);
            Assert.That(vitals.ApplyDamage(in first).AppliedDamage, Is.Zero);
            vitals.SetInvulnerable(false);
            Assert.That(vitals.ApplyDamage(in first).AppliedDamage, Is.EqualTo(7));

            ResolvedDamage overkill = new(
                new EntityId(1), new EntityId(2), 99, true,
                new HitContext(new WorldPosition(1f, 0f, 1f)));
            DamageResult result = vitals.ApplyDamage(in overkill);
            Assert.That(result.AppliedDamage, Is.EqualTo(13));
            Assert.That(result.WasKilled, Is.True);
            Assert.That(vitals.CurrentHealth, Is.Zero);
        }

        [Test]
        public void ContractMechanic_TriggersOnce_AndUsesConfiguredRate()
        {
            BossSpec spec = CreateSpec(contractSeconds: 10f, contractRate: 2f);
            BossEncounterState encounter = new(spec);
            ContractCountdownMechanic mechanic = new(0.2f, 2f);

            MechanicResult inactive = mechanic.Tick(new BossMechanicContext(encounter, 0.5f, 1f));
            Assert.That(inactive.IsActive, Is.False);
            MechanicResult triggered = mechanic.Tick(new BossMechanicContext(encounter, 0.2f, 1f));
            Assert.That(triggered.TriggeredThisTick, Is.True);
            Assert.That(triggered.RemainingSeconds, Is.EqualTo(8f));
            Assert.That(mechanic.Tick(new BossMechanicContext(encounter, 0.1f, 4f)).Expired, Is.True);
            encounter.Reset();
            Assert.That(encounter.ContractSeconds, Is.EqualTo(10f));
        }

        [Test]
        public void SemanticStrategies_ProduceExpectedExecutionKinds()
        {
            BossAttackContext context = new(
                new EntityId(2), BossPhase.PhaseThree,
                WorldPosition.Origin, new WorldPosition(8f, 0f, 0f), 10d);
            BossAttackSpec dashSpec = CreateAttack(BossAttackId.RectangleDash);
            AttackPlan dash = new RectangleDashAttack().BuildPlan(context, dashSpec);
            Assert.That(dash.Movement.Kind, Is.EqualTo(MovementPlanKind.Dash));
            Assert.That(dash.DamageWindows.Count, Is.EqualTo(1));

            BossAttackSpec barrageSpec = CreateAttack(BossAttackId.RotatingBarrage, projectileCount: 12);
            AttackPlan barrage = new RotatingBarrageAttack().BuildPlan(context, barrageSpec);
            Assert.That(barrage.Projectiles.Count, Is.EqualTo(1));
            Assert.That(barrage.Projectiles[0].Count, Is.EqualTo(12));
            Assert.That(barrage.DamageWindows.Count, Is.Zero);
        }

        private static BossSpec CreateSpec(
            int maxHealth = 100,
            int guardIntegrity = 20,
            float guardReduction = 0.5f,
            float contractSeconds = 30f,
            float contractRate = 2f)
        {
            PhaseSpecSet phases = new(new[]
            {
                new BossPhaseSpec(BossPhase.PhaseOne, 1f),
                new BossPhaseSpec(BossPhase.PhaseTwo, 0.7f),
                new BossPhaseSpec(BossPhase.PhaseThree, 0.3f)
            });
            BossAttackSpecSet attacks = new(new[]
            {
                CreateAttack(BossAttackId.GuardSweep),
                CreateAttack(BossAttackId.RotatingBarrage, projectileCount: 8),
                CreateAttack(BossAttackId.CrossSlash),
                CreateAttack(BossAttackId.ChargedSlash),
                CreateAttack(BossAttackId.RectangleDash)
            });
            return new BossSpec(
                maxHealth, phases, attacks, guardIntegrity, guardReduction,
                4f, 0.2f, contractSeconds, contractRate);
        }

        private static BossAttackSpec CreateAttack(BossAttackId id, int projectileCount = 0) =>
            new(id, 1f, 1f, 5, BossPhase.PhaseOne,
                0.5f, 0.25f, 8f, 2f, 2f,
                projectileCount, 10f, new PresentationCueId(id.ToString()));
    }
}
