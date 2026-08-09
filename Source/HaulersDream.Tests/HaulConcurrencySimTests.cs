using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The concurrency harness itself (<see cref="HaulConcurrencySim"/>), graded before anything is graded
    /// with it. A simulation that quietly repairs a bad decision, or that reports the same run differently
    /// twice, would hand every future over-haul fix a green light it did not earn — which is exactly the
    /// failure this harness exists to end, so it gets checked first and by name.
    ///
    /// <para>The scenario suite that uses it lives in <c>HaulDestinationOverCommitTests</c>.</para>
    /// </summary>
    [TestFixture]
    public class HaulConcurrencySimTests
    {
        // ── degenerate rules, each isolating one property of the machinery ────────────────────────────

        /// <summary>A rule with no restraint at all: takes what it wants and ignores the destination.</summary>
        private static int TakeWhateverIsWanted(HaulSight sight) => sight.Desire;

        /// <summary>A rule that never hauls. The over-correction every fix to this bug risks becoming.</summary>
        private static int StandDown(HaulSight sight) => 0;

        /// <summary>A broken rule answering nonsense, used to prove the trace shows it rather than tidies it.</summary>
        private static int AnswerNonsense(HaulSight sight) => -5;

        // ── the deposit arithmetic: what fits lands, what does not rides back ─────────────────────────

        [Test]
        public void Deposit_LandsWhatFitsAndCarriesBackTheRest()
        {
            var script = new List<HaulSimCommand> { HaulSimCommand.Decide(1, 20), HaulSimCommand.Deposit(1) };
            var trace = HaulConcurrencySim.Run(freeCapacity: 3, script, TakeWhateverIsWanted);

            Assert.That(trace.TotalDeposited, Is.EqualTo(3), "only what fits can land");
            Assert.That(trace.TotalCarriedBack, Is.EqualTo(17), "the rest is the wasted work, in units");
            Assert.That(trace.Trips, Is.EqualTo(1));
            Assert.That(trace.EmptyHandedTrips, Is.EqualTo(0), "it did put something down");
        }

        [Test]
        public void Deposit_ByAHaulerHoldingNothing_IsNotATrip()
        {
            // A hauler that stood down was never given a job, so it never set off. The step is still recorded
            // (the trace should show who declined) but it must not be counted as work done or wasted.
            var script = new List<HaulSimCommand> { HaulSimCommand.Decide(1, 20), HaulSimCommand.Deposit(1) };
            var trace = HaulConcurrencySim.Run(freeCapacity: 50, script, StandDown);

            Assert.That(trace.Steps.Count, Is.EqualTo(2), "both events are recorded");
            Assert.That(trace.Trips, Is.EqualTo(0), "standing down is not a trip");
            Assert.That(trace.TotalDeposited, Is.EqualTo(0));
            Assert.That(trace.TotalCarriedBack, Is.EqualTo(0), "nothing was carried, so nothing was carried back");
        }

        // ── the harness must never rescue the rule under test ─────────────────────────────────────────

        [Test]
        public void TheHarnessNeverClampsToCapacity_OrItCouldOnlyEverReportSuccess()
        {
            // The single most important property of the machinery. If the simulation trimmed a commitment to
            // what fits, every rule would satisfy the invariant and the harness would be decoration.
            var script = new List<HaulSimCommand>
            {
                HaulSimCommand.Decide(1, 40),
                HaulSimCommand.Decide(2, 40),
                HaulSimCommand.Decide(3, 40)
            };
            var trace = HaulConcurrencySim.Run(freeCapacity: 5, script, TakeWhateverIsWanted);

            Assert.That(trace.PeakSubscription, Is.EqualTo(120), "the over-commitment must stand, unrepaired");
            Assert.That(trace.OverSubscription, Is.EqualTo(115));
            Assert.That(trace.HoldsInvariant, Is.False);
        }

        [Test]
        public void NonsenseDecision_IsRecordedVerbatimAndBookedAsZero()
        {
            var script = new List<HaulSimCommand> { HaulSimCommand.Decide(1, 20), HaulSimCommand.Deposit(1) };
            var trace = HaulConcurrencySim.Run(freeCapacity: 50, script, AnswerNonsense);

            Assert.That(trace.Steps[0].Decided, Is.EqualTo(-5), "a rule that answers nonsense must be visible");
            Assert.That(trace.Steps[0].Committed, Is.EqualTo(0), "negative cargo is not physically expressible");
            Assert.That(trace.Steps[0].LiveCargo, Is.EqualTo(0));
            Assert.That(trace.TotalDeposited, Is.EqualTo(0));
        }

        [Test]
        public void NegativeCapacity_ReadsAsFull()
        {
            var script = new List<HaulSimCommand> { HaulSimCommand.Decide(1, 20), HaulSimCommand.Deposit(1) };
            var trace = HaulConcurrencySim.Run(freeCapacity: -10, script, TakeWhateverIsWanted);

            Assert.That(trace.FreeCapacity, Is.EqualTo(0));
            Assert.That(trace.TotalDeposited, Is.EqualTo(0), "nothing can land in a destination with no room");
            Assert.That(trace.TotalCarriedBack, Is.EqualTo(20));
        }

        // ── determinism: the same scenario must tell the same story every time ────────────────────────

        [Test]
        public void SameInputs_ProduceAnIdenticalTrace()
        {
            // There is no clock, no randomness and no scheduler here, and the only aggregation over
            // unordered state is integer addition. Comparing the rendered report compares every recorded
            // number at once, including the ones no other test reads.
            var first = HaulConcurrencySim.Run(9, HaulConcurrencySim.Interleave(6, 4, 6), TakeWhateverIsWanted);
            var second = HaulConcurrencySim.Run(9, HaulConcurrencySim.Interleave(6, 4, 6), TakeWhateverIsWanted);

            Assert.That(second.Describe(), Is.EqualTo(first.Describe()));
        }

        // ── the scenario builder's concurrency dial ───────────────────────────────────────────────────

        [Test]
        public void Interleave_AtOne_SerialisesTheColony()
        {
            var script = HaulConcurrencySim.Interleave(pawnCount: 3, desirePerPawn: 5, decideAhead: 1);

            Assert.That(Shape(script), Is.EqualTo("D1 P1 D2 P2 D3 P3"),
                "each hauler plans only after the previous one has unloaded");
        }

        [Test]
        public void Interleave_AtTheCrewSize_PlansEveryoneBeforeAnythingLands()
        {
            var script = HaulConcurrencySim.Interleave(pawnCount: 3, desirePerPawn: 5, decideAhead: 3);

            Assert.That(Shape(script), Is.EqualTo("D1 D2 D3 P1 P2 P3"), "the reported scenario");
        }

        [Test]
        public void Interleave_InTheMiddle_KeepsThatManyPlansInFlightThroughout()
        {
            // Not just at the start: after each deposit the next hauler plans, so two commitments stay live
            // for the whole run. This is the realistic middle a rule can fail in while passing both ends.
            var script = HaulConcurrencySim.Interleave(pawnCount: 4, desirePerPawn: 5, decideAhead: 2);

            Assert.That(Shape(script), Is.EqualTo("D1 D2 P1 D3 P2 D4 P3 P4"));
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void Interleave_WithNoHaulers_IsAnEmptyScenario(int pawnCount)
        {
            Assert.That(HaulConcurrencySim.Interleave(pawnCount, 5, 1), Is.Empty);
        }

        // ── in-flight visibility: the mod's snapshot is per tick, and that costs a trip ───────────────

        [Test]
        public void TickSnapshot_HidesACommitmentMadeInTheSameTick()
        {
            // Both haulers plan in tick 0, so the second reads a snapshot taken before the first committed.
            // This is the residual the shipped in-flight subtraction still has, reproduced deliberately
            // rather than assumed away.
            var script = new List<HaulSimCommand> { HaulSimCommand.Decide(1, 75), HaulSimCommand.Decide(2, 75) };
            var trace = HaulConcurrencySim.Run(10, script, SubtractInFlight, EnrouteVisibility.TickSnapshot);

            Assert.That(trace.Steps[1].Sight.UnitsEnroute, Is.EqualTo(0), "the first hauler is invisible this tick");
            Assert.That(trace.PeakSubscription, Is.EqualTo(20), "so both commit the same ten units");
            Assert.That(trace.HoldsInvariant, Is.False);
        }

        [Test]
        public void TickSnapshot_RevealsTheCommitmentOnceTheClockMoves()
        {
            var script = new List<HaulSimCommand>
            {
                HaulSimCommand.Decide(1, 75),
                HaulSimCommand.Tick(),
                HaulSimCommand.Decide(2, 75)
            };
            var trace = HaulConcurrencySim.Run(10, script, SubtractInFlight, EnrouteVisibility.TickSnapshot);

            Assert.That(trace.Steps[2].Sight.UnitsEnroute, Is.EqualTo(10), "a new tick takes a fresh snapshot");
            Assert.That(trace.HoldsInvariant, Is.True);
        }

        [Test]
        public void ImmediateVisibility_ShowsACommitmentTheInstantItIsMade()
        {
            var script = new List<HaulSimCommand> { HaulSimCommand.Decide(1, 75), HaulSimCommand.Decide(2, 75) };
            var trace = HaulConcurrencySim.Run(10, script, SubtractInFlight);

            Assert.That(trace.Steps[1].Sight.UnitsEnroute, Is.EqualTo(10));
            Assert.That(trace.HoldsInvariant, Is.True);
        }

        // ── the two ways a claim and its cargo come apart ─────────────────────────────────────────────

        [Test]
        public void ReleaseCommitment_LeavesTheCargoInFlight()
        {
            var script = new List<HaulSimCommand> { HaulSimCommand.Decide(1, 4), HaulSimCommand.ReleaseCommitment(1) };
            var trace = HaulConcurrencySim.Run(10, script, TakeWhateverIsWanted);

            var afterRelease = trace.Steps[1];
            Assert.That(afterRelease.LiveCommitments, Is.EqualTo(0), "the accounting has forgotten it");
            Assert.That(afterRelease.LiveCargo, Is.EqualTo(4), "the goods are still on their way");
        }

        [Test]
        public void DropCargo_LeavesTheCommitmentLive()
        {
            var script = new List<HaulSimCommand> { HaulSimCommand.Decide(1, 4), HaulSimCommand.DropCargo(1) };
            var trace = HaulConcurrencySim.Run(10, script, TakeWhateverIsWanted);

            var afterDrop = trace.Steps[1];
            Assert.That(afterDrop.LiveCargo, Is.EqualTo(0), "the load will never arrive");
            Assert.That(afterDrop.LiveCommitments, Is.EqualTo(4), "but the accounting still blocks the space");
            Assert.That(trace.TotalCarriedBack, Is.EqualTo(0), "a lost load is not a wasted round trip");
        }

        // ── a re-plan sees its own commitment separately from everyone else's ─────────────────────────

        [Test]
        public void AReplanSeesItsOwnCommitmentApartFromTheOthers()
        {
            // Hauler 1 plans, hauler 2 plans, then hauler 1 re-plans. Its own four units must reach it as
            // OwnUnitsEnroute and not be buried in UnitsEnroute: a rule that cannot tell them apart either
            // shrinks its own allowance every re-plan or competes with itself.
            var script = new List<HaulSimCommand>
            {
                HaulSimCommand.Decide(1, 4),
                HaulSimCommand.Decide(2, 3),
                HaulSimCommand.Decide(1, 4)
            };
            var trace = HaulConcurrencySim.Run(20, script, TakeWhateverIsWanted);

            var replan = trace.Steps[2];
            Assert.That(replan.Sight.OwnUnitsEnroute, Is.EqualTo(4), "its own live commitment");
            Assert.That(replan.Sight.UnitsEnroute, Is.EqualTo(3), "everyone else's, and only that");
            Assert.That(replan.LiveCommitments, Is.EqualTo(7), "a re-plan replaces its claim, it does not add one");
        }

        // ── the trace has to be readable by a human who did not write it ──────────────────────────────

        [Test]
        public void Describe_StatesTheOverCommitmentInWords()
        {
            var script = HaulConcurrencySim.Interleave(pawnCount: 5, desirePerPawn: 75, decideAhead: 5);
            var report = HaulConcurrencySim.Run(3, script, RawFreeSpace).Describe();

            Assert.That(report, Does.Contain("OVER BY 12"), "the overshoot must be stated, not left as subtraction");
            Assert.That(report, Does.Contain("destination: 3 free units"));
            Assert.That(report, Does.Contain("decide"), "and the per-hauler rows that explain how it got there");
            Assert.That(report, Does.Contain("carried back 12"));
        }

        // ── caller mistakes fail loudly rather than passing the invariant by accident ─────────────────

        [Test]
        public void ANullRule_Throws()
        {
            // Treating a missing rule as "commit nothing" would make a broken test satisfy the invariant.
            // Block-bodied lambdas here on purpose: a lambda whose body is an expression would also convert
            // to NUnit's value-returning delegate and pick a different overload.
            Assert.That(() => { HaulConcurrencySim.Run(5, new List<HaulSimCommand>(), null); },
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ANullScenario_Throws()
        {
            Assert.That(() => { HaulConcurrencySim.Run(5, null, TakeWhateverIsWanted); },
                Throws.TypeOf<ArgumentNullException>());
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A scenario as a compact readable shape — <c>D2</c> is hauler 2 deciding, <c>P2</c> is hauler 2
        /// putting its load down, <c>R2</c>/<c>X2</c> its claim released or its cargo lost, <c>T</c> a clock
        /// advance. Comparing shapes keeps the builder's tests about ORDER, which is the only thing the
        /// builder decides, instead of about command field values.
        /// </summary>
        /// <param name="script">The scenario to render.</param>
        /// <returns>Space-separated tokens in scenario order.</returns>
        private static string Shape(IEnumerable<HaulSimCommand> script)
        {
            var tokens = new List<string>();
            foreach (var command in script)
            {
                switch (command.Action)
                {
                    case HaulSimAction.Decide: tokens.Add("D" + command.PawnId); break;
                    case HaulSimAction.Deposit: tokens.Add("P" + command.PawnId); break;
                    case HaulSimAction.ReleaseCommitment: tokens.Add("R" + command.PawnId); break;
                    case HaulSimAction.DropCargo: tokens.Add("X" + command.PawnId); break;
                    default: tokens.Add("T"); break;
                }
            }
            return string.Join(" ", tokens);
        }

        /// <summary>Today's shipped rule, reproduced for the visibility tests: free space minus what other
        /// haulers are already bringing. Its full treatment is in <c>HaulDestinationOverCommitTests</c>.</summary>
        /// <param name="sight">The hauler's view.</param>
        /// <returns>Units committed.</returns>
        private static int SubtractInFlight(HaulSight sight) =>
            Math.Min(sight.Desire, DestinationEnroutePolicy.FreeAfterEnroute(sight.FreeCapacity, sight.UnitsEnroute));

        /// <summary>The uncoordinated rule: each hauler reads raw free space and nothing else.</summary>
        /// <param name="sight">The hauler's view.</param>
        /// <returns>Units committed.</returns>
        private static int RawFreeSpace(HaulSight sight) => Math.Min(sight.Desire, sight.FreeCapacity);
    }
}
