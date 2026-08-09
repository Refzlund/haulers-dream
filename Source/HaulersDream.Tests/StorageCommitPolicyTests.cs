using System;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The production storage-commitment rule at its boundaries. The concurrency harness grades this same
    /// function under several haulers at once; these are the single-decision edges that harness cannot
    /// reach — the unknown destination, the sentinel meeting a subtraction, and the planning/delivering
    /// split that decides whether a pawn competes with itself.
    /// </summary>
    [TestFixture]
    public class StorageCommitPolicyTests
    {
        /// <summary>Build one hauler's view. Named arguments at every call site, because five bare integers
        /// in a row is exactly how "free" and "enroute" get transposed.</summary>
        /// <param name="free">Units the destination can physically still take.</param>
        /// <param name="others">Units other haulers have committed and not delivered.</param>
        /// <param name="own">This hauler's own undelivered commitment.</param>
        /// <param name="desire">What it would take against a bottomless destination.</param>
        /// <returns>The view.</returns>
        private static HaulSight Sight(int free, int others, int own, int desire)
            => new HaulSight(pawnId: 1, tick: 0, freeCapacity: free, unitsEnroute: others,
                ownUnitsEnroute: own, desire: desire);

        // ── the unknown destination ───────────────────────────────────────────────────────────────────

        [Test]
        public void AnUnmeasuredDestinationPassesTheAppetiteThrough()
        {
            // Reading "not measured" as "full" satisfies the invariant perfectly and stops the colony
            // hauling to any stockpile too large or too odd to price — a worse bug than the one this
            // replaces. Unknown stays unknown, and it survives a live in-flight figure.
            Assert.That(StorageCommitPolicy.Commit(Sight(int.MaxValue, 0, 0, 75), delivering: false),
                Is.EqualTo(75));
            Assert.That(StorageCommitPolicy.Commit(Sight(int.MaxValue, 500, 500, 75), delivering: false),
                Is.EqualTo(75), "an unknown minus a known is still unknown, never a negative");
        }

        // ── the planning / delivering split ───────────────────────────────────────────────────────────

        [Test]
        public void APlanningPawnSubtractsItsOwnInFlightLoad()
        {
            // The "pawn competes with itself" trap in reverse: its in-flight load is going to land in the
            // very space it is now pricing, so planning a second load against the same units is how one
            // hauler alone over-fills a shelf across two trips.
            Assert.That(StorageCommitPolicy.Commit(Sight(10, 0, 10, 10), delivering: false), Is.EqualTo(0));
            Assert.That(StorageCommitPolicy.Commit(Sight(10, 0, 4, 10), delivering: false), Is.EqualTo(6));
        }

        [Test]
        public void ADeliveringPawnKeepsTheSpaceItAlreadyReserved()
        {
            // The anti-churn guarantee: a pawn holding goods can always find a home, so the ledger can
            // never strand cargo or force a carry-back.
            Assert.That(StorageCommitPolicy.Commit(Sight(10, 0, 10, 10), delivering: true), Is.EqualTo(10));
            Assert.That(StorageCommitPolicy.Commit(Sight(10, 4, 10, 10), delivering: true), Is.EqualTo(6),
                "but it still yields to OTHER haulers' commitments");
        }

        // ── boundaries ────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void NoAppetiteMeansNoCommitment()
        {
            Assert.That(StorageCommitPolicy.Commit(Sight(100, 0, 0, 0), delivering: false), Is.EqualTo(0));
            Assert.That(StorageCommitPolicy.Commit(Sight(100, 0, 0, -5), delivering: false), Is.EqualTo(0));
        }

        [Test]
        public void AFullOrNegativeDestinationTakesNothing()
        {
            Assert.That(StorageCommitPolicy.Commit(Sight(0, 0, 0, 75), delivering: false), Is.EqualTo(0));
            Assert.That(StorageCommitPolicy.Commit(Sight(-4, 0, 0, 75), delivering: true), Is.EqualTo(0));
        }

        [Test]
        public void NegativeInFlightFiguresReadAsNothingInFlight()
        {
            // The rule never trusts its inputs to be sane; a negative would otherwise ADD room that does
            // not exist, which is the direction that causes the reported bug.
            Assert.That(StorageCommitPolicy.Commit(Sight(10, -50, -50, 75), delivering: false), Is.EqualTo(10));
        }

        [Test]
        public void TheTwoInFlightFiguresSumWithoutOverflowing()
        {
            // A plain `others + mine` here wraps to a large negative, which FreeAfterEnroute reads as
            // "nothing in flight" and hands the pawn the whole destination — a full shelf that the entire
            // colony is then told to haul into.
            Assert.That(StorageCommitPolicy.Commit(Sight(10, int.MaxValue, int.MaxValue, 75), delivering: false),
                Is.EqualTo(0));
            Assert.That(StorageCommitPolicy.Commit(Sight(int.MaxValue - 1, int.MaxValue, int.MaxValue, 75),
                delivering: false), Is.EqualTo(0));
        }

        [Test]
        public void NeverCommitsMoreThanTheAppetiteOrThanTheRoom()
        {
            // Stated as a property over the grid rather than a handful of cases, because both halves have
            // been broken separately before: one fix clamped to room and forgot the appetite, another
            // clamped the appetite and forgot the room.
            foreach (int free in new[] { 0, 1, 2, 5, 74, 75, 150 })
            {
                foreach (int others in new[] { 0, 1, 3, 75, 1000 })
                {
                    foreach (int own in new[] { 0, 2, 75 })
                    {
                        foreach (int desire in new[] { 1, 10, 75 })
                        {
                            foreach (bool delivering in new[] { false, true })
                            {
                                int committed = StorageCommitPolicy.Commit(
                                    Sight(free, others, own, desire), delivering);
                                string where = $"free {free}, others {others}, own {own}, want {desire}, "
                                    + $"delivering {delivering}";

                                Assert.That(committed, Is.GreaterThanOrEqualTo(0), where);
                                Assert.That(committed, Is.LessThanOrEqualTo(desire), where);
                                Assert.That(committed, Is.LessThanOrEqualTo(free), where);
                                // The gate asks the same question with the same call — `FreeUnitsFor(...) <= 0`
                                // is literally this rule at appetite 1 — so "is there room" and "how much" cannot
                                // disagree by construction. Pinned here as the property rather than as a second
                                // method that could drift.
                                Assert.That(committed > 0, Is.EqualTo(Math.Min(desire, free) > 0 && free > 0
                                    && StorageCommitPolicy.Commit(Sight(free, others, own, 1), delivering) > 0),
                                    "the gate and the counter must never disagree about whether there is room — "
                                    + where);
                            }
                        }
                    }
                }
            }
        }

        // ── the saturating sum this all rests on ──────────────────────────────────────────────────────

        [Test]
        public void SaturatingAddClampsAtBothEnds()
        {
            Assert.That(DestinationEnroutePolicy.SaturatingAdd(int.MaxValue, 1), Is.EqualTo(int.MaxValue));
            Assert.That(DestinationEnroutePolicy.SaturatingAdd(int.MinValue, -1), Is.EqualTo(int.MinValue));
            Assert.That(DestinationEnroutePolicy.SaturatingAdd(7, -9), Is.EqualTo(-2),
                "and it does NOT floor at zero — a caller that sums signed deltas keeps its sign");
            Assert.That(DestinationEnroutePolicy.SaturatingAdd(0, 0), Is.EqualTo(0));
        }
    }
}
