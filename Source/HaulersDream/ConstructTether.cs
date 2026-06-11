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
                bool canConstruct;
                try { canConstruct = GenConstruct.CanConstruct(frame, pawn, checkSkills: true, forced: true); }
                catch { canConstruct = false; }
                if (!canConstruct)
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
            var priorIntent = InventoryConstructDelivery.RouteIntent;
            try
            {
                InventoryConstructDelivery.RouteIntent = ConstructRouteIntent.HaulBuild;
                foreach (var scanner in DeliverScanners())
                {
                    Job next;
                    try { next = scanner.HasJobOnThing(pawn, site, forced: true) ? scanner.JobOnThing(pawn, site, forced: true) : null; }
                    catch { next = null; }
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
            }
            return false;
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
