using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Unit tests for the PURE count-ladder ranker <see cref="TransferableMatchPolicy.ChooseBestMatchIndex"/>
    /// (exact → largest-absorbing → smallest-partial). NOTE: as of the MF-1 desync fix, the manifest-decrement
    /// intercept NO LONGER uses this ranker — <see cref="Global.FindBestMatchFor"/> now delegates entry selection
    /// directly to vanilla's <c>TransferableUtility.TransferableMatchingDesperate</c> (the same 3-tier
    /// identity→TransferAsOne→def-only matcher <c>CompTransporter/MapPortal.SubtractFromToLoadList</c> and the deposit
    /// CLAMP use), so clamp and decrement can never disagree. The stuff/quality + def-only discrimination is therefore
    /// a VERSE-SIDE concern (decompile-verified, mirrored from vanilla), NOT something these pure tests exercise.
    /// These tests remain as documentation/regression for the ranker type itself, which is still public API.
    /// </summary>
    [TestFixture]
    public class TransferableMatchPolicyTests
    {
        private static TransferableMatchPolicy.Candidate C(int index, int remaining)
            => new TransferableMatchPolicy.Candidate(index, remaining);

        [Test]
        public void Exact_Wins()
        {
            // Deposit 25: the entry whose remaining is exactly 25 wins over a larger one that could also absorb it.
            var cands = new List<TransferableMatchPolicy.Candidate> { C(0, 60), C(1, 25), C(2, 40) };
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(cands, 25), Is.EqualTo(1));
        }

        [Test]
        public void NoExact_TieGoesToLargestAbsorbing()
        {
            // Deposit 20, no exact: among entries ≥20 (60 and 40) the LARGEST (60, index 0) wins.
            var cands = new List<TransferableMatchPolicy.Candidate> { C(0, 60), C(1, 40) };
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(cands, 20), Is.EqualTo(0));
        }

        [Test]
        public void AllPartial_SmallestRemainingWins()
        {
            // Deposit 50, every entry is smaller (a multi-stack deposit spanning entries): finish the SMALLEST (10)
            // first so its leftover rolls cleanly to the next step.
            var cands = new List<TransferableMatchPolicy.Candidate> { C(0, 30), C(1, 10), C(2, 20) };
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(cands, 50), Is.EqualTo(1));
        }

        [Test]
        public void EmptySet_ReturnsMinusOne()
        {
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(
                new List<TransferableMatchPolicy.Candidate>(), 10), Is.EqualTo(-1));
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(null, 10), Is.EqualTo(-1));
        }

        [Test]
        public void IgnoresNonPositiveRemaining()
        {
            // A 0/negative-remaining entry is skipped; the only real candidate (index 2) is chosen.
            var cands = new List<TransferableMatchPolicy.Candidate> { C(0, 0), C(1, -3), C(2, 40) };
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(cands, 100), Is.EqualTo(2));
        }

        [Test]
        public void ExactPreferredOverLargerAndPartial()
        {
            // Mixed: a partial (15), a larger absorbing (90), and an exact (30) → exact wins.
            var cands = new List<TransferableMatchPolicy.Candidate> { C(0, 15), C(1, 90), C(2, 30) };
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(cands, 30), Is.EqualTo(2));
        }

        // --------------------------------------------------------------------------------------------------------
        // Legacy MF-1 ranker scenarios. IMPORTANT: these do NOT verify the stuff/quality/def-only discriminator that
        // makes the LIVE decrement correct — that discriminator is vanilla's TransferableUtility.
        // TransferableMatchingDesperate (3-tier: identity → TransferAsOne variant → def-only fallback), which
        // Global.FindBestMatchFor now calls DIRECTLY. The decrement, the deposit clamp (PortalRemainingFor /
        // MemberRemainingFor) and the routing gate (LoadTransportersAdapter.WantsThing / ActiveMemberFor) all share
        // that one matcher, so they cannot disagree (verified by decompiling SubtractFromToLoadList — both vanilla
        // overloads pick the entry via the same TransferableMatchingDesperate call). The tests below only assert the
        // pure ranker's count-ladder behaviour over a pre-selected candidate list; they are NOT evidence that the
        // mixed-quality manifest is decremented correctly. (The cases are written as "if the ranker were fed only the
        // matching entries, this is what it would pick" — useful as ranker regression, not as a fix proof.)
        // --------------------------------------------------------------------------------------------------------

        [Test]
        public void Ranker_PartialOverSmallerWhenFedOneEntry()
        {
            // A single candidate (remaining 10) vs a deposit of 12 → the sole partial, index 0. (Ranker only; the live
            // path's "pick the normal-armor entry, not a smaller good-armor entry" is vanilla's matcher, not this.)
            var oneEntry = new List<TransferableMatchPolicy.Candidate> { C(0, 10) };
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(oneEntry, 12), Is.EqualTo(0));
        }

        [Test]
        public void Ranker_ExactWhenFedOneEntry()
        {
            // Single candidate at a non-zero index (remaining 5), deposit 5 → exact → that index. (Ranker only.)
            var oneEntry = new List<TransferableMatchPolicy.Candidate> { C(1, 5) };
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(oneEntry, 5), Is.EqualTo(1));
        }

        [Test]
        public void Ranker_SingleCandidate_FullExactPartialAllReturnIt()
        {
            // One candidate (index 0, remaining 10) is always chosen regardless of the deposited count — exact,
            // larger-absorbing, or partial. Documents that a single-entry-per-def manifest is unambiguous. (Ranker
            // only; on the live path TransferableMatchingDesperate likewise returns that one entry.)
            var single = new List<TransferableMatchPolicy.Candidate> { C(0, 10) };
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(single, 10), Is.EqualTo(0)); // exact
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(single, 4), Is.EqualTo(0));  // larger-absorbing
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(single, 25), Is.EqualTo(0)); // partial
        }

        [Test]
        public void Ranker_TwoCandidates_LargestAbsorbingWins()
        {
            // Two candidates (remaining 8 @index0 and 3 @index2), deposit 5: no exact; 8 absorbs the deposit fully and
            // 3 does not → the largest-absorbing (8 @index0) wins. (Ranker only — the live path never feeds the ranker
            // two entries for one SubtractFromToLoadList call, since each runs against a single member's leftToLoad.)
            var two = new List<TransferableMatchPolicy.Candidate> { C(0, 8), C(2, 3) };
            Assert.That(TransferableMatchPolicy.ChooseBestMatchIndex(two, 5), Is.EqualTo(0));
        }
    }
}
