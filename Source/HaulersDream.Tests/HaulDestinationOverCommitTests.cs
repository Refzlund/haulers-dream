using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Several haulers committing to the same nearly-full destination — the symptom family that has now
    /// shipped as fixed three times and come back three times (#114, #248, Eversset's Steam report).
    ///
    /// <para>Every earlier fix was graded against its own decision rule in isolation, and each was genuinely
    /// correct in isolation. What none of them was ever graded against is several haulers running that rule
    /// at once, because the unit suite can observe a rule and never a race. These tests close that gap: the
    /// rule is an argument here, so the same scenario grades today's behaviour and a candidate replacement
    /// and the two can be compared instead of argued about.</para>
    ///
    /// <para>The invariant, above all: <b>the sum of units committed by all haulers toward a destination
    /// never exceeds that destination's free capacity.</b></para>
    /// </summary>
    [TestFixture]
    public class HaulDestinationOverCommitTests
    {
        // ── the two rules under contrast ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The behaviour that SHIPPED THREE TIMES: read the destination's free space and take what fits.
        ///
        /// <para>Correct for a single hauler and unanswerable for a colony — nothing has landed while N
        /// haulers are being planned, so the space reads as genuinely free to every one of them and each
        /// pockets a load for it. Kept as the harness's own control: a sweep that can no longer catch this
        /// has stopped measuring, and would then read exactly like success.</para>
        /// </summary>
        /// <param name="sight">The hauler's view; only free space and appetite are consulted.</param>
        /// <returns>Units committed.</returns>
        private static int Uncoordinated(HaulSight sight) => Math.Min(sight.Desire, sight.FreeCapacity);

        /// <summary>
        /// THE PRODUCTION RULE, bound directly rather than restated. <see cref="StorageCommitPolicy.Commit"/>
        /// is what <c>StorageCommitments.FreeUnitsFor</c> calls in the running game, so these scenarios grade
        /// the shipped decision and not a look-alike written for the test.
        ///
        /// <para><c>delivering: false</c> is the PLANNING mode — a pawn deciding a fresh pickup, which is
        /// what every hauler in these scenarios is doing. It is the strict side of the rule: a planning pawn
        /// subtracts its own in-flight load as well, because that load lands in the very space it is
        /// pricing.</para>
        /// </summary>
        /// <param name="sight">The hauler's view.</param>
        /// <returns>Units committed.</returns>
        private static int ProductionRule(HaulSight sight) => StorageCommitPolicy.Commit(sight, delivering: false);

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  1. The reported scenario, against today's behaviour.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        /*
            ────────────────────────────────────────────────────────────────────────────────
                        THIS TEST WAS [Ignore]d UNTIL PHASE 1 LANDED. IT IS NOW GREEN.
            ────────────────────────────────────────────────────────────────────────────────
            The scenario is the report: five haulers plan against a shelf missing three units, all five
            before any of them arrives, each reading the same three units of room. Under the rule HD
            shipped, fifteen units set off for three units of work; three land and twelve ride back.

            It failed on purpose so that the day the fix landed it would turn green BY ITSELF, and the fix
            could never again be "verified" by reasoning about a rule. What changed is not the assertion —
            it is verbatim — but what the scenario is run against: `ProductionRule` is
            StorageCommitPolicy.Commit, the decision the running game actually takes, bound rather than
            restated. The shipped-three-times rule survives under its own name as `Uncoordinated`, and
            Issue248_TheContrastInOneLine and Sweep_TheSameGridCatchesTodaysRule still REQUIRE it to fail,
            so a harness that quietly stopped measuring is still caught.

            Do not weaken the assertion, and do not delete the test.
            ────────────────────────────────────────────────────────────────────────────────
        */

        [Test]
        public void Issue248_FiveHaulersForThreeUnitsOfRoom_OverCommitUnderTodaysRule()
        {
            var scenario = HaulConcurrencySim.Interleave(pawnCount: 5, desirePerPawn: 75, decideAhead: 5);
            var trace = HaulConcurrencySim.Run(freeCapacity: 3, scenario, ProductionRule);

            Assert.That(trace.PeakSubscription, Is.LessThanOrEqualTo(trace.FreeCapacity),
                "the colony committed more than the destination can take; the excess is what rides back\n\n"
                + trace.Describe());
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  2. The same scenario, with the in-flight term. Proves the harness distinguishes the two rules
        //     rather than failing everything put in front of it.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        [Test]
        public void Issue248_TheSameScenario_HoldsOnceInFlightLoadsAreSubtracted()
        {
            var scenario = HaulConcurrencySim.Interleave(pawnCount: 5, desirePerPawn: 75, decideAhead: 5);
            var trace = HaulConcurrencySim.Run(freeCapacity: 3, scenario, ProductionRule);

            Assert.That(trace.PeakSubscription, Is.LessThanOrEqualTo(trace.FreeCapacity),
                "the invariant" + Newline + trace.Describe());
            Assert.That(trace.TotalCarriedBack, Is.EqualTo(0), "nobody should have to carry a load back");
            Assert.That(trace.Trips, Is.EqualTo(1), "one hauler sets off; the other four are told there is no room");
            Assert.That(trace.TotalDeposited, Is.EqualTo(3), "and the three units still get delivered");
        }

        [Test]
        public void Issue248_TheContrastInOneLine()
        {
            // The report made legible, both rules through the same scenario. Fifteen units set off against
            // three, versus three against three.
            var scenario = HaulConcurrencySim.Interleave(pawnCount: 5, desirePerPawn: 75, decideAhead: 5);

            Assert.That(HaulConcurrencySim.Run(3, scenario, Uncoordinated).PeakSubscription, Is.EqualTo(15));
            Assert.That(HaulConcurrencySim.Run(3, scenario, ProductionRule).PeakSubscription, Is.EqualTo(3));
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  3. The sweep: crew sizes 1..8 crossed with capacities, appetites and concurrency levels.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>Destination sizes worth crossing: empty, tiny, either side of a full stack, a couple of
        /// stacks with an awkward remainder, and roomy enough that nobody has to stand down.</summary>
        private static readonly int[] Capacities = { 0, 1, 2, 3, 5, 7, 74, 75, 76, 150, 151, 600 };

        /// <summary>Appetites: a single unit, a partial load, and a full stack.</summary>
        private static readonly int[] Appetites = { 1, 10, 75 };

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void Sweep_TheInvariantHoldsForEveryCrewSizeAndDestination(int pawnCount)
        {
            foreach (int capacity in Capacities)
            {
                foreach (int desire in Appetites)
                {
                    // 1 = perfectly serialised, 2 = the realistic middle, pawnCount = everyone plans first.
                    foreach (int decideAhead in new[] { 1, 2, pawnCount })
                    {
                        var scenario = HaulConcurrencySim.Interleave(pawnCount, desire, decideAhead);
                        var trace = HaulConcurrencySim.Run(capacity, scenario, ProductionRule);
                        string where = pawnCount + " haulers, " + capacity + " free, wanting " + desire
                            + " each, " + decideAhead + " planning ahead" + Newline + trace.Describe();

                        Assert.That(trace.PeakSubscription, Is.LessThanOrEqualTo(capacity),
                            "committed more than the destination can take — " + where);

                        foreach (var step in trace.Steps)
                            Assert.That(step.Decided, Is.GreaterThanOrEqualTo(0),
                                "a hauler was told to commit a negative amount — " + where);

                        Assert.That(trace.TotalCarriedBack, Is.EqualTo(0),
                            "a hauler set off and had to bring part of its load home — " + where);
                        Assert.That(trace.EmptyHandedTrips, Is.EqualTo(0),
                            "a hauler walked the whole way and put nothing down — " + where);

                        // The other half of the bargain: a clamp that overshoots into "nobody hauls" would
                        // satisfy everything above and be a worse bug than the one it replaced.
                        Assert.That(trace.TotalDeposited, Is.EqualTo(Math.Min(capacity, pawnCount * desire)),
                            "the destination was left short — " + where);
                    }
                }
            }
        }

        [Test]
        public void Sweep_TheSameGridCatchesTodaysRule()
        {
            // The sweep above is only worth anything if it can fail, so run the identical grid against the
            // uncoordinated rule and require it to be caught. Without this, a sweep that silently stopped
            // measuring would keep passing forever and read exactly like success.
            int violations = 0;
            int serialisedViolations = 0;
            foreach (int capacity in Capacities)
            {
                foreach (int desire in Appetites)
                {
                    foreach (int decideAhead in new[] { 1, 2, 8 })
                    {
                        var scenario = HaulConcurrencySim.Interleave(8, desire, decideAhead);
                        var trace = HaulConcurrencySim.Run(capacity, scenario, Uncoordinated);
                        if (trace.HoldsInvariant)
                            continue;
                        violations++;
                        if (decideAhead == 1)
                            serialisedViolations++;
                    }
                }
            }

            Assert.That(violations, Is.GreaterThan(0), "the sweep must be able to fail, or it proves nothing");
            Assert.That(serialisedViolations, Is.EqualTo(0),
                "and it must fail on CONCURRENCY, not on the rule: one hauler at a time is fine even uncoordinated");
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  4. The over-correction, pinned. "Nobody hauls" satisfies the invariant perfectly.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        [Test]
        public void RoomForOneLoad_ExactlyOneHaulerSetsOff()
        {
            var scenario = HaulConcurrencySim.Interleave(pawnCount: 8, desirePerPawn: 75, decideAhead: 8);
            var trace = HaulConcurrencySim.Run(freeCapacity: 75, scenario, ProductionRule);

            Assert.That(CommittingHaulers(trace), Is.EqualTo(1),
                "one hauler must take the room, and only one" + Newline + trace.Describe());
            Assert.That(trace.TotalDeposited, Is.EqualTo(75), "the shelf still gets filled");
            Assert.That(trace.TotalCarriedBack, Is.EqualTo(0));
        }

        [Test]
        public void RoomForTwoLoads_ExactlyTwoHaulersSetOff()
        {
            // The starvation guard from the other side: the fix must scale with the room, not stop at one.
            var scenario = HaulConcurrencySim.Interleave(pawnCount: 8, desirePerPawn: 75, decideAhead: 8);
            var trace = HaulConcurrencySim.Run(freeCapacity: 150, scenario, ProductionRule);

            Assert.That(CommittingHaulers(trace), Is.EqualTo(2), Newline + trace.Describe());
            Assert.That(trace.TotalDeposited, Is.EqualTo(150));
        }

        [Test]
        public void AHaulerIsNeverStarvedWhileRoomGoesUnclaimed()
        {
            // Stated as the property rather than a count: for every crew size, whatever the colony's
            // appetite and the room have in common still gets delivered.
            for (int pawnCount = 1; pawnCount <= 8; pawnCount++)
            {
                var scenario = HaulConcurrencySim.Interleave(pawnCount, 75, pawnCount);
                var trace = HaulConcurrencySim.Run(200, scenario, ProductionRule);
                Assert.That(trace.TotalDeposited, Is.EqualTo(Math.Min(200, pawnCount * 75)),
                    pawnCount + " haulers left room unfilled" + Newline + trace.Describe());
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  5. The two ways a claim ledger fails: released too early, and never released at all.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        [Test]
        public void AClaimReleasedBeforeItsCargoLands_BreaksTheInvariantAtTheDeposit()
        {
            // Hauler 1 commits the shelf's last three units, then its claim is dropped while it is still
            // walking — a job that ended, a re-task, a snapshot that stopped counting it. Hauler 2 now reads
            // three free units that are not free.
            var scenario = new List<HaulSimCommand>
            {
                HaulSimCommand.Decide(1, 75),
                HaulSimCommand.ReleaseCommitment(1),
                HaulSimCommand.Decide(2, 75),
                HaulSimCommand.Deposit(1),
                HaulSimCommand.Deposit(2)
            };
            var trace = HaulConcurrencySim.Run(freeCapacity: 3, scenario, ProductionRule);

            // The part that makes this class of bug so durable: BOTH decisions obeyed the rule exactly, and
            // each looked legal at the moment it was taken. The breach only surfaces when cargo lands.
            foreach (var step in trace.Steps)
                if (step.Action == HaulSimAction.Decide)
                    Assert.That(step.Committed,
                        Is.LessThanOrEqualTo(step.Sight.FreeCapacity - step.Sight.UnitsEnroute),
                        "every decision obeyed the rule it was given" + Newline + trace.Describe());

            Assert.That(trace.HoldsInvariant, Is.False,
                "and the invariant still broke, at the deposit" + Newline + trace.Describe());
            Assert.That(trace.PeakInbound, Is.GreaterThan(trace.FreeCapacity),
                "six units were on their way to a shelf that could take three");
            Assert.That(trace.TotalCarriedBack, Is.EqualTo(3), "so three of them came back");
            Assert.That(trace.EmptyHandedTrips, Is.EqualTo(1));
        }

        [Test]
        public void AClaimNeverReleased_StarvesTheDestinationInstead()
        {
            // The opposite failure, and the reason a ledger cannot simply hold claims until they settle.
            // Hauler 1 commits and then loses its load without the accounting hearing about it. Its claim
            // stays live, so every later hauler is correctly told there is no room — forever.
            var scenario = new List<HaulSimCommand>
            {
                HaulSimCommand.Decide(1, 75),
                HaulSimCommand.DropCargo(1),
                HaulSimCommand.Decide(2, 75),
                HaulSimCommand.Decide(3, 75),
                HaulSimCommand.Deposit(2),
                HaulSimCommand.Deposit(3)
            };
            var trace = HaulConcurrencySim.Run(freeCapacity: 3, scenario, ProductionRule);

            Assert.That(trace.HoldsInvariant, Is.True,
                "a phantom claim never over-commits — it under-delivers" + Newline + trace.Describe());
            Assert.That(trace.TotalDeposited, Is.EqualTo(0),
                "three units of room, two willing haulers, and nothing moved" + Newline + trace.Describe());
            Assert.That(LastStep(trace).LiveCommitments, Is.EqualTo(3), "the claim is still holding the shelf");
        }

        [Test]
        public void ReleasingAnAbandonedClaim_LetsTheDestinationFillAgain()
        {
            // The same run with the claim released when the load is lost. This is why the mod derives its
            // in-flight figure from live jobs rather than persisting claims: a claim that cannot outlive its
            // job cannot become a phantom.
            var scenario = new List<HaulSimCommand>
            {
                HaulSimCommand.Decide(1, 75),
                HaulSimCommand.DropCargo(1),
                HaulSimCommand.ReleaseCommitment(1),
                HaulSimCommand.Decide(2, 75),
                HaulSimCommand.Decide(3, 75),
                HaulSimCommand.Deposit(2),
                HaulSimCommand.Deposit(3)
            };
            var trace = HaulConcurrencySim.Run(freeCapacity: 3, scenario, ProductionRule);

            Assert.That(trace.HoldsInvariant, Is.True, Newline + trace.Describe());
            Assert.That(trace.TotalDeposited, Is.EqualTo(3),
                "the shelf fills again once the abandoned claim is let go" + Newline + trace.Describe());
            Assert.That(trace.TotalCarriedBack, Is.EqualTo(0));
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  6. VISIBILITY. The fourth root cause, made executable: the rule was right and what it could SEE
        //     was wrong. The ledger is immediate; a per-tick snapshot is not, and cannot be repaired.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>Crew sizes the design named, from a pair up to a colony that plans in a crowd.</summary>
        private static readonly int[] VisibilityCrews = { 2, 3, 6, 10 };

        /// <summary>Appetites: one unit, and a full stack.</summary>
        private static readonly int[] VisibilityAppetites = { 1, 75 };

        /// <summary>Destinations: full, a sliver, either side of a stack, and two stacks.</summary>
        private static readonly int[] VisibilityCapacities = { 0, 1, 2, 74, 75, 150 };

        [Test]
        public void Immediate_TheProductionRuleHoldsTheInvariantAndStillHauls()
        {
            // Every hauler plans before anything lands — the reported shape — across the whole grid.
            foreach (int crew in VisibilityCrews)
            {
                foreach (int desire in VisibilityAppetites)
                {
                    foreach (int capacity in VisibilityCapacities)
                    {
                        var scenario = HaulConcurrencySim.Interleave(crew, desire, crew);
                        var trace = HaulConcurrencySim.Run(capacity, scenario, ProductionRule,
                            EnrouteVisibility.Immediate);
                        string where = crew + " haulers, " + capacity + " free, wanting " + desire
                            + " each" + Newline + trace.Describe();

                        Assert.That(trace.PeakSubscription, Is.LessThanOrEqualTo(capacity),
                            "committed more than the destination can take — " + where);

                        // Safety alone is not the bargain. A rule that stands every hauler down satisfies
                        // the line above perfectly and stops the colony hauling, which is why the harness
                        // grades progress in the same breath.
                        if (capacity >= 1)
                            Assert.That(CommittingHaulers(trace), Is.GreaterThanOrEqualTo(1),
                                "room went unclaimed and nobody set off — " + where);
                        Assert.That(trace.TotalDeposited, Is.EqualTo(Math.Min(capacity, crew * desire)),
                            "the destination was left short — " + where);
                    }
                }
            }
        }

        [Test]
        public void TickSnapshot_BreaksTheSameRule_WhichIsWhyTheLedgerCannotBeASnapshot()
        {
            /*
                This is an EXECUTABLE STATEMENT OF WHY, not a wish. A pawn that cannot see a commitment
                made earlier in the same tick has no information with which to avoid duplicating it, so no
                decision rule can hold the invariant under tick-snapshot visibility — the only rules that
                manage it blindly are the ones that starve the colony. The production rule is therefore
                REQUIRED to fail here.

                If this test ever goes green, one of two things happened and both matter: the simulation
                started repairing bad answers (the failure mode that let this bug pass review three times),
                or something behind the seam began memoising commitments per tick again. The second is what
                scripts/check-storage-commit-seam.ts rule 5 fails the build over.
            */
            int violations = 0;
            foreach (int crew in VisibilityCrews)
            {
                foreach (int desire in VisibilityAppetites)
                {
                    foreach (int capacity in VisibilityCapacities)
                    {
                        var scenario = HaulConcurrencySim.Interleave(crew, desire, crew);
                        var trace = HaulConcurrencySim.Run(capacity, scenario, ProductionRule,
                            EnrouteVisibility.TickSnapshot);

                        // The arithmetic, stated rather than assumed: blind to every commitment, each
                        // hauler takes min(appetite, room), so the colony subscribes crew times that. The
                        // invariant breaks EXACTLY where that exceeds the room, and holds everywhere else
                        // — not because the rule coped, but because there was never enough appetite in the
                        // colony to over-fill the destination in the first place. Asserting equality (not
                        // "at least one failure") is what stops a simulation that quietly started
                        // repairing answers from reading as success.
                        bool mustOverCommit = capacity >= 1
                            && (long)crew * Math.Min(desire, capacity) > capacity;
                        Assert.That(trace.HoldsInvariant, Is.EqualTo(!mustOverCommit),
                            crew + " haulers, " + capacity + " free, wanting " + desire + " each"
                            + Newline + trace.Describe());
                        if (!trace.HoldsInvariant)
                            violations++;
                    }
                }
            }

            Assert.That(violations, Is.GreaterThan(0),
                "a grid in which nothing over-commits under a snapshot is not measuring anything");
        }

        [Test]
        public void TheOnlyDifferenceBetweenThoseTwoRunsIsWhatTheHaulerCouldSee()
        {
            // Same rule, same script, same destination — and the two visibilities part company. Stated as
            // one comparison so a reader never has to take on faith that the grids above differ for the
            // reason claimed rather than because they were written differently.
            var scenario = HaulConcurrencySim.Interleave(pawnCount: 5, desirePerPawn: 75, decideAhead: 5);

            Assert.That(HaulConcurrencySim.Run(3, scenario, ProductionRule, EnrouteVisibility.Immediate)
                .PeakSubscription, Is.EqualTo(3));
            Assert.That(HaulConcurrencySim.Run(3, scenario, ProductionRule, EnrouteVisibility.TickSnapshot)
                .PeakSubscription, Is.EqualTo(15));
        }

        [Test]
        public void DeliveringPawnsAreNeverStranded_TheAntiChurnHalfOfTheRule()
        {
            // A pawn already holding cargo asks a different question: not "may I take more" but "where do I
            // put what I have". Charging it for its own claim is how a hauler ends up unable to put its
            // load down anywhere — the carry-back the reports describe. Pinned on the rule directly,
            // because the harness only ever plays planning pawns.
            var holdingItsOwnClaim = new HaulSight(
                pawnId: 1, tick: 0, freeCapacity: 3, unitsEnroute: 0, ownUnitsEnroute: 3, desire: 3);

            Assert.That(StorageCommitPolicy.Commit(holdingItsOwnClaim, delivering: true), Is.EqualTo(3),
                "a delivering pawn is entitled to the space it already reserved");
            Assert.That(StorageCommitPolicy.Commit(holdingItsOwnClaim, delivering: false), Is.EqualTo(0),
                "and a pawn planning a FRESH pickup must not price the same three units twice");
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────────

        /// <summary>Haulers that actually committed something — the ones that would have been given a job.
        /// Counted rather than assumed, because "how many set off" is the number a player watches.</summary>
        /// <param name="trace">A finished run.</param>
        /// <returns>How many distinct haulers committed a positive amount.</returns>
        private static int CommittingHaulers(HaulSimTrace trace)
        {
            var committed = new HashSet<int>();
            foreach (var step in trace.Steps)
                if (step.Action == HaulSimAction.Decide && step.Committed > 0)
                    committed.Add(step.PawnId);
            return committed.Count;
        }

        /// <summary>The final state of a run, for asserting on what was still outstanding when it ended.</summary>
        /// <param name="trace">A finished run; must have at least one step.</param>
        /// <returns>The last recorded step.</returns>
        private static HaulSimStep LastStep(HaulSimTrace trace) => trace.Steps[trace.Steps.Count - 1];

        /// <summary>Separator that puts a rendered trace on its own lines inside an assertion message, so
        /// the table's column alignment survives the report NUnit prints.</summary>
        private static string Newline => Environment.NewLine + Environment.NewLine;
    }
}
