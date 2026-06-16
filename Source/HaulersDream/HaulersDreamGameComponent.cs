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
