using System;
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
        public void SpeedFactor_Fair_FollowsConvexQuadraticTable()
        {
            // Fair (α ≈ 0.11264): f(r) = max(0.25, 1 − α·(r−1)²). Convex — gentle near 100%, steeper far out.
            // 100% → 1.0 (no penalty); the published Fair spectrum the design was tuned to:
            Assert.That(OverloadTuning.SpeedFactor(5, 1.00f), Is.EqualTo(1.00f).Within(1e-3));
            Assert.That(OverloadTuning.SpeedFactor(5, 1.50f), Is.EqualTo(0.97f).Within(0.01f));
            Assert.That(OverloadTuning.SpeedFactor(5, 2.00f), Is.EqualTo(0.89f).Within(0.01f));
            Assert.That(OverloadTuning.SpeedFactor(5, 2.75f), Is.EqualTo(0.65f).Within(0.02f));
            Assert.That(OverloadTuning.SpeedFactor(5, 3.00f), Is.EqualTo(0.55f).Within(0.02f));
        }

        [Test]
        public void SpeedFactor_Convex_GentleNearCapacitySteeperFarOut()
        {
            // Convexity signature: the FIRST 0.5 ratio past 100% costs less speed than a later 0.5 ratio.
            float drop1 = OverloadTuning.SpeedFactor(5, 1.0f) - OverloadTuning.SpeedFactor(5, 1.5f); // 1.0→1.5
            float drop2 = OverloadTuning.SpeedFactor(5, 2.0f) - OverloadTuning.SpeedFactor(5, 2.5f); // 2.0→2.5
            Assert.That(drop2, Is.GreaterThan(drop1), "the quadratic must steepen as you overload further");
        }

        [Test]
        public void SpeedFactor_ExtremeOverload_FlooredNeverZero()
        {
            // Far past the break-even every level rests on the floor and never below it.
            Assert.That(OverloadTuning.SpeedFactor(5, 8.0f), Is.EqualTo(OverloadTuning.SpeedFloor).Within(1e-4));
            Assert.That(OverloadTuning.SpeedFactor(9, 10.0f), Is.EqualTo(OverloadTuning.SpeedFloor).Within(1e-4));
            for (int lv = 1; lv < OverloadTuning.OffLevel; lv++)
                Assert.That(OverloadTuning.SpeedFactor(lv, 20.0f), Is.GreaterThanOrEqualTo(OverloadTuning.SpeedFloor),
                    $"level {lv} dropped below the floor at extreme overload");
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

        [Test]
        public void SpeedFactor_MonotoneDecreasing_InRatio_EveryActiveLevel()
        {
            for (int lv = 1; lv < OverloadTuning.OffLevel; lv++)
            {
                float prev = OverloadTuning.SpeedFactor(lv, 1.0f);
                for (float r = 1.0f; r <= 8.0f; r += 0.05f)
                {
                    float f = OverloadTuning.SpeedFactor(lv, r);
                    Assert.That(f, Is.LessThanOrEqualTo(prev + 1e-5f),
                        $"level {lv}: SpeedFactor must not increase with ratio (at r={r})");
                    prev = f;
                }
            }
        }

        [Test]
        public void SpeedFactor_MonotoneDecreasing_InLevel_AtFixedRatio()
        {
            // At any overloaded ratio, a higher (harsher) level is never faster than a lower one.
            foreach (float r in new[] { 1.25f, 1.5f, 2.0f, 2.5f, 3.0f })
            {
                float prev = OverloadTuning.SpeedFactor(0, r); // 1.0 (no slowdown)
                for (int lv = 1; lv < OverloadTuning.OffLevel; lv++)
                {
                    float f = OverloadTuning.SpeedFactor(lv, r);
                    Assert.That(f, Is.LessThanOrEqualTo(prev + 1e-5f),
                        $"r={r}: level {lv} is faster than level {lv - 1}");
                    prev = f;
                }
            }
        }

        // ---- break-even / overload ratio ----------------------------------------------------

        // The design's monotonic break-even spectrum (encumbrance ratio at which overload stops paying off).
        // Level 0 = ∞ (carry freely), level 10 = off. Tuned so the default "Fair" (level 5) is 275%.
        private static readonly float[] TargetBreakEven =
        {
            float.PositiveInfinity, // 0 — Free
            4.50f,                  // 1 — Eager
            3.80f,                  // 2 — Eager
            3.30f,                  // 3 — Eager
            3.00f,                  // 4 — Eager
            2.75f,                  // 5 — Fair (default)
            2.45f,                  // 6 — Cautious
            2.15f,                  // 7 — Cautious
            1.85f,                  // 8 — Cautious
            1.50f,                  // 9 — Cautious
            1.00f,                  // 10 — Off
        };

        [Test]
        public void MaxOverloadRatio_HitsTheTargetSpectrum()
        {
            // Level 0 is unbounded; off is exactly 1.0; the active levels land within ±5% of the design target.
            Assert.That(float.IsPositiveInfinity(OverloadTuning.MaxOverloadRatio(0)), Is.True);
            Assert.That(OverloadTuning.MaxOverloadRatio(OverloadTuning.OffLevel), Is.EqualTo(1f).Within(1e-4));
            for (int lv = 1; lv < OverloadTuning.OffLevel; lv++)
            {
                float got = OverloadTuning.MaxOverloadRatio(lv);
                float want = TargetBreakEven[lv];
                Assert.That(got, Is.EqualTo(want).Within(0.05f * want),
                    $"level {lv}: break-even {got:F3} is outside ±5% of the target {want:F2}");
            }
        }

        [Test]
        public void MaxOverloadRatio_Fair_IsAbout275Percent()
        {
            Assert.That(OverloadTuning.MaxOverloadRatio(OverloadTuning.FairLevel), Is.EqualTo(2.75f).Within(0.05f));
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
        public void MaxOverloadRatio_HigherLevel_AllowsLessOverload()
        {
            // Strictly decreasing across every active level (1..9).
            float prev = float.PositiveInfinity;
            for (int lv = 1; lv < OverloadTuning.OffLevel; lv++)
            {
                float r = OverloadTuning.MaxOverloadRatio(lv);
                Assert.That(r, Is.LessThan(prev), $"level {lv} should allow less overload than level {lv - 1}");
                Assert.That(r, Is.GreaterThanOrEqualTo(1f));
                prev = r;
            }
        }

        [Test]
        public void MaxOverloadRatio_EqualsInteriorThroughputOptimum()
        {
            // ORACLE: the published break-even must equal the INTERIOR (pre-floor) throughput optimum — the
            // peak of the UN-FLOORED carry curve r·g(r)/(1+g(r)), g(r) = 1 − α(r−1)². Recomputed here by a
            // GENUINELY INDEPENDENT method: a different algorithm (exhaustive fine grid, not production's
            // ternary), a different objective (the un-floored g, never the floored f), and a different
            // domain bound — (1, rZero) where rZero = 1 + √(1/α) is the g=0 crossing, derived from the
            // un-floored curve, NOT production's ratioAtFloor = 1 + √((1−floor)/α). In (1, rZero) the
            // un-floored g stays ≥ 0 (so floored and un-floored throughput agree up to the floor knee, which
            // lies below rZero) and the interior peak sits strictly inside — so this independent domain +
            // algorithm rediscovers the same peak and can catch a mis-placed production bound. Distance
            // cancels in the cycle throughput.
            for (int lv = 1; lv < OverloadTuning.OffLevel; lv++)
            {
                float alpha = OverloadTuning.AlphaForLevel(lv);
                double argmax = GridArgmaxUnflooredThroughput(alpha);
                Assert.That(OverloadTuning.MaxOverloadRatio(lv), Is.EqualTo(argmax).Within(0.01),
                    $"level {lv}: published break-even disagrees with the independent interior throughput optimum");
            }
        }

        [Test]
        public void MaxOverloadRatio_IsTheInteriorOptimum_NotTheFlooredGlobalMax()
        {
            // DESIGN-CHOICE CONTRACT: MaxOverloadRatio is deliberately the INTERIOR (pre-floor) optimum — the
            // peak of the un-floored carry curve — NOT the global argmax of the FLOORED throughput over the
            // slider's real domain [1, 8]. The point of the floor knee: once the speed clamps to the floor,
            // floored throughput r·f/(1+f) = r·0.25/1.25 = 0.2·r is monotone-INCREASING in r, so the floored
            // curve has NO meaningful interior ceiling past the knee — it just keeps rising to the domain
            // edge (a pawn crawling under an ever-bigger load). That edge value is fixed at 0.2·8 = 1.6.
            const double tAtEdge = 8.0 * OverloadTuning.SpeedFloor / (1.0 + OverloadTuning.SpeedFloor); // 1.6
            for (int lv = 1; lv < OverloadTuning.OffLevel; lv++)
            {
                float alpha = OverloadTuning.AlphaForLevel(lv);

                // The floored global argmax is NEVER a moderate interior sweet spot: it is either the domain
                // edge r=8 (harsher levels, where the floor knee sits below the interior region so the
                // monotone-rising tail overtakes the interior peak) OR the un-floored interior peak itself
                // (the gentlest level, whose peak throughput is high enough to still beat the r=8 edge). In
                // both cases the FLOORED curve gives no sensible interior ceiling other than the un-floored
                // peak — which is exactly why production optimizes the un-floored curve, capped at the knee.
                double flooredArgmax = GridArgmaxFlooredThroughput(alpha, hi: 8.0, step: 0.001);
                double interiorOpt = GridArgmaxUnflooredThroughput(alpha);
                bool edgeWins = Math.Abs(flooredArgmax - 8.0) <= 0.01;
                bool interiorWins = Math.Abs(flooredArgmax - interiorOpt) <= 0.01;
                Assert.That(edgeWins || interiorWins, Is.True,
                    $"level {lv}: floored global argmax {flooredArgmax:F3} is neither the domain edge (8) nor the un-floored peak ({interiorOpt:F3})");

                // Whenever the edge wins, it must do so because the floored tail (0.2·8 = 1.6) overtakes the
                // interior peak — confirming the monotone-past-knee behavior that motivates excluding the floor.
                if (edgeWins && !interiorWins)
                {
                    double interiorThroughput = FlooredThroughput(alpha, interiorOpt);
                    Assert.That(tAtEdge, Is.GreaterThanOrEqualTo(interiorThroughput - 1e-9),
                        $"level {lv}: r=8 should win only by the monotone floored tail beating the interior peak");
                }

                // The shipped ceiling is the INTERIOR optimum, strictly below the floored global max edge (8).
                float ceiling = OverloadTuning.MaxOverloadRatio(lv);
                Assert.That(ceiling, Is.LessThan(8.0f),
                    $"level {lv}: MaxOverloadRatio must be the interior optimum, strictly below the slider's far edge (8)");
                Assert.That((double)ceiling, Is.EqualTo(interiorOpt).Within(0.01),
                    $"level {lv}: MaxOverloadRatio must equal the un-floored interior optimum, not the floored global max");
            }
        }

        [Test]
        public void SpeedFactor_AtOverloadCeiling_EqualsBreakEvenFactor()
        {
            // The whole design rests on this: the slowdown a pawn ACCEPTS when it overloads (the speed at
            // the break-even ceiling, used by OverloadPolicy) must equal the slowdown it then EXPERIENCES
            // (SpeedFactor, used by StatPart_Overload). Both come from the SAME α now (one table), so this
            // mostly guards against a future split. Levels 1..9 only: level 0 (α 0) has an infinite ceiling;
            // 10 (OffLevel) is disabled.
            for (int lv = 1; lv < OverloadTuning.OffLevel; lv++)
            {
                float ratio = OverloadTuning.MaxOverloadRatio(lv);
                float expected = OverloadTuning.BreakEvenFactor(OverloadTuning.AlphaForLevel(lv));
                Assert.That(OverloadTuning.SpeedFactor(lv, ratio), Is.EqualTo(expected).Within(1e-3f),
                    $"level {lv}: pickup decision and actual slowdown disagree at the ceiling");
            }
        }

        [Test]
        public void BreakEvenFactor_NoSlowdown_IsFullSpeed()
        {
            Assert.That(OverloadTuning.BreakEvenFactor(0f), Is.EqualTo(1f).Within(1e-4));
        }

        // ---- oracle helper ------------------------------------------------------------------

        // INDEPENDENT grid recomputation of the interior throughput optimum, deliberately NOT sharing any of
        // production's machinery: it maximizes the UN-FLOORED throughput r·g(r)/(1+g(r)), g(r)=1−α(r−1)²,
        // over (1, rZero) where rZero = 1 + √(1/α) is the g=0 crossing — a bound DERIVED FROM THE UN-FLOORED
        // CURVE, distinct from production's ratioAtFloor = 1 + √((1−floor)/α). In this domain g ≥ 0 so the
        // un-floored throughput is well-defined and unimodal, and its peak is the meaningful break-even. If
        // production's bound were mis-placed, this independent domain/objective would disagree.
        private static double GridArgmaxUnflooredThroughput(float alpha)
        {
            double rZero = 1.0 + Math.Sqrt(1.0 / alpha); // g(r) = 0 here (un-floored curve crosses zero)
            double best = double.NegativeInfinity, bestR = 1.0;
            const double step = 0.0005;
            for (double r = 1.0; r <= rZero; r += step)
            {
                double g = 1.0 - alpha * (r - 1.0) * (r - 1.0); // un-floored speed; ≥ 0 across (1, rZero)
                double t = (r * g) / (1.0 + g);
                if (t > best) { best = t; bestR = r; }
            }
            return bestR;
        }

        // Grid argmax of the FLOORED throughput r·f(r)/(1+f(r)) over [1, hi], used only to DOCUMENT the
        // design choice: past the floor knee f is constant so this objective is monotone-increasing and its
        // max is the domain edge (unless the un-floored interior peak is high enough to still win) — which
        // is exactly why production optimizes the un-floored curve instead.
        private static double GridArgmaxFlooredThroughput(float alpha, double hi, double step)
        {
            double best = double.NegativeInfinity, bestR = 1.0;
            for (double r = 1.0; r <= hi; r += step)
            {
                if (FlooredThroughput(alpha, r) is var t && t > best) { best = t; bestR = r; }
            }
            return bestR;
        }

        // FLOORED throughput r·f(r)/(1+f(r)) at a single ratio, f(r) = max(floor, 1 − α(r−1)²).
        private static double FlooredThroughput(float alpha, double r)
        {
            double raw = 1.0 - alpha * (r - 1.0) * (r - 1.0);
            double f = raw < OverloadTuning.SpeedFloor ? OverloadTuning.SpeedFloor : raw;
            return (r * f) / (1.0 + f);
        }
    }
}
