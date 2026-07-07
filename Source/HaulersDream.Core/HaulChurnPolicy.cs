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

        // --- Per-THING failed-job budget (the recurring "still loops" layer, issue #144) -------------------
        //  The per-JOB budget above only counts failed PLACEMENT ARRIVALS. But a vanilla JobDriver_HaulToCell
        //  storage haul can fail BEFORE it ever reaches the placement toil: with the destination cell left
        //  unreserved (HaulToStack), the cell's IsValidStorageFor can flip to false while the pawn is still
        //  walking to the item (the GotoThing fail-on) or carrying it (CarryHauledThingToCell's fail-on), which
        //  ends the job Incompletable and drops the stack at the pawn's feet. The work scan then rebuilds an
        //  identical job at once. The pawn never arrives, so the per-job counter never ticks and no delivery
        //  aside-degrades, and the reported loop paces on with nothing ever counted (confirmed by a report log full
        //  of the haul yet with zero churn-bail lines). This layer counts those whole-job failures PER THING
        //  across job instances, and escalates in two steps: once a thing fails too many hauls in quick succession
        //  HD first ACTIVELY RESCUES it (a directed recovery haul that reserves its destination cell, so no
        //  competing hauler can invalidate it mid-carry, the exact cause of the loop, see HaulChurnGuard.IsRecovering
        //  and the HaulToStack patches); only if that reserved-cell haul ALSO keeps failing does it fall back to the
        //  re-offer backoff the per-job layer uses (the safety net, so a genuinely un-storable item stops pacing).

        /// <summary>
        /// How many storage-haul jobs for ONE thing may fail in quick succession (each within
        /// <see cref="FailGapTicks"/> of the previous) before the thing is backed off from the automatic haul
        /// scan. A legitimate haul does not fail the same thing repeatedly: it either succeeds (which resets
        /// the tally) or the thing is stored by someone else, so four rapid whole-job failures for one item is
        /// already the pacing loop, not a transient re-route.
        /// </summary>
        public const int MaxFailedJobsPerThing = 4;

        /// <summary>
        /// The maximum gap (in game ticks) between two consecutive failed storage hauls of the same thing for
        /// them to still count as the SAME churn run. 300 ticks (5 seconds at normal speed) comfortably spans a
        /// single pace-and-drop cycle, so a sustained loop keeps accumulating, while an isolated failure whose
        /// next failure is more than this apart resets the tally to one (it was not a loop). This gap-based
        /// reset (rather than a fixed window from the first failure) makes the bound robust to a slow loop
        /// cadence without ever accumulating unrelated, well-spaced failures.
        /// </summary>
        public const int FailGapTicks = 300;

        /// <summary>
        /// Fold one more failed storage haul of a thing into its running tally. Resets the count to one when
        /// this failure is the first seen (<paramref name="priorCount"/> &lt;= 0) or came more than
        /// <see cref="FailGapTicks"/> after the previous one (an isolated failure, not a loop); otherwise
        /// increments it. The last-failure tick is always advanced to now, so the next call measures its gap
        /// from THIS failure.
        /// </summary>
        /// <param name="nowTick">The tick this failure occurred.</param>
        /// <param name="lastFailTick">The tick of the thing's previous counted failure (ignored when
        /// <paramref name="priorCount"/> is 0).</param>
        /// <param name="priorCount">The thing's failure count before this one (0 if none tracked).</param>
        /// <param name="newLastFailTick">Out: the tick to store as the thing's latest failure (always
        /// <paramref name="nowTick"/>).</param>
        /// <param name="newCount">Out: the thing's failure count after folding this failure in.</param>
        public static void RecordThingFailure(int nowTick, int lastFailTick, int priorCount,
            out int newLastFailTick, out int newCount)
        {
            newLastFailTick = nowTick;
            newCount = (priorCount <= 0 || nowTick - lastFailTick > FailGapTicks) ? 1 : priorCount + 1;
        }

        /// <summary>
        /// Whether a thing that has now failed <paramref name="failCount"/> storage hauls in quick succession
        /// must be backed off. Reaching (not merely exceeding) the budget bails: the fourth rapid failure is the
        /// one that stops the loop.
        /// </summary>
        /// <param name="failCount">The thing's current rapid-failure count (from
        /// <see cref="RecordThingFailure"/>).</param>
        public static bool ShouldBackOffThing(int failCount)
            => failCount >= MaxFailedJobsPerThing;

        /// <summary>
        /// How many DIRECTED recovery hauls (a haul that reserves its destination cell, so it holds the cell
        /// exclusively and cannot be invalidated by a competing hauler) a thing may fail before HD gives up on
        /// actively rescuing it and falls back to the re-offer backoff. A reserved-cell haul behaves exactly like a
        /// vanilla haul, so it should complete on the first attempt; three is a generous bound that only trips when
        /// storage is genuinely, persistently unavailable (in which case the backoff safety net takes over).
        /// </summary>
        public const int MaxRecoveryAttempts = 3;

        /// <summary>The escalation step to take after a thing's storage haul just failed. Drives the two-stage
        /// per-thing response: count rapid failures, then actively rescue, then (only if rescue keeps failing) back
        /// off.</summary>
        public enum StorageHaulFailureResponse
        {
            /// <summary>Still inside the normal rapid-failure window and below the recovery threshold: keep
            /// tallying, take no special action.</summary>
            KeepCounting,

            /// <summary>The rapid-failure budget was just reached: switch this thing to directed recovery, so its
            /// next haul reserves its destination cell and completes instead of oscillating.</summary>
            StartRecovery,

            /// <summary>A directed recovery haul failed but attempts remain: keep this thing in recovery and let it
            /// try another reserved-cell haul.</summary>
            KeepRecovering,

            /// <summary>Directed recovery kept failing (storage is genuinely unavailable): stop actively rescuing
            /// and fall back to the re-offer backoff so the pointless pacing ends.</summary>
            GiveUpAndBackOff
        }

        /// <summary>
        /// Decide the response to a failed storage haul of a thing, given whether it is already in directed recovery
        /// and the thing's post-increment counts. When NOT recovering, the thing enters recovery the moment its
        /// rapid-failure count reaches <see cref="MaxFailedJobsPerThing"/>. When recovering, it keeps trying
        /// reserved-cell hauls until its recovery attempts reach <see cref="MaxRecoveryAttempts"/>, then falls back
        /// to the backoff. Pure: the caller supplies the already-updated counts and owns the live state.
        /// </summary>
        /// <param name="recovering">Whether the thing is currently in directed recovery.</param>
        /// <param name="failCount">The thing's rapid-failure count AFTER folding in this failure (used only when
        /// <paramref name="recovering"/> is false).</param>
        /// <param name="recoveryAttempts">The thing's recovery-attempt count AFTER incrementing for this failure
        /// (used only when <paramref name="recovering"/> is true).</param>
        public static StorageHaulFailureResponse OnStorageHaulFailed(bool recovering, int failCount, int recoveryAttempts)
        {
            if (recovering)
                return recoveryAttempts >= MaxRecoveryAttempts
                    ? StorageHaulFailureResponse.GiveUpAndBackOff
                    : StorageHaulFailureResponse.KeepRecovering;
            return failCount >= MaxFailedJobsPerThing
                ? StorageHaulFailureResponse.StartRecovery
                : StorageHaulFailureResponse.KeepCounting;
        }
    }
}
