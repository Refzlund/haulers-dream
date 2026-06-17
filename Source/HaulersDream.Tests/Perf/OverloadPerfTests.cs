using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guards for the smart-overload model — the per-tick MoveSpeed factor
    /// (<see cref="OverloadTuning.SpeedFactor"/>) and the per-candidate pickup count
    /// (<see cref="OverloadPolicy.UnitsToCarry"/>). These run on the hottest path in the mod
    /// (per-cell while a loaded pawn moves), so any future regression to allocating math here is a
    /// jitter source we must catch. Pre-built delegates, inputs hoisted out of the measured body.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class OverloadPerfTests
    {
        // Representative overloaded-pawn inputs, hoisted so the measured delegate captures no
        // freshly-built state and never re-reads a loop variable.
        private const int Level = 5;        // Fair
        private const float Encumbrance = 1.6f;
        private const float Slope = 0.45f;

        private const float MaxCap = 35f;
        private const float BaseCap = 35f;
        private const float CurMass = 20f;
        private const float UnitMass = 0.5f;
        private const int Demand = 200;
        private const int Available = 150;

        [Test]
        public void SpeedFactor_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => OverloadTuning.SpeedFactor(Level, Encumbrance),
                "per-tick MoveSpeed slowdown must not allocate");

        [Test]
        public void MaxOverloadRatio_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => OverloadTuning.MaxOverloadRatio(Level),
                "the overload ceiling must not allocate (per-candidate)");

        [Test]
        public void BreakEvenFactor_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => OverloadTuning.BreakEvenFactor(Slope),
                "break-even factor must not allocate");

        [Test]
        public void UnitsToCarry_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => OverloadPolicy.UnitsToCarry(Level, MaxCap, BaseCap, CurMass, UnitMass, Demand, Available),
                "per-candidate pickup count must not allocate");
    }
}
