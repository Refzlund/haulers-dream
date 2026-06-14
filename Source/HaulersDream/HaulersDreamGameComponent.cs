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
    public class HaulersDreamGameComponent : GameComponent
    {
        private List<VeinRevealTracker> veinTrackers = new List<VeinRevealTracker>();
        private const int VeinTickInterval = 60;   // re-scan tracked veins ~once a second
        private const int IdleTickInterval = 250;  // idle-backstop scan ~4x per in-game minute

        public HaulersDreamGameComponent(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // BulkHaul's per-tick plan cache is STATIC state, so it survives a quickload into the freshly
            // loaded game — where its cached plans reference the previous game's (now-stale) things. Clear it
            // whenever a game finishes initialising (new game and load alike). Same hygiene for the batch
            // handoff map (entries whose ordered job never started).
            BulkHaul.ClearPlanCache();
            BatchCraftHandoff.Clear();
        }

        /// <summary>Register (or replace, per pawn) a deferred-reveal tracker for a vein route that ran into fog.</summary>
        public void RegisterVeinTracker(VeinRevealTracker tracker)
        {
            if (tracker?.pawn == null)
                return;
            // Bind the tracker to the map the route was planned on (registration runs synchronously right
            // after queueing, so the pawn is still there) — TryExtend drops it if the pawn changes maps.
            tracker.mapId = tracker.pawn.Map?.uniqueID ?? -1;
            if (veinTrackers == null)
                veinTrackers = new List<VeinRevealTracker>();
            veinTrackers.RemoveAll(t => t.pawn == tracker.pawn); // a new route supersedes the pawn's old one
            veinTrackers.Add(tracker);
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
                RunIdleBackstop();

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

        private void ProcessVeinTrackers()
        {
            // Master-switch gate (the F25 idiom): with the route planner off, in-flight trackers must not keep
            // designating/queueing newly revealed cells. The trackers are kept — re-enabling resumes them.
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.planRoutes)
                return;
            for (int i = veinTrackers.Count - 1; i >= 0; i--)
            {
                // No try/catch: a vein-tracker failure is a real bug to surface as a red error, not a verbose-only
                // debug line that swallows it and silently drops the tracker.
                if (!TryExtend(veinTrackers[i]))
                    veinTrackers.RemoveAt(i);
            }
        }

        // Extend one vein route as fog clears. Returns false to DROP the tracker (pawn gone, route superseded,
        // cap reached, or the vein is fully revealed with nothing left hidden).
        private bool TryExtend(VeinRevealTracker tr)
        {
            var pawn = tr?.pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null || tr.veinDef == null || tr.included == null)
                return false;
            // Map identity: the tracker's seed/lastCell/included are coordinates ON THE MAP the route was
            // planned on. A pawn that has changed maps since (caravan, drop pod, …) must never re-flood the
            // NEW map from them — the final-attempt branch below could otherwise designate + queue mining
            // there. Drop the tracker (matching the self-drop idiom; -1 = a pre-fix save, also dropped).
            if (pawn.Map.uniqueID != tr.mapId)
                return false;
            if (tr.included.Count >= tr.cap)
                return false; // route already at its chosen Amount — never grow past it

            // Only extend while the route's last task is STILL the pawn's last task — if the player has queued
            // other work after it (so the tail moved), leave the route alone, exactly as requested. The tail must
            // be the SAME cell AND a Mine job: once the final cell is mined its ore drops there, and a later haul
            // of that ore would coincidentally match the cell — the def check rejects that false positive.
            // EXCEPTION (the flagship 1-2-visible-cell stub): when the FINAL queued stop IS the fog-boundary cell,
            // the reveal happens at the exact moment the route completes — the tail check fails right then. If the
            // tail cell was MINED (not superseded) and the pawn has nothing else queued, make one last extend
            // attempt instead of dropping the tracker at the moment it was about to pay off.
            bool finalAttempt = false;
            if (!RouteExecutor.TryGetQueueTailCell(pawn, out IntVec3 tail, out Verse.AI.Job tailJob)
                || tail != tr.lastCell || tailJob.def != JobDefOf.Mine)
            {
                bool lastCellMined = !AnyVeinThingAt(pawn.Map, tr.veinDef, tr.lastCell);
                // "Nothing else queued" must ignore the mod's OWN housekeeping (the yield hook queues a
                // self-pickup — and possibly an unload — at the exact moment the final cell is mined);
                // only REAL queued work means the route was superseded. Same idiom as PawnUnloadChecker.
                var queue = pawn.jobs?.jobQueue;
                var queuedDefNames = new List<string>();
                if (queue != null)
                    foreach (var qj in queue)
                        if (qj?.job?.def != null)
                            queuedDefNames.Add(qj.job.def.defName);
                bool nothingElseQueued = !UnloadPolicy.HasPendingRealWork(queuedDefNames,
                    HaulersDreamDefOf.HaulersDream_SelfPickup.defName,
                    HaulersDreamDefOf.HaulersDream_UnloadInventory.defName);
                if (!lastCellMined || !nothingElseQueued)
                    return false; // genuinely superseded / diverted — leave the route alone
                finalAttempt = true;
            }

            var kind = WorkKindResolver.MiningKind(tr.veinDef);
            if (kind?.scanner == null)
                return false;

            // Re-flood the now-visible vein from the seed, with the route's own (mined) footprint as virtual
            // connectors; new stops are visible cells we haven't queued yet.
            var visible = RouteSelection.ReFloodVisibleVein(pawn.Map, pawn, tr.veinDef, tr.seed, tr.cap, tr.included, out bool stillFog);
            var newStops = new List<Thing>();
            for (int i = 0; i < visible.Count; i++)
            {
                var t = visible[i];
                if (t == null || tr.included.Contains(t.Position))
                    continue;
                if (tr.included.Count + newStops.Count >= tr.cap)
                    break;
                newStops.Add(t);
            }
            if (newStops.Count == 0)
                return !finalAttempt && stillFog; // a fruitless FINAL attempt drops the tracker (no route jobs remain to mine more)

            // Continue the route from the current tail: order the new cells nearest-first from there.
            newStops.Sort((a, b) =>
                (a.Position - tr.lastCell).LengthHorizontalSquared.CompareTo((b.Position - tr.lastCell).LengthHorizontalSquared));

            int appended = RouteExecutor.AppendStops(pawn, kind, newStops, out IntVec3 newLast);
            // Mark every revealed cell as covered (all were designated; any that didn't queue fall to normal mining).
            for (int i = 0; i < newStops.Count; i++)
                tr.included.Add(newStops[i].Position);
            if (appended > 0)
                tr.lastCell = newLast;
            return true;
        }

        /// <summary>Is a spawned thing of the vein's def still at <paramref name="cell"/>? (False = it was mined.)</summary>
        private static bool AnyVeinThingAt(Map map, ThingDef def, IntVec3 cell)
        {
            if (map == null || def == null || !cell.InBounds(map))
                return false;
            var things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
                if (things[i]?.def == def && things[i].Spawned)
                    return true;
            return false;
        }

        /// <summary>True when this component holds nothing that needs to persist across a save/load — i.e. no
        /// active vein-reveal trackers. The on-save removal-safety patch (see SafeRemoval) omits the component
        /// from the WRITTEN save when this is true, so disabling Hauler's Dream loads with no "could not load
        /// class HaulersDreamGameComponent" error; nothing is lost (a fresh component is re-created by RimWorld's
        /// FillComponents on the next load while HD is present). When trackers ARE active (a fog mining route mid
        /// extension), the component is kept so the trackers persist — the rare cost being one harmless load
        /// warning if the mod is removed at that exact moment.</summary>
        public bool HasNoSavedState => veinTrackers == null || veinTrackers.Count == 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref veinTrackers, "haulersDreamVeinTrackers", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && veinTrackers == null)
                veinTrackers = new List<VeinRevealTracker>();
        }
    }
}
