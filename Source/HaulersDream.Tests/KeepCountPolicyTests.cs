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

        // ── #225: keep 7, held 9 → unload exactly 2 ───────────────────────────────────────────────

        [Test]
        public void Issue225_Held9_Keep7_UnloadsExactly2()
        {
            // The reported case: one merged stack of 9, keep 7 → the surplus math sheds exactly 2. (The bug was
            // never here: SurplusForKeptDef is correct; the pin got inflated to 9 in migration, and the keep
            // driver never scheduled the unload. Guards the arithmetic contract the two fixes rely on.)
            Assert.That(KeepCountPolicy.SurplusForKeptDef(7, 9, 9), Is.EqualTo(2));
        }

        [Test]
        public void Issue225_TaggedStackInMixedSplit_Unloads2()
        {
            // Same def held as two stacks (a kept 7 and a scooped 2 that didn't merge): the 2-unit stack is fully
            // surplus (held 9 over keep 7 = 2, capped by its own size 2). Summed with the 7-stack's 0 → unload 2.
            Assert.That(KeepCountPolicy.SurplusForKeptDef(7, 9, 2), Is.EqualTo(2));
        }

        [Test]
        public void HeldAtOrBelowKeep_NoSurplus()
        {
            // At the pin (7/7) and below it (5/7) nothing is surplus; the keep is never over-shed.
            Assert.That(KeepCountPolicy.SurplusForKeptDef(7, 7, 7), Is.EqualTo(0));
            Assert.That(KeepCountPolicy.SurplusForKeptDef(7, 5, 5), Is.EqualTo(0));
        }

        // ── MigratedKeep: pre-#197 whole-stack keep → per-def amount, tagged haul cargo excluded ───

        [Test]
        public void Migration_NeverKeepsTaggedHaulUnits()
        {
            // Legacy kept a 7-stack; the pawn also carries 2 HD-tagged haul units of the def (held 9). Migrate to
            // the kept 7, NOT the raw held 9, so SurplusForKeptDef(7, 9, …) then sheds the 2 (the #225 fix).
            Assert.That(KeepCountPolicy.MigratedKeep(7, 9, 2), Is.EqualTo(7));
        }

        [Test]
        public void Migration_CapsAtHeldMinusTagged()
        {
            // Even if the legacy kept-sum reads high (9, e.g. the kept and scooped stacks merged on load), the cap
            // at held - taggedUnits = 9 - 2 = 7 keeps the 2 tagged haul units surplus.
            Assert.That(KeepCountPolicy.MigratedKeep(9, 9, 2), Is.EqualTo(7));
        }

        [Test]
        public void Migration_NoTagged_KeepsSum()
        {
            // No tagged haul cargo: the whole kept stock (7 held, all personal) migrates intact, nothing to shed.
            Assert.That(KeepCountPolicy.MigratedKeep(7, 7, 0), Is.EqualTo(7));
        }

        [Test]
        public void Migration_ZeroWhenAllTagged()
        {
            // A def held ENTIRELY as tagged haul cargo (2 held, 2 tagged, nothing legacy-kept) migrates to a 0 keep
            // (floored, never negative): all of it stays surplus to unload.
            Assert.That(KeepCountPolicy.MigratedKeep(0, 2, 2), Is.EqualTo(0));
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
