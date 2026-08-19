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
    }
}
