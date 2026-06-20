using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Two periodic jobs, run for every loaded save (GameComponents with a Game-ctor are added automatically):
    /// <list type="bullet">
    /// <item>Interval unloading — every <c>intervalUnloadHours</c> game-hours, ask each player pawn carrying
    /// scooped yields to make its consolidated unload trip.</item>
    /// <item>Deferred vein reveal — for each tracked vein route that ran into fog, append newly-uncovered cells
    /// as the pawn mines (only while the route's tail is still the pawn's last task).</item>
    /// </list>
    /// </summary>
    public partial class HaulersDreamGameComponent : GameComponent
    {
        private const int IdleTickInterval = 250;  // idle-backstop scan ~4x per in-game minute

        /// <summary>The component for the running game (null at the main menu / before a game loads).</summary>
        public static HaulersDreamGameComponent Instance => Current.Game?.GetComponent<HaulersDreamGameComponent>();

        public HaulersDreamGameComponent(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // CROSS-SESSION CACHE HYGIENE. Every per-session static cache (the bulk-haul plan memo, the per-tick
            // mass / surplus / tracked-mass memos, the per-(worker,def,tick) availability counts, the haul-to-stack
            // cell memo, the load-work memo, the route-picker claimed-by-others memo, the Common Sense owns-flow
            // memo, the batch-craft handoff map, ...) is STATIC state that survives a quickload into the freshly
            // loaded game — where its keys (thingIDNumber / TicksGame) can collide with the previous session's.
            // Each such cache REGISTERS its own idempotent Clear() with CacheRegistry from a static constructor, so
            // this single call clears every one of them and a newly-added cache can't be forgotten here (the former
            // hand-maintained list of X.Clear() calls silently rotted whenever a cache was added without a matching
            // line). This is hygiene only — each cache's own `tick != -1` / tick-stamp populate guard is the actual
            // cross-session safeguard; ClearAll just drops the stale references promptly on the load (main) thread.
            CacheRegistry.ClearAll();

            // fix/mix recovery: drop phantom bulk-load claims an older version's save-time job swap could have left
            // in the scribed ledger (those made the load/bulk planners read "fully claimed" and stalled hauling
            // colony-wide). No-op on a new game / clean ledger; self-heals an affected save on first load.
            ValidateLoadLedgerAfterLoad();

            // fix/mix recovery: clear jobs orphaned by a removed mod (notably a Pick Up And Haul save migrated to
            // Hauler's Dream) before the tick loop starts, so the per-tick NullReferenceException flood never begins.
            // No-op on a clean save. See RepairOrphanedJobsAfterLoad.
            RepairOrphanedJobsAfterLoad();
        }

        // fix/mix recovery: a save migrated off a mod that contributed a JobDef + JobDriver (e.g. Pick Up And
        // Haul → Hauler's Dream) loads pawns whose in-progress job deserialized with a NULL def and NULL driver
        // (the def name no longer resolves; the driver class is gone). Vanilla's own PostLoadInit cleanup
        // (Pawn_JobTracker.ExposeData → EndCurrentJob(Errored)) CRASHES on such a job — EndCurrentJob reads
        // curJob.def.collideWithPawns, which NREs — so the broken job is never cleared and the pawn re-throws
        // every tick (PatherTick → Job.MakeDriver → this.def.driverClass). The EndCurrentJob null-def prefix
        // (Patch_Pawn_JobTracker_EndCurrentJob_NullDefGuard) clears the CURRENT job on the PostLoadInit path;
        // this once-per-load sweep is the defense-in-depth backstop that ALSO clears null-def QUEUED jobs (which
        // that prefix never sees — they'd NRE later when promoted to current) and releases the orphaned
        // reservations vanilla's ReservationManager load-prune misses (it drops null-JOB/claimant reservations,
        // but a reservation pointing at a non-null Job whose def is null survives and would pin the item forever).
        //
        // This is corruption REPAIR, not exception suppression: a null-def job is unrunnable state RimWorld
        // itself tries (and fails) to clear; completing that cleanup is the fix. All field clears below are
        // def-safe — none dereferences curJob.def, unlike EndCurrentJob/CleanupCurrentJob/StopAll.
        private static void RepairOrphanedJobsAfterLoad()
        {
            var maps = Find.Maps;
            if (maps == null)
                return;

            int clearedJobs = 0, clearedReservations = 0;
            for (int m = 0; m < maps.Count; m++)
            {
                var map = maps[m];

                var pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns != null)
                {
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        var jt = pawns[i]?.jobs;
                        if (jt == null)
                            continue;

                        // Current job whose def vanished with its mod: clear it WITHOUT touching curJob.def.
                        if (jt.curJob != null && jt.curJob.def == null)
                        {
                            pawns[i].ClearReservationsForJob(jt.curJob); // by-reference release; def-independent
                            pawns[i].pather?.StopDead();                 // drop a stale path that would resume into MakeDriver
                            jt.curDriver = null;
                            jt.curJob = null;
                            clearedJobs++;
                        }

                        // Queued null-def jobs would NRE in MakeDriver once promoted to current — drop them now.
                        // RemoveAll(pawn, predicate) releases each removed job's reservations + returns it to pool;
                        // Job.Clear() assigns def=null (never reads it), so this is def-safe.
                        jt.jobQueue?.RemoveAll(pawns[i], j => j == null || j.def == null);
                    }
                }

                // Reservations pointing at a null-def job aren't pruned by ReservationManager's load-prune (it only
                // drops null-job/null-claimant ones), so they'd pin their target item forever. Snapshot then
                // release (ReleaseClaimedBy mutates the backing list).
                var rm = map.reservationManager;
                if (rm != null)
                {
                    var reservations = rm.ReservationsReadOnly;
                    List<Verse.AI.ReservationManager.Reservation> orphans = null;
                    for (int r = 0; r < reservations.Count; r++)
                    {
                        var res = reservations[r];
                        if (res?.Job != null && res.Job.def == null && res.Claimant != null)
                            (orphans ??= new List<Verse.AI.ReservationManager.Reservation>()).Add(res);
                    }
                    if (orphans != null)
                        for (int r = 0; r < orphans.Count; r++)
                        {
                            rm.ReleaseClaimedBy(orphans[r].Claimant, orphans[r].Job);
                            clearedReservations++;
                        }
                }
            }

            if (clearedJobs > 0 || clearedReservations > 0)
                HDLog.Msg($"Migration cleanup: cleared {clearedJobs} orphaned job(s) and "
                    + $"{clearedReservations} stranded reservation(s) left by a removed mod (e.g. Pick Up And "
                    + "Haul). Affected pawns will pick new jobs normally. This is a one-time repair for this save.");
        }

        public override void GameComponentTick()
        {
            int tick = Find.TickManager.TicksGame;
            if (veinTrackers != null && veinTrackers.Count > 0 && tick % VeinTickInterval == 0)
                ProcessVeinTrackers();

            // Idle/checkpoint backstop: when a colonist is idle — or in a between-runs job like a meal or
            // recreation — (a) scoop any pending fresh drops it still owes itself (DropThenHaul) and
            // (b) run the unload pass for tracked stock. This used to be a Harmony postfix on
            // JobGiver_Idle.TryGiveJob — DEAD in 1.6, where ordinary colonists never execute that node
            // (it lives only in gathering/ritual duty trees). Driven from here instead.
            if (tick % IdleTickInterval == 0)
            {
                RunIdleBackstop();
                PruneInertLoadTasks(); // drop fully-settled/released bulk-load ledger entries (cheap when empty)
            }

            // A2 anti-softlock auto-drop: refill the queue every SoftlockCheckInterval ticks, then service one
            // pawn per tick. Gated on the setting (byte-inert when off — the queue is also drained so it never
            // holds stale refs while disabled). Mirrors BLFT's SoftlockCleaner cadence + time-slicing.
            RunSoftlockDropDriver(tick);

            var s = HaulersDreamMod.Settings;
            if (s == null || !s.markForUnload
                || !SchedulePolicy.IntervalDueNow(tick, s.intervalUnloadHours))
                return;

            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m].mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < pawns.Count; i++)
                    PawnUnloadChecker.CheckIfShouldUnload(pawns[i]);
            }
        }

        private static void RunIdleBackstop()
        {
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return;
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m].mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (!IsUnloadCheckpoint(p))
                        continue;
                    // Unload check FIRST, then the self-pickup: both EnqueueFirst, so this order makes the
                    // queue [SelfPickup, Unload] — the pawn scoops its pending drops and unloads everything
                    // in ONE trip. (The reverse order ran the unload before the scoop, so freshly scooped
                    // stock waited for the next idle cycle: a second trip.) Safe either way for the strict-
                    // mode livelock: HasPendingRealWork excludes housekeeping jobs by defName, not by order.
                    if (s.markForUnload)
                        PawnUnloadChecker.CheckIfShouldUnload(p);
                    // Same eligibility gate as scoop time (FindWorker uses IsCandidate): a pawn that turned
                    // ineligible since its drops were recorded (drafted with pauseWhileDrafted, a settings
                    // flip, a non-home map) must not walk off to scoop into an inventory the unload side
                    // refuses to serve.
                    if (YieldRouter.IsCandidate(p))
                        YieldRouter.EnsureSelfPickupJob(p);
                }
            }
        }

        // A moment the backstop may act on: genuinely idle (no job, or a wander/wait filler), OR in a
        // between-runs job — eating or recreation — that an unload can be queued BEHIND. The queued
        // unload starts the instant the meal/joy job ends (queued jobs precede every think-tree scan),
        // so a pawn that drifted to dinner or horseshoes with a full backpack puts its stuff away right
        // after, instead of carrying it for the rest of the day; the current job itself is never
        // interrupted. (A queued job means the pawn is between tasks of a real run — the backstop must
        // not jump in; HasPendingRealWork in the checker also guards that, but skipping here avoids the
        // scan entirely.)
        private static bool IsUnloadCheckpoint(Pawn p)
        {
            if (p?.jobs == null || p.Drafted)
                return false;
            if (p.jobs.jobQueue != null && p.jobs.jobQueue.Count > 0)
                return false;
            if (p.CurJob == null)
                return true;
            var def = p.CurJobDef;
            // A loaded-but-orphaned job (its JobDef was removed with a mod — e.g. a Pick Up And Haul save migrated
            // to HD) has a non-null CurJob but a NULL def, so CurJobDef is null here and the def.joyKind read below
            // would NRE. RepairOrphanedJobsAfterLoad clears such jobs on load; this is the per-tick backstop in
            // case one is created mid-session. Treat a defless job as "not an unload checkpoint".
            if (def == null)
                return false;
            if (def == JobDefOf.Wait || def == JobDefOf.Wait_Wander
                || def == JobDefOf.GotoWander || def == JobDefOf.Wait_MaintainPosture)
                return true;
            // Eating and joy jobs: between work runs by definition. Sleep is deliberately NOT included —
            // a queued job fires before everything on wake (even urgent breakfast), and the morning work
            // scan / end-of-run trigger covers it anyway.
            return def == JobDefOf.Ingest || def.joyKind != null;
        }

        // The HD job defs whose pawn-running state must SUPPRESS a softlock drop: while a pawn is actively
        // running an HD load / unload / cleanup / craft-gather job, that job owns the tagged cargo and will
        // resolve it — never yank items out from under a live job. Shares the SINGLE canonical custom-driver set
        // with the pre-save cleanup (HdJobDefSets.CustomDriverJobDefs / plan A1) so the two can never drift: a
        // narrower set here previously let the softlock driver force-drop a Hauling-priority-0 crafter's tagged
        // ingredients mid-BatchCraft / BillPrepGather (those jobs tag cargo but were missing from this list).
        private static JobDef[] HdJobDefs => HdJobDefSets.CustomDriverJobDefs;

        private static bool IsRunningHdJob(Pawn pawn)
        {
            var def = pawn.CurJobDef;
            if (def == null)
                return false;
            var defs = HdJobDefs;
            for (int i = 0; i < defs.Length; i++)
                if (defs[i] == def)
                    return true;
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // Subsystem scribe blocks, in the ORIGINAL order so the save format is byte-identical:
            // veinTrackers -> batchBills -> loadTasks -> loadVehicleTasks.
            ExposeVein();
            ExposeBatchBills();
            ExposeLedger();
        }
    }
}
