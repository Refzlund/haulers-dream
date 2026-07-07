using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the anti-churn bound for vanilla-style storage hauls (the hemogen pack infinite haul loop fix):
    /// the per-job consecutive-failure budget (with the reset-on-success and reset-on-progress contract the
    /// Verse glue implements), the classification of a placement arrival's outcome (including the
    /// partial-absorb shape, where vanilla delivers part of the load and still reports a failed drop), and
    /// the re-offer backoff window arithmetic. The oracle tests reproduce the decisive shapes
    /// decision-for-decision: a job whose every arrival delivers nothing must be ended exactly one failure
    /// past the budget, while legitimate short re-routes, partial-absorb top-up cascades and multi-item
    /// unload trips must never trip it.
    /// </summary>
    [TestFixture]
    public class HaulChurnPolicyTests
    {
        // --- outcome classification (CountsAsRetarget) --------------------------------------------------

        [Test]
        public void ArrivalThatLoopedBackStillCarrying_Counts()
        {
            Assert.That(
                HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: true, stillCarrying: true, madeProgress: false),
                Is.True);
        }

        [Test]
        public void SuccessfulPlacement_DoesNotCount()
        {
            // Hands empty after the arrival: the thing was placed (or absorbed into the cell's stack).
            Assert.That(
                HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: true, stillCarrying: false, madeProgress: false),
                Is.False);
        }

        [Test]
        public void PartialAbsorbStillCarrying_DoesNotCount()
        {
            // The drop reported failure for the remainder, but part of the load was absorbed into the cell's
            // near-full stack: units left the pawn's hands, so this is progress toward delivery, never churn.
            Assert.That(
                HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: true, stillCarrying: true, madeProgress: true),
                Is.False);
        }

        [Test]
        public void ArrivalThatEndedTheJobItself_DoesNotCount()
        {
            // Any in-toil job end is not churn to bound, whatever the hands held afterwards.
            Assert.That(
                HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: false, stillCarrying: true, madeProgress: false),
                Is.False);
            Assert.That(
                HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: false, stillCarrying: false, madeProgress: false),
                Is.False);
        }

        // --- the per-job budget (ShouldBail) -------------------------------------------------------------

        [Test]
        public void WithinBudget_NeverBails()
        {
            for (int consecutive = 1; consecutive <= HaulChurnPolicy.MaxRetargetsPerJob; consecutive++)
                Assert.That(HaulChurnPolicy.ShouldBail(consecutive), Is.False,
                    $"failure {consecutive} is within the budget and must not bail");
        }

        [Test]
        public void FirstFailurePastBudget_Bails()
        {
            Assert.That(HaulChurnPolicy.ShouldBail(HaulChurnPolicy.MaxRetargetsPerJob + 1), Is.True);
        }

        [Test]
        public void ZeroFailures_NeverBails()
        {
            Assert.That(HaulChurnPolicy.ShouldBail(0), Is.False);
        }

        /// <summary>
        /// ORACLE: the reported infinite oscillation, decision for decision. A pawn's job arrives, fails to
        /// place, retargets and walks again, forever (the two-pawn corridor pacing). Simulating the Verse
        /// glue's counting contract (increment per failed arrival, reset on success), the policy must end the
        /// job on exactly the first failure past the budget, and never earlier.
        /// </summary>
        [Test]
        public void Oracle_EndlessFailedArrivals_BailExactlyOncePastBudget()
        {
            int consecutive = 0;
            int bailedAtArrival = -1;
            for (int arrival = 1; arrival <= 50 && bailedAtArrival < 0; arrival++)
            {
                // Every arrival in the loop scenario fails, delivers zero units and loops back still carrying.
                if (!HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: true, stillCarrying: true, madeProgress: false))
                    Assert.Fail("a zero-progress failed arrival must classify as a retarget");
                consecutive++;
                if (HaulChurnPolicy.ShouldBail(consecutive))
                    bailedAtArrival = arrival;
            }
            Assert.That(bailedAtArrival, Is.EqualTo(HaulChurnPolicy.MaxRetargetsPerJob + 1),
                "the endless loop must be ended exactly one failed arrival past the budget");
        }

        /// <summary>
        /// ORACLE: a legitimate multi-item unload trip. The inventory unload reuses the same arrival toil once
        /// per carried item; each delivery may retarget once or twice (a sniped cell) before succeeding. With
        /// the reset-on-success contract, the tally never accumulates across items, so a long healthy trip
        /// must never bail.
        /// </summary>
        [Test]
        public void Oracle_ManyDeliveriesWithOccasionalRetries_NeverBails()
        {
            int consecutive = 0;
            for (int item = 0; item < 30; item++)
            {
                // Two failed arrivals, then the item places (the realistic worst case per delivery).
                for (int fail = 0; fail < 2; fail++)
                {
                    consecutive++;
                    Assert.That(HaulChurnPolicy.ShouldBail(consecutive), Is.False,
                        "a short re-route must never end a healthy trip");
                }
                // Success: hands empty, the glue resets the tally (NotifyPlaced).
                Assert.That(
                    HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: true, stillCarrying: false, madeProgress: false),
                    Is.False);
                consecutive = 0;
            }
        }

        /// <summary>
        /// ORACLE: the freezer top-up cascade. A pawn carries a stack of meals into a freezer of near-full
        /// stacks and absorbs a little per hop; every hop reports a failed drop with the remainder still in
        /// hand (vanilla's partial-absorb-then-false shape), a pattern this mod's own haul-to-stack refinement
        /// actively manufactures by steering re-finds to partial stacks. Settled decision, pinned here: a
        /// partial absorb RESETS the consecutive count, it is progress exactly like a success, so an
        /// arbitrarily long top-up trip must never bail.
        /// </summary>
        [Test]
        public void Oracle_PartialAbsorbCascade_NeverBails()
        {
            int consecutive = 0;
            for (int hop = 1; hop <= 12; hop++)
            {
                if (HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: true, stillCarrying: true, madeProgress: true))
                    consecutive++;
                else
                    consecutive = 0; // progress resets, per the glue's NotifyProgress contract
                Assert.That(HaulChurnPolicy.ShouldBail(consecutive), Is.False,
                    $"top-up hop {hop} delivered units and must never end the job");
            }
        }

        /// <summary>
        /// ORACLE: partial absorbs interleaved with zero-progress failures. Only the zero-progress arrivals
        /// count toward the consecutive budget and every absorb resets it, so a mixed trip survives far more
        /// total failed drops than the budget; once the absorbs stop, the pure zero-progress run bails exactly
        /// one failure past the budget, counted from the last absorb.
        /// </summary>
        [Test]
        public void Oracle_MixedAbsorbsAndFailures_OnlyZeroProgressArrivalsCount()
        {
            int consecutive = 0;

            // Four rounds of budget-many zero-progress failures, each defused by one absorb: 24 failed drops
            // in total, yet the tally never crosses the budget because progress keeps resetting it.
            for (int round = 0; round < 4; round++)
            {
                for (int fail = 0; fail < HaulChurnPolicy.MaxRetargetsPerJob; fail++)
                {
                    if (HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: true, stillCarrying: true, madeProgress: false))
                        consecutive++;
                    Assert.That(HaulChurnPolicy.ShouldBail(consecutive), Is.False,
                        "zero-progress failures within the budget must not bail");
                }
                Assert.That(
                    HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: true, stillCarrying: true, madeProgress: true),
                    Is.False, "an absorb between the failures is progress, not a retarget");
                consecutive = 0; // the glue resets on progress (NotifyProgress)
            }

            // The absorbs stop: the job is now the pure pathological loop and must bail exactly one
            // zero-progress failure past the budget, measured from the last absorb.
            int bailedAfter = -1;
            for (int fail = 1; fail <= 20 && bailedAfter < 0; fail++)
            {
                if (HaulChurnPolicy.CountsAsRetarget(jobStillCurrent: true, stillCarrying: true, madeProgress: false))
                    consecutive++;
                if (HaulChurnPolicy.ShouldBail(consecutive))
                    bailedAfter = fail;
            }
            Assert.That(bailedAfter, Is.EqualTo(HaulChurnPolicy.MaxRetargetsPerJob + 1),
                "after the last absorb the zero-progress budget starts fresh and bails exactly one past it");
        }

        // --- the re-offer backoff window ------------------------------------------------------------------

        [Test]
        public void SuppressUntil_AddsTheBackoffWindow()
        {
            Assert.That(HaulChurnPolicy.SuppressUntil(1000), Is.EqualTo(1000 + HaulChurnPolicy.BackoffTicks));
        }

        [Test]
        public void InsideTheWindow_Suppressed()
        {
            int until = HaulChurnPolicy.SuppressUntil(1000);
            Assert.That(HaulChurnPolicy.IsSuppressed(1000, until), Is.True, "the stamping tick itself is suppressed");
            Assert.That(HaulChurnPolicy.IsSuppressed(until - 1, until), Is.True, "the last window tick is suppressed");
        }

        [Test]
        public void AtAndPastTheWindowEnd_Free()
        {
            int until = HaulChurnPolicy.SuppressUntil(1000);
            Assert.That(HaulChurnPolicy.IsSuppressed(until, until), Is.False, "the window end is exclusive");
            Assert.That(HaulChurnPolicy.IsSuppressed(until + 1, until), Is.False);
        }

        // --- constants sanity ------------------------------------------------------------------------------

        [Test]
        public void Budget_AllowsLegitimateReroutes()
        {
            // A sniped cell legitimately re-routes once or twice (partial absorbs never even consume budget,
            // they classify as progress); the budget must sit safely above that so no healthy haul is ever
            // ended.
            Assert.That(HaulChurnPolicy.MaxRetargetsPerJob, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void Backoff_IsAShortSelfHealingWindow()
        {
            // Long enough for a competing hauler's job to finish (a few hundred ticks), short enough that a
            // genuinely haulable item is retried promptly (well under a half in-game hour of 1250 ticks).
            Assert.That(HaulChurnPolicy.BackoffTicks, Is.GreaterThanOrEqualTo(250));
            Assert.That(HaulChurnPolicy.BackoffTicks, Is.LessThanOrEqualTo(1250));
        }

        // --- per-THING failed-job budget (#144: the loop that fails before the placement toil) -------------

        [Test]
        public void FirstFailure_StartsTallyAtOne()
        {
            HaulChurnPolicy.RecordThingFailure(nowTick: 1000, lastFailTick: 0, priorCount: 0,
                out int newLast, out int newCount);
            Assert.That(newCount, Is.EqualTo(1));
            Assert.That(newLast, Is.EqualTo(1000), "the last-failure tick advances to now");
        }

        [Test]
        public void RapidFailure_ExactlyAtTheGap_StillCounts()
        {
            // A failure exactly FailGapTicks after the previous one is still the same run (the gap is inclusive).
            HaulChurnPolicy.RecordThingFailure(nowTick: 1000 + HaulChurnPolicy.FailGapTicks, lastFailTick: 1000,
                priorCount: 1, out int newLast, out int newCount);
            Assert.That(newCount, Is.EqualTo(2));
            Assert.That(newLast, Is.EqualTo(1000 + HaulChurnPolicy.FailGapTicks));
        }

        [Test]
        public void IsolatedFailure_PastGap_ResetsToOne()
        {
            // The next failure lands more than a gap after the previous one -> a new run, not the same loop.
            HaulChurnPolicy.RecordThingFailure(nowTick: 1000 + HaulChurnPolicy.FailGapTicks + 1, lastFailTick: 1000,
                priorCount: 3, out int _, out int newCount);
            Assert.That(newCount, Is.EqualTo(1));
        }

        [Test]
        public void ShouldBackOff_OnlyAtOrPastTheBudget()
        {
            Assert.That(HaulChurnPolicy.ShouldBackOffThing(HaulChurnPolicy.MaxFailedJobsPerThing - 1), Is.False);
            Assert.That(HaulChurnPolicy.ShouldBackOffThing(HaulChurnPolicy.MaxFailedJobsPerThing), Is.True);
            Assert.That(HaulChurnPolicy.ShouldBackOffThing(HaulChurnPolicy.MaxFailedJobsPerThing + 1), Is.True);
        }

        [Test]
        public void SustainedLoop_TripsBackoffExactlyAtTheBudget()
        {
            // Reproduce the reported loop: the same pack fails a storage haul every ~200 ticks (inside the gap),
            // never delivering. Drive the pure policy the way the Verse glue does and prove it backs the thing off
            // on exactly the MaxFailedJobsPerThing-th failure, and no sooner.
            const int cadence = 200; // ticks between pace-and-drop cycles, comfortably under FailGapTicks
            Assert.That(cadence, Is.LessThanOrEqualTo(HaulChurnPolicy.FailGapTicks));
            int last = 0, count = 0, tick = 10_000;
            int backedOffAtFailure = -1;
            for (int failure = 1; failure <= HaulChurnPolicy.MaxFailedJobsPerThing; failure++)
            {
                HaulChurnPolicy.RecordThingFailure(tick, last, count, out last, out count);
                if (HaulChurnPolicy.ShouldBackOffThing(count))
                {
                    backedOffAtFailure = failure;
                    break;
                }
                tick += cadence;
            }
            Assert.That(backedOffAtFailure, Is.EqualTo(HaulChurnPolicy.MaxFailedJobsPerThing),
                "the loop must be bounded on exactly the budget-th rapid failure");
        }

        [Test]
        public void WellSpacedFailures_NeverTripBackoff()
        {
            // A busy colony where the same item legitimately fails a haul now and then, each failure more than a
            // gap after the last, must NEVER accumulate to a backoff: every failure resets the run to one.
            int last = 0, count = 0, tick = 10_000;
            for (int i = 0; i < 20; i++)
            {
                HaulChurnPolicy.RecordThingFailure(tick, last, count, out last, out count);
                Assert.That(count, Is.EqualTo(1), "each well-spaced failure restarts the run");
                Assert.That(HaulChurnPolicy.ShouldBackOffThing(count), Is.False);
                tick += HaulChurnPolicy.FailGapTicks + 1; // just past the gap each time
            }
        }

        [Test]
        public void SuccessClearedTally_NextFailureStartsFresh()
        {
            // The Verse glue clears the tally on a Succeeded finish; the policy models "no prior" as priorCount 0.
            // After a success wipes the count, the next failure starts a brand-new run at one.
            HaulChurnPolicy.RecordThingFailure(nowTick: 10_100, lastFailTick: 10_000, priorCount: 0,
                out int _, out int newCount);
            Assert.That(newCount, Is.EqualTo(1));
        }

        [Test]
        public void PerThingBudget_IsTightButAboveTransients()
        {
            // Four whole-job failures for one item is unambiguously the loop; keep the budget low enough to stop
            // the pacing quickly, but above a legitimate one-or-two-reroute transient.
            Assert.That(HaulChurnPolicy.MaxFailedJobsPerThing, Is.GreaterThanOrEqualTo(3));
            Assert.That(HaulChurnPolicy.MaxFailedJobsPerThing, Is.LessThanOrEqualTo(6));
            // The gap must span at least one pace-and-drop cycle so a real loop keeps accumulating.
            Assert.That(HaulChurnPolicy.FailGapTicks, Is.GreaterThanOrEqualTo(120));
        }
    }
}
