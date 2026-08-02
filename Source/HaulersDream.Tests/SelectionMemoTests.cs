using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Drives <see cref="SelectionMemo"/> the way vanilla's Selector drives it, so the auto-open gate is pinned on
    /// whole click SEQUENCES rather than one predicate call at a time. Two shapes only:
    ///   • a plain click = <c>NotifyCleared(F); NotifySelected(id, F)</c> on the SAME frame (vanilla's
    ///     <c>SelectUnderMouse</c> runs <c>ClearSelection(); Select(obj)</c> for every click);
    ///   • a lone deselect (bare ground, or shift-deselecting the last selection) = <c>NotifyCleared(F)</c> with no
    ///     <c>NotifySelected</c> that frame.
    /// </summary>
    [TestFixture]
    public class SelectionMemoTests
    {
        /// <summary>One plain click on <paramref name="id"/>: the clear+select pair vanilla emits per click.</summary>
        private static bool Click(SelectionMemo memo, int id, int frame)
        {
            memo.NotifyCleared(frame);
            return memo.NotifySelected(id, frame);
        }

        [Test]
        public void FirstClick_Opens()
        {
            var memo = new SelectionMemo();
            Assert.That(Click(memo, 7, 10), Is.True);
        }

        [Test]
        public void ReClickingTheSameThing_DoesNotReOpen()
        {
            // THE #224 pin: you closed the Gear tab by hand, then clicked the same animal again. The clear that
            // vanilla emits for that click is same-frame, so it must not read as "you deselected it first".
            var memo = new SelectionMemo();
            Click(memo, 7, 10);
            Assert.That(Click(memo, 7, 11), Is.False);
        }

        [Test]
        public void ClickAwayToBareGround_ThenSameThing_Opens()
        {
            // Pins the don't-overwrite-an-unconsumed-gap rule: the lone clear on frame 11 must survive the clear
            // that the frame-12 click emits, or this reads as a plain re-click and returns false.
            var memo = new SelectionMemo();
            Click(memo, 7, 10);
            memo.NotifyCleared(11);
            Assert.That(Click(memo, 7, 12), Is.True);
        }

        [Test]
        public void DeselectPath_ReachesTheMemoInTheSameShapeAsABareGroundClick()
        {
            // NOT extra coverage, and deliberately not named as if it were: the memo cannot tell WHERE an emptied
            // selection came from — Selector.ClearSelection and a Selector.Deselect that left NumSelected == 0
            // both arrive as a bare NotifyCleared(frame) — so this is call-for-call the bare-ground case above.
            // It exists only to make that equivalence explicit for the next reader, who would otherwise go looking
            // for a Deselect-specific test.
            //
            // What is genuinely NEW in the Deselect path is the `NumSelected == 0` guard in
            // Patch_Selector_Deselect, and that lives in Verse glue: it reads a live Selector, so it CANNOT be
            // tested headlessly. It is verified by inspection, not by this fixture — do not add a fake test that
            // implies otherwise.
            var memo = new SelectionMemo();
            Click(memo, 7, 10);
            memo.NotifyCleared(11); // Selector.Deselect left NumSelected == 0
            Assert.That(Click(memo, 7, 12), Is.True);
        }

        [Test]
        public void ClickingBetweenTwoThings_OpensEachTime()
        {
            var memo = new SelectionMemo();
            Assert.That(Click(memo, 7, 10), Is.True);
            Assert.That(Click(memo, 8, 11), Is.True);
            Assert.That(Click(memo, 7, 12), Is.True);
        }

        [Test]
        public void MultiSelectInBetween_MakesTheNextSingleSelectionFresh()
        {
            // Invalidate() is what the patch calls when the selection stops being a single thing.
            var memo = new SelectionMemo();
            Click(memo, 7, 10);
            memo.Invalidate();
            Assert.That(Click(memo, 7, 11), Is.True);
        }

        [Test]
        public void Invalidate_DropsThePendingGapAndTheId()
        {
            var memo = new SelectionMemo();
            memo.NotifyCleared(10);
            memo.Invalidate();
            Assert.That(memo.GapPending, Is.False);
            Assert.That(memo.LastSelectedId, Is.EqualTo(-1));
        }

        [Test]
        public void Reset_ClearsEverything_AndTheNextClickOpens()
        {
            // Game load: thingIDNumber counters restart, so the memo must not carry an id into the new session.
            var memo = new SelectionMemo();
            Click(memo, 7, 10);
            memo.Reset();
            Assert.That(memo.LastSelectedId, Is.EqualTo(-1));
            Assert.That(memo.GapPending, Is.False);
            Assert.That(memo.GapFrame, Is.EqualTo(-1));
            Assert.That(Click(memo, 7, 11), Is.True);
        }

        [Test]
        public void DoubleClearInOneFrame_StillReadsAsTheSameClick()
        {
            // SelectInternal itself can call ClearSelection (its zone/plan-mixing branches) on top of the click's
            // own clear. Both land on this frame, so the pair is still "this click" and a re-select stays shut.
            var memo = new SelectionMemo();
            Click(memo, 7, 9);
            memo.NotifyCleared(10);
            memo.NotifyCleared(10);
            Assert.That(memo.NotifySelected(7, 10), Is.False);
        }

        [Test]
        public void MemoStaysCoherentWhenTheCallerDiscardsTheResult()
        {
            // The both-toggles-off path: the patch keeps driving the memo but ignores the answer, so a mid-game
            // toggle flip is correct on the very next click. The id must still have been recorded each time.
            var memo = new SelectionMemo();
            Click(memo, 7, 10);
            Click(memo, 7, 11);
            Assert.That(Click(memo, 7, 12), Is.False);
        }
    }
}
