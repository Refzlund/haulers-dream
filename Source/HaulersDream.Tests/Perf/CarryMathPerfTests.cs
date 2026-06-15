using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guards for the pure carry/encumbrance math fed per-candidate during scooping and
    /// per-tick by the move-speed model.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class CarryMathPerfTests
    {
        private const float MaxCap = 35f;
        private const float Fraction = 1.0f;
        private const float Capacity = 35f;
        private const float CurMass = 20f;
        private const float UnitMass = 0.5f;
        private const int StackCount = 75;

        [Test]
        public void EffectiveCapacity_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => CarryMath.EffectiveCapacity(MaxCap, Fraction),
                "effective carry-limit math must not allocate");

        [Test]
        public void CountToPickUp_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => CarryMath.CountToPickUp(Capacity, CurMass, UnitMass, StackCount),
                "per-candidate pickup count must not allocate");

        [Test]
        public void EncumbranceFraction_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => CarryMath.EncumbranceFraction(CurMass, MaxCap),
                "encumbrance fraction must not allocate (per-tick)");

        [Test]
        public void ReachedCarryLimit_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => CarryMath.ReachedCarryLimit(Capacity, CurMass),
                "carry-limit check must not allocate (per scoop step)");
    }
}
