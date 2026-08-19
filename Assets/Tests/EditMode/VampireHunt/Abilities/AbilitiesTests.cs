using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Abilities.Contracts;
using VampireHunt.Abilities.Domain;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Combat.Domain;
using VampireHunt.Core;
using VampireHunt.Stats;

namespace VampireHunt.Tests.Abilities
{
    public sealed class AbilitiesTests
    {
        [Test]
        public void StackedPeriodicEffectTicksAndRemovesItsModifier()
        {
            EntityId source = new EntityId(1);
            EntityId target = new EntityId(2);
            TestVitals vitals = new TestVitals(100);
            RecordingSink combatEvents = new RecordingSink();
            CombatApplicationService combat = new CombatApplicationService(
                new CombatResolver(new FixedRandom(0f)), new TestDirectory(vitals), combatEvents);
            TestAttributes attributes = new TestAttributes();
            RecordingSink cueEvents = new RecordingSink();
            GameplayEffectExecutor executor = new GameplayEffectExecutor(combat, attributes, cueEvents);
            GameplayAbilitySystem system = new GameplayAbilitySystem(target, executor);
            GameplayEffectSpec spec = new GameplayEffectSpec(
                new EffectId("burn"),
                DurationPolicy.Duration,
                StackingPolicy.AddStacks,
                durationSeconds: 2f,
                periodSeconds: 1f,
                maxStacks: 2,
                executeOnApplication: false,
                modifiers: new[] { new GameplayModifierSpec(new StatKey(1), ModifierOperation.AddFlat, 3f) },
                executions: new[] { GameplayEffectExecution.Damage(2f) },
                cue: new CueId("burn"));

            Assert.That(system.Apply(spec, source), Is.True);
            Assert.That(system.Apply(spec, source), Is.True);
            Assert.That(system.ActiveEffectCount, Is.EqualTo(1));
            Assert.That(attributes.Modifiers, Has.Count.EqualTo(1));

            system.Tick(1f);
            Assert.That(combatEvents.Events, Has.Count.EqualTo(1));
            Assert.That(((DamageConfirmedEvent)combatEvents.Events[0]).Flags, Is.EqualTo(DamageFlags.NoCritical | DamageFlags.Periodic));
            system.Tick(1f);
            Assert.That(system.ActiveEffectCount, Is.EqualTo(0));
            Assert.That(attributes.RemovedSources, Has.Member(source));
        }

        [Test]
        public void RemovingOneEffect_PreservesOtherModifiersFromTheSameSource()
        {
            EntityId source = new(1);
            EntityId target = new(2);
            TestAttributes attributes = new();
            GameplayEffectExecutor executor = CreateExecutor(attributes);
            GameplayAbilitySystem system = new(target, executor);
            GameplayEffectSpec speed = PersistentModifier(
                "speed",
                new StatKey(1),
                3f);
            GameplayEffectSpec power = PersistentModifier(
                "power",
                new StatKey(2),
                5f);

            Assert.That(system.Apply(speed, source), Is.True);
            Assert.That(system.Apply(power, source), Is.True);
            Assert.That(attributes.Modifiers, Has.Count.EqualTo(2));

            Assert.That(system.Remove(speed.Id, source), Is.True);

            Assert.That(attributes.Modifiers, Has.Count.EqualTo(1),
                "Removing one effect must not delete sibling modifiers that share its source entity.");
            Assert.That(attributes.Modifiers[0].Stat, Is.EqualTo(new StatKey(2)));
        }

        [Test]
        public void AttributeExecution_IsRemovedWhenItsOwningEffectExpires()
        {
            EntityId source = new(1);
            EntityId target = new(2);
            TestAttributes attributes = new();
            GameplayEffectExecutor executor = CreateExecutor(attributes);
            GameplayAbilitySystem system = new(target, executor);
            GameplayEffectSpec temporaryAttribute = new(
                new EffectId("temporary-attribute"),
                DurationPolicy.Duration,
                StackingPolicy.RefreshDuration,
                durationSeconds: 1f,
                executeOnApplication: true,
                executions: new[]
                {
                    GameplayEffectExecution.Attribute(
                        new GameplayModifierSpec(new StatKey(3), ModifierOperation.AddFlat, 4f))
                });

            Assert.That(system.Apply(temporaryAttribute, source), Is.True);
            Assert.That(attributes.Modifiers, Has.Count.EqualTo(1));

            system.Tick(1f);

            Assert.That(system.ActiveEffectCount, Is.Zero);
            Assert.That(attributes.Modifiers, Is.Empty,
                "Attribute executions must be owned and removed by the effect that created them.");
        }

        private static GameplayEffectExecutor CreateExecutor(TestAttributes attributes)
        {
            TestVitals vitals = new(100);
            CombatApplicationService combat = new(
                new CombatResolver(new FixedRandom(0f)),
                new TestDirectory(vitals));
            return new GameplayEffectExecutor(combat, attributes);
        }

        private static GameplayEffectSpec PersistentModifier(
            string id,
            StatKey stat,
            float magnitude)
        {
            return new GameplayEffectSpec(
                new EffectId(id),
                DurationPolicy.Infinite,
                StackingPolicy.RefreshDuration,
                executeOnApplication: false,
                modifiers: new[]
                {
                    new GameplayModifierSpec(stat, ModifierOperation.AddFlat, magnitude)
                });
        }

        private sealed class FixedRandom : IRandomSource
        {
            private readonly float value;
            public FixedRandom(float value) { this.value = value; }
            public float NextFloat() => value;
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        }

        private sealed class TestVitals : IDamageReceiver, IHealingReceiver
        {
            private int health;
            public TestVitals(int health) { this.health = health; }
            public DamageResult ApplyDamage(in ResolvedDamage damage)
            {
                int applied = System.Math.Min(health, damage.FinalDamage);
                health -= applied;
                return new DamageResult(damage.FinalDamage, applied, damage.WasCritical, health == 0, damage.Hit.Position);
            }
            public int ApplyHealing(int amount) { health += amount; return amount; }
        }

        private sealed class TestDirectory : ICombatEntityDirectory
        {
            private readonly TestVitals vitals;
            public TestDirectory(TestVitals vitals) { this.vitals = vitals; }
            public IDamageReceiver TryGetDamageReceiver(EntityId id) => vitals;
            public IHealingReceiver TryGetHealingReceiver(EntityId id) => vitals;
            public IKnockbackReceiver TryGetKnockbackReceiver(EntityId id) => null;
        }

        private sealed class TestAttributes : IAttributeModifierTarget
        {
            public readonly List<StatModifier> Modifiers = new List<StatModifier>();
            public readonly List<EntityId> RemovedSources = new List<EntityId>();
            public void AddModifier(StatModifier modifier) => Modifiers.Add(modifier);
            public int RemoveModifiers(EntityId sourceId)
            {
                int count = Modifiers.RemoveAll(modifier => modifier.SourceId == sourceId);
                RemovedSources.Add(sourceId);
                return count;
            }
        }

        private sealed class RecordingSink : IGameplayEventSink
        {
            public readonly List<IGameplayEvent> Events = new List<IGameplayEvent>();
            public void Publish(IGameplayEvent @event) => Events.Add(@event);
        }
    }
}
