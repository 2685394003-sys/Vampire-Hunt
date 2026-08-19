using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Combat.Domain;
using VampireHunt.Core;
using VampireHunt.Stats;

namespace VampireHunt.Tests.Combat
{
    public sealed class CombatTests
    {
        [Test]
        public void PeriodicDamageCannotCritAndServicePublishesDeathChain()
        {
            EntityId source = new EntityId(1);
            EntityId target = new EntityId(2);
            TestVitals vitals = new TestVitals(5);
            TestDirectory directory = new TestDirectory(vitals);
            RecordingSink sink = new RecordingSink();
            CombatApplicationService service = new CombatApplicationService(
                new CombatResolver(new FixedRandom(0f)), directory, sink);
            TestStats stats = new TestStats();
            stats.Set(CombatStatKeys.CriticalChance, 1f);
            stats.Set(CombatStatKeys.CriticalMultiplier, 2f);

            DamageResult result = service.ApplyDamage(new DamageRequest(
                source, target, 10, DamageFlags.NoCritical | DamageFlags.Periodic), stats);

            Assert.That(result.AppliedDamage, Is.EqualTo(5));
            Assert.That(result.WasCritical, Is.False);
            Assert.That(result.WasKilled, Is.True);
            Assert.That(sink.Events, Has.Count.EqualTo(2));
            Assert.That(((DamageConfirmedEvent)sink.Events[0]).Flags, Is.EqualTo(DamageFlags.NoCritical | DamageFlags.Periodic));
            Assert.That(sink.Events[1], Is.TypeOf<EntityDiedEvent>());

            // A subsequent blocked/already-dead hit is a zero-result resolution,
            // not another combat-text or death event.
            service.ApplyDamage(new DamageRequest(source, target, 10), stats);
            Assert.That(sink.Events, Has.Count.EqualTo(2));
        }

        [Test]
        public void KnockbackResolverHonorsResistanceAndImmunity()
        {
            KnockbackRequest request = new KnockbackRequest(
                new EntityId(1), new EntityId(2), new WorldPosition(3f, 0f, 0f), 10f, 0.25f);
            KnockbackImpulse impulse = new KnockbackResolver().Resolve(request, 0.25f);
            Assert.That(impulse.Direction, Is.EqualTo(new WorldPosition(1f, 0f, 0f)));
            Assert.That(impulse.Force, Is.EqualTo(7.5f).Within(0.0001f));
            Assert.That(new KnockbackResolver().Resolve(request, 0f, true).Force, Is.EqualTo(0f));
        }

        private sealed class FixedRandom : IRandomSource
        {
            private readonly float value;
            public FixedRandom(float value) { this.value = value; }
            public float NextFloat() => value;
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        }

        private sealed class TestStats : IStatSnapshot
        {
            private readonly Dictionary<StatKey, float> values = new Dictionary<StatKey, float>();
            public void Set(StatKey key, float value) => values[key] = value;
            public float GetValue(StatKey key) => values.TryGetValue(key, out float value) ? value : 0f;
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

        private sealed class RecordingSink : IGameplayEventSink
        {
            public readonly List<IGameplayEvent> Events = new List<IGameplayEvent>();
            public void Publish(IGameplayEvent @event) => Events.Add(@event);
        }
    }
}
