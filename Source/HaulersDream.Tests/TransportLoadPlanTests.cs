using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class TransportLoadPlanTests
    {
        // --- DeliverableUnits: each min term wins in isolation ---

        [Test]
        public void Deliverable_StackInHandWins()
            => Assert.That(TransportLoadPlan.DeliverableUnits(5, 100, 100, 100), Is.EqualTo(5));

        [Test]
        public void Deliverable_ManifestRemainingWins()
            => Assert.That(TransportLoadPlan.DeliverableUnits(100, 7, 100, 100), Is.EqualTo(7));

        [Test]
        public void Deliverable_LedgerAvailableWins()
            => Assert.That(TransportLoadPlan.DeliverableUnits(100, 100, 9, 100), Is.EqualTo(9));

        [Test]
        public void Deliverable_CarryAffordableWins()
            => Assert.That(TransportLoadPlan.DeliverableUnits(100, 100, 100, 3), Is.EqualTo(3));

        [Test]
        public void Deliverable_NeverNegative()
        {
            // A negative term (e.g. an over-full ledger) clamps the whole result to 0.
            Assert.That(TransportLoadPlan.DeliverableUnits(10, 10, -4, 10), Is.EqualTo(0));
            Assert.That(TransportLoadPlan.DeliverableUnits(0, 0, 0, 0), Is.EqualTo(0));
        }

        // --- TripMassBudget ---

        [Test]
        public void Budget_TransporterTakesGroupHeadroomWhenTighter()
        {
            // pawn has 50 kg free, group has 30 kg headroom (cap 100 − usage 70) → 30.
            Assert.That(TransportLoadPlan.TripMassBudget(50f, 100f, 70f, hasMassCap: true), Is.EqualTo(30f));
        }

        [Test]
        public void Budget_TransporterTakesPawnFreeSpaceWhenTighter()
        {
            // pawn has 20 kg free, group headroom 80 → 20.
            Assert.That(TransportLoadPlan.TripMassBudget(20f, 100f, 20f, hasMassCap: true), Is.EqualTo(20f));
        }

        [Test]
        public void Budget_PortalIgnoresGroupCap()
        {
            // hasMassCap=false → the group terms (here a meaningless cap/usage) are ignored; pawn free space only.
            Assert.That(TransportLoadPlan.TripMassBudget(42f, 0f, 9999f, hasMassCap: false), Is.EqualTo(42f));
        }

        [Test]
        public void Budget_NegativeGroupHeadroomClampsToZero()
        {
            // Group already over capacity (usage 120 > cap 100) → headroom −20 → budget 0.
            Assert.That(TransportLoadPlan.TripMassBudget(50f, 100f, 120f, hasMassCap: true), Is.EqualTo(0f));
        }

        [Test]
        public void Budget_NegativePawnFreeSpaceClampsToZero()
        {
            // Over-encumbered pawn (negative free space) → 0, even for a portal.
            Assert.That(TransportLoadPlan.TripMassBudget(-5f, 100f, 0f, hasMassCap: false), Is.EqualTo(0f));
        }

        // --- UnitsWithinMassBudget (mass clamp edges) ---

        [Test]
        public void Units_MasslessTakenInFull()
            => Assert.That(TransportLoadPlan.UnitsWithinMassBudget(0f, 0f, 12), Is.EqualTo(12));

        [Test]
        public void Units_RoundsDown()
            => Assert.That(TransportLoadPlan.UnitsWithinMassBudget(2.9f, 1f, 50), Is.EqualTo(2));

        [Test]
        public void Units_ZeroBudgetTakesNone()
            => Assert.That(TransportLoadPlan.UnitsWithinMassBudget(0f, 1f, 50), Is.EqualTo(0));

        [Test]
        public void Units_ClampsToOffered()
            => Assert.That(TransportLoadPlan.UnitsWithinMassBudget(1000f, 1f, 5), Is.EqualTo(5));

        [Test]
        public void Units_ZeroOfferedTakesNone()
            => Assert.That(TransportLoadPlan.UnitsWithinMassBudget(1000f, 1f, 0), Is.EqualTo(0));
    }
}
