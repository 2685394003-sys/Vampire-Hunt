using NUnit.Framework;
using VampireHunt.Core;

namespace VampireHunt.Tests.Core
{
    public sealed class CorePrimitiveTests
    {
        [Test]
        public void EntityAllocatorIsMonotonicAndNeverReturnsZero()
        {
            EntityIdAllocator allocator = new EntityIdAllocator(7);
            Assert.That(allocator.Allocate(), Is.EqualTo(new EntityId(7)));
            Assert.That(allocator.Next(), Is.EqualTo(new EntityId(8)));
            Assert.That(allocator.Allocate().IsValid, Is.True);
        }

        [Test]
        public void EntityZeroAndNonFinitePositionAreRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EntityId(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WorldPosition(float.NaN, 0f, 0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WorldPosition(0f, float.PositiveInfinity, 0f));
        }

        [Test]
        public void WorldPositionUsesValueEquality()
        {
            WorldPosition left = new WorldPosition(1f, 2f, 3f);
            WorldPosition right = new WorldPosition(1f, 2f, 3f);
            Assert.That(left, Is.EqualTo(right));
            Assert.That(left + new WorldPosition(1f, 0f, 0f), Is.EqualTo(new WorldPosition(2f, 2f, 3f)));
        }
    }
}
