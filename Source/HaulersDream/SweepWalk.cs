using System;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The ONE walk toil every HD sweep driver yields between its decide toil and its take toil (issue #250):
    /// path to the stack the decide toil chose, and abandon that walk the instant the stack becomes forbidden.
    ///
    /// The hole this closes: HD's sweep drivers tested forbidden when the chain PICKED a stack and again when
    /// the pawn ARRIVED at it, and ran no code whatsoever in between — the walk toil's initAction called
    /// <c>StartPath</c> and the next thing that executed was the arrival. A player who forbids something
    /// mid-walk forbids it because it is UNSAFE, and the pawn kept walking all the way there (then stood at it
    /// for up to another 240 ticks if the pickup pause is on) before deciding otherwise. Vanilla has no such
    /// gap: its haul drivers hang a <c>FailOnForbidden</c> end condition on the JOB, and
    /// <c>JobDriver.DriverTick</c> re-evaluates every end condition through <c>CheckCurrentToilEndOrFail</c>
    /// before anything else it does, so vanilla's worst-case reaction time is one tick.
    ///
    /// Why a per-toil pre-tick action rather than vanilla's job-level fail condition: <c>FailOnForbidden</c>
    /// returns <c>JobCondition.Incompletable</c>, which ends the WHOLE job. For a sweep that is the wrong
    /// remedy — one forbidden stack would cost the pawn the other nine stacks of the trip. Every other
    /// invalid-stack case these drivers already handle (despawned, claimed, stored, unreservable, unreachable)
    /// SKIPS the one stack and keeps the trip, and the inventory is flushed at the end regardless, so
    /// forbidding gets the same treatment: drop this stack, walk the rest of the chain.
    ///
    /// GOTCHA — it must be <c>AddPreTickAction</c>, never <c>tickAction</c>. Decompiled 1.6
    /// <c>JobDriver.DriverTick</c> walks <c>CurToil.preTickActions</c> and re-tests
    /// <c>JobChanged() || CurToil != curToil || wantBeginNextToil</c> after EACH one, returning if any holds —
    /// so a <c>JumpToToil</c> from inside a pre-tick action is re-entrancy-safe by construction. After
    /// <c>tickAction</c> the driver re-tests only <c>JobChanged()</c>, which stays FALSE for a jump inside the
    /// same job (<c>JumpToToil</c> calls <c>ReadyForNextToil</c>, which starts the next toil synchronously),
    /// so the rest of the tick would run against a toil that is no longer current. <c>tickIntervalAction</c> is
    /// re-tested not at all, and it also fires only at the pawn's variable update rate.
    ///
    /// GOTCHA — 1.6's variable tick rate does NOT throttle this. A pawn's ThingDef is
    /// <c>TickerType.Normal</c>, and <c>Thing.DoTick</c> calls <c>Tick()</c> (→ <c>Pawn.Tick</c> →
    /// <c>Pawn_JobTracker.JobTrackerTick</c> → <c>JobDriver.DriverTick</c>) on EVERY tick; only
    /// <c>TickInterval(delta)</c> — and with it <c>DriverTickInterval</c>/<c>preTickIntervalActions</c> — runs
    /// at the throttled rate. Expressing this check as a pre-tick-INTERVAL action would silently give it a
    /// latency of however many ticks that pawn's current update rate happens to be.
    ///
    /// SCOPE — forbidden ONLY, deliberately. Despawned, reserved-by-another, already-stored, mass-ceiling and
    /// RimIOT re-checks all stay where they are, at arrival: they cost a scan or a reservation-manager lookup,
    /// this runs once per tick for every sweeping pawn in the colony, and widening it would turn a safety fix
    /// into a performance regression that shows up in exactly the late-game colony that reported the bug.
    /// One <c>Thing.IsForbidden</c> per tick is precisely the budget vanilla already spends on its own
    /// <c>FailOnForbidden</c>, and nothing here allocates.
    ///
    /// Loop-safe, same as <see cref="PickupPause"/>: HD's sweep toils sit inside decide→goto→take jump loops,
    /// so the SAME toil object is re-entered once per stack. <c>preTickActions</c> is a list on the toil built
    /// once at <c>MakeNewToils</c> time, and the action resolves both the stack (via
    /// <c>job.GetTarget(stackInd)</c>, which the decide toil re-points each pass) and the cursor (via the
    /// caller's closures) live, so re-entry needs no reset.
    ///
    /// Deliberately SILENT when it fires. Vanilla shows the player nothing when <c>FailOnForbidden</c> ends a
    /// job, and a sweep can drop several stacks from one chain — a message here would spam once per stack for
    /// an outcome the player just caused on purpose.
    ///
    /// NOT a home for pathing-failure recovery: <c>Notify_PatherFailed</c> is an event callback, fires on a
    /// completely different condition, and each driver's override is scoped to ITS walk toil (so a failure on a
    /// DEPOSIT leg keeps vanilla's behaviour). Those overrides stay in the drivers and keep comparing
    /// <c>CurToil</c> against the toil this factory returned.
    /// </summary>
    internal static class SweepWalk
    {
        /// <summary>
        /// Build one sweep walk toil: path to the current stack, re-test forbidding every tick, and hand the
        /// chain back to <paramref name="loopHead"/> the moment the stack turns forbidden — after advancing the
        /// caller's own cursor so the same stack is not re-chosen forever.
        /// </summary>
        /// <param name="driver">
        /// The driver that owns the toil. Used for its live <c>pawn</c>/<c>job</c> and for
        /// <c>JumpToToil</c>; all three are public on <c>JobDriver</c>, so no publicizer is involved. Passed
        /// rather than read off <c>toil.actor</c> because the jump target belongs to the driver, not the pawn.
        /// </param>
        /// <param name="stackInd">
        /// The job target slot the driver keeps pointed at the stack currently being walked to. Resolved live
        /// on every tick, so a decide toil that re-points the slot each pass retargets this check for free.
        /// </param>
        /// <param name="toilName">
        /// The <c>ToilMaker</c> debug name, kept per driver (e.g. <c>HD_Bulk_LoadGoto</c>) so a toil error
        /// report still names the driver it came from rather than this shared factory.
        /// </param>
        /// <param name="loopHead">
        /// The driver's decide/loop toil — where the chain resumes after this stack is dropped. Must be the
        /// toil that CHOOSES the next stack, not the walk itself, or a dropped stack would be re-walked.
        /// </param>
        /// <param name="skipCurrent">
        /// Advance the driver's own cursor past the stack being abandoned, whatever that driver's cursor is: a
        /// queue index (<c>loadIndex++</c>) for the chain drivers, a no-op for the drivers whose loop head
        /// already POPS its queue. Called exactly once per abandoned stack, immediately before the jump, and
        /// never for a stack the pawn actually reaches. Getting it wrong is a livelock (decide re-picks the
        /// same forbidden stack, walk drops it again), which is why it is the caller's own existing statement
        /// rather than something this factory guesses.
        /// </param>
        /// <param name="isOrderedAnchor">
        /// Is the stack currently targeted the one the player's order was actually placed on, as opposed to an
        /// extra HD swept into the same trip? Evaluated live per tick because the chain cursor moves. Drivers
        /// with no anchor concept pass a constant false, which makes every stack a swept extra — matching what
        /// their take toils already do. See <see cref="SweepForbidPolicy"/> for why only the anchor may be
        /// taken while forbidden.
        /// </param>
        /// <returns>The walk toil to yield. Complete mode is <c>PatherArrival</c>; never null.</returns>
        internal static Toil MakeToil(JobDriver driver, TargetIndex stackInd, string toilName,
                                      Toil loopHead, Action skipCurrent, Func<bool> isOrderedAnchor)
        {
            Toil walk = ToilMaker.MakeToil(toilName);
            walk.initAction = delegate
            {
                var t = driver.job.GetTarget(stackInd).Thing;
                if (t == null || !t.Spawned) { skipCurrent(); driver.JumpToToil(loopHead); return; }
                driver.pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            walk.AddPreTickAction(delegate
            {
                var t = driver.job.GetTarget(stackInd).Thing;
                // Spawned is a PRECONDITION of the forbidden read, not a despawn re-check: IsForbidden resolves
                // through PositionHeld, and every HD call site already establishes Spawned before reading it. A
                // stack that vanished mid-walk is NOT abandoned here — the arrival re-check owns that case, and
                // acting on it would widen this per-tick check past the one thing it is allowed to cost.
                if (t == null || !t.Spawned)
                    return;
                if (!SweepForbidPolicy.AbandonWalk(t.IsForbidden(driver.pawn), driver.job.playerForced, isOrderedAnchor()))
                    return;
                skipCurrent();
                driver.JumpToToil(loopHead);
            });
            walk.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            return walk;
        }
    }
}
