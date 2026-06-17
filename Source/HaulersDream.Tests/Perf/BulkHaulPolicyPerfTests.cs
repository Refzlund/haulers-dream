using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guards for the per-candidate bulk-haul "snowball" decision math (run for every nearby
    /// haulable a sweeping pawn considers).
    /// </summary>
    [TestFixture, Category("Perf")]
    public class BulkHaulPolicyPerfTests
    {
        private const int Level = 5;
        private const bool Strict = false;
        private const float BaseCap = 35f;

        private const float Ceiling = 68f;
        private const float CurMass = 20f;
        private const float UnitMass = 0.5f;
        private const int StackCount = 75;

        private const BulkHaulTrigger Trigger = BulkHaulTrigger.SecondTasked;
        private const bool Forced = true;
        private const bool SecondTasked = true;

        private const bool IncomingIsBulk = true;
        private const bool CurIsLoadingBulk = false;
        private const bool CurIsSoloHaulInSweep = true;

        private const int HandCap = 40;
        private const int Deliverable = 60;

        [Test]
        public void CountWithinCeiling_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => BulkHaulPolicy.CountWithinCeiling(Ceiling, CurMass, UnitMass, StackCount),
                "per-candidate snowball count must not allocate");

        [Test]
        public void CeilingKg_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => BulkHaulPolicy.CeilingKg(Level, Strict, BaseCap),
                "bulk-haul mass ceiling must not allocate");

        [Test]
        public void TriggerSatisfied_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => BulkHaulPolicy.TriggerSatisfied(Trigger, Forced, SecondTasked),
                "sweep trigger gate must not allocate");

        [Test]
        public void DecideTakeover_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => BulkHaulPolicy.DecideTakeover(Trigger, IncomingIsBulk, CurIsLoadingBulk, CurIsSoloHaulInSweep),
                "takeover decision must not allocate");

        [Test]
        public void OversizedStackWorthInventory_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => BulkHaulPolicy.OversizedStackWorthInventory(StackCount, HandCap, Deliverable),
                "oversized-stack routing gate must not allocate");
    }
}
