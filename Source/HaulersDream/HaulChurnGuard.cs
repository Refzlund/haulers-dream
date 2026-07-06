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
                backoffUntil.Clear();
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
            int now = Find.TickManager?.TicksGame ?? 0;
            lock (sync)
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
}
