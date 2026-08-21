using NUnit.Framework;
using VampireHunt.Core;
using VampireHunt.Stats;

namespace VampireHunt.Tests.Stats
{
    public sealed class StatsTests
    {
        [Test]
        public void CalculatorUsesFlatThenPercentThenMultiplicativeOrder()
        {
            StatKey key = new StatKey(1);
            EntityId source = new EntityId(10);
            StatModifierCollection collection = new StatModifierCollection();
            collection.Add(new StatModifier(key, ModifierOperation.AddFlat, 10f, source));
            collection.Add(new StatModifier(key, ModifierOperation.AddPercent, 0.2f, source));
            collection.Add(new StatModifier(key, ModifierOperation.Multiply, 1.5f, source));

            float value = new StatCalculator().Evaluate(key, 100f, collection.Snapshot());
            Assert.That(value, Is.EqualTo(198f).Within(0.0001f));
        }

        [Test]
        public void RemovingBySourceRemovesOnlyThatSource()
        {
            StatKey key = new StatKey(1);
            StatModifierCollection collection = new StatModifierCollection();
            collection.Add(new StatModifier(key, ModifierOperation.AddFlat, 5f, new EntityId(1)));
            collection.Add(new StatModifier(key, ModifierOperation.AddFlat, 7f, new EntityId(2)));

            Assert.That(collection.RemoveBySource(new EntityId(1)), Is.EqualTo(1));
            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(new StatCalculator().Evaluate(key, 10f, collection.Snapshot()), Is.EqualTo(17f));
        }

        [Test]
        public void InvalidModifierValuesAreRejected()
        {
            StatKey key = new StatKey(1);
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new StatModifier(key, ModifierOperation.Multiply, -1f, new EntityId(1)));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new StatKey(0));
        }

        [Test]
        public void ModifierHandlesRemoveOnlyTheirOwnRegistration()
        {
            StatKey key = new StatKey(1);
            EntityId source = new EntityId(7);
            StatModifier modifier = new StatModifier(
                key,
                ModifierOperation.AddFlat,
                5f,
                source);
            StatModifierCollection collection = new StatModifierCollection();

            StatModifierHandle first = collection.AddWithHandle(modifier);
            StatModifierHandle second = collection.AddWithHandle(modifier);

            Assert.That(collection.Remove(second), Is.True);
            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection.Remove(second), Is.False);
            Assert.That(collection.Remove(first), Is.True);
            Assert.That(collection.Count, Is.Zero);
        }

        [Test]
        public void SnapshotIsolatedFromLaterCollectionChanges()
        {
            StatKey key = new StatKey(1);
            EntityId source = new EntityId(8);
            StatModifierCollection collection = new StatModifierCollection();
            collection.Add(new StatModifier(key, ModifierOperation.AddFlat, 4f, source));
            StatModifierSnapshot snapshot = collection.Snapshot();

            collection.Clear();

            Assert.That(new StatCalculator().Evaluate(key, 10f, snapshot), Is.EqualTo(14f));
            Assert.That(collection.Count, Is.Zero);
        }
    }
}
