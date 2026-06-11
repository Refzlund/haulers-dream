using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class BulkHaulPolicyTests
    {
        // ── CeilingKg: the worth-it mass ceiling derives from the smart-overload break-even ──────────

        [Test]
        public void Ceiling_NoSlowdownLevel_IsUnbounded()
        {
            // Slider at 0 = carrying more is free → more is always worth it.
            Assert.That(float.IsPositiveInfinity(BulkHaulPolicy.CeilingKg(0, false, 35f)), Is.True);
        }

        [Test]
        public void Ceiling_OffLevel_IsExactlyTheCarryLimit()
        {
            Assert.That(BulkHaulPolicy.CeilingKg(OverloadTuning.OffLevel, false, 35f), Is.EqualTo(35f).Within(0.001f));
        }

        [Test]
        public void Ceiling_StrictOverridesSliderToCarryLimit()
        {
            // Fair slider, but strict carry weight on → never overload.
            Assert.That(BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, true, 35f), Is.EqualTo(35f).Within(0.001f));
        }

        [Test]
        public void Ceiling_FairLevel_IsTheBreakEvenRatioTimesBaseCap()
        {
            float expected = OverloadTuning.MaxOverloadRatio(OverloadTuning.FairLevel) * 35f;
            Assert.That(BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, false, 35f), Is.EqualTo(expected).Within(0.001f));
            Assert.That(expected, Is.GreaterThan(35f)); // Fair overloads past 100%…
            Assert.That(expected, Is.LessThan(35f * 3f)); // …but not absurdly (≈2× capacity break-even)
        }

        [Test]
        public void Ceiling_SteeperSlope_LowersTheCeiling()
        {
            // Worth-it intuition: a harsher slowdown makes extra weight pay off later → lower ceiling.
            float fair = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, false, 35f);
            float cautious = BulkHaulPolicy.CeilingKg(9, false, 35f);
            Assert.That(cautious, Is.LessThan(fair));
            Assert.That(cautious, Is.GreaterThanOrEqualTo(35f));
        }

        [Test]
        public void Ceiling_NonPositiveBaseCap_IsZero()
        {
            Assert.That(BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, false, 0f), Is.EqualTo(0f));
        }

        // ── TriggerSatisfied: automatic always sweeps; forced respects the finer-control option ─────

        [Test]
        public void Trigger_AutomaticHaul_AlwaysSweeps_BothModes()
        {
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.Always, forced: false, secondNearbyTasked: false), Is.True);
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.SecondTasked, forced: false, secondNearbyTasked: false), Is.True);
        }

        [Test]
        public void Trigger_ForcedSingleOrder_SecondTaskedMode_DoesNotSweep()
        {
            // The finer-control default: ordering ONE haul truly hauls one thing.
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.SecondTasked, forced: true, secondNearbyTasked: false), Is.False);
        }

        [Test]
        public void Trigger_ForcedOrder_SweepsWhenSecondNearbyTasked()
        {
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.SecondTasked, forced: true, secondNearbyTasked: true), Is.True);
        }

        [Test]
        public void Trigger_ForcedOrder_AlwaysMode_Sweeps()
        {
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.Always, forced: true, secondNearbyTasked: false), Is.True);
        }

        // ── CountWithinCeiling ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Count_FitsUnderCeiling()
        {
            // 10 kg of room, 0.5 kg/unit → 20 fit; stack has 30.
            Assert.That(BulkHaulPolicy.CountWithinCeiling(45f, 35f, 0.5f, 30), Is.EqualTo(20));
        }

        [Test]
        public void Count_StackSmallerThanRoom_TakesWholeStack()
        {
            Assert.That(BulkHaulPolicy.CountWithinCeiling(45f, 35f, 0.5f, 10), Is.EqualTo(10));
        }

        [Test]
        public void Count_NoRoom_TakesNothing()
        {
            Assert.That(BulkHaulPolicy.CountWithinCeiling(35f, 35f, 0.5f, 10), Is.EqualTo(0));
        }

        [Test]
        public void Count_UnboundedCeiling_TakesWholeStack()
        {
            Assert.That(BulkHaulPolicy.CountWithinCeiling(float.PositiveInfinity, 9999f, 75f, 12), Is.EqualTo(12));
        }

        [Test]
        public void Count_MasslessItem_TakesWholeStack()
        {
            Assert.That(BulkHaulPolicy.CountWithinCeiling(35f, 35f, 0f, 40), Is.EqualTo(40));
        }
    }
}
