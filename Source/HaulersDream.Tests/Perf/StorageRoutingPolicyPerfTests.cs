using HaulersDream.Core;
using NUnit.Framework;
using static HaulersDream.Core.StorageRoutingPolicy;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guards for the storage-routing relocation core (<see cref="StorageRoutingPolicy"/>). The Verse
    /// layer calls these per candidate slot group / per candidate store cell during a routing scan (which can
    /// run over many slot groups per relocated stack), so they must be branch-only — any per-call allocation
    /// would trade the routing win for GC jitter. Every method is primitives in / bool out, so the whole
    /// decision stays on the stack with no boxing.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class StorageRoutingPolicyPerfTests
    {
        [Test]
        public void PriorityEligibleForRoute_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => PriorityEligibleForRoute(3, 2, beforeCarryActive: true, allowEqualPriority: true,
                    isOwnGroup: false),
                "the routing priority-eligibility gate must stay branch-only (it runs per candidate slot group)");

        [Test]
        public void WorthRelocating_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => WorthRelocating(100, 49),
                "the relocation worthwhileness gate must stay branch-only (it runs per relocation candidate)");

        [Test]
        public void CompareByDestinationDistance_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => CompareByDestinationDistance(10, 20),
                "the destination-distance ranking must be allocation-free (it runs per candidate store cell)");

        [Test]
        public void MidwayBetter_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => MidwayBetter(4, 9),
                "the midway ranking (delegated to the shared en-route helper) must be allocation-free");
    }
}
