using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CarryMathTests
    {
        // ---- EffectiveCapacity -------------------------------------------------------------

        [Test]
        public void EffectiveCapacity_DefaultFraction_IsFullMax()
        {
            // The headline behaviour: fraction 1.0 → full maximum carry capacity.
            Assert.That(CarryMath.EffectiveCapacity(75f, 1.0f), Is.EqualTo(75f).Within(1e-4));
        }

        [Test]
        public void EffectiveCapacity_LowerFraction_ScalesDown()
        {
            Assert.That(CarryMath.EffectiveCapacity(100f, 0.8f), Is.EqualTo(80f).Within(1e-4));
        }

        [Test]
        public void EffectiveCapacity_NonPositiveFraction_FallsBackToFullMax()
        {
            Assert.That(CarryMath.EffectiveCapacity(60f, 0f), Is.EqualTo(60f).Within(1e-4));
            Assert.That(CarryMath.EffectiveCapacity(60f, -2f), Is.EqualTo(60f).Within(1e-4));
        }

        [Test]
        public void EffectiveCapacity_ClampsToBounds()
        {
            // Above MaxFraction clamps; tiny positive clamps up to MinFraction.
            Assert.That(CarryMath.EffectiveCapacity(10f, 999f), Is.EqualTo(10f * CarryMath.MaxFraction).Within(1e-4));
            Assert.That(CarryMath.EffectiveCapacity(10f, 0.0001f), Is.EqualTo(10f * CarryMath.MinFraction).Within(1e-4));
        }

        [Test]
        public void EffectiveCapacity_ZeroMax_IsZero()
        {
            Assert.That(CarryMath.EffectiveCapacity(0f, 1.0f), Is.EqualTo(0f).Within(1e-4));
        }

        // ---- CountToPickUp -----------------------------------------------------------------

        [Test]
        public void CountToPickUp_TakesWholeStack_WhenItFits()
        {
            // cap 100, empty, 1kg units, 5 available → all 5.
            Assert.That(CarryMath.CountToPickUp(100f, 0f, 1f, 5), Is.EqualTo(5));
        }

        [Test]
        public void CountToPickUp_LimitedByCapacity()
        {
            // cap 100, empty, 10kg units, 20 available → only 10 fit.
            Assert.That(CarryMath.CountToPickUp(100f, 0f, 10f, 20), Is.EqualTo(10));
        }

        [Test]
        public void CountToPickUp_PartialCapacity_FloorsDown()
        {
            // cap 35, empty, 10kg units → 3 (30kg), not 3.5.
            Assert.That(CarryMath.CountToPickUp(35f, 0f, 10f, 20), Is.EqualTo(3));
        }

        [Test]
        public void CountToPickUp_AccountsForCurrentMass()
        {
            // cap 100, already carrying 95, 10kg units → 0 fit.
            Assert.That(CarryMath.CountToPickUp(100f, 95f, 10f, 20), Is.EqualTo(0));
            // cap 100, already carrying 50, 10kg units → 5 fit.
            Assert.That(CarryMath.CountToPickUp(100f, 50f, 10f, 20), Is.EqualTo(5));
        }

        [Test]
        public void CountToPickUp_OverCapacity_TakesNone()
        {
            Assert.That(CarryMath.CountToPickUp(100f, 120f, 5f, 10), Is.EqualTo(0));
        }

        [Test]
        public void CountToPickUp_MasslessItems_TakesWholeStack()
        {
            Assert.That(CarryMath.CountToPickUp(100f, 99f, 0f, 42), Is.EqualTo(42));
        }

        [Test]
        public void CountToPickUp_NonPositiveStack_TakesNone()
        {
            Assert.That(CarryMath.CountToPickUp(100f, 0f, 1f, 0), Is.EqualTo(0));
            Assert.That(CarryMath.CountToPickUp(100f, 0f, 1f, -3), Is.EqualTo(0));
        }

        // ---- EncumbranceFraction / ReachedCarryLimit ---------------------------------------

        [Test]
        public void EncumbranceFraction_IsCurrentOverMax()
        {
            Assert.That(CarryMath.EncumbranceFraction(40f, 80f), Is.EqualTo(0.5f).Within(1e-4));
        }

        [Test]
        public void EncumbranceFraction_ZeroMax_IsZero()
        {
            Assert.That(CarryMath.EncumbranceFraction(40f, 0f), Is.EqualTo(0f).Within(1e-4));
        }

        [Test]
        public void ReachedCarryLimit_TrueAtOrAboveLimit()
        {
            Assert.That(CarryMath.ReachedCarryLimit(75f, 75f), Is.True);
            Assert.That(CarryMath.ReachedCarryLimit(75f, 80f), Is.True);
            Assert.That(CarryMath.ReachedCarryLimit(75f, 74.9f), Is.False);
        }

        // ---- UnitsThatFitBulk (the CE bulk dimension; issue #125) ----------------------------
        // The construction planner and the delivery driver both clamp through this ONE function, so
        // "the plan offers a load the driver cannot pick up" (the stand-in-place re-offer livelock)
        // is impossible by construction as long as these semantics hold.

        [Test]
        public void UnitsThatFitBulk_NoBulkStat_NeverBinds()
        {
            // bulkPerUnit <= 0 means "the dimension does not apply" (CE off, or a zero-bulk item),
            // NOT "fits zero" (a fits-zero reading would kill every plan without CE).
            Assert.That(CarryMath.UnitsThatFitBulk(10f, 0f), Is.EqualTo(int.MaxValue));
            Assert.That(CarryMath.UnitsThatFitBulk(10f, -1f), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void UnitsThatFitBulk_InfiniteRoom_NeverBinds()
        {
            // CE absent or its read failed open: AvailableBulk reports +infinity. A raw (int) cast of
            // infinity / x is int.MinValue, which would silently kill every plan; the helper must map
            // it to "never binds" instead.
            Assert.That(CarryMath.UnitsThatFitBulk(float.PositiveInfinity, 1f), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void UnitsThatFitBulk_NoRoom_FitsNone()
        {
            // The issue #125 reporter's state: a loadout-full CE pawn (availableBulk at or below 0)
            // fits ZERO units of a Bulk-1.0 textile, while the mass math says it could carry hundreds.
            Assert.That(CarryMath.UnitsThatFitBulk(0f, 1f), Is.EqualTo(0));
            Assert.That(CarryMath.UnitsThatFitBulk(-3.5f, 1f), Is.EqualTo(0));
        }

        [Test]
        public void UnitsThatFitBulk_SubUnitRoom_FitsNone()
        {
            // Room smaller than one unit's bulk: the take toil would pick up 0, so the plan must say 0.
            Assert.That(CarryMath.UnitsThatFitBulk(0.9f, 1f), Is.EqualTo(0));
        }

        [Test]
        public void UnitsThatFitBulk_FlooredDivision()
        {
            Assert.That(CarryMath.UnitsThatFitBulk(10f, 3f), Is.EqualTo(3));
            Assert.That(CarryMath.UnitsThatFitBulk(3f, 3f), Is.EqualTo(1));   // exact fit boundary
            Assert.That(CarryMath.UnitsThatFitBulk(40f, 0.5f), Is.EqualTo(80));
        }

        [Test]
        public void UnitsThatFitBulk_HugeRatio_SaturatesAtMaxValue()
        {
            // A featherweight bulk against a big room must not overflow the int conversion.
            Assert.That(CarryMath.UnitsThatFitBulk(float.MaxValue, 0.001f), Is.EqualTo(int.MaxValue));
        }
    }
}
