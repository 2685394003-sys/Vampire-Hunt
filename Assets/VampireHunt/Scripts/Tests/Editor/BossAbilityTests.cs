using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VampireHunt.Boss.Abilities;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Tests.Editor
{
    public sealed class BossAbilityTests
    {
        [Test]
        public void Controller_AdvancesThroughServerTimeline()
        {
            var controller = new BossAbilityController();
            BossAbilityDefinition ability = CreateAbility(10, 0.5, 0.25, 0.75);
            controller.LoadPhase(new BossPhaseDefinition(
                1,
                "Phase 1",
                new[] { new BossPhaseAbilityEntry(ability, 1f, 0, 0d) }), 10d);

            Float3 zero = Float3.Zero;
            var selection = new BossAbilitySelectionContext(5f, 1f, true);
            controller.Tick(10d, selection, 0, zero, new Float3(0, 0, 1), 1, true);
            Assert.That(controller.CaptureSnapshot().CastPhase, Is.EqualTo(BossAbilityCastPhase.Telegraph));

            controller.Tick(10.5d, selection, 0, zero, zero, 1, false);
            Assert.That(controller.CaptureSnapshot().CastPhase, Is.EqualTo(BossAbilityCastPhase.Resolve));

            controller.Tick(10.75d, selection, 0, zero, zero, 1, false);
            Assert.That(controller.CaptureSnapshot().CastPhase, Is.EqualTo(BossAbilityCastPhase.Recover));

            controller.Tick(11.5d, selection, 0, zero, zero, 1, false);
            Assert.That(controller.CaptureSnapshot().IsCasting, Is.False);
        }

        [Test]
        public void Scheduler_RespectsOneShotAbilities()
        {
            var controller = new BossAbilityController();
            BossAbilityDefinition ability = CreateAbility(20, 0d, 0.01d, 0d, oneShot: true);
            controller.LoadPhase(new BossPhaseDefinition(
                1,
                "Phase 1",
                new[] { new BossPhaseAbilityEntry(ability, 1f, 0, 0d) }), 0d);

            Float3 zero = Float3.Zero;
            var selection = new BossAbilitySelectionContext(5f, 1f, true);
            controller.Tick(0d, selection, 0, zero, zero, 1, true);
            controller.Tick(1d, selection, 0, zero, zero, 1, true);
            controller.Tick(2d, selection, 0, zero, zero, 1, true);

            BossAbilitySnapshot snapshot = controller.CaptureSnapshot();
            Assert.That(snapshot.IsCasting, Is.False);
            Assert.That(snapshot.CastSequence, Is.EqualTo(1));
        }

        [Test]
        public void Scheduler_RejectsOutOfRangeAbility()
        {
            var controller = new BossAbilityController();
            BossAbilityDefinition ability = CreateAbility(30, 0.1, 0.1, 0.1, minDistance: 3f, maxDistance: 6f);
            controller.LoadPhase(new BossPhaseDefinition(
                1,
                "Phase 1",
                new[] { new BossPhaseAbilityEntry(ability, 1f, 0, 0d) }), 0d);

            Float3 zero = Float3.Zero;
            controller.Tick(0d, new BossAbilitySelectionContext(9f, 1f, true), 0, zero, zero, 1, true);

            Assert.That(controller.CaptureSnapshot().IsCasting, Is.False);
        }

        [Test]
        public void Controller_BindsServicesBeforeLogicStarts()
        {
            var services = new BossAbilityServices(null, null, null, null, null, null, null);
            var logic = new ServiceAwareLogic();
            BossAbilityDefinition ability = new BossAbilityDefinition(
                40,
                "Service Aware",
                1f,
                0d,
                0f,
                100f,
                0f,
                1f,
                false,
                false,
                0.1d,
                0.1d,
                0.1d,
                () => logic);
            var controller = new BossAbilityController(services);
            controller.LoadPhase(new BossPhaseDefinition(
                1,
                "Phase 1",
                new[] { new BossPhaseAbilityEntry(ability, 1f, 0, 0d) }), 0d);

            controller.Tick(
                0d,
                new BossAbilitySelectionContext(1f, 1f, true),
                1,
                Float3.Zero,
                Float3.Up,
                1,
                true);

            Assert.That(logic.Services, Is.SameAs(services));
            Assert.That(logic.WasBoundWhenStarted, Is.True);
        }

        [Test]
        public void BossContentAssets_HaveLoadableMonoScripts()
        {
            AssertHasLoadableMonoScript<BossAbilityAsset>();
            AssertHasLoadableMonoScript<BossPhaseAsset>();
            AssertHasLoadableMonoScript<BossPhaseSetAsset>();
        }

        private static void AssertHasLoadableMonoScript<T>() where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            try
            {
                MonoScript script = MonoScript.FromScriptableObject(instance);
                Assert.That(script, Is.Not.Null, $"{typeof(T).Name} has no MonoScript asset.");
                Assert.That(script.GetClass(), Is.EqualTo(typeof(T)));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static BossAbilityDefinition CreateAbility(
            uint id,
            double telegraph,
            double resolve,
            double recover,
            bool oneShot = false,
            float minDistance = 0f,
            float maxDistance = 100f)
        {
            return new BossAbilityDefinition(
                id,
                $"Ability {id}",
                1f,
                0d,
                minDistance,
                maxDistance,
                0f,
                1f,
                false,
                oneShot,
                telegraph,
                resolve,
                recover,
                () => new TestNoOpLogic());
        }

        private sealed class TestNoOpLogic : IBossAbilityLogicRuntime
        {
            public void OnCastStarted(in BossAbilityCastContext context) { }
            public void OnPhaseEntered(BossAbilityCastPhase phase, double serverTime) { }
            public void Tick(double serverTime) { }
            public void Cancel(double serverTime) { }
            public void Dispose() { }
        }

        private sealed class ServiceAwareLogic : IBossAbilityLogicRuntime, IBossAbilityServiceConsumer
        {
            public BossAbilityServices Services { get; private set; }
            public bool WasBoundWhenStarted { get; private set; }

            public void BindServices(BossAbilityServices services) => Services = services;
            public void OnCastStarted(in BossAbilityCastContext context) =>
                WasBoundWhenStarted = Services != null;
            public void OnPhaseEntered(BossAbilityCastPhase phase, double serverTime) { }
            public void Tick(double serverTime) { }
            public void Cancel(double serverTime) { }
            public void Dispose() { }
        }
    }
}
