using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// Perf guards for the vein flood-fill (route "Touching / vein" mode).
    ///
    /// <para><see cref="VeinFloodMath.FloodOrder"/> intrinsically allocates (it returns a fresh
    /// <c>List&lt;RouteCell&gt;</c> and builds a transient <c>HashSet</c>+<c>Queue</c> frontier), so it
    /// is pinned to a committed byte baseline × a generous tolerance — that catches an algorithmic
    /// blow-up (e.g. an accidental O(n²) frontier) while accepting the inherent BFS allocation.</para>
    ///
    /// <para><see cref="RouteCell.GetHashCode"/> / <see cref="RouteCell.Equals(RouteCell)"/> operate on
    /// struct values and are asserted 0-alloc.</para>
    ///
    /// <para><see cref="VeinFloodMath.AnyAdjacent"/> takes <c>IEnumerable&lt;RouteCell&gt;</c>; iterating
    /// any collection through that interface boxes one heap enumerator per call, so it is NOT 0-alloc —
    /// it is pinned to a small per-call baseline (see <see cref="AnyAdjacentBaselineBytes"/>) rather than
    /// asserted zero. (A genuinely 0-alloc variant would need a concrete-typed overload; out of scope
    /// for the harness.)</para>
    /// </summary>
    [TestFixture, Category("Perf")]
    public class VeinFloodPerfTests
    {
        // A fixed ~50-cell square cluster (7×7 = 49 cells) — a representative vein. Built once outside
        // the measured delegate; the seed sits at a corner.
        private static readonly HashSet<RouteCell> Cluster = BuildSquare(7);
        private static readonly RouteCell Seed = new RouteCell(0, 0);

        // FloodOrder over the 49-cell cluster: a fresh List + transient HashSet/Queue. Committed
        // baseline (bytes/op) × a generous tolerance so a frontier blow-up trips it but normal BFS
        // growth does not. Measured ~4880 B/op on net48 x64; raise only with a documented reason.
        private const long FloodOrderBaselineBytes = 4880;

        // AnyAdjacent: one boxed IEnumerable<RouteCell> enumerator per call (the interface-iteration
        // cost), no per-cell allocation. Generously bounded.
        private const long AnyAdjacentBaselineBytes = 128;

        private static readonly RouteCell[] Visible = { new RouteCell(0, 0), new RouteCell(1, 0), new RouteCell(2, 0) };
        private static readonly HashSet<RouteCell> Fogged = new HashSet<RouteCell> { new RouteCell(3, 1) };

        private static readonly RouteCell CellA = new RouteCell(12, 34);
        private static readonly RouteCell CellB = new RouteCell(12, 35);

        private static HashSet<RouteCell> BuildSquare(int n)
        {
            var set = new HashSet<RouteCell>();
            for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    set.Add(new RouteCell(x, z));
            return set;
        }

        [Test]
        public void FloodOrder_WithinBaseline()
        {
            long bytes = AllocationAssert.Allocations(() => VeinFloodMath.FloodOrder(Cluster, Seed, -1));
            TestContext.WriteLine($"FloodOrder(49-cell cluster) = {bytes} B/op (baseline {FloodOrderBaselineBytes})");
            // Generous tolerance over the committed baseline: catches an algorithmic blow-up, not normal BFS alloc.
            AllocationAssert.AssertAllocAtMost(
                () => VeinFloodMath.FloodOrder(Cluster, Seed, -1),
                FloodOrderBaselineBytes * 4,
                "flood-fill allocation regressed well past the committed BFS baseline");
        }

        [Test]
        public void RouteCell_GetHashCode_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => CellA.GetHashCode(),
                "RouteCell hashing must not allocate (HashSet/Queue hot path)");

        [Test]
        public void RouteCell_Equals_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => CellA.Equals(CellB),
                "RouteCell typed equality must not allocate (no boxing)");

        [Test]
        public void AnyAdjacent_WithinBaseline()
        {
            long bytes = AllocationAssert.Allocations(() => VeinFloodMath.AnyAdjacent(Visible, Fogged));
            TestContext.WriteLine($"AnyAdjacent = {bytes} B/op (boxed IEnumerable enumerator; baseline {AnyAdjacentBaselineBytes})");
            AllocationAssert.AssertAllocAtMost(
                () => VeinFloodMath.AnyAdjacent(Visible, Fogged),
                AnyAdjacentBaselineBytes,
                "AnyAdjacent allocates only the one boxed IEnumerable enumerator per call");
        }
    }
}
