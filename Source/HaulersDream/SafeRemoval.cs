using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Prepare for safe removal": clears every Hauler's Dream job from all pawns (and drops the loot HD
    /// scooped into their packs) so the player can DISABLE the mod without bricking the save on the next load.
    ///
    /// WHY THIS IS NEEDED: HD's custom JobDefs (see <see cref="HaulersDreamDefOf"/>) get written into a save
    /// whenever a pawn is mid- (or has queued-) one of them. If the mod is then removed, that save's
    /// curJob/curDriver point at an HD JobDef + JobDriver class that no longer exist, so the job loads with
    /// def == null. Vanilla's own invalid-job cleanup (Pawn_JobTracker.EndCurrentJob) then dereferences
    /// `curJob.def.collideWithPawns` with no null guard and throws, leaving the pawn half-loaded; it re-throws
    /// every tick in PostMapInit/PatherTick, and an unguarded colonist-bar OnGUI in another mod (e.g. Color
    /// Coded Mood Bar) can then NRE every frame and blank the whole HUD. Once HD's assembly is gone NONE of
    /// its code runs, so HD can only prevent this WHILE STILL INSTALLED — by making sure no HD job is sitting
    /// in the save. That is what this action does.
    ///
    /// Deliberately MANUAL (a settings button + a dev action), NOT auto-run on every save: force-ending an
    /// in-progress batch-craft / bill-gather on each autosave would throw away that job's accumulated progress
    /// and visibly interrupt pawns during normal play. The poison only matters when the player actually intends
    /// to remove the mod, so the clear is tied to that explicit intent.
    /// </summary>
    public static class SafeRemoval
    {
        private static HashSet<JobDef> hdJobDefs;

        /// <summary>Every HD JobDef, by reference identity (so a rename stays in sync via HaulersDreamDefOf).
        /// Skips any that failed to bind (null) — a def that doesn't exist can never be a live pawn's job.</summary>
        private static HashSet<JobDef> HdJobDefs()
        {
            if (hdJobDefs != null)
                return hdJobDefs;
            var set = new HashSet<JobDef>();
            void Add(JobDef d) { if (d != null) set.Add(d); }
            Add(HaulersDreamDefOf.HaulersDream_UnloadInventory);
            Add(HaulersDreamDefOf.HaulersDream_SelfPickup);
            Add(HaulersDreamDefOf.HaulersDream_OverloadConstructDeliver);
            Add(HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild);
            Add(HaulersDreamDefOf.HaulersDream_ClaimFromHauler);
            Add(HaulersDreamDefOf.HaulersDream_BatchCraft);
            Add(HaulersDreamDefOf.HaulersDream_InventoryDoBill);
            Add(HaulersDreamDefOf.HaulersDream_BillPrepGather);
            Add(HaulersDreamDefOf.HaulersDream_BulkHaul);
            Add(HaulersDreamDefOf.HaulersDream_LoadPackAnimal);
            return hdJobDefs = set;
        }

        /// <summary>True if this JobDef is one of Hauler's Dream's custom jobs — the only save data that turns
        /// into a brick when the mod is removed mid-job.</summary>
        public static bool IsHaulersDreamJob(JobDef def) => def != null && HdJobDefs().Contains(def);

        /// <summary>
        /// Ends every HD job (current + queued) on every pawn and drops the inventory HD scooped, so a
        /// subsequent disable of the mod leaves no unresolvable HD job in the save. Returns a human summary.
        /// Safe to call at any time: pawns just re-pick from the think tree next tick. No try/catch — with HD
        /// installed these are ordinary, fully-resolvable calls, and a genuine failure should surface, not hide.
        /// </summary>
        public static string PrepareForSafeRemoval()
        {
            int activeCleared = 0, queuedCleared = 0, pawnsDropped = 0, stacksDropped = 0;

            var pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null)
                    continue;

                var jobs = p.jobs;
                if (jobs != null)
                {
                    // Collect first (EndCurrentOrQueuedJob mutates curJob + the queue), then end each. Leaves
                    // any NON-HD queued jobs untouched.
                    bool curIsHd = IsHaulersDreamJob(jobs.curJob?.def);
                    List<Job> toEnd = null;
                    if (curIsHd)
                        (toEnd = toEnd ?? new List<Job>()).Add(jobs.curJob);
                    if (jobs.jobQueue != null)
                        foreach (var qj in jobs.jobQueue)
                            if (qj?.job != null && IsHaulersDreamJob(qj.job.def))
                                (toEnd = toEnd ?? new List<Job>()).Add(qj.job);

                    if (toEnd != null)
                    {
                        if (curIsHd) activeCleared++;
                        queuedCleared += toEnd.Count - (curIsHd ? 1 : 0);
                        // startNewJob:false — don't let a pawn instantly grab ANOTHER HD job mid-sweep; it
                        // re-evaluates next tick (or, once the mod is gone, loads as a clean vanilla pawn).
                        for (int j = 0; j < toEnd.Count; j++)
                            jobs.EndCurrentOrQueuedJob(toEnd[j], JobCondition.InterruptForced, startNewJob: false);
                    }
                }

                if (DropHauledInventory(p, ref stacksDropped))
                    pawnsDropped++;
            }

            return "Hauler's Dream — prepared for safe removal: cleared " + activeCleared + " active and " +
                   queuedCleared + " queued job(s), and returned " + stacksDropped + " carried stack(s) from " +
                   pawnsDropped + " pawn(s) to the ground. Save your game, then you can disable the mod safely.";
        }

        /// <summary>Drops the loot HD scooped into this pawn's pack back to the ground (spawned pawns only — a
        /// caravan pawn has no map, and its loot rides home as caravan inventory regardless). The comp itself
        /// is orphaned-safe on removal, so this is courtesy (returning the player's goods), not the brick fix.</summary>
        private static bool DropHauledInventory(Pawn p, ref int stacksDropped)
        {
            if (!p.Spawned || p.Map == null)
                return false;
            var comp = p.GetComp<CompHauledToInventory>();
            var owner = p.inventory?.innerContainer;
            if (comp == null || owner == null)
                return false;

            // Snapshot: TryDrop + Deregister both mutate live collections; never iterate them directly.
            var tagged = new List<Thing>(comp.PeekHashSet());
            bool any = false;
            for (int i = 0; i < tagged.Count; i++)
            {
                var thing = tagged[i];
                if (thing == null || !owner.Contains(thing))
                {
                    comp.Deregister(thing);
                    continue;
                }
                if (owner.TryDrop(thing, p.Position, p.Map, ThingPlaceMode.Near, out Thing _))
                {
                    stacksDropped++;
                    any = true;
                }
                comp.Deregister(thing);
            }
            return any;
        }

        // ---------------------------------------------------------------------------------------------
        // Automatic protection: rewrite HD jobs to a harmless placeholder in the WRITTEN SAVE ONLY, so a
        // save is always safe to disable the mod from — without touching the live game. See the patch below.
        // ---------------------------------------------------------------------------------------------

        /// <summary>A vanilla JobDef that always resolves and whose cleanup is harmless. Saved HD jobs are
        /// rewritten to this; on load they are cleanly ended by vanilla (curDriver is nulled, so
        /// Pawn_JobTracker.ExposeData's PostLoadInit ends the job) instead of NRE-ing on a missing def.</summary>
        private static JobDef PlaceholderDef => JobDefOf.Wait;

        /// <summary>The pre-save job state stashed so the LIVE game is restored byte-for-byte after the save
        /// bytes are written. The game is frozen on the main thread during serialization, so this swap/restore
        /// window is never observed by a tick.</summary>
        public sealed class SaveSwapState
        {
            public Pawn_JobTracker tracker;
            public JobDriver stashedCurDriver;
            public bool curDriverNulled;
            public readonly List<Job> jobs = new List<Job>();
            public readonly List<JobDef> defs = new List<JobDef>();
        }

        /// <summary>Just before this pawn's job tracker serializes (Saving mode), rewrite any HD job (current +
        /// queued) to <see cref="PlaceholderDef"/> and null the current driver, so the WRITTEN bytes contain no
        /// HD JobDef/JobDriver reference. Returns the stash to restore afterwards, or null if nothing changed
        /// (the common case — most pawns are not in an HD job). The live job objects are mutated in place and
        /// restored by <see cref="RestoreAfterSave"/> in the patch finalizer.</summary>
        public static SaveSwapState NeutralizeForSave(Pawn_JobTracker tracker)
        {
            if (tracker == null)
                return null;
            SaveSwapState state = null;

            var cur = tracker.curJob;
            if (cur != null && IsHaulersDreamJob(cur.def))
            {
                state = new SaveSwapState { tracker = tracker };
                state.jobs.Add(cur);
                state.defs.Add(cur.def);
                cur.def = PlaceholderDef;
                // Null the driver so the saved job has curDriver==null && curJob!=null -> vanilla's PostLoadInit
                // cleanly ENDS it (the placeholder def resolves, so no NRE), with or without HD present on load.
                state.stashedCurDriver = tracker.curDriver;
                state.curDriverNulled = true;
                tracker.curDriver = null;
            }

            var queue = tracker.jobQueue;
            if (queue != null)
            {
                foreach (var qj in queue)
                {
                    if (qj?.job != null && IsHaulersDreamJob(qj.job.def))
                    {
                        if (state == null)
                            state = new SaveSwapState { tracker = tracker };
                        state.jobs.Add(qj.job);
                        state.defs.Add(qj.job.def);
                        qj.job.def = PlaceholderDef;
                    }
                }
            }

            return state;
        }

        /// <summary>Restores the live job state stashed by <see cref="NeutralizeForSave"/>. Pure field
        /// reassignment of held references — cannot fail — and runs in the patch FINALIZER so it happens even if
        /// serialization throws (the live game is never left with a placeholder job).</summary>
        public static void RestoreAfterSave(SaveSwapState state)
        {
            if (state == null)
                return;
            for (int i = 0; i < state.jobs.Count; i++)
                state.jobs[i].def = state.defs[i];
            if (state.curDriverNulled && state.tracker != null)
                state.tracker.curDriver = state.stashedCurDriver;
        }
    }

    /// <summary>
    /// Makes every save automatically safe to disable Hauler's Dream from: during serialization only, an HD job
    /// on a pawn is rewritten to a harmless vanilla placeholder (see <see cref="SafeRemoval.NeutralizeForSave"/>),
    /// then the live job is restored in the finalizer — so colonists are never interrupted during play, but the
    /// bytes on disk reference no HD JobDef. Without this, a pawn saved mid-HD-job leaves a null-def job when the
    /// mod is removed, which bricks the save's load (see SafeRemoval's class doc). Gated on Scribe Saving mode and
    /// the safeRemovalOnSave setting (default on; a kill-switch in case it ever conflicts with another save patch).
    /// Safe because RimWorld serializes synchronously on the main thread (game frozen), so the swap/restore window
    /// is never observed by a tick.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.ExposeData))]
    public static class Patch_PawnJobTracker_ExposeData_RemovalSafety
    {
        private static void Prefix(Pawn_JobTracker __instance, ref SafeRemoval.SaveSwapState __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving)
                return;
            var settings = HaulersDreamMod.Settings;
            if (settings != null && !settings.safeRemovalOnSave)
                return;
            __state = SafeRemoval.NeutralizeForSave(__instance);
        }

        // Finalizer (not Postfix) so the live job is restored even if serialization throws; it returns void and
        // takes no Exception param, so it never swallows an in-flight exception (no-suppression policy).
        private static void Finalizer(SafeRemoval.SaveSwapState __state)
        {
            SafeRemoval.RestoreAfterSave(__state);
        }
    }
}
