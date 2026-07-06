namespace HaulersDream.Core
{
    /// <summary>
    /// The anti-churn bound for vanilla-style storage hauls (the "hemogen pack infinite haul loop" fix).
    ///
    /// <para><b>The failure this bounds.</b> Vanilla's arrival machinery
    /// (<c>Toils_Haul.PlaceHauledThingInCell</c> with <c>storageMode:true</c>) is UNBOUNDED: when the drop at
    /// the destination cell fails, it re-resolves storage (<c>TryFindBestBetterStoreCellFor</c>), retargets the
    /// SAME job at the new cell and jumps back to the carry toil, and when no storage resolves it degrades to a
    /// "haul aside" spot (<c>HaulMode.ToCellNonStorage</c>) and jumps back all the same. Nothing counts these
    /// retries, and the re-resolve measures candidate distance from the CARRIED item's held position, which is
    /// the pawn's own cell, so every failed arrival re-anchors the search to wherever the pawn now stands. Two
    /// haulers converging on the same few viable cells (Hauler's Dream deliberately skips vanilla's
    /// destination-cell reservation for stackable storage hauls, see HaulToStack) can therefore invalidate each
    /// other's arrivals forever: each pawn walks cell to cell, drop after drop fails, the job never ends, and
    /// the pawn paces with the item in hand indefinitely. That is the reported clean-save loop: extracted
    /// hemogen packs in a prison barracks, two haulers pacing the corridor "hauling hemogen pack." without ever
    /// depositing, each fixed only by a player order that REPLACED the stuck job.</para>
    ///
    /// <para><b>The bound.</b> Count consecutive ZERO-PROGRESS failed placement arrivals per job (each one is a
    /// retarget jump; the counter RESETS on every successful placement and on every partial absorb, which is
    /// real progress, so multi-item trips such as the inventory unload and near-full stack top-ups never
    /// accumulate). Once a job exceeds <see cref="MaxRetargetsPerJob"/> consecutive
    /// failures, it is ended (Incompletable, the carried stack drops at the pawn's feet exactly as any failed
    /// vanilla haul does) and the hauled thing is placed on a short re-offer backoff
    /// (<see cref="BackoffTicks"/>) so the work scan does not immediately rebuild the identical doomed job. The
    /// player's own forced orders bypass the backoff at the patch layer: an explicit order must always work,
    /// which is also exactly how the reporter un-stuck a looping pawn.</para>
    ///
    /// <para>Pure and allocation-free (primitives in, bool/int out); the Verse layer supplies the live job
    /// state and owns the per-job counter and the per-thing backoff stamps. All decisions are tick- and
    /// count-based (no randomness, no client-local state), so they are multiplayer-deterministic.</para>
    /// </summary>
    public static class HaulChurnPolicy
    {
        /// <summary>
        /// How many CONSECUTIVE zero-progress failed placement arrivals one storage-haul job may make before it
        /// is ended. A legitimate job re-routes at most once or twice (a sniped cell; a partial absorb into a
        /// near-full stack does not even consume budget, it classifies as progress), so five consecutive
        /// failures without a single delivered unit is already deep in pathological territory while staying far
        /// above any legitimate sequence.
        /// </summary>
        public const int MaxRetargetsPerJob = 5;

        /// <summary>
        /// How long (in game ticks) a thing whose haul job was churn-ended stays suppressed from the AUTOMATIC
        /// haul work scan. 600 ticks is 10 seconds at normal speed: long enough for the competing hauler's job
        /// to finish or fail (releasing the contested cells) and for storage state to settle, short enough that
        /// a genuinely haulable item is retried promptly. Forced player orders ignore the backoff entirely.
        /// </summary>
        public const int BackoffTicks = 600;

        /// <summary>
        /// Whether the just-run placement arrival counts as a churn retry. Vanilla's placement initAction
        /// leaves one of these states behind: the job ended IN-TOIL (a side effect of the placement ended it;
        /// a dead job has nothing to count), the pawn's hands are EMPTY (the thing was placed, fully absorbed
        /// into a stack, or hard-bailed via CarriedThing.Destroy(), which removes it from the carry tracker
        /// WITHOUT ending the job synchronously; the driver's FailOnDestroyedOrNull kills the job on a later
        /// tick), or the job jumped back to the carry toil with the thing STILL IN HAND. The still-in-hand
        /// state splits in two: vanilla's TryPlaceDirect can partially absorb the load into a near-full
        /// same-def stack, fire placedAction for the absorbed part, then return false for the remainder, which
        /// enters the very same fail branch as a true failed drop. That partial absorb is real progress toward
        /// delivery (units left the pawn's hands) and RESETS the consecutive tally exactly like a success; the
        /// reported pathological loop can never disguise itself as progress because it places zero units per
        /// arrival. Only the zero-progress still-in-hand arrival counts.
        /// </summary>
        /// <param name="jobStillCurrent">The same job is still the pawn's current job after the placement ran.</param>
        /// <param name="stillCarrying">The pawn still holds a carried thing after the placement ran.</param>
        /// <param name="madeProgress">The placement delivered part of the load even though it reported failure:
        /// the SAME carried thing remains in hand with a strictly smaller stack count (the partial-absorb
        /// shape). The caller must prove this against a before/after snapshot; anything unprovable (a swapped
        /// carried thing, an unchanged count) is not progress.</param>
        /// <returns>True when this arrival failed without delivering a single unit and the job looped back for
        /// another carry leg.</returns>
        public static bool CountsAsRetarget(bool jobStillCurrent, bool stillCarrying, bool madeProgress)
            => jobStillCurrent && stillCarrying && !madeProgress;

        /// <summary>
        /// Whether a job that has now made <paramref name="consecutiveRetargets"/> consecutive failed arrivals
        /// must be ended. Strictly-greater comparison: the job is allowed exactly
        /// <see cref="MaxRetargetsPerJob"/> retries, and the next failure past the budget bails.
        /// </summary>
        /// <param name="consecutiveRetargets">Failed placement arrivals since the last successful placement
        /// in this job (the caller resets the count to zero on every success).</param>
        public static bool ShouldBail(int consecutiveRetargets)
            => consecutiveRetargets > MaxRetargetsPerJob;

        /// <summary>The tick until which a churn-ended thing stays suppressed from the automatic haul scan.</summary>
        /// <param name="nowTick">The current game tick (the moment the job was churn-ended).</param>
        public static int SuppressUntil(int nowTick)
            => nowTick + BackoffTicks;

        /// <summary>
        /// Whether a stamped thing is still inside its re-offer backoff window. Exclusive upper bound: at
        /// exactly <paramref name="suppressedUntilTick"/> the thing is offered again.
        /// </summary>
        /// <param name="nowTick">The current game tick.</param>
        /// <param name="suppressedUntilTick">The stamp produced by <see cref="SuppressUntil"/>.</param>
        public static bool IsSuppressed(int nowTick, int suppressedUntilTick)
            => nowTick < suppressedUntilTick;
    }
}
