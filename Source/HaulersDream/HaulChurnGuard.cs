using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// HAUL CHURN GUARD (the "hemogen pack infinite haul loop" fix). Vanilla's storage-haul arrival toil,
    /// <see cref="Toils_Haul.PlaceHauledThingInCell"/> with <c>storageMode:true</c>, retries WITHOUT BOUND
    /// inside one job: a failed drop re-resolves storage from the pawn's CURRENT position, retargets the same
    /// job and jumps back to the carry toil; with no storage resolving it degrades to a non-storage
    /// "haul aside" spot and jumps back all the same (decompile-verified, RW 1.6). Because Hauler's Dream
    /// deliberately removes vanilla's destination-cell reservation for stackable storage hauls (HaulToStack's
    /// no-reserve, the multi-pawn stack-on-one-tile feature), two haulers can converge on the same few viable
    /// cells and invalidate each other's arrivals indefinitely, and the re-resolve re-anchoring to the moving
    /// pawn makes the destination oscillate between near-equidistant candidates. Observed in the wild as the
    /// reported clean-save loop: two pawns pacing a prison-barracks corridor forever, each with a freshly
    /// extracted hemogen pack in hand, the inspector reading vanilla's "hauling hemogen pack." with the
    /// destination already degraded to a non-storage aside cell, nothing ever deposited, and each pawn fixed
    /// only by a forced player order that REPLACED its immortal job.
    ///
    /// <para>Two layers, both decided by the pure <see cref="HaulChurnPolicy"/>:</para>
    /// <list type="bullet">
    ///   <item><b>Per-job retry budget</b> (the placement-toil postfix): count CONSECUTIVE zero-progress
    ///   failed placement arrivals per job, reset on every successful placement and on every partial absorb
    ///   (a top-up into a near-full stack is progress, not churn); past the budget, end the job Incompletable.
    ///   Vanilla's own cleanup then drops the carried stack at the pawn's feet (the standard failed-haul
    ///   shape, nothing is lost) and the pawn re-plans from scratch.</item>
    ///   <item><b>Re-offer backoff</b> (the work-scan postfix): a churn-ended thing, and a thing whose
    ///   delivery had to DEGRADE to a bare-ground aside spot because the in-toil storage re-find failed
    ///   map-wide, is not re-offered to the AUTOMATIC haul scan for a short window, so the scan cannot
    ///   instantly rebuild the identical doomed job. Forced player orders bypass the backoff: an explicit
    ///   order must always work, which is exactly how the reporter un-stuck a looping pawn.</item>
    /// </list>
    ///
    /// <para>The budget also bounds every OTHER creator of storage hauls that reuses the same arrival toil
    /// (this mod's own inventory unload included), so any future variant of the loop, whatever flips the cell
    /// state, degrades to sparse bounded retries instead of a permanent pacing loop. Multiplayer: all state
    /// here mutates only on synced simulation paths (toil execution, the work scan) and is keyed on
    /// thingIDNumber and TicksGame, so every client makes identical decisions.</para>
    /// </summary>
    internal static class HaulChurnGuard
    {
        /// <summary>Per-job churn state, with the owning job's loadID pinned so a pooled and recycled
        /// <see cref="Job"/> instance (JobMaker.ReturnToPool reassigns loadID on reuse,
        /// decompile-verified) can never inherit a previous job's state.</summary>
        private sealed class Counter
        {
            /// <summary>The job.loadID this state belongs to; a mismatch means the Job instance was recycled.</summary>
            internal int loadID;
            /// <summary>Consecutive ZERO-PROGRESS failed placement arrivals since the last successful
            /// placement or partial absorb in this job.</summary>
            internal int retargets;
            /// <summary>Whether the current delivery DEGRADED to a non-storage aside spot (the in-toil
            /// storage re-find failed map-wide). Survives partial-absorb tally resets, because absorbing a
            /// few units along the way does not undo that failure for what is still in hand; dies when the
            /// delivery concludes hands-empty.</summary>
            internal bool asideDegraded;
        }

        // Keyed by Job INSTANCE: a ConditionalWeakTable holds no strong reference (GC-safe; pooled instances
        // stay keyed only while the pool itself keeps them alive) and needs no cleanup hook when a job ends
        // outside the guard. The loadID pin above handles instance recycling. NOT readonly: net48's
        // ConditionalWeakTable has no Clear(), so the load-time hygiene sweep re-news the table instead
        // (see Clear).
        private static ConditionalWeakTable<Job, Counter> retargetsByJob =
            new ConditionalWeakTable<Job, Counter>();

        // thingIDNumber -> suppressed-until tick for churn-ended haulables. Guarded by a lock: stamps happen
        // on the synced sim path (toil execution), but reads run inside the haul work scan, which a threading
        // mod may fan out to worker threads; the ops are nanosecond-scale dictionary hits, so a plain lock is
        // the simple, provably safe choice. Entries are pruned on stamp (the dict only ever holds the handful
        // of things that recently churned) and cleared on game load.
        private static readonly object sync = new object();
        private static readonly Dictionary<int, int> backoffUntil = new Dictionary<int, int>();

        // thingIDNumber -> (tick of the thing's last counted failure, rapid-failure count). A stackable storage
        // haul that keeps failing BEFORE its placement toil runs (the unreserved destination cell going invalid
        // mid-walk / mid-carry) never reaches the per-job arrival counter, so this per-thing tally is what stamps
        // such a thing into backoffUntil once it fails MaxFailedJobsPerThing times in quick succession (#144).
        // Guarded by the same `sync` lock (both are tiny dictionary ops); cleared on success and on game load.
        private static readonly Dictionary<int, (int lastFailTick, int failCount)> thingFails
            = new Dictionary<int, (int lastFailTick, int failCount)>();

        // Reused prune scratch (only ever touched under the lock).
        private static readonly List<int> expiredScratch = new List<int>();

        // Self-register the per-session clears with the game-load hygiene sweep (see CacheRegistry), so they
        // can never be forgotten. thingIDNumbers collide across saves, so a stale stamp could suppress an
        // unrelated item for up to BackoffTicks after a quickload; the load-time clear removes even that.
        static HaulChurnGuard() => CacheRegistry.Register(Clear);

        /// <summary>Drop every backoff stamp AND every per-job tally (game-load hygiene). Stamps: cross-save
        /// thingIDNumber collisions could otherwise suppress an unrelated item after a quickload. Tallies: the
        /// job pool is a process-static SimplePool that survives a load while UniqueIDsManager (the loadID
        /// source) rewinds with the save, so a surviving pooled Job instance could re-receive the exact loadID
        /// a stale counter pinned; re-newing the table (net48's ConditionalWeakTable has no Clear) closes even
        /// that.</summary>
        internal static void Clear()
        {
            lock (sync)
            {
                backoffUntil.Clear();
                thingFails.Clear();
            }
            // Loads never interleave with the running sim, so a plain reference swap is race-free here.
            retargetsByJob = new ConditionalWeakTable<Job, Counter>();
        }

        /// <summary>The live churn state for <paramref name="job"/>. A recycled Job instance (different loadID)
        /// starts fresh, so a pooled instance can never inherit a previous job's state.</summary>
        private static Counter CounterFor(Job job)
        {
            var counter = retargetsByJob.GetValue(job, _ => new Counter { loadID = job.loadID });
            if (counter.loadID != job.loadID)
            {
                // The pooled Job instance was recycled for a brand-new job; restart its state.
                counter.loadID = job.loadID;
                counter.retargets = 0;
                counter.asideDegraded = false;
            }
            return counter;
        }

        /// <summary>Record one more consecutive zero-progress failed placement arrival for
        /// <paramref name="job"/> and return the new tally.</summary>
        internal static int CountRetarget(Job job)
            => ++CounterFor(job).retargets;

        /// <summary>The current redirect tally for <paramref name="job"/> WITHOUT incrementing it. The in-flight
        /// re-route (<see cref="Patch_CarryHauledThingToCell_ReRoute"/>) reads the shared per-job budget to decide
        /// whether another mid-carry redirect is still allowed before it spends one. Returns 0 for a job with no
        /// state, or one whose pooled instance was recycled (loadID mismatch), so a fresh job always gets the full
        /// budget.</summary>
        internal static int PeekRetargets(Job job)
            => retargetsByJob.TryGetValue(job, out var c) && c.loadID == job.loadID ? c.retargets : 0;

        /// <summary>Remember that <paramref name="job"/>'s current delivery DEGRADED to a non-storage aside
        /// spot. The placement fail branch only does that after a map-wide storage re-find came up empty, so
        /// when the aside placement later completes, the thing has earned the same re-offer backoff as a
        /// churn-ended thing (without it the scan instantly rebuilds a storage job against unchanged storage).
        /// The note lives until the delivery concludes hands-empty, surviving partial-absorb tally resets on
        /// the way.</summary>
        internal static void NoteAsideDegrade(Job job)
            => CounterFor(job).asideDegraded = true;

        /// <summary>A partial absorb PROGRESSED <paramref name="job"/>'s delivery (units left the pawn's hands
        /// even though the drop reported failure): restart the consecutive-failure tally, progress is as good
        /// as a success for the churn bound, but KEEP the delivery's aside-degrade note (see
        /// <see cref="NoteAsideDegrade"/>).</summary>
        internal static void NotifyProgress(Job job)
        {
            if (retargetsByJob.TryGetValue(job, out var counter) && counter.loadID == job.loadID)
                counter.retargets = 0;
        }

        /// <summary>Whether <paramref name="job"/>'s concluding delivery had degraded to a non-storage aside
        /// spot. Read-only probe (never creates state); the loadID pin keeps a recycled Job instance from
        /// reading a previous job's note.</summary>
        internal static bool WasAsideDegraded(Job job)
            => retargetsByJob.TryGetValue(job, out var counter)
               && counter.loadID == job.loadID
               && counter.asideDegraded;

        /// <summary>The delivery CONCLUDED for <paramref name="job"/> (hands empty after an arrival, or the
        /// job was churn-ended): forget all its per-delivery state, so the budget measures consecutive
        /// failures within one delivery only (a multi-item unload trip must not accumulate across delivered
        /// items) and the aside-degrade note never outlives its delivery.</summary>
        internal static void NotifyPlaced(Job job)
            => retargetsByJob.Remove(job);

        /// <summary>Stamp <paramref name="thing"/> as churn-ended: the automatic haul scan will not re-offer it
        /// until the backoff window passes. Prunes expired stamps in the same breath, so the table stays tiny.</summary>
        internal static void StampBackoff(Thing thing)
        {
            if (thing == null)
                return;
            lock (sync)
                StampBackoffLocked(thing, Find.TickManager?.TicksGame ?? 0);
        }

        /// <summary>The body of <see cref="StampBackoff"/>, assuming the caller ALREADY holds <c>sync</c>, so
        /// <see cref="NoteThingHaulFailed"/> can stamp within its own locked tally update without re-taking the
        /// lock or re-reading the tick. Prunes expired stamps, then stamps <paramref name="thing"/>.</summary>
        private static void StampBackoffLocked(Thing thing, int now)
        {
            // Opportunistic prune: drop every expired stamp (the table only ever holds recent churn).
            expiredScratch.Clear();
            foreach (var pair in backoffUntil)
                if (!HaulChurnPolicy.IsSuppressed(now, pair.Value))
                    expiredScratch.Add(pair.Key);
            for (int i = 0; i < expiredScratch.Count; i++)
                backoffUntil.Remove(expiredScratch[i]);
            expiredScratch.Clear();

            backoffUntil[thing.thingIDNumber] = HaulChurnPolicy.SuppressUntil(now);
        }

        /// <summary>Record that a stackable storage <see cref="JobDriver_HaulToCell"/> job for
        /// <paramref name="thing"/> ended WITHOUT depositing it (an Incompletable goto/carry storage-invalid fail,
        /// or the per-job arrival guard's own bail). Folds the failure into the thing's rapid-failure tally; once
        /// it reaches <see cref="HaulChurnPolicy.MaxFailedJobsPerThing"/> failures in quick succession the thing is
        /// stamped into the SAME re-offer backoff the per-job layer uses (so the automatic scan stops rebuilding
        /// the identical doomed job) and its tally resets, giving it a fresh budget after the window. This is the
        /// layer that bounds the reported loop the arrival guard never sees: the pawn fails before it ever reaches
        /// the placement toil, so nothing counted the churn.</summary>
        internal static void NoteThingHaulFailed(Thing thing)
        {
            if (thing == null)
                return;
            int now = Find.TickManager?.TicksGame ?? 0;
            lock (sync)
            {
                thingFails.TryGetValue(thing.thingIDNumber, out var state);
                HaulChurnPolicy.RecordThingFailure(now, state.lastFailTick, state.failCount,
                    out int newLast, out int newCount);
                if (HaulChurnPolicy.ShouldBackOffThing(newCount))
                {
                    thingFails.Remove(thing.thingIDNumber);
                    StampBackoffLocked(thing, now);
                    HDLog.Dbg($"{thing.LabelShort} failed {newCount} storage hauls in quick succession; "
                              + "backing it off from the automatic haul scan.");
                }
                else
                {
                    thingFails[thing.thingIDNumber] = (newLast, newCount);
                }
            }
        }

        /// <summary>A stackable storage haul for <paramref name="thing"/> SUCCEEDED (it reached storage): clear
        /// its rapid-failure tally, the loop is broken. Nothing un-stamps an active backoff, but a stored thing is
        /// off the floor and the scan will not re-offer it regardless.</summary>
        internal static void NoteThingHaulSucceeded(Thing thing)
        {
            if (thing == null)
                return;
            lock (sync)
                thingFails.Remove(thing.thingIDNumber);
        }

        /// <summary>Whether <paramref name="thing"/> is inside its churn backoff window (suppressed from the
        /// AUTOMATIC haul scan; forced orders never ask). Read-only: expired entries are left for the next
        /// stamp's prune, so this stays safe from any scan thread.</summary>
        internal static bool IsBackedOff(Thing thing)
        {
            // Unlocked fast path: the table is almost always empty, and a torn read of Count can only defer
            // one suppression by a single scan tick (the per-job budget still bounds that job).
            if (thing == null || backoffUntil.Count == 0)
                return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            lock (sync)
            {
                return backoffUntil.TryGetValue(thing.thingIDNumber, out int until)
                       && HaulChurnPolicy.IsSuppressed(now, until);
            }
        }
    }

    /// <summary>
    /// Layer 1, the per-job retry budget: wrap the arrival toil that vanilla's storage hauls (and this mod's
    /// inventory unload, which reuses it) finish with, and end the job once its consecutive zero-progress
    /// failed arrivals exceed <see cref="HaulChurnPolicy.MaxRetargetsPerJob"/>. Only the retarget-capable variant is wrapped
    /// (storageMode with a jump-back toil); every other PlaceHauledThingInCell caller cannot loop (with no
    /// jump-back toil the fail path ends or falls through) and is left byte-identical.
    /// </summary>
    [HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.PlaceHauledThingInCell))]
    public static class Patch_PlaceHauledThingInCell_ChurnGuard
    {
        static void Postfix(Toil __result, Toil nextToilOnPlaceFailOrIncomplete, bool storageMode)
        {
            // No jump-back toil means no retry cycle to bound; non-storage placements never re-resolve.
            // A null __result means a foreign prefix skipped the factory; degrade to no-wrap.
            if (!storageMode || nextToilOnPlaceFailOrIncomplete == null || __result == null)
                return;
            var toil = __result;
            var original = toil.initAction;
            if (original == null)
                return;

            toil.initAction = delegate
            {
                var actor = toil.actor;
                var jobs = actor?.jobs;
                var job = jobs?.curJob;
                if (job == null)
                {
                    original();
                    return;
                }

                // Snapshot the carry and the haul mode before the placement runs. The stack snapshot tells a
                // PARTIAL ABSORB (vanilla tops up a near-full stack, fires placedAction for the absorbed part,
                // then returns false for the remainder, landing in the same fail branch as a true failed drop)
                // apart from zero-progress churn. The haul mode snapshot detects the in-toil aside DEGRADE
                // (the fail branch sets ToCellNonStorage only after the map-wide storage re-find failed).
                var carriedBefore = actor.carryTracker?.CarriedThing;
                int countBefore = carriedBefore?.stackCount ?? 0;
                var haulModeBefore = job.haulMode;

                original();

                bool jobStillCurrent = jobs.curJob == job;

                // The aside degrade happened in THIS arrival's fail branch: note it on the job so the aside
                // placement's completion can back off the re-offer. The TRANSITION test (was not aside before,
                // is now) keeps jobs CREATED as non-storage asides (construction clearing and friends; the
                // HaulToCell driver passes storageMode:true unconditionally, so those run this wrapped toil
                // too) from ever being mistaken for a failed storage haul.
                if (jobStillCurrent && job.haulMode == HaulMode.ToCellNonStorage
                    && haulModeBefore != HaulMode.ToCellNonStorage)
                    HaulChurnGuard.NoteAsideDegrade(job);

                var carried = actor.carryTracker?.CarriedThing;
                bool stillCarrying = carried != null;
                // Progress must be PROVEN: the same thing still in hand with a strictly smaller stack. A
                // different in-hand thing (foreign code swapping the carry mid-placement) is not provable
                // progress and counts as a failure, which only errs toward bounding a strange job sooner.
                bool madeProgress = stillCarrying && carried == carriedBefore
                                    && carried.stackCount < countBefore;

                if (!HaulChurnPolicy.CountsAsRetarget(jobStillCurrent, stillCarrying, madeProgress))
                {
                    if (!jobStillCurrent)
                        return; // the placement ended the job itself; a dead job has nothing to count or reset

                    if (stillCarrying)
                    {
                        // Partial absorb: part of the load was delivered, so the pawn is making real headway
                        // even though the drop reported failure for the remainder. Reset the tally like a
                        // success; the pathological loop can never look like this (it places zero units per
                        // arrival), so the reported-bug bound is untouched.
                        HaulChurnGuard.NotifyProgress(job);
                        return;
                    }

                    // Hands empty: placed, fully absorbed, or vanilla's destroy bail. If this delivery had
                    // DEGRADED to a bare-ground aside spot, a map-wide storage re-find already failed
                    // conclusively for this thing, so give it the same re-offer backoff as a churn-ended
                    // thing; without it the scan instantly rebuilds the identical doomed storage job against
                    // unchanged storage. Read the note BEFORE NotifyPlaced drops it.
                    if (HaulChurnGuard.WasAsideDegraded(job))
                        HaulChurnGuard.StampBackoff(carriedBefore);
                    HaulChurnGuard.NotifyPlaced(job);
                    return;
                }

                int consecutive = HaulChurnGuard.CountRetarget(job);
                if (!HaulChurnPolicy.ShouldBail(consecutive))
                    return; // within budget: let vanilla's retry run (a legitimate re-route resolves in 1-2)

                // Over budget: this job is churning (the reported pacing loop). Suppress the thing from the
                // automatic scan for a short window, then end the job. Vanilla's CleanupCurrentJob drops the
                // carried stack at the pawn's feet (ThingPlaceMode.Near), the standard failed-haul shape, so
                // nothing is ever lost; a fresh scan re-plans once the backoff passes (or a player order,
                // which bypasses the backoff, takes it anywhere at once).
                HaulChurnGuard.StampBackoff(carried);
                HaulChurnGuard.NotifyPlaced(job); // drop the tally with the job
                HDLog.Dbg($"{actor} storage haul churned {consecutive} consecutive zero-progress arrivals "
                          + $"(carrying {carried.LabelShort}); ending the job and backing off.");
                jobs.EndCurrentJob(JobCondition.Incompletable);
            };
        }

        // Seam guard (fix/mix): a throw in this factory postfix would break haul-job construction wholesale.
        // Log it attributably, then rethrow (never swallow).
        static System.Exception Finalizer(System.Exception __exception)
            => HDGuard.SeamThrew(__exception, "Toils_Haul.PlaceHauledThingInCell (HD churn guard)", null,
                "storage-haul placement could not be wrapped; vanilla behavior stands for this toil.");
    }

    /// <summary>
    /// Layer 2, the re-offer backoff: keep the AUTOMATIC haul work scan from instantly rebuilding the exact
    /// job the budget just ended. Runs FIRST among JobOnThing postfixes so the bulk-haul upgrade postfix
    /// (Patch_WorkGiver_HaulGeneral_BulkHaul) sees the suppressed null and stands down too, covering both the
    /// vanilla single haul and its bulk upgrade in one gate. Forced orders (the float menu's "Prioritize
    /// hauling", which probes and clicks with forced:true) return before the table is ever consulted, so a
    /// player can always override, exactly as the reporter did. Other, rarer creators (vanilla opportunistic
    /// hauls, stack merges) are deliberately not gated here: each of their jobs is already bounded by the
    /// Layer 1 budget, so the worst case without this gate is sparse bounded retries, never a tight loop.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_HaulGeneral), nameof(WorkGiver_HaulGeneral.JobOnThing))]
    public static class Patch_WorkGiver_HaulGeneral_ChurnBackoff
    {
        [HarmonyPriority(Priority.First)]
        static void Postfix(ref Job __result, Thing t, bool forced)
        {
            if (__result == null || forced)
                return;
            if (HaulChurnGuard.IsBackedOff(t))
                __result = null; // leave it where it lies; retried automatically once the window passes
        }
    }

    /// <summary>
    /// Layer 3, the per-THING failed-job budget (issue #144: the loop that "still" reproduced after Layer 1).
    /// The reported pacing is a rapid SEQUENCE of vanilla <see cref="JobDriver_HaulToCell"/> storage jobs that
    /// each fail BEFORE the placement toil runs: with the destination cell unreserved (HaulToStack), the cell's
    /// <c>IsValidStorageFor</c> flips to false while the pawn walks to the item (vanilla's GotoThing fail-on) or
    /// carries it (CarryHauledThingToCell's fail-on), ending the job Incompletable and dropping the stack at the
    /// pawn's feet; the work scan rebuilds an identical job at once. The per-job arrival budget never counts these
    /// (the pawn never arrives to place) and no delivery aside-degrades, so nothing stamps the thing off, leaving
    /// a report log full of the haul yet with ZERO churn-bail lines, exactly as the reporter's log shows. This
    /// postfix attaches a finish action to each such job that tallies its outcome per THING across job instances
    /// (<see cref="HaulChurnGuard.NoteThingHaulFailed"/> / <see cref="HaulChurnGuard.NoteThingHaulSucceeded"/>);
    /// past the budget the thing rides the same re-offer backoff Layer 1 uses.
    ///
    /// <para>Scoped to exactly the jobs that can loop this way: an AUTOMATIC (non-player-forced) storage haul of a
    /// STACKABLE thing while HaulToStack is on, the same predicate under which
    /// <see cref="Patch_JobDriver_HaulToCell_NoCellReservation"/> removes the cell reservation. With the feature
    /// off vanilla reserves the cell (which prevents the contention outright), so this stays completely inert.
    /// Player-forced orders never count and bypass the backoff, so an explicit order always works, exactly how
    /// the reporter un-stuck a looping pawn. Multiplayer: the tally mutates only on the synced job-cleanup path
    /// and is keyed on thingIDNumber + TicksGame, so every client stamps identically.</para>
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_HaulToCell), nameof(JobDriver_HaulToCell.Notify_Starting))]
    public static class Patch_JobDriver_HaulToCell_PerThingChurnGuard
    {
        static void Postfix(JobDriver_HaulToCell __instance)
        {
            var settings = HaulersDreamMod.Settings;
            if (settings == null || !settings.haulToStack)
                return; // the cell-reservation-skipping feature (the loop's cause) is off -> vanilla reserves the cell
            var job = __instance.job;
            if (job == null || job.playerForced || job.haulMode != HaulMode.ToCellStorage)
                return;
            var thing = job.GetTarget(TargetIndex.A).Thing;
            if (thing?.def == null || thing.def.stackLimit <= 1)
                return; // unstackables keep vanilla's cell reservation (see the no-reserve patch) -> can't loop this way

            // Tally this job's outcome per THING when it ends. A success clears the loop; an Incompletable finish
            // (the goto/carry storage-invalid fails, and Layer 1's own bail) counts toward the per-thing backoff
            // budget. Other end conditions (a drafted/interrupted pawn) are deliberately ignored: neither a loop
            // signal nor proof the thing can be stored. The captured `thing` is read only for its thingIDNumber,
            // so a destroyed/merged stack at finish time is harmless.
            __instance.AddFinishAction(condition =>
            {
                if (condition == JobCondition.Succeeded)
                    HaulChurnGuard.NoteThingHaulSucceeded(thing);
                else if (condition == JobCondition.Incompletable)
                    HaulChurnGuard.NoteThingHaulFailed(thing);
            });
        }

        // Seam guard (fix/mix): a throw in this Notify_Starting postfix would break HaulToCell startup wholesale.
        // Log it attributably, then rethrow (never swallow).
        static System.Exception Finalizer(System.Exception __exception)
            => HDGuard.SeamThrew(__exception, "JobDriver_HaulToCell.Notify_Starting (HD per-thing churn guard)",
                null, "per-thing churn tally not attached; the per-job arrival guard still applies for this job.");
    }

    /// <summary>
    /// The CORE strengthening (issue #144 follow-up): make Haul To Stack's multi-pawn hauls RE-ROUTE in hand when
    /// their shared destination cell fills mid-carry, instead of letting vanilla fail the whole job and drop the
    /// item at the pawn's feet, the drop-and-rescan that IS the reported loop. Haul To Stack deliberately leaves
    /// the destination cell unreserved so several pawns can pile onto one tile
    /// (<see cref="Patch_JobDriver_HaulToCell_NoCellReservation"/>); vanilla's <see cref="JobDriver_HaulToCell"/>
    /// was never built for that, so <see cref="Toils_Haul.CarryHauledThingToCell"/>'s fail-on ends the job the
    /// instant the cell stops being valid storage (it filled while the pawn walked over).
    ///
    /// <para>This postfix PREPENDS an end-condition to that carry toil. CheckCurrentToilEndOrFail evaluates a
    /// toil's endConditions in order, so ours runs BEFORE vanilla's fail-on: for a stackable no-reserve storage
    /// haul whose cell just went invalid, it re-resolves another stacking cell (StoreUtility's own search, which
    /// HD refines to a partial stack so the load still consolidates), retargets the job and restarts the path, so
    /// the pawn keeps carrying to a good cell and vanilla's fail-on then reads the new VALID target and does not
    /// fire. The item is never dropped and the pawn never paces. It reserves NOTHING, so the multi-pawn stacking
    /// that is the whole point of the feature stays fully on.</para>
    ///
    /// <para>Bounded by the SAME per-job budget the arrival re-route uses: once a delivery has redirected past
    /// <see cref="HaulChurnPolicy.MaxRetargetsPerJob"/> times it stops re-routing and lets vanilla's fail-on run
    /// (drop), which the per-thing tally then escalates to the re-offer backoff, the safety net. Scoped to exactly
    /// the reported loop: a vanilla <c>JobDefOf.HaulToCell</c> storage haul of a STACKABLE thing while Haul To
    /// Stack is on. Inert otherwise, and when the feature is off (vanilla reserves the cell, so there is no
    /// contention to re-route). A pooled toil is Clear()'d on ReturnToPool (endConditions emptied), so the inserted
    /// condition never accumulates across reuses. Multiplayer: the retarget + StartPath run on the synced job tick
    /// and read only synced storage + tally state, so every client redirects to the same cell.</para>
    /// </summary>
    [HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.CarryHauledThingToCell))]
    public static class Patch_CarryHauledThingToCell_ReRoute
    {
        static void Postfix(Toil __result, TargetIndex squareIndex, PathEndMode pathEndMode)
        {
            if (__result == null)
                return;
            // Prepend, so this runs before vanilla's storage-invalid fail-on for the same toil.
            __result.endConditions.Insert(0, () => ReRouteIfDestinationFilled(__result, squareIndex, pathEndMode));
        }

        /// <summary>The prepended end-condition. ALWAYS returns Ongoing (it never ends the toil): its only job is
        /// the side effect of redirecting a stranded stacking haul to a fresh cell so vanilla's own fail-on then
        /// sees a valid target. When it cannot (feature off, unstackable, over budget, or no other cell) it leaves
        /// the invalid cell to vanilla's fail-on, which drops the item as before and feeds the per-thing backoff.</summary>
        static JobCondition ReRouteIfDestinationFilled(Toil toil, TargetIndex squareIndex, PathEndMode pathEndMode)
        {
            try
            {
                var actor = toil.actor;
                var job = actor?.jobs?.curJob;
                if (job == null || job.def != JobDefOf.HaulToCell || job.haulMode != HaulMode.ToCellStorage)
                    return JobCondition.Ongoing; // only the vanilla storage haul carries the fail-on we pre-empt
                var settings = HaulersDreamMod.Settings;
                if (settings == null || !settings.haulToStack)
                    return JobCondition.Ongoing; // feature off -> vanilla reserves the cell, nothing can strand it
                var carried = actor.carryTracker?.CarriedThing;
                if (carried?.def == null || carried.def.stackLimit <= 1)
                    return JobCondition.Ongoing; // unstackables keep vanilla's reserved cell -> no contention
                var cell = job.GetTarget(squareIndex).Cell;
                if (cell.IsValidStorageFor(actor.Map, carried))
                    return JobCondition.Ongoing; // still a good destination -> keep walking (the common case)

                // The shared cell filled or went invalid while we carried toward it. Redirect in hand to another
                // stacking cell rather than dropping the load, bounded by the same budget the arrival re-route uses.
                if (HaulChurnPolicy.ShouldBail(HaulChurnGuard.PeekRetargets(job)))
                    return JobCondition.Ongoing; // redirected too many times -> let vanilla drop it -> backoff net
                if (StoreUtility.TryFindBestBetterStoreCellFor(carried, actor, actor.Map, StoragePriority.Unstored,
                        actor.Faction, out var newCell) && newCell.IsValid && newCell != cell)
                {
                    HaulChurnGuard.CountRetarget(job);
                    job.SetTarget(squareIndex, newCell);
                    actor.pather.StartPath(newCell, pathEndMode);
                }
                return JobCondition.Ongoing;
            }
            catch (System.Exception e)
            {
                // Degrade to vanilla: log once (HD-attributed), let the vanilla fail-on handle the filled cell.
                HDGuard.SeamDegraded(e, "Toils_Haul.CarryHauledThingToCell (HD stacking re-route)", toil?.actor,
                    "in-flight re-route skipped; vanilla's fail-on drops the item as before.");
                return JobCondition.Ongoing;
            }
        }

        // Seam guard: a throw while BUILDING the toil (the Insert above) would break haul-job construction. Log it
        // attributably, then rethrow (never swallow). The tick-time lambda has its own degrade try/catch above.
        static System.Exception Finalizer(System.Exception __exception)
            => HDGuard.SeamThrew(__exception, "Toils_Haul.CarryHauledThingToCell (HD stacking re-route wrap)", null,
                "in-flight re-route not attached; vanilla's fail-on handles a filled cell as before.");
    }

    // DIAGNOSTIC (issue #162): log job STARTS for any pawn that is carrying something or has tracked inventory,
    // so the "pacing up and down" loop — which is silent in every existing log — reveals which jobs are
    // thrashing. This postfix fires on every StartJob but only logs for pawns with items (carry tracker or
    // tracked inventory), keeping the log clean. Grep for [#162] job-start.
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_Diag_JobStartLog
    {
        static void Postfix(Pawn_JobTracker __instance, Job newJob)
        {
            if (newJob == null)
                return;
            var pawn = __instance?.pawn;
            if (pawn == null)
                return;
            var carry = pawn.carryTracker?.CarriedThing;
            var comp = pawn.TryGetComp<CompHauledToInventory>();
            int tracked = comp?.PeekHashSet()?.Count ?? 0;
            if (carry == null && tracked == 0)
                return; // pawn has no items — not relevant to the haul loop
            HDLog.Dbg($"[#162] job-start: {pawn} starting {newJob.def?.defName}"
                      + (newJob.targetA.HasThing ? $" target={newJob.targetA.Thing?.LabelShort}" : "")
                      + (carry != null ? $" carrying={carry.LabelShort}" : "")
                      + (tracked > 0 ? $" tracked={tracked}" : "")
                      + $" pos={pawn.Position}");
        }
    }
}
