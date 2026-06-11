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
    }
}
