using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Tethers a player-ordered construction into ONE continuous task: when an ordered delivery to a site
    /// completes, queue the NEXT step on that same site at the FRONT of the queue — another delivery if other
    /// material types are still missing, or vanilla's own FinishFrame once buildable — each step player-forced,
    /// so "prioritize constructing" means haul AND build, not haul-then-wander-off. Survives the blueprint→frame
    /// transition by re-finding the constructible at the site's position (the Thing is replaced). Used by the
    /// right-click order and the route planner's haul+build mode; the haul-only paths never call it.
    ///
    /// Vanilla's own continuation (PriorityWork) scans the cell with non-forced HasJobOnThing and self-clears the
    /// moment nothing matches — too fragile across the transition and our job conversions. This is deterministic.
    /// </summary>
    public static class ConstructTether
    {
        /// <summary>Queue the next step for the constructible at <paramref name="cell"/>, if any. Returns true
        /// if a follow-up job was queued. <paramref name="allowDeliverTether"/> false = only the FinishFrame
        /// branch may fire (the just-ended delivery made NO progress — tethering another delivery would walk
        /// an over-ceiling pawn stockpile↔site forever; the site stays designated for normal work instead).</summary>
        public static bool QueueNext(Pawn pawn, IntVec3 cell, bool allowDeliverTether = true)
        {
            if (pawn?.Map == null || pawn.Dead || !pawn.Spawned || pawn.Drafted || pawn.jobs == null)
                return false;
            var site = FindConstructibleAt(pawn.Map, cell, pawn.Faction);
            if (site == null)
                return false;

            // Frame ready to build → vanilla's own FinishFrame does the building with full fidelity.
            if (site is Frame frame && frame.IsCompleted())
            {
                if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction))
                    return false; // a haul-capable-only pawn delivered — building is someone else's job
                // No try/catch: GenConstruct.CanConstruct is a vanilla call — a throw is a real bug to surface.
                if (!GenConstruct.CanConstruct(frame, pawn, checkSkills: true, forced: true))
                    return false;
                var build = JobMaker.MakeJob(JobDefOf.FinishFrame, frame);
                build.playerForced = true;
                if (build.TryMakePreToilReservations(pawn, false))
                {
                    pawn.jobs.jobQueue.EnqueueFirst(build, JobTag.Misc);
                    return true;
                }
                return false;
            }

            // Still needs materials (this or another type) → order the next delivery. The deliver scanner's
            // forced JobOnThing goes through our ResourceDeliverJobFor postfix, which (forced) converts it to
            // the tethered deliver+build job — so the chain continues until the building stands.
            if (!allowDeliverTether)
                return false; // 0-progress delivery: stop the chain here (livelock guard)

            // Blueprints and frames have DIFFERENT deliver scanners — try whichever matches the site's stage.
            // QueueNext only ever runs at the end of a HAUL+BUILD (tether) job, so the scanner re-entry must
            // carry that intent: with RouteIntent left at None and the global orderedConstructTether OFF, the
            // conversion would hand back the PLAIN deliver def — whose finish never calls QueueNext — and a
            // haul+build route would lose its tether from the second material onward.
            //
            // It must ALSO carry the route's remaining whole-route demand: the route executor only published
            // RouteDemandByDef for the FIRST material it gathered (synchronously, at route creation). Every
            // OTHER material per stop (steel/components after the wood) is delivered through THIS tether — so
            // without the demand here, each one gathered only a single frame's worth and the pawn topped off
            // at the stockpile for steel after every wall. Publishing the remaining whole-route demand lets the
            // first steel trip load the route's steel in one sweep; the driver's entry gate then delivers the
            // intervening frames from the carried surplus. Restored in the finally (symmetric with RouteIntent).
            var priorIntent = InventoryConstructDelivery.RouteIntent;
            var priorDemand = InventoryConstructDelivery.RouteDemandByDef;
            try
            {
                InventoryConstructDelivery.RouteIntent = ConstructRouteIntent.HaulBuild;
                InventoryConstructDelivery.RouteDemandByDef = RemainingRouteDemand(pawn, site);
                foreach (var scanner in DeliverScanners())
                {
                    // No try/catch: the deliver scanner throwing is a real bug to surface (the outer finally still
                    // restores RouteIntent on the way out); the "no job" case is the explicit ternary null below.
                    Job next = scanner.HasJobOnThing(pawn, site, forced: true) ? scanner.JobOnThing(pawn, site, forced: true) : null;
                    if (next == null)
                        continue;
                    next.playerForced = true;
                    if (next.TryMakePreToilReservations(pawn, false))
                    {
                        pawn.jobs.jobQueue.EnqueueFirst(next, JobTag.Misc);
                        return true;
                    }
                }
            }
            finally
            {
                InventoryConstructDelivery.RouteIntent = priorIntent;
                InventoryConstructDelivery.RouteDemandByDef = priorDemand;
            }
            return false;
        }

        /// <summary>
        /// The per-def material still needed by the constructible at <paramref name="site"/> PLUS every other
        /// construction site this pawn has queued ahead (its remaining route stops). Lets a tethered delivery
        /// of a SECOND material (steel after wood) gather the WHOLE route's steel in one sweep — the same way
        /// the route executor batches the first material — so later steel deliveries come from the carried
        /// surplus instead of a per-wall stockpile trip. Needers are deduped by identity (a frame can have
        /// more than one queued material job); destroyed / despawned / non-constructible queued targets are
        /// skipped. For a plain single-building tether (no queued route stops) this reduces to just the
        /// building's own per-def need — a no-op vs the old single-frame gather.
        /// </summary>
        private static Dictionary<ThingDef, int> RemainingRouteDemand(Pawn pawn, Thing site)
        {
            var demand = new Dictionary<ThingDef, int>();
            var seen = new HashSet<Thing>();
            AccumulateDemand(site, demand, seen);
            var queue = pawn.jobs?.jobQueue;
            if (queue != null)
                for (int i = 0; i < queue.Count; i++)
                {
                    var job = queue[i]?.job;
                    if (job == null)
                        continue;
                    if (job.def == HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild
                        || job.def == HaulersDreamDefOf.HaulersDream_OverloadConstructDeliver)
                        AccumulateDemand(job.targetB.Thing ?? job.targetC.Thing, demand, seen);
                }
            return demand;
        }

        private static void AccumulateDemand(Thing t, Dictionary<ThingDef, int> demand, HashSet<Thing> seen)
        {
            if (t == null || t.Destroyed || !t.Spawned || !(t is IConstructible ic) || !seen.Add(t))
                return;
            var costs = ic.TotalMaterialCost();
            if (costs == null)
                return;
            for (int k = 0; k < costs.Count; k++)
            {
                var d = costs[k]?.thingDef;
                if (d == null)
                    continue;
                int n = ic.ThingCountNeeded(d);
                if (n <= 0)
                    continue;
                demand.TryGetValue(d, out int cur);
                demand[d] = cur + n;
            }
        }

        /// <summary>The blueprint or frame of <paramref name="faction"/> at <paramref name="cell"/> — the original
        /// needer Thing may have been REPLACED (blueprint→frame) by the delivery that just finished.</summary>
        public static Thing FindConstructibleAt(Map map, IntVec3 cell, Faction faction)
        {
            if (map == null || !cell.InBounds(map))
                return null;
            var things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                if (t is IConstructible && t.Faction == faction && t.Spawned && !t.Destroyed
                    && (t is Blueprint || t is Frame))
                    return t;
            }
            return null;
        }

        private static System.Collections.Generic.List<WorkGiver_Scanner> deliverScanners;

        internal static System.Collections.Generic.List<WorkGiver_Scanner> DeliverScanners()
        {
            if (deliverScanners != null)
                return deliverScanners;
            deliverScanners = new System.Collections.Generic.List<WorkGiver_Scanner>(2);
            var defs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
                if ((typeof(WorkGiver_ConstructDeliverResourcesToBlueprints).IsAssignableFrom(defs[i].giverClass)
                     || typeof(WorkGiver_ConstructDeliverResourcesToFrames).IsAssignableFrom(defs[i].giverClass))
                    && defs[i].Worker is WorkGiver_Scanner s)
                    deliverScanners.Add(s);
            return deliverScanners;
        }
    }
}
