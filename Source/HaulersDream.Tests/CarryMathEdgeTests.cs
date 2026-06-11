using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>Boundary / edge cases complementing CarryMathTests.</summary>
    [TestFixture]
    public class CarryMathEdgeTests
    {
        [Test]
        public void CountToPickUp_ExactFit_TakesExactlyThatMany()
        {
            // 30kg cap, 10kg units => exactly 3 fit
            Assert.That(CarryMath.CountToPickUp(30f, 0f, 10f, 3), Is.EqualTo(3));
            Assert.That(CarryMath.CountToPickUp(30f, 0f, 10f, 4), Is.EqualTo(3)); // 4th would exceed
        }

        [Test]
        public void CountToPickUp_FractionalUnitMass_FloorsCorrectly()
        {
            // 10kg cap, 0.45kg units => floor(10/0.45) = 22
            Assert.That(CarryMath.CountToPickUp(10f, 0f, 0.45f, 100), Is.EqualTo(22));
        }

        [Test]
        public void CountToPickUp_LargeStack_CappedByCapacity()
        {
            Assert.That(CarryMath.CountToPickUp(75f, 0f, 1f, 100000), Is.EqualTo(75));
        }

        [Test]
        public void CountToPickUp_NearlyFull_TakesRemainder()
        {
            // 100 cap, carrying 97, 1kg units, stack 10 => only 3 fit
            Assert.That(CarryMath.CountToPickUp(100f, 97f, 1f, 10), Is.EqualTo(3));
        }

        [Test]
        public void EffectiveCapacity_AtExactBounds()
        {
            Assert.That(CarryMath.EffectiveCapacity(50f, CarryMath.MinFraction), Is.EqualTo(50f * CarryMath.MinFraction).Within(1e-4));
            Assert.That(CarryMath.EffectiveCapacity(50f, CarryMath.MaxFraction), Is.EqualTo(50f * CarryMath.MaxFraction).Within(1e-4));
        }

        [Test]
        public void EncumbranceFraction_CanExceedOne()
        {
            Assert.That(CarryMath.EncumbranceFraction(120f, 80f), Is.EqualTo(1.5f).Within(1e-4));
        }

        [Test]
        public void ReachedCarryLimit_EpsilonBoundary()
        {
            // just under by less than epsilon counts as reached
            Assert.That(CarryMath.ReachedCarryLimit(75f, 75f - 0.00001f), Is.True);
            // clearly under does not
            Assert.That(CarryMath.ReachedCarryLimit(75f, 74f), Is.False);
        }

        [Test]
        public void MaxFraction_IsOne_NoOverFill()
        {
            // the design decision: carry limit caps at the pawn's true maximum (100%)
            Assert.That(CarryMath.MaxFraction, Is.EqualTo(1.0f));
        }
    }
}
