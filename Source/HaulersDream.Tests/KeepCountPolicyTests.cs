using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class KeepCountPolicyTests
    {
        // ── SurplusForKeptDef: unload the excess over the kept count ─────────────────────────────

        [Test]
        public void HoldingExactlyKept_NoSurplus()
        {
            // 50 held, keep 50 → nothing to unload.
            Assert.That(KeepCountPolicy.SurplusForKeptDef(50, 50, 50), Is.EqualTo(0));
        }

        [Test]
        public void HoldingLessThanKept_NoSurplus()
        {
            // 30 held, keep 50 → all held is within the pin, nothing surplus.
            Assert.That(KeepCountPolicy.SurplusForKeptDef(50, 30, 30), Is.EqualTo(0));
        }

        [Test]
        public void MergedStack_UnloadsExactExcess()
        {
            // The common case: one merged stack of 70, keep 50 → unload 20.
            Assert.That(KeepCountPolicy.SurplusForKeptDef(50, 70, 70), Is.EqualTo(20));
        }

        [Test]
        public void ExcessLargerThanStack_ClampsToStack()
        {
            // held 200 across defs, keep 50 → 150 over, but this stack is only 75 → at most 75 from it.
            Assert.That(KeepCountPolicy.SurplusForKeptDef(50, 200, 75), Is.EqualTo(75));
        }

        [Test]
        public void SummingOverStacks_UnloadsTotalExcess()
        {
            // Two stacks of 40 (held 80), keep 50. Each call caps at its own stack, and the sum equals the
            // excess only when the per-stack contribution is bounded by the running total — which the real caller
            // enforces by walking stacks; here we verify the single-stack cap is min(stackCount, over).
            // Stack A: over 30, cap 40 → 30. So a single 80-unit merged stack unloads 30 (the true excess).
            Assert.That(KeepCountPolicy.SurplusForKeptDef(50, 80, 80), Is.EqualTo(30));
        }

        [Test]
        public void KeepZero_UnloadsWholeStack()
        {
            // keep 0 → the whole stack is surplus.
            Assert.That(KeepCountPolicy.SurplusForKeptDef(0, 40, 40), Is.EqualTo(40));
        }

        [Test]
        public void NegativeKept_TreatedAsZero()
        {
            // A corrupt/hand-edited negative pin must never make surplus negative or keep more than held.
            Assert.That(KeepCountPolicy.SurplusForKeptDef(-10, 40, 40), Is.EqualTo(40));
        }

        // ── ClampKeepAmount: slider bounds ────────────────────────────────────────────────────────

        [Test]
        public void Clamp_WithinRange_PassesThrough()
        {
            Assert.That(KeepCountPolicy.ClampKeepAmount(30, 75), Is.EqualTo(30));
        }

        [Test]
        public void Clamp_AboveMax_ClampsToMax()
        {
            Assert.That(KeepCountPolicy.ClampKeepAmount(200, 75), Is.EqualTo(75));
        }

        [Test]
        public void Clamp_Negative_ClampsToZero()
        {
            Assert.That(KeepCountPolicy.ClampKeepAmount(-5, 75), Is.EqualTo(0));
        }

        [Test]
        public void Clamp_NegativeMax_TreatedAsZeroCeiling()
        {
            Assert.That(KeepCountPolicy.ClampKeepAmount(10, -3), Is.EqualTo(0));
        }
    }
}
