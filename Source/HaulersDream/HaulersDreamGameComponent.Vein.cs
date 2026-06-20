using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    public partial class HaulersDreamGameComponent
    {
        private List<VeinRevealTracker> veinTrackers = new List<VeinRevealTracker>();
        private const int VeinTickInterval = 60;   // re-scan tracked veins ~once a second

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
            if (VeinExtendPolicy.AtCap(tr.included.Count, tr.cap))
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
            bool tailStillMatchesRoute = RouteExecutor.TryGetQueueTailCell(pawn, out IntVec3 tail, out Verse.AI.Job tailJob)
                                         && tail == tr.lastCell && tailJob.def == JobDefOf.Mine;
            if (!tailStillMatchesRoute)
            {
                // Only compute the expensive Verse queries when the tail no longer matches (preserve the
                // short-circuit — a normal extend never pays for these).
                bool lastCellMined = !AnyVeinThingAt(pawn.Map, tr.veinDef, tr.lastCell);
                // "Nothing else queued" must ignore the mod's OWN housekeeping (the yield hook queues a
                // self-pickup — and possibly an unload — at the exact moment the final cell is mined);
                // only REAL queued work means the route was superseded. Same idiom as PawnUnloadChecker.
                var queue = pawn.jobs?.jobQueue;
                // Allocation-free queue scan (indexed for — the JobQueue enumerator boxes; the indexer does not)
                // + a reference compare against the two housekeeping defs. No List<string>/params allocation.
                bool nothingElseQueued = true;
                if (queue != null)
                {
                    var selfPickup = HaulersDreamDefOf.HaulersDream_SelfPickup;
                    var unload = HaulersDreamDefOf.HaulersDream_UnloadInventory;
                    for (int i = 0; i < queue.Count; i++)
                        if (UnloadPolicy.IsPendingRealWork(queue[i]?.job?.def, selfPickup, unload))
                        {
                            nothingElseQueued = false;
                            break;
                        }
                }
                var outcome = VeinExtendPolicy.DecideSupersession(false, lastCellMined, nothingElseQueued);
                if (outcome == ExtendOutcome.Drop)
                    return false; // genuinely superseded / diverted — leave the route alone
                finalAttempt = outcome == ExtendOutcome.FinalAttempt;
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
                if (!VeinExtendPolicy.CanAddStop(tr.included.Count, newStops.Count, tr.cap))
                    break;
                newStops.Add(t);
            }
            if (newStops.Count == 0)
                return VeinExtendPolicy.KeepAfterNoNewStops(finalAttempt, stillFog); // a fruitless FINAL attempt drops the tracker (no route jobs remain to mine more)

            // Continue the route from the current tail: order the new cells nearest-first from there.
            newStops.Sort((a, b) =>
            {
                // MP determinism: total-order tiebreak so ties don't depend on input order across clients.
                int c = (a.Position - tr.lastCell).LengthHorizontalSquared.CompareTo((b.Position - tr.lastCell).LengthHorizontalSquared);
                return c != 0 ? c : a.thingIDNumber.CompareTo(b.thingIDNumber);
            });

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

        // The deferred-reveal vein-tracker scribing (additive to base.ExposeData via ExposeData() -> ExposeVein()).
        private void ExposeVein()
        {
            Scribe_Collections.Look(ref veinTrackers, "haulersDreamVeinTrackers", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && veinTrackers == null)
                veinTrackers = new List<VeinRevealTracker>();
        }
    }
}
