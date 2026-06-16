using System;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc net for <see cref="UnloadDestinationOrder.Less"/> (C1b closest-destination-first ordering).
    /// It is the running-best comparison in the unload driver's FirstUnloadableThing min-scan (re-run once per
    /// candidate per pick, per unload trip), so — like the OFF-path
    /// <see cref="SelectFirstByCategoryThenDef.LessThan"/> it delegates to — it MUST allocate nothing: int args
    /// pass by value (no boxing), the string defName is fed by reference, and it returns a bare bool.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class UnloadDestinationOrderPerfTests
    {
        // Non-boxing sink so the JIT can't elide the call (a bool field write is free; GC.KeepAlive(bool) would
        // box and read as a false allocation).
        private static bool sink;

        [Test]
        public void Less_DistanceDriven_IsZeroAlloc()
        {
            // Distinct distances: returns on the distance branch (never reaches the tiebreak).
            Action body = () => sink = UnloadDestinationOrder.Less(25, 1, "Wood", 100, 0, "Steel");
            AllocationAssert.AssertZeroAlloc(body,
                "the distance-driven comparison must not allocate (unload min-scan primitive)");
        }

        [Test]
        public void Less_TiebreakDriven_IsZeroAlloc()
        {
            // Equal distance: exercises the SelectFirstByCategoryThenDef.LessThan fallback path.
            Action body = () => sink = UnloadDestinationOrder.Less(49, 1, "Wood", 49, 1, "Steel");
            AllocationAssert.AssertZeroAlloc(body,
                "the equal-distance tiebreak fallback must not allocate");
        }

        [Test]
        public void Less_NoDestination_IsZeroAlloc()
        {
            // The sentinel branch (no resolvable destination) must also be alloc-free.
            Action body = () => sink = UnloadDestinationOrder.Less(
                UnloadDestinationOrder.NoDestination, 0, "Aaa", 10, 9, "Zzz");
            AllocationAssert.AssertZeroAlloc(body,
                "the no-destination sentinel comparison must not allocate");
        }
    }
}
