using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class OverloadTuningTests
    {
        // ---- level bounds / off -------------------------------------------------------------

        [Test]
        public void ClampLevel_ClampsToRange()
        {
            Assert.That(OverloadTuning.ClampLevel(-3), Is.EqualTo(0));
            Assert.That(OverloadTuning.ClampLevel(99), Is.EqualTo(OverloadTuning.MaxLevel));
            Assert.That(OverloadTuning.ClampLevel(5), Is.EqualTo(5));
        }

        [Test]
        public void IsOff_OnlyAtFarRight()
        {
            Assert.That(OverloadTuning.IsOff(OverloadTuning.OffLevel), Is.True);
            Assert.That(OverloadTuning.IsOff(OverloadTuning.MaxLevel + 4), Is.True); // clamps to off
            for (int lv = 0; lv < OverloadTuning.OffLevel; lv++)
                Assert.That(OverloadTuning.IsOff(lv), Is.False, $"level {lv} should not be off");
        }

        [Test]
        public void FairLevel_IsTheMiddleStop()
        {
            Assert.That(OverloadTuning.FairLevel, Is.EqualTo(OverloadTuning.MaxLevel / 2));
        }

        // ---- SpeedFactor --------------------------------------------------------------------

        [Test]
        public void SpeedFactor_AtOrUnderCapacity_NoPenalty()
        {
            Assert.That(OverloadTuning.SpeedFactor(OverloadTuning.FairLevel, 1.0f), Is.EqualTo(1f).Within(1e-4));
            Assert.That(OverloadTuning.SpeedFactor(OverloadTuning.FairLevel, 0.5f), Is.EqualTo(1f).Within(1e-4));
        }

        [Test]
        public void SpeedFactor_NoSlowdownLevel_AlwaysFullSpeed()
        {
            Assert.That(OverloadTuning.SpeedFactor(0, 3.0f), Is.EqualTo(1f).Within(1e-4));
        }

        [Test]
        public void SpeedFactor_Off_AlwaysFullSpeed()
        {
            Assert.That(OverloadTuning.SpeedFactor(OverloadTuning.OffLevel, 3.0f), Is.EqualTo(1f).Within(1e-4));
        }

        [Test]
        public void SpeedFactor_Fair_DeclinesPastCapacity()
        {
            // slope 0.45: factor = 1 - 0.45*(t-1)
            Assert.That(OverloadTuning.SpeedFactor(5, 1.5f), Is.EqualTo(0.775f).Within(1e-3));
            Assert.That(OverloadTuning.SpeedFactor(5, 2.0f), Is.EqualTo(0.55f).Within(1e-3));
        }

        [Test]
        public void SpeedFactor_ExtremeOverload_FlooredNeverZero()
        {
            Assert.That(OverloadTuning.SpeedFactor(5, 5.0f), Is.EqualTo(OverloadTuning.SpeedFloor).Within(1e-4));
            Assert.That(OverloadTuning.SpeedFactor(9, 10.0f), Is.GreaterThanOrEqualTo(OverloadTuning.SpeedFloor));
        }

        [Test]
        public void SpeedFactor_HigherLevel_SlowsMore()
        {
            float f1 = OverloadTuning.SpeedFactor(1, 2.0f);
            float f5 = OverloadTuning.SpeedFactor(5, 2.0f);
            float f9 = OverloadTuning.SpeedFactor(9, 2.0f);
            Assert.That(f1, Is.GreaterThan(f5));
            Assert.That(f5, Is.GreaterThan(f9).Or.EqualTo(f9)); // f9 may sit on the floor
            Assert.That(f9, Is.GreaterThanOrEqualTo(OverloadTuning.SpeedFloor));
        }

        // ---- break-even / overload ratio ----------------------------------------------------

        [Test]
        public void BreakEvenFactor_MatchesClosedForm()
        {
            // f* = sqrt(slope+2) - 1
            Assert.That(OverloadTuning.BreakEvenFactor(0.45f), Is.EqualTo(0.56525f).Within(1e-3));
            Assert.That(OverloadTuning.BreakEvenFactor(0f), Is.EqualTo(0.41421f).Within(1e-3));
        }

        [Test]
        public void MaxOverloadRatio_NoSlowdown_IsInfinite()
        {
            Assert.That(float.IsPositiveInfinity(OverloadTuning.MaxOverloadRatio(0)), Is.True);
        }

        [Test]
        public void MaxOverloadRatio_Off_IsOne()
        {
            Assert.That(OverloadTuning.MaxOverloadRatio(OverloadTuning.OffLevel), Is.EqualTo(1f).Within(1e-4));
        }

        [Test]
        public void MaxOverloadRatio_Fair_IsAboutDouble()
        {
            Assert.That(OverloadTuning.MaxOverloadRatio(5), Is.EqualTo(1.966f).Within(0.02f));
        }

        [Test]
        public void MaxOverloadRatio_HigherLevel_AllowsLessOverload()
        {
            float r1 = OverloadTuning.MaxOverloadRatio(1);
            float r5 = OverloadTuning.MaxOverloadRatio(5);
            float r9 = OverloadTuning.MaxOverloadRatio(9);
            Assert.That(r1, Is.GreaterThan(r5));
            Assert.That(r5, Is.GreaterThan(r9));
            Assert.That(r9, Is.GreaterThanOrEqualTo(1f));
        }

        [Test]
        public void SpeedFactor_AtOverloadCeiling_EqualsBreakEven()
        {
            // The whole design rests on this: the slowdown a pawn ACCEPTS when it overloads
            // (the break-even factor that defines the ceiling, used by OverloadPolicy) must equal
            // the slowdown it then EXPERIENCES (SpeedFactor, used by StatPart_Overload). They are
            // computed by separate formulas; this pins them together so a future re-tune of one
            // can't silently desync them while every per-formula test still passes.
            // Levels 1..9 only: level 0 (slope 0) has an infinite ceiling; 10 (OffLevel) is disabled.
            for (int lv = 1; lv < OverloadTuning.OffLevel; lv++)
            {
                float ratio = OverloadTuning.MaxOverloadRatio(lv);
                float expected = OverloadTuning.BreakEvenFactor(OverloadTuning.SlopeForLevel(lv));
                Assert.That(OverloadTuning.SpeedFactor(lv, ratio), Is.EqualTo(expected).Within(1e-4f),
                    $"level {lv}: pickup decision and actual slowdown disagree at the ceiling");
            }
        }
    }
}
