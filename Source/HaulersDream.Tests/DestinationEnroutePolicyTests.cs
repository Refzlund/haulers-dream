using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The cross-pawn over-haul arithmetic (issue #114). Two properties matter and both are asserted here:
    /// N pawns planning one after another can never collectively claim MORE than the destination's free space
    /// (the bug), and they must still collectively claim ALL of it when they want it (a clamp that overshoots
    /// into "nobody hauls" would be a worse regression than the original).
    /// </summary>
    [TestFixture]
    public class DestinationEnroutePolicyTests
    {
        // ── int.MaxValue means "not known to be limited" and must survive untouched ────────────────────

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(500)]
        [TestCase(int.MaxValue)]
        [TestCase(-1)]
        public void UnknownSpace_PassesThroughUnchanged(int enroute)
        {
            // An unbounded/unpriced destination must stay unbounded: subtracting from it would turn a large but
            // unmeasured stockpile into an apparently full one and stop every haul to it.
            Assert.That(DestinationEnroutePolicy.FreeAfterEnroute(int.MaxValue, enroute), Is.EqualTo(int.MaxValue));
        }

        // ── ordinary subtraction ──────────────────────────────────────────────────────────────────────

        [Test]
        public void PartiallyClaimed_ReportsTheRemainder()
        {
            Assert.That(DestinationEnroutePolicy.FreeAfterEnroute(75, 20), Is.EqualTo(55));
        }

        [Test]
        public void NothingEnroute_ReportsTheWholeSpace()
        {
            Assert.That(DestinationEnroutePolicy.FreeAfterEnroute(75, 0), Is.EqualTo(75));
        }

        // ── the boundary the bug lives on ─────────────────────────────────────────────────────────────

        [Test]
        public void ExactFit_LeavesNothing()
        {
            // Another pawn is already bringing exactly what fits: this one must stand down rather than pocket a
            // stack it could only carry back.
            Assert.That(DestinationEnroutePolicy.FreeAfterEnroute(75, 75), Is.EqualTo(0));
        }

        [Test]
        public void OverClaimed_ClampsToZeroAndNeverGoesNegative()
        {
            Assert.That(DestinationEnroutePolicy.FreeAfterEnroute(3, 1500), Is.EqualTo(0));
        }

        [Test]
        public void OverClaimedByTheLargestPossibleAmount_DoesNotOverflow()
        {
            // int.MaxValue enroute against a small space: the difference must never be computed as a wrapped
            // negative. (int.MaxValue is the "unknown" sentinel for SPACE, but it is a legitimate value here.)
            Assert.That(DestinationEnroutePolicy.FreeAfterEnroute(10, int.MaxValue), Is.EqualTo(0));
        }

        // ── defensive inputs: neither side may produce a negative or a wrapped result ──────────────────

        [TestCase(0, 0)]
        [TestCase(0, 40)]
        [TestCase(-5, 0)]
        [TestCase(-5, 40)]
        [TestCase(int.MinValue, 40)]
        [TestCase(int.MinValue, int.MaxValue)]
        public void NoSpace_ReportsNone(int spaceLeft, int enroute)
        {
            Assert.That(DestinationEnroutePolicy.FreeAfterEnroute(spaceLeft, enroute), Is.EqualTo(0));
        }

        [TestCase(-1)]
        [TestCase(-1000)]
        [TestCase(int.MinValue)]
        public void NegativeEnroute_ReadsAsNothingEnroute(int enroute)
        {
            // A negative in-flight count is nonsense (the scan sums non-negative stack counts), but it must not
            // ADD space — that would hand a pawn more room than the destination has.
            Assert.That(DestinationEnroutePolicy.FreeAfterEnroute(75, enroute), Is.EqualTo(75));
        }

        [Test]
        public void ResultIsNeverNegative_AcrossAWideGrid()
        {
            foreach (int space in new[] { int.MinValue, -7, 0, 1, 13, 75, 100000, int.MaxValue - 1, int.MaxValue })
            {
                foreach (int enroute in new[] { int.MinValue, -7, 0, 1, 13, 75, 100000, int.MaxValue })
                    Assert.That(DestinationEnroutePolicy.FreeAfterEnroute(space, enroute), Is.GreaterThanOrEqualTo(0),
                        $"FreeAfterEnroute({space}, {enroute}) must never be negative");
            }
        }

        // ── the reported scenario: N pawns planning against one nearly-full stockpile ──────────────────

        /// <summary>
        /// Plan <paramref name="pawnCount"/> pawns one after another against a destination with
        /// <paramref name="freeSpace"/> room, each wanting <paramref name="perPawnDesire"/> units. Each pawn
        /// sees what the pawns before it committed (nothing has LANDED yet — that is the whole point) and takes
        /// what the policy says is genuinely left.
        /// </summary>
        /// <param name="freeSpace">The destination's free space, unchanging: no load arrives during planning.</param>
        /// <param name="perPawnDesire">Units each pawn would pocket if the destination looked empty (a full stack).</param>
        /// <param name="pawnCount">How many pawns plan before any of them arrives.</param>
        /// <returns>The total units the colony committed to that destination.</returns>
        private static int PlanColony(int freeSpace, int perPawnDesire, int pawnCount)
        {
            int enroute = 0;
            int committed = 0;
            for (int i = 0; i < pawnCount; i++)
            {
                int free = DestinationEnroutePolicy.FreeAfterEnroute(freeSpace, enroute);
                int take = Math.Min(perPawnDesire, free);
                if (take <= 0)
                    continue; // this pawn stands down — vanilla's own space-clamped haul still stands
                committed += take;
                enroute += take; // now in flight, so the pawns planned after it can see the commitment
            }
            return committed;
        }

        // (freeSpace, perPawnDesire, pawnCount) — the reported shape and its neighbours.
        private static IEnumerable<TestCaseData> ColonyCases()
        {
            yield return new TestCaseData(3, 75, 8).SetName("the report: 8 haulers, full stacks, 3 slots of room");
            yield return new TestCaseData(75, 75, 8).SetName("exactly one stack of room, 8 haulers");
            yield return new TestCaseData(150, 75, 8).SetName("two stacks of room, 8 haulers");
            yield return new TestCaseData(160, 75, 8).SetName("two stacks plus a remainder, 8 haulers");
            yield return new TestCaseData(1000, 75, 8).SetName("plenty of room, 8 haulers, nobody stands down");
            yield return new TestCaseData(0, 75, 8).SetName("no room at all, 8 haulers");
            yield return new TestCaseData(3, 1, 8).SetName("single-unit stacks, 3 slots of room");
            yield return new TestCaseData(50, 75, 1).SetName("a lone hauler is still clamped to the room");
        }

        [TestCaseSource(nameof(ColonyCases))]
        public void NPawnsCannotCollectivelyClaimMoreThanTheFreeSpace(int freeSpace, int perPawnDesire, int pawnCount)
        {
            int committed = PlanColony(freeSpace, perPawnDesire, pawnCount);
            Assert.That(committed, Is.LessThanOrEqualTo(freeSpace),
                "the colony must never commit more than the destination can take — the excess is what gets carried back");
        }

        [TestCaseSource(nameof(ColonyCases))]
        public void NPawnsStillFillEverythingTheyCan(int freeSpace, int perPawnDesire, int pawnCount)
        {
            // The clamp must not overshoot into "nobody hauls": whatever the colony's appetite and the room have
            // in common still gets committed. (Whole stacks only, so the last pawn takes the remainder.)
            int committed = PlanColony(freeSpace, perPawnDesire, pawnCount);
            Assert.That(committed, Is.EqualTo(Math.Min(freeSpace, perPawnDesire * pawnCount)),
                "the destination must still get filled — a clamp that leaves room unused would be a worse bug");
        }

        [TestCaseSource(nameof(ColonyCases))]
        public void WithoutTheEnrouteTerm_TheColonyOverCommits(int freeSpace, int perPawnDesire, int pawnCount)
        {
            // The contrast this policy exists to remove: read the destination's free space with no in-flight
            // term and every pawn independently claims the same room. This is the reported behaviour.
            int naive = 0;
            for (int i = 0; i < pawnCount; i++)
                naive += Math.Min(perPawnDesire, freeSpace);
            Assert.That(naive, Is.GreaterThanOrEqualTo(PlanColony(freeSpace, perPawnDesire, pawnCount)),
                "the naive per-pawn read can only ever claim at least as much as the coordinated one");
        }

        [Test]
        public void TheReportedNumbers_EightHaulersForThreeSlotsOfRoom()
        {
            // The report made legible. Three slots of room, eight haulers, a 75-unit stack each:
            //   * no clamp at all (the destination read as unlimited) — 8 x 75 = 600 units set off, 3 land,
            //     597 ride back to where they came from;
            //   * per-pawn clamp only — every pawn independently reads the same 3 slots, so 8 x 3 = 24 units
            //     set off for 3 slots: still eight trips for three units of work;
            //   * with the in-flight term — one pawn takes 3 and the other seven stand down.
            Assert.That(8 * 75, Is.EqualTo(600));
            Assert.That(8 * Math.Min(75, 3), Is.EqualTo(24));
            Assert.That(PlanColony(freeSpace: 3, perPawnDesire: 75, pawnCount: 8), Is.EqualTo(3));
        }
    }
}
