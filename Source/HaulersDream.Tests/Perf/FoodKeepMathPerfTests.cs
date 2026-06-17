using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// Correctness + allocation net for the closed-form packable-food keep math (HD-GIZMO step 3): the per-frame
    /// inspect-pane surplus scan reaches <c>InventorySurplus.FoodKeepCountOf</c>, whose old incremental
    /// <c>k</c>-loop is replaced by <see cref="FoodKeepMath.KeepCount"/>. This fixture proves:
    ///   1. ORACLE — the closed form returns the EXACT same value as the old loop for every input on a randomized
    ///      grid (so the O(1) substitution changes no behaviour).
    ///   2. 0-alloc — the pure leaf allocates nothing (it runs inside the surplus scan per stack).
    ///
    /// Measurement note: <see cref="GC.GetAllocatedBytesForCurrentThread"/> (net48, .NET FW 4.6+) is the per-thread
    /// jitter proxy; the body must be a pre-built delegate on one thread. See <see cref="AllocationAssert"/>.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class FoodKeepMathPerfTests
    {
        /// <summary>The original incremental loop, kept here verbatim as the ORACLE the closed form must match.</summary>
        private static int LoopKeepCount(float total, float maxLevel, float perUnit, int stackCount)
        {
            if (perUnit <= 0f || total - perUnit * stackCount > maxLevel)
                return 0;
            int k = 0;
            while (total - perUnit * k > maxLevel)
                k++;
            return stackCount - k;
        }

        private static IEnumerable<TestCaseData> Grid()
        {
            // A deterministic seed so the grid is reproducible. Span the regimes: whole stack surplus (over cap
            // even empty), whole stack kept (already under cap), and partial keeps where k lands mid-stack — plus
            // edge perUnit/maxLevel/stackCount values.
            var rng = new Random(20260615);
            int n = 0;
            foreach (var stackCount in new[] { 1, 2, 5, 10, 37, 75, 200 })
            foreach (var perUnit in new[] { 0f, 0.01f, 0.05f, 0.25f, 0.9f, 1f, 3.3f })
            foreach (var maxLevel in new[] { 0f, 0.5f, 1f, 1.6f, 4f, 9f })
            {
                // total spans below, at, and well above maxLevel + the whole-stack nutrition, with jitter.
                float span = maxLevel + perUnit * stackCount + 2f;
                for (int s = 0; s < 4; s++)
                {
                    float total = (float)(rng.NextDouble() * span * 1.2);
                    yield return new TestCaseData(total, maxLevel, perUnit, stackCount)
                        .SetName($"keep#{n++} total={total:0.###} max={maxLevel} per={perUnit} n={stackCount}");
                }
            }
        }

        [TestCaseSource(nameof(Grid))]
        public void KeepCount_MatchesOriginalLoop(float total, float maxLevel, float perUnit, int stackCount)
        {
            int closed = FoodKeepMath.KeepCount(total, maxLevel, perUnit, stackCount);
            int loop = LoopKeepCount(total, maxLevel, perUnit, stackCount);
            Assert.That(closed, Is.EqualTo(loop),
                $"closed form must equal the incremental loop (total={total} max={maxLevel} per={perUnit} n={stackCount})");
            // Sanity on the contract: result is always a valid keep count in [0, stackCount].
            Assert.That(closed, Is.InRange(0, Math.Max(0, stackCount)));
        }

        [Test]
        public void KeepCount_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                // A representative partial-keep case (total above cap by a few units).
                () => FoodKeepMath.KeepCount(3.0f, 1.6f, 0.25f, 37),
                "packable-food keep math must not allocate (runs per stack in the surplus scan)");
    }
}
