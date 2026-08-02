using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guard for the cross-pawn over-haul clamp (<see cref="DestinationEnroutePolicy"/>). It sits on the
    /// haul work-scan path — reached while a bulk plan is being priced, which happens for every candidate the
    /// scan considers — so it must stay pure branch-and-subtract. Any allocation here would trade a fixed
    /// wasted-trip bug for per-scan GC jitter, which is the trade the whole perf harness exists to prevent.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class DestinationEnroutePolicyPerfTests
    {
        [Test]
        public void FreeAfterEnroute_IsZeroAlloc_WhenSpaceRemains() =>
            AllocationAssert.AssertZeroAlloc(
                () => DestinationEnroutePolicy.FreeAfterEnroute(75, 20),
                "the enroute clamp must stay branch-only on the common path (space left after in-flight loads)");

        [Test]
        public void FreeAfterEnroute_IsZeroAlloc_WhenFullyClaimed() =>
            AllocationAssert.AssertZeroAlloc(
                () => DestinationEnroutePolicy.FreeAfterEnroute(3, 1500),
                "the stand-down path (in-flight loads already cover the space) must allocate nothing either");

        [Test]
        public void FreeAfterEnroute_IsZeroAlloc_ForAnUnboundedDestination() =>
            AllocationAssert.AssertZeroAlloc(
                () => DestinationEnroutePolicy.FreeAfterEnroute(int.MaxValue, 20),
                "the unbounded-destination short-circuit (the most common answer of all) must allocate nothing");
    }
}
