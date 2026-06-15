using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guards for the per-candidate transporter/portal/vehicle load sweep clamps.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class TransportLoadPlanPerfTests
    {
        private const int StackInHand = 75;
        private const int ManifestRemaining = 120;
        private const int LedgerAvailable = 90;
        private const int CarryAffordable = 60;

        private const float PawnFreeSpace = 15f;
        private const float GroupMassCap = 150f;
        private const float GroupMassUsage = 40f;
        private const bool HasMassCap = true;

        private const float MassBudget = 15f;
        private const float UnitMass = 0.5f;
        private const int Offered = 75;

        [Test]
        public void DeliverableUnits_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => TransportLoadPlan.DeliverableUnits(StackInHand, ManifestRemaining, LedgerAvailable, CarryAffordable),
                "per-candidate deliverable clamp must not allocate");

        [Test]
        public void TripMassBudget_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => TransportLoadPlan.TripMassBudget(PawnFreeSpace, GroupMassCap, GroupMassUsage, HasMassCap),
                "per-trip mass-budget math must not allocate");

        [Test]
        public void UnitsWithinMassBudget_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => TransportLoadPlan.UnitsWithinMassBudget(MassBudget, UnitMass, Offered),
                "mass-budget unit clamp must not allocate");
    }
}
