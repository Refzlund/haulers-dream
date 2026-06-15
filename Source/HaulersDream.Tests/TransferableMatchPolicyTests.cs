using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
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
    }
}
