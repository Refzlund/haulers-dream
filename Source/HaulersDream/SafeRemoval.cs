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
    /// <see cref="PrepareForSafeRemoval"/> is the one-shot, force-everything cleanup (it actually ends jobs and
    /// drops loot in the LIVE game). It is now exposed ONLY as a dev-mode action — the user-facing settings button
    /// was removed — because the automatic on-save protection (<see cref="NeutralizeForSave"/> + the components
    /// patches below) already makes every save removal-safe with no effect on play. Keep it for testing / manual
    /// recovery of a save first made by an older version.
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
        // Automatic protection: remove HD jobs (current cleared, queued extracted) from the WRITTEN SAVE ONLY,
        // so a save is always safe to disable the mod from — without touching the live game. See the patch below.
        // ---------------------------------------------------------------------------------------------

        /// <summary>The pre-save job state stashed so the LIVE game is restored byte-for-byte after the save
        /// bytes are written. The game is frozen on the main thread during serialization, so this clear/restore
        /// window is never observed by a tick.</summary>
        public sealed class SaveSwapState
        {
            public Pawn_JobTracker tracker;
            public Job stashedCurJob;
            public JobDriver stashedCurDriver;
            public bool curJobCleared;
            // Full pre-save queue order, captured when the queue held any HD job. The HD jobs are extracted from
            // the written save and the whole queue is rebuilt in this exact order afterward, so the live queue is
            // unchanged by the save while the written bytes reference no HD JobDef.
            public List<QueuedJob> queueSnapshot;
        }

        /// <summary>Just before this pawn's job tracker serializes (Saving mode), remove every HD job from the
        /// WRITTEN bytes so disabling the mod leaves no unresolvable HD JobDef.
        /// <para>
        /// The CURRENT HD job is CLEARED entirely (curJob + curDriver nulled), NOT rewritten to a placeholder:
        /// vanilla's PostLoadInit treats <c>curJob != null &amp;&amp; curDriver == null</c> as an INVALID job state
        /// (<c>Pawn_JobTracker.ExposeData</c>) and calls <c>EndCurrentJob(JobCondition.Errored)</c>, which starts a
        /// recovery job while the pawn isn't on a map yet during load — so <c>JobDriver_Wait</c>'s initAction NREs
        /// on <c>base.Map</c> and the pawn is left jobless (the reported "Cleaning up invalid job state …
        /// NullReferenceException" on load). A null curJob is the clean state: the pawn just picks a new job on its
        /// first tick. (Verified in Assembly-CSharp 1.6.4850.)
        /// </para><para>
        /// QUEUED HD jobs are EXTRACTED from the queue for the save (the full queue is rebuilt verbatim after).
        /// Vanilla would auto-drop a null-def queued job on load anyway (<c>JobQueue.ExposeData</c>), but extracting
        /// avoids both its "Could not load reference to JobDef" warning AND the earlier placeholder-Wait approach,
        /// which could FREEZE the pawn (a swapped <c>Wait</c> never completes).
        /// </para>
        /// Returns the stash to restore, or null if nothing changed (the common case — most pawns hold no HD job).
        /// Restored by <see cref="RestoreAfterSave"/> in the patch finalizer.</summary>
        public static SaveSwapState NeutralizeForSave(Pawn_JobTracker tracker)
        {
            if (tracker == null)
                return null;
            SaveSwapState state = null;

            var cur = tracker.curJob;
            if (cur != null && IsHaulersDreamJob(cur.def))
            {
                state = new SaveSwapState { tracker = tracker };
                state.stashedCurJob = cur;
                state.stashedCurDriver = tracker.curDriver;
                state.curJobCleared = true;
                tracker.curJob = null;
                tracker.curDriver = null;
            }

            var queue = tracker.jobQueue;
            if (queue != null && queue.Count > 0)
            {
                bool anyHd = false;
                var snapshot = new List<QueuedJob>();
                foreach (var qj in queue)
                {
                    snapshot.Add(qj);
                    if (qj?.job != null && IsHaulersDreamJob(qj.job.def))
                        anyHd = true;
                }
                if (anyHd)
                {
                    if (state == null)
                        state = new SaveSwapState { tracker = tracker };
                    state.queueSnapshot = snapshot;
                    // Pull the HD jobs out of the LIVE queue so the serialized queue omits them. The full order is
                    // rebuilt from the snapshot in RestoreAfterSave, so the live queue ends up unchanged.
                    foreach (var qj in snapshot)
                        if (qj?.job != null && IsHaulersDreamJob(qj.job.def))
                            queue.Extract(qj.job);
                }
            }

            return state;
        }

        /// <summary>Restores the live job state stashed by <see cref="NeutralizeForSave"/> — pure reassignment /
        /// list rebuild of held references. Runs in the patch FINALIZER so it happens even if serialization throws
        /// (the live game is never left missing its current job or its queued jobs).</summary>
        public static void RestoreAfterSave(SaveSwapState state)
        {
            if (state == null)
                return;
            if (state.curJobCleared && state.tracker != null)
            {
                state.tracker.curJob = state.stashedCurJob;
                state.tracker.curDriver = state.stashedCurDriver;
            }
            if (state.queueSnapshot != null && state.tracker?.jobQueue != null)
            {
                var q = state.tracker.jobQueue;
                // Empty the live remainder (the non-HD jobs left after the extract) and re-enqueue the full pre-save
                // snapshot in its exact original order, so the queue is byte-identical to before the save.
                while (q.Count > 0)
                    q.Extract(q[0].job);
                for (int i = 0; i < state.queueSnapshot.Count; i++)
                {
                    var qj = state.queueSnapshot[i];
                    if (qj?.job != null)
                        q.EnqueueLast(qj.job, qj.tag);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Automatic protection, part 2: omit HD's own Game/Map components from the WRITTEN SAVE so removing
        // the mod doesn't log "Could not find class HaulersDream..." / "Can't load abstract class" on load.
        // RimWorld auto-recreates a missing GameComponent/MapComponent via Game.FillComponents/Map.FillComponents
        // on the next load (verified against Assembly-CSharp), so the component is restored intact while HD is
        // present; the live game keeps its component (re-inserted in the patch finalizer, below).
        // ---------------------------------------------------------------------------------------------

        /// <summary>The component temporarily pulled out of a (Game/Map) components list for serialization, plus
        /// where it was, so the finalizer can re-insert it in place. Uses the non-generic IList view so one helper
        /// serves both List&lt;GameComponent&gt; and List&lt;MapComponent&gt;.</summary>
        public sealed class ComponentStripState
        {
            public System.Collections.IList list;
            public object component;
            public int index;
        }

        /// <summary>Remove <paramref name="component"/> from <paramref name="list"/> for the duration of the save,
        /// remembering its index. Returns null (no-op) if it isn't present. Pure list mutation on the frozen main
        /// thread — never observed by a tick.</summary>
        public static ComponentStripState StripComponent(System.Collections.IList list, object component)
        {
            if (list == null || component == null)
                return null;
            int idx = list.IndexOf(component);
            if (idx < 0)
                return null;
            list.RemoveAt(idx);
            return new ComponentStripState { list = list, component = component, index = idx };
        }

        /// <summary>Restore the stripped component to its original index, leaving EXACTLY ONE instance of its type
        /// in the list. Runs in the patch FINALIZER so the live list is restored even if serialization throws.
        /// <para>
        /// Why "leave exactly one" and not a simple re-insert: <c>Map.ExposeComponents</c> calls
        /// <c>Map.FillComponents()</c> UNCONDITIONALLY at its tail (verified in Assembly-CSharp), so during the SAVE
        /// — right after the list is serialized without our component — vanilla re-adds a FRESH instance of it to
        /// the live list. A plain re-insert would then leave TWO (the fresh one + our original), and the next save
        /// would only strip one, serializing the other back into the file (re-introducing the very "could not load
        /// class" error this removes, with the count climbing per save). So: drop every instance of the type, then
        /// re-insert ONLY the original (preserving its identity/transient state) at its index. (Game.FillComponents
        /// is load-only, so the Game patch never sees the fresh re-add — but the dedup is correct there too.)
        /// </para></summary>
        public static void RestoreComponentDedup<T>(ComponentStripState state) where T : class
        {
            if (state?.list == null || state.component == null)
                return;
            var list = state.list;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] is T)
                    list.RemoveAt(i);
            int idx = state.index;
            if (idx < 0 || idx > list.Count)
                idx = list.Count;
            list.Insert(idx, state.component);
        }
    }

    /// <summary>
    /// Makes every save automatically safe to disable Hauler's Dream from: during serialization only, any HD job on
    /// a pawn is removed from the written bytes (current job CLEARED, queued HD jobs EXTRACTED — see
    /// <see cref="SafeRemoval.NeutralizeForSave"/>), then the live job state is restored in the finalizer — so
    /// colonists are never interrupted during play, but the bytes on disk reference no HD JobDef. Without this, a
    /// pawn saved mid-HD-job leaves a null-def job when the mod is removed, which bricks the save's load (see
    /// SafeRemoval's class doc). Gated on Scribe Saving mode and the safeRemovalOnSave setting (default on; a
    /// kill-switch in case it ever conflicts with another save patch). Safe because RimWorld serializes
    /// synchronously on the main thread (game frozen), so the clear/restore window is never observed by a tick.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.ExposeData))]
    public static class Patch_PawnJobTracker_ExposeData_RemovalSafety
    {
        private static void Prefix(Pawn_JobTracker __instance, ref SafeRemoval.SaveSwapState __state)
        {
            __state = null;

            // LOAD-time safety net (any save, any setting — a fallback for OLD saves): a curJob whose curDriver
            // didn't load is exactly the state vanilla's own PostLoadInit treats as "invalid" — it logs "Cleaning
            // up invalid job state" and calls EndCurrentJob(JobCondition.Errored), which starts a recovery job
            // while the pawn isn't on a map yet, so JobDriver_Wait's initAction NREs on base.Map and the pawn is
            // left jobless. A save written by an OLDER version of this mod (which rewrote the current HD job to a
            // placeholder Wait and nulled curDriver) lands in exactly that state. CLEAR curJob FIRST (the clean
            // "no current job" state) so vanilla's else-if is skipped — the pawn just picks a fresh job next tick.
            // Runs BEFORE the patched method body, so this pre-empts vanilla's recovery. (Harmless for healthy
            // pawns: a normally-loaded pawn has curJob==null or curDriver!=null, so this never fires for them.)
            if (Scribe.mode == LoadSaveMode.PostLoadInit && __instance.curJob != null && __instance.curDriver == null)
            {
                __instance.curJob = null;
                return;
            }

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

    /// <summary>
    /// Omits <see cref="HaulersDreamGameComponent"/> from the WRITTEN save (only while it carries no persistent
    /// state — see <see cref="HaulersDreamGameComponent.HasNoSavedState"/>), so disabling the mod no longer logs
    /// "Could not find class HaulersDreamGameComponent" / "Can't load abstract class Verse.GameComponent" on load.
    /// The live game keeps the component (re-inserted in the finalizer); RimWorld's Game.FillComponents re-creates
    /// it on the next load while HD is present, so nothing is lost. Gated on Scribe Saving + safeRemovalOnSave.
    /// Game.ExposeData is the save path only (it early-returns on LoadingVars), so the Saving guard is sufficient.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.ExposeData))]
    public static class Patch_Game_ExposeData_RemovalSafety
    {
        private static void Prefix(Game __instance, ref SafeRemoval.ComponentStripState __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving)
                return;
            var settings = HaulersDreamMod.Settings;
            if (settings != null && !settings.safeRemovalOnSave)
                return;
            var comp = __instance.GetComponent<HaulersDreamGameComponent>();
            // Keep it (don't strip) when it holds active vein-reveal trackers, so that state persists; the rare
            // cost is one harmless load warning if the mod is removed mid fog-route. Empty is the common case.
            if (comp == null || !comp.HasNoSavedState)
                return;
            __state = SafeRemoval.StripComponent(__instance.components, comp);
        }

        private static void Finalizer(SafeRemoval.ComponentStripState __state)
        {
            SafeRemoval.RestoreComponentDedup<HaulersDreamGameComponent>(__state);
        }
    }

    /// <summary>
    /// Omits <see cref="MapComponent_RoutePreview"/> (a purely transient route-preview overlay — it persists no
    /// data) from each map's WRITTEN save, so disabling the mod no longer logs "Could not find class
    /// HaulersDream.MapComponent_RoutePreview" / "Can't load abstract class Verse.MapComponent" on load. The live
    /// map keeps it (re-inserted in the finalizer); Map.FillComponents re-creates it on load while HD is present.
    /// Gated on Scribe Saving + safeRemovalOnSave. Map.ExposeData serializes components only in its Saving branch.
    /// </summary>
    [HarmonyPatch(typeof(Map), nameof(Map.ExposeData))]
    public static class Patch_Map_ExposeData_RemovalSafety
    {
        private static void Prefix(Map __instance, ref SafeRemoval.ComponentStripState __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving)
                return;
            var settings = HaulersDreamMod.Settings;
            if (settings != null && !settings.safeRemovalOnSave)
                return;
            var comp = __instance.GetComponent<MapComponent_RoutePreview>();
            if (comp == null)
                return;
            __state = SafeRemoval.StripComponent(__instance.components, comp);
        }

        // Dedup-restore: Map.FillComponents re-adds a fresh MapComponent_RoutePreview during the save itself, so a
        // plain re-insert would duplicate it (see RestoreComponentDedup). Keep exactly the original.
        private static void Finalizer(SafeRemoval.ComponentStripState __state)
        {
            SafeRemoval.RestoreComponentDedup<MapComponent_RoutePreview>(__state);
        }
    }
}
