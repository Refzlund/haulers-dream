using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the bound on an unload delivery whose destination passes the storage search but cannot be pathed
    /// to — the "colonist stands still for hours instead of getting on with the rest of its load" report.
    ///
    /// Two properties matter and both are pinned here. First, the trip must keep going while it has other
    /// stacks to try and must stop once it plainly cannot deliver anything (either nothing is left, or too
    /// many destinations in a row have failed to path). Second — the one that actually breaks the stall — the
    /// re-offer backoff a set-aside stack rides must OUTLAST the idle re-queue period, so the two cannot lock
    /// into the same cadence and rebuild the loop. That second test is written as a relationship between the
    /// two live constants rather than against literals, so retuning either one fails loudly here.
    /// </summary>
    [TestFixture]
    public class UnreachableDestinationPolicyTests
    {
        // --- the continue-or-stop decision ---------------------------------------------------------------

        [Test]
        public void FirstFailureWithMoreToTry_SetsAsideAndContinues()
        {
            // The reported case: one shelf is walled off, the rest of the pack still has somewhere to go.
            Assert.That(UnreachableDestinationPolicy.Choose(pathFailuresThisTrip: 1, remainingCandidates: 4),
                Is.EqualTo(UnreachableDestinationAction.SetAsideAndContinue));
        }

        [Test]
        public void FailuresUpToTheBudgetKeepGoing()
        {
            // Everything strictly below the budget continues; the budget is spent, not merely approached.
            for (int failures = 1; failures < UnreachableDestinationPolicy.MaxPathFailuresPerTrip; failures++)
                Assert.That(UnreachableDestinationPolicy.Choose(failures, remainingCandidates: 10),
                    Is.EqualTo(UnreachableDestinationAction.SetAsideAndContinue),
                    $"failure {failures} of {UnreachableDestinationPolicy.MaxPathFailuresPerTrip} should not end the trip");
        }

        [Test]
        public void ReachingTheBudget_EndsTheTrip()
        {
            // Three distinct destinations unreachable inside one trip is structural, not contention: the pawn
            // is walled off from the storage side of the base and every further stack walks into the same wall.
            Assert.That(
                UnreachableDestinationPolicy.Choose(UnreachableDestinationPolicy.MaxPathFailuresPerTrip,
                    remainingCandidates: 10),
                Is.EqualTo(UnreachableDestinationAction.EndTrip));
        }

        [Test]
        public void PastTheBudget_StillEndsTheTrip()
        {
            Assert.That(
                UnreachableDestinationPolicy.Choose(UnreachableDestinationPolicy.MaxPathFailuresPerTrip + 7,
                    remainingCandidates: 10),
                Is.EqualTo(UnreachableDestinationAction.EndTrip));
        }

        // --- the boundary: nothing left to try -----------------------------------------------------------

        [Test]
        public void NothingLeftToTry_EndsTheTrip_EvenOnTheFirstFailure()
        {
            // The whole load was one stack and its destination is unreachable. Continuing would walk the loop
            // toil into its own "nothing unloadable" branch; stopping here says the same thing directly.
            Assert.That(UnreachableDestinationPolicy.Choose(pathFailuresThisTrip: 1, remainingCandidates: 0),
                Is.EqualTo(UnreachableDestinationAction.EndTrip));
        }

        [Test]
        public void NothingLeftToTry_OutranksAFreshBudget()
        {
            // Precedence check: emptiness decides before the budget is consulted, so a trip with a pristine
            // budget and no remaining stacks still stops.
            Assert.That(UnreachableDestinationPolicy.Choose(pathFailuresThisTrip: 0, remainingCandidates: 0),
                Is.EqualTo(UnreachableDestinationAction.EndTrip));
        }

        [Test]
        public void NegativeCountsReadAsNoneAndEndTheTrip()
        {
            // Total for every input: a miscounted caller degrades to stopping the trip, never to looping.
            Assert.That(UnreachableDestinationPolicy.Choose(pathFailuresThisTrip: -1, remainingCandidates: -3),
                Is.EqualTo(UnreachableDestinationAction.EndTrip));
        }

        [Test]
        public void OneStackLeft_IsStillWorthTrying()
        {
            // The off-by-one that matters at the boundary: one remaining candidate is NOT "nothing left".
            Assert.That(UnreachableDestinationPolicy.Choose(pathFailuresThisTrip: 1, remainingCandidates: 1),
                Is.EqualTo(UnreachableDestinationAction.SetAsideAndContinue));
        }

        // --- the phase lock ------------------------------------------------------------------------------

        [Test]
        public void ChurnBackoffOutlastsTheIdleScanPeriod()
        {
            // THE load-bearing relationship. The driver stamps HaulChurnPolicy.BackoffTicks on a stack whose
            // destination it could not reach; the idle backstop re-offers the unload every
            // IdleScanIntervalTicks. Asserted as a relationship between the two live constants (not against
            // 600 and 250) so lowering the backoff or lengthening the scan fails right here instead of
            // quietly letting the stall re-form in-game.
            Assert.That(UnreachableDestinationPolicy.BreaksPhaseLock(HaulChurnPolicy.BackoffTicks), Is.True,
                $"the churn backoff ({HaulChurnPolicy.BackoffTicks} ticks) must outlast the idle re-queue "
                + $"period ({UnreachableDestinationPolicy.IdleScanIntervalTicks} ticks), or a set-aside stack "
                + "is re-offered on the same cadence that failed it.");
        }

        [Test]
        public void ABackoffEqualToTheScanPeriodDoesNotBreakThePhaseLock()
        {
            // Strict inequality: a window that expires exactly on a scan tick is re-offered by that same tick.
            Assert.That(
                UnreachableDestinationPolicy.BreaksPhaseLock(UnreachableDestinationPolicy.IdleScanIntervalTicks),
                Is.False);
        }

        [Test]
        public void AShorterBackoffDoesNotBreakThePhaseLock()
        {
            Assert.That(
                UnreachableDestinationPolicy.BreaksPhaseLock(UnreachableDestinationPolicy.IdleScanIntervalTicks - 1),
                Is.False);
        }

        [Test]
        public void ALongerBackoffBreaksThePhaseLock()
        {
            Assert.That(
                UnreachableDestinationPolicy.BreaksPhaseLock(UnreachableDestinationPolicy.IdleScanIntervalTicks + 1),
                Is.True);
        }
    }
}
