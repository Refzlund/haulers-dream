using HaulersDream.Core;
using NUnit.Framework;
using static HaulersDream.Core.EnRoutePickupPolicy;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guards for the en-route pickup cascade (<see cref="EnRoutePickupPolicy"/>) and the en-route
    /// mutex (<see cref="EnRouteMutexPolicy"/>). The Verse layer calls these per candidate per job-start
    /// (potentially every haulable on the map, once per pawn that's about to start a job), so they must be
    /// branch-only — any per-call allocation would trade WYU's cheap-to-expensive short-circuit saving for
    /// GC jitter. <see cref="MaxRanges"/> is a value type passed by <c>in</c>, so the whole cascade stays
    /// on the stack with no boxing.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class EnRoutePickupPolicyPerfTests
    {
        // A representative on-path band + legs (the common "is this worth grabbing?" call shape).
        static MaxRanges Band()
        {
            var r = new MaxRanges();
            r.Reset();
            return r;
        }

        [Test]
        public void CheckBeforeStore_IsZeroAlloc()
        {
            var r = Band();
            AllocationAssert.AssertZeroAlloc(
                () => CheckBeforeStore(10f, 40f, 35f, r),
                "the phase-1 en-route gate must stay branch-only (it runs per candidate before the store search)");
        }

        [Test]
        public void CheckAfterStore_IsZeroAlloc()
        {
            var r = Band();
            AllocationAssert.AssertZeroAlloc(
                () => CheckAfterStore(10f, 40f, 8f, 20f, r),
                "the phase-2 en-route gate must stay branch-only (it runs per candidate after the store search)");
        }

        [Test]
        public void WithinPathLegBounds_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => WithinPathLegBounds(30f, 20f, 60f, 100f),
                "the path-cost leg bounds must stay branch-only (run per candidate in Default/Pathfinding mode)");

        [Test]
        public void Midway_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => Midway(2, 0, 4, 9, 7, 11, out _, out _, out _),
                "the midway cell computation must be allocation-free (run per candidate store search)");

        [Test]
        public void MidwayDistanceSquared_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => MidwayDistanceSquared(8, 9, 5, 5),
                "the midway ranking key must be allocation-free (run per candidate store cell)");

        [Test]
        public void MidwayBetter_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => MidwayBetter(10, 20),
                "the midway ranking comparison must be allocation-free (run per candidate store cell)");

        [Test]
        public void Expand_IsZeroAlloc()
        {
            var r = Band();
            AllocationAssert.AssertZeroAlloc(
                () => r.Expand(),
                "expanding the range band (per fruitless pass) must be allocation-free");
        }

        [Test]
        public void MutexMustStandDown_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => EnRouteMutexPolicy.MustStandDown(true, 0.3f, true, true),
                "the en-route/unload mutex must stay branch-only (it runs before each en-route grab)");
    }
}
