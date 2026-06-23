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

        // ══════════════════ overshoot-by-Y overload (issue #3) ══════════════════
        // MayCraftMore(count, target, overshoot, paused):
        //   Y := clamp0(overshoot); if (paused && Y == 0) stop; else craft while count < target + Y.
        // The paused latch (vanilla "Pause when satisfied", latched at X) is honoured ONLY when there's no
        // overshoot to finish — inside an active X..X+Y window the player asked for Y more, so it overrides
        // the latch (the X+Y ceiling still bounds it). Manual stop is the separate Bill.suspended, checked
        // elsewhere, so a hand-paused bill still stops.

        // ── overshoot 0 == the original (3-arg) behaviour ───────────────────────────────
        [TestCase(9, 10, false, true)]   // below target → craft
        [TestCase(10, 10, false, false)] // at target, no overshoot → stop
        [TestCase(11, 10, false, false)] // over target → stop
        [TestCase(5, 10, true, false)]   // paused → stop
        public void MayCraftMore_Overshoot0_MatchesPlainOverload(int count, int target, bool paused, bool expected)
        {
            Assert.That(BatchPausePolicy.MayCraftMore(count, target, overshoot: 0, paused: paused), Is.EqualTo(expected));
            // And it equals the plain 3-arg overload for the same inputs.
            Assert.That(BatchPausePolicy.MayCraftMore(count, target, 0, paused),
                Is.EqualTo(BatchPausePolicy.MayCraftMore(count, target, paused)));
        }

        // ── overshoot Y: keep crafting up to X+Y, stop at/over X+Y ──────────────────────
        [Test]
        public void MayCraftMore_Overshoot_CraftsPastTarget_UpToTargetPlusY()
        {
            // X=10, Y=5 → continue while count < 15.
            Assert.That(BatchPausePolicy.MayCraftMore(10, 10, 5, false), Is.True);  // at X, overshoot allows more
            Assert.That(BatchPausePolicy.MayCraftMore(14, 10, 5, false), Is.True);  // one below X+Y
            Assert.That(BatchPausePolicy.MayCraftMore(15, 10, 5, false), Is.False); // exactly X+Y → stop
            Assert.That(BatchPausePolicy.MayCraftMore(16, 10, 5, false), Is.False); // past X+Y → stop
        }

        // ── paused with NO overshoot still stops (Y == 0 ⇒ vanilla latch honoured) ──────
        [Test]
        public void MayCraftMore_NoOvershoot_Paused_Stops()
        {
            Assert.That(BatchPausePolicy.MayCraftMore(7, 10, 0, paused: true), Is.False);
            Assert.That(BatchPausePolicy.MayCraftMore(0, 10, 0, paused: true), Is.False);
        }

        // ── paused with an active overshoot OVERRIDES the latch (finish to X+Y) ──────────
        // This is the load-bearing fix for issue #3: vanilla auto-latches `paused` the instant the
        // banked-inclusive count reaches X, which would otherwise stop the batch at X and silently defeat
        // the requested Y. Within the X..X+Y window the overshoot wins; the X+Y ceiling still stops it.
        [Test]
        public void MayCraftMore_Overshoot_Paused_OverridesLatch_UpToTargetPlusY()
        {
            Assert.That(BatchPausePolicy.MayCraftMore(10, 10, 5, paused: true), Is.True);  // at X, paused → still craft toward X+Y
            Assert.That(BatchPausePolicy.MayCraftMore(0, 10, 5, paused: true), Is.True);   // far below, paused → craft
            Assert.That(BatchPausePolicy.MayCraftMore(14, 10, 5, paused: true), Is.True);  // one below X+Y, paused → craft
            Assert.That(BatchPausePolicy.MayCraftMore(15, 10, 5, paused: true), Is.False); // reached X+Y → stop even within overshoot
            Assert.That(BatchPausePolicy.MayCraftMore(16, 10, 5, paused: true), Is.False); // past X+Y → stop
        }

        // ── negative overshoot clamps to 0 (treated as no overshoot) ────────────────────
        [Test]
        public void MayCraftMore_NegativeOvershoot_ClampedToZero()
        {
            // Negative Y must behave exactly like Y == 0: stop at X.
            Assert.That(BatchPausePolicy.MayCraftMore(10, 10, -5, false), Is.False);
            Assert.That(BatchPausePolicy.MayCraftMore(9, 10, -5, false), Is.True);
            Assert.That(BatchPausePolicy.MayCraftMore(9, 10, -5, false),
                Is.EqualTo(BatchPausePolicy.MayCraftMore(9, 10, 0, false)));
        }
    }
}
