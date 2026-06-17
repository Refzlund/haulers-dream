using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class BatchPausePolicyTests
    {
        // ── below target: keep crafting ────────────────────────────────────────────────
        [Test]
        public void MayCraftMore_BelowTarget_NotPaused_True()
        {
            Assert.That(BatchPausePolicy.MayCraftMore(effectiveCount: 3, targetCount: 10, paused: false), Is.True);
        }

        [Test]
        public void MayCraftMore_OneBelowTarget_True()
        {
            Assert.That(BatchPausePolicy.MayCraftMore(9, 10, false), Is.True);
        }

        // ── at/over target: stop (this is the bug — banked products must make this trip) ─
        [Test]
        public void MayCraftMore_AtTarget_False()
        {
            Assert.That(BatchPausePolicy.MayCraftMore(10, 10, false), Is.False);
        }

        [Test]
        public void MayCraftMore_OverTarget_False()
        {
            // Effective count includes in-flight banked products, so an overshoot reads as satisfied.
            Assert.That(BatchPausePolicy.MayCraftMore(15, 10, false), Is.False);
        }

        // ── paused latch: vanilla owns `paused`; respect it (stop while paused) ─────────
        [Test]
        public void MayCraftMore_Paused_AlwaysFalse_EvenBelowTarget()
        {
            // A pause-when-satisfied bill vanilla latched paused (count between unpause-at and target):
            // must stay stopped until vanilla unpauses it, even though count < target.
            Assert.That(BatchPausePolicy.MayCraftMore(7, 10, paused: true), Is.False);
        }

        [Test]
        public void MayCraftMore_Paused_AtZero_False()
        {
            Assert.That(BatchPausePolicy.MayCraftMore(0, 10, paused: true), Is.False);
        }

        // ── degenerate targets ──────────────────────────────────────────────────────────
        [Test]
        public void MayCraftMore_ZeroTarget_False()
        {
            Assert.That(BatchPausePolicy.MayCraftMore(0, 0, false), Is.False);
        }

        // ── oracle: matches vanilla Bill_Production.ShouldDoNow's TargetCount branch ─────
        // Vanilla (paused already resolved by its pause/unpause lines): returns num < targetCount.
        [TestCase(0, 10, false, true)]
        [TestCase(5, 10, false, true)]
        [TestCase(10, 10, false, false)]
        [TestCase(11, 10, false, false)]
        [TestCase(5, 10, true, false)]   // latched paused → stop regardless
        public void MayCraftMore_MatchesVanillaTargetBranch(int count, int target, bool paused, bool expected)
        {
            Assert.That(BatchPausePolicy.MayCraftMore(count, target, paused), Is.EqualTo(expected));
        }
    }
}
