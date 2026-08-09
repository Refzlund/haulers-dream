namespace HaulersDream.Core
{
    /// <summary>
    /// The one forbidden rule every checkpoint of an HD sweep shares — choosing a stack, walking to it, and
    /// pocketing it (issue #250).
    ///
    /// A player forbids things that are UNSAFE: a stack next to a downed manhunter, inside a room that just
    /// caught fire, past a breach the raiders came through. Forbidding is the only "stop, don't go there"
    /// control RimWorld gives, so the moment it is set the pawn must stop walking. HD's sweep drivers used to
    /// test forbidden exactly twice — when the chain picked the stack, and again on arrival — with nothing at
    /// all in between, so a forbid set one step after the pawn set off bought nothing: it walked the whole way
    /// there, stood at the item (up to 240 more ticks with the pickup pause on), and only then changed its
    /// mind. Vanilla never had that hole because its haul drivers carry a job-level
    /// <c>FailOnForbidden</c> end condition, which <c>JobDriver.DriverTick</c> re-evaluates through
    /// <c>CheckCurrentToilEndOrFail</c> on EVERY tick; worst-case latency is one tick.
    ///
    /// Vanilla is equally deliberate about the EXCEPTION, and copying it exactly is the whole reason this type
    /// exists as shared arithmetic instead of three hand-written conditions.
    /// <c>Pawn_JobTracker.StartJob</c> sets <c>newJob.ignoreForbidden = true</c> whenever
    /// <c>pawn.Drafted || newJob.playerForced</c>, and <c>ToilFailConditions.FailOnForbidden</c> short-circuits
    /// to <c>JobCondition.Ongoing</c> on that flag — so vanilla does NOT abandon a player-forced haul whose
    /// target becomes forbidden. Overriding a forbid is precisely what "forced" means, and a player who
    /// force-hauls the forbidden prison meal off the prison floor must still get it (issue #3).
    ///
    /// The asymmetry that makes this a POLICY rather than a one-liner: an HD sweep carries more than the thing
    /// the player pointed at. One order anchors the job and then sweeps every nearby stack into the same trip,
    /// and the player never consented to any of those extras. So "forced" licenses the ANCHOR to be taken
    /// while forbidden and NOTHING else — a forbidden stack swept in as an extra is abandoned even on a forced
    /// job, which is exactly the case the #250 reporter hit. Drivers that carry no anchor concept at all (the
    /// bulk loads, the refuel sweep, the self-pickup scoop) pass <c>isOrderedAnchor: false</c> for every stack
    /// and so never exempt anything, matching what they already did at their take.
    ///
    /// Both members are deliberately total and side-effect free so the walk gate and the take gate are the
    /// SAME arithmetic: <see cref="AbandonWalk"/> is by construction "forbidden and not exempt", the exact
    /// complement of <see cref="MayTakeWhileForbidden"/>. Two hand-written conditions would drift, and the
    /// direction they drift in is a pawn that walks somewhere the player told it not to go.
    ///
    /// NAMING: this is <c>Sweep</c>ForbidPolicy, never <c>EnRoute</c>ForbidPolicy. The repo's <c>EnRoute*</c>
    /// family (<c>EnRoutePickupPolicy</c>, <c>EnRouteMutexPolicy</c>, <c>EnRoutePathChecker</c>) names the
    /// vanilla <c>IHaulEnroute</c> reservation system, which is about a DESTINATION accepting a pending
    /// delivery — a different subject that happens to share the English word for "on the way".
    /// </summary>
    public static class SweepForbidPolicy
    {
        /// <summary>
        /// May this pawn pocket THIS stack even though it is forbidden right now? Only the explicitly ordered
        /// anchor of a forbid-overriding order may; every swept extra, and every stack of an automatic job, may
        /// not. The caller has already established that the stack IS forbidden — a false result therefore means
        /// "skip this one", never "the stack is fine".
        /// </summary>
        /// <param name="orderIgnoresForbidden">
        /// Does the JOB as a whole override forbidding? This is vanilla's own <c>Job.ignoreForbidden</c>
        /// condition, which <c>Pawn_JobTracker.StartJob</c> raises for <c>pawn.Drafted || job.playerForced</c>;
        /// HD reads <c>job.playerForced</c> because a sweep is never issued to a drafted pawn. True means the
        /// player deliberately pointed at something forbidden, false means the job came from a work scan.
        /// </param>
        /// <param name="isOrderedAnchor">
        /// Is this particular stack the one the order was actually placed ON, rather than an extra HD swept
        /// into the same trip? For the chain drivers that is the head of the queue (cursor 0); for drivers with
        /// no anchor concept it is always false, so their whole chain is treated as swept extras.
        /// </param>
        /// <returns>True only for the ordered anchor of a forbid-overriding order.</returns>
        public static bool MayTakeWhileForbidden(bool orderIgnoresForbidden, bool isOrderedAnchor)
            => orderIgnoresForbidden && isOrderedAnchor;

        /// <summary>
        /// Should the pawn stop walking to this stack RIGHT NOW? Evaluated every tick of the walk, so the
        /// answer must depend only on its arguments and must cost nothing — the caller re-reads the live
        /// forbidden state per tick and this decides what to do with it.
        /// </summary>
        /// <param name="forbiddenNow">
        /// The stack's forbidden state as of THIS tick (<c>Thing.IsForbidden(pawn)</c>, which folds in the
        /// item's own flag, its cell's flag and faction ownership). Re-read per tick on purpose: the whole
        /// point of #250 is reacting to a flag the player flips mid-walk.
        /// </param>
        /// <param name="orderIgnoresForbidden">The job-level override; see
        /// <see cref="MayTakeWhileForbidden"/>.</param>
        /// <param name="isOrderedAnchor">Whether this stack is the ordered anchor; see
        /// <see cref="MayTakeWhileForbidden"/>.</param>
        /// <returns>
        /// True when the walk must be abandoned in favour of the next stack in the chain. Never true for an
        /// unforbidden stack, so an ordinary sweep is byte-identical to its pre-#250 behaviour.
        /// </returns>
        public static bool AbandonWalk(bool forbiddenNow, bool orderIgnoresForbidden, bool isOrderedAnchor)
            => forbiddenNow && !MayTakeWhileForbidden(orderIgnoresForbidden, isOrderedAnchor);
    }
}
