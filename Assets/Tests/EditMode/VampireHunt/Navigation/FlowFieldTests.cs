using System.Collections.Generic;
using NUnit.Framework;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Enemies.Domain;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Navigation.Domain;

namespace VampireHunt.Tests.Modules.Navigation
{
    public sealed class FlowFieldTests
    {
        [Test]
        public void SolverRoutesAroundBlockedCellAndMarksDisconnectedRegion()
        {
            FlowGrid grid = new FlowGrid(3, 3, true);
            List<FlowCell> cells = new List<FlowCell>();
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    CellIndex index = new CellIndex(x, y);
                    cells.Add(index == new CellIndex(1, 1)
                        ? FlowCell.Blocked(index)
                        : FlowCell.Walkable(index));
                }
            }

            FlowField field = new FlowFieldSolver().Solve(new FlowGrid(3, 3, cells), new CellIndex(2, 2));

            Assert.That(field.CanReach(new CellIndex(0, 0)), Is.True);
            Assert.That(field.GetDirection(new CellIndex(1, 1)), Is.EqualTo(Direction.None));
            Assert.That(field.GetDirection(new CellIndex(2, 2)), Is.EqualTo(Direction.None));
        }

        [Test]
        public void CacheSeparatesTargetsAndObstacleRevisions()
        {
            FlowGrid grid = new FlowGrid(2, 2, true);
            FlowFieldCache cache = new FlowFieldCache(grid, capacity: 4);

            FlowField first = cache.GetOrBuild(new CellIndex(0, 0), 1);
            FlowField second = cache.GetOrBuild(new CellIndex(0, 0), 1);
            Assert.That(second, Is.SameAs(first));

            cache.Invalidate(2);
            FlowField rebuilt = cache.GetOrBuild(new CellIndex(0, 0), 2);
            Assert.That(rebuilt, Is.Not.SameAs(first));
        }

        [Test]
        public void EnemyRecovery_SamplesFromCurrentPositionTowardTheRecoveryCell()
        {
            WorldPosition current = WorldPosition.Origin;
            WorldPosition recovery = new(1f, 0f, 0f);
            WorldPosition target = new(0f, 0f, 5f);
            RecordingRecoveryField navigation = new(current, recovery);
            EnemyBrain brain = new(navigation, attackRange: 1f, moveSpeed: 2f);
            EnemyPerception perception = new(
                new EntityId(1),
                current,
                new EntityId(2),
                target,
                distance: 5f,
                hasLineOfTravel: false,
                targetIsAlive: true);

            EnemyIntent intent = brain.Decide(perception);

            Assert.That(intent.State, Is.EqualTo(EnemyState.Recovering));
            Assert.That(navigation.LastSampleFrom, Is.EqualTo(current),
                "Recovery must start at the entity's current blocked position.");
            Assert.That(navigation.LastSampleTarget, Is.EqualTo(recovery),
                "Recovery must first route to the nearest walkable position, not to the combat target.");
        }

        private sealed class RecordingRecoveryField : INavigationField
        {
            private readonly WorldPosition blocked;
            private readonly WorldPosition recovery;

            public RecordingRecoveryField(WorldPosition blocked, WorldPosition recovery)
            {
                this.blocked = blocked;
                this.recovery = recovery;
            }

            public WorldPosition LastSampleFrom { get; private set; }
            public WorldPosition LastSampleTarget { get; private set; }

            public Direction SampleDirection(WorldPosition position, WorldPosition target)
            {
                LastSampleFrom = position;
                LastSampleTarget = target;
                return Direction.Right;
            }

            public bool IsWalkable(WorldPosition position) => position != blocked;

            public WorldPosition TryFindRecovery(WorldPosition position) => recovery;
        }
    }
}
