using System.Collections.Generic;
using NUnit.Framework;
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
    }
}
