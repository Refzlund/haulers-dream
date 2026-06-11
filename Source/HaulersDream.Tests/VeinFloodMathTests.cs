using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class VeinFloodMathTests
    {
        private static HashSet<RouteCell> Cells(params (int x, int z)[] cs)
            => new HashSet<RouteCell>(cs.Select(c => new RouteCell(c.x, c.z)));

        [Test]
        public void SeedNotInMembers_ReturnsEmpty()
        {
            var members = Cells((0, 0), (1, 0));
            Assert.That(VeinFloodMath.FloodOrder(members, new RouteCell(5, 5), 10), Is.Empty);
        }

        [Test]
        public void CapZero_ReturnsEmpty()
        {
            var members = Cells((0, 0), (1, 0));
            Assert.That(VeinFloodMath.FloodOrder(members, new RouteCell(0, 0), 0), Is.Empty);
        }

        [Test]
        public void WholeContiguousCluster_WhenUnbounded()
        {
            // an L-shaped vein of 5 cells.
            var members = Cells((0, 0), (1, 0), (2, 0), (2, 1), (2, 2));
            var flood = VeinFloodMath.FloodOrder(members, new RouteCell(0, 0), cap: -1);
            Assert.That(flood.Count, Is.EqualTo(5));
            Assert.That(new HashSet<RouteCell>(flood), Is.EqualTo(members));
            Assert.That(flood[0], Is.EqualTo(new RouteCell(0, 0)), "seed comes first");
        }

        [Test]
        public void StopsAtDisconnectedGap()
        {
            // two cells touching the seed, then a GAP at x=3, then an island at x=5,6 (unreachable).
            var members = Cells((0, 0), (1, 0), (2, 0), (5, 0), (6, 0));
            var flood = VeinFloodMath.FloodOrder(members, new RouteCell(0, 0), cap: -1);
            Assert.That(new HashSet<RouteCell>(flood), Is.EqualTo(Cells((0, 0), (1, 0), (2, 0))));
            Assert.That(flood, Does.Not.Contain(new RouteCell(5, 0)));
        }

        [Test]
        public void DiagonalsAreContiguous_EightWay()
        {
            // a pure diagonal staircase is connected only under 8-way adjacency.
            var members = Cells((0, 0), (1, 1), (2, 2), (3, 3));
            var flood = VeinFloodMath.FloodOrder(members, new RouteCell(0, 0), cap: -1);
            Assert.That(flood.Count, Is.EqualTo(4));
        }

        [Test]
        public void CapTakesNearestByHops_BreadthFirst()
        {
            // a straight line of 10 cells from the seed; cap 4 → seed + the 3 nearest.
            var members = Cells((0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (6, 0), (7, 0), (8, 0), (9, 0));
            var flood = VeinFloodMath.FloodOrder(members, new RouteCell(0, 0), cap: 4);
            Assert.That(flood, Is.EqualTo(new[]
            {
                new RouteCell(0, 0), new RouteCell(1, 0), new RouteCell(2, 0), new RouteCell(3, 0)
            }));
        }

        [Test]
        public void CapLargerThanCluster_ReturnsWholeCluster()
        {
            var members = Cells((0, 0), (1, 0), (1, 1));
            var flood = VeinFloodMath.FloodOrder(members, new RouteCell(0, 0), cap: 99);
            Assert.That(flood.Count, Is.EqualTo(3));
        }

        [Test]
        public void NullMembers_ReturnsEmpty()
        {
            Assert.That(VeinFloodMath.FloodOrder(null, new RouteCell(0, 0), 10), Is.Empty);
        }

        [Test]
        public void AnyAdjacent_DetectsFoggedNeighbour_EightWay()
        {
            // The visible cluster ends at (2,0); a fogged same-kind cell sits diagonally at (3,1) → caution.
            var visible = new[] { new RouteCell(0, 0), new RouteCell(1, 0), new RouteCell(2, 0) };
            var fogged = Cells((3, 1));
            Assert.That(VeinFloodMath.AnyAdjacent(visible, fogged), Is.True);
        }

        [Test]
        public void AnyAdjacent_NoTouch_WhenFoggedIsTwoCellsAway()
        {
            var visible = new[] { new RouteCell(0, 0), new RouteCell(1, 0) };
            var fogged = Cells((3, 0)); // a one-cell gap → not 8-adjacent to any visible cell
            Assert.That(VeinFloodMath.AnyAdjacent(visible, fogged), Is.False);
        }

        [Test]
        public void AnyAdjacent_EmptyOrNull_IsFalse()
        {
            var visible = new[] { new RouteCell(0, 0) };
            Assert.That(VeinFloodMath.AnyAdjacent(visible, new HashSet<RouteCell>()), Is.False);
            Assert.That(VeinFloodMath.AnyAdjacent(visible, null), Is.False);
            Assert.That(VeinFloodMath.AnyAdjacent(null, Cells((0, 0))), Is.False);
        }

        [Test]
        public void FloodOrder_IsPrefixStable_AcrossCaps()
        {
            // The vein selection feeds the route's budget chain; raising the "amount" must only EXTEND the flood,
            // never reorder earlier cells — otherwise the max-travel truncation would shrink when you add stops.
            var members = Cells((0, 0), (1, 0), (2, 0), (1, 1), (2, 1), (3, 1), (2, 2), (3, 2), (4, 2), (4, 3), (5, 3), (5, 4));
            var seed = new RouteCell(0, 0);
            var small = VeinFloodMath.FloodOrder(members, seed, 5);
            var large = VeinFloodMath.FloodOrder(members, seed, 9);
            Assert.That(large.Count, Is.GreaterThan(small.Count));
            for (int i = 0; i < small.Count; i++)
                Assert.That(large[i], Is.EqualTo(small[i]), $"flood reordered at index {i} when the cap grew");
        }
    }
}
