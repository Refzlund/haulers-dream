using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Turns a planned REMOVE-FLOOR route into queued work: for each planned cell it ensures the
    /// <see cref="DesignationDefOf.RemoveFloor"/> designation is present, then builds a vanilla RemoveFloor job
    /// (<see cref="JobDefOf.RemoveFloor"/>) via the vanilla WorkGiver's cell path
    /// (<see cref="WorkGiver_ConstructRemoveFloor.JobOnCell"/>), and either REPLACES current work with the route
    /// (interrupt + clear queue, start now) or APPENDS it to the pawn's existing manual queue. Cell-based throughout —
    /// it deliberately never enters the Thing-based <see cref="RouteExecutor"/> or the sow-cell
    /// <see cref="SowRouteExecutor"/> pipeline.
    ///
    /// <para>Unlike sowing (which needs no designation — the zone + plant def drive it), the remove-floor WorkGiver's
    /// base gate REQUIRES the RemoveFloor designation on the cell. Since issue #110 the planner only selects cells that
    /// are ALREADY marked for removal, so the designation is normally present already; the <c>AddDesignation</c> below
    /// stays as a defensive safety net (it fires only if an undesignated cell somehow reaches here). Adding
    /// designations + queueing jobs are synced world mutations that only run inside the synced <see cref="Execute"/> path.</para>
    /// </summary>
    public static class RemoveFloorRouteExecutor
    {
        // The vanilla remove-floor WorkGiver scanner, resolved once — the same lookup SowScanner() uses. Its JobOnCell
        // just MakeJob(RemoveFloor, c); HasJobOnCell re-checks the designation + terrain + blocking-building gates.
        private static WorkGiver_ConstructRemoveFloor removeFloorScanner;
        private static bool removeFloorScannerResolved;

        public static WorkGiver_ConstructRemoveFloor RemoveFloorScanner()
        {
            if (removeFloorScannerResolved)
                return removeFloorScanner;
            removeFloorScannerResolved = true;
            var defs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                if (typeof(WorkGiver_ConstructRemoveFloor).IsAssignableFrom(defs[i].giverClass)
                    && defs[i].Worker is WorkGiver_ConstructRemoveFloor s)
                {
                    removeFloorScanner = s;
                    break;
                }
            }
            return removeFloorScanner;
        }

        /// <summary>
        /// Multiplayer entry point for the remove-floor dialog's Append/Replace buttons — the cell-based sibling of
        /// <see cref="SowRouteExecutor.ExecuteSowRouteSynced"/>. Like that method, the BODY references NO Multiplayer.API
        /// type and carries NO [SyncMethod] attribute (issue #6); it is registered BY NAME from the MP-gated
        /// <see cref="MultiplayerCompat"/> shim, so a non-MP game never resolves the unshipped API assembly.
        ///
        /// <para>Args are MP-serializable only: a <see cref="Pawn"/>, an <see cref="IntVec3"/> anchor cell,
        /// primitives/enums, and <c>List&lt;IntVec3&gt;</c>. Everything is re-derived per client from the synced
        /// anchor + recomputed deterministically (see <see cref="RemoveFloorRoutePlanner"/>), matching the
        /// recompute-on-all-clients lockstep model the Thing/sow routes already use (precomputed is null on this path).</para>
        /// </summary>
        public static void ExecuteRemoveFloorRouteSynced(Pawn pawn, IntVec3 anchor, RemoveFloorRouteMode mode, int amount,
            int radius, float maxDistance, bool replace, List<IntVec3> mustInclude,
            HaulersDream.Core.RouteSelectionMethod selectionMethod, int exactMax)
        {
            if (pawn?.Map == null || !anchor.IsValid || !anchor.InBounds(pawn.Map))
                return;
            Execute(pawn, anchor, mode, amount, radius, maxDistance, replace, mustInclude, selectionMethod, exactMax);
        }

        public static void Execute(Pawn pawn, IntVec3 anchor, RemoveFloorRouteMode mode, int amount, int radius,
            float maxDistance, bool replace, IReadOnlyList<IntVec3> mustInclude = null,
            HaulersDream.Core.RouteSelectionMethod selectionMethod = HaulersDream.Core.RouteSelectionMethod.MostStopsPerTravel,
            int exactMax = HaulersDream.Core.RouteOrderPolicy.ExactMax, RemoveFloorRoutePlan precomputed = null)
        {
            if (pawn?.Map == null)
                return;

            var scanner = RemoveFloorScanner();
            if (scanner == null)
                return;

            // Prefer the dialog's already-computed plan so the queued route matches the preview; fall back to a
            // fresh plan (the MP path always passes null → every client recomputes deterministically).
            var plan = (precomputed != null && precomputed.cells.Count > 0)
                ? precomputed
                : RemoveFloorRoutePlanner.Plan(pawn, anchor, mode, amount, radius, maxDistance, mustInclude,
                    selectionMethod, exactMax);
            if (plan.cells.Count == 0)
            {
                if (MultiplayerCompat.ShouldShowLocalFeedback)
                    Messages.Message("HaulersDream.PlanRoute.NoTargets".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            int selected = plan.cells.Count;
            var map = pawn.Map;

            // Build a forced RemoveFloor job per cell, in route order. The RemoveFloor WorkGiver's base gate needs the
            // designation PRESENT; since #110 the planner only selects already-marked cells, so the AddDesignation is a
            // safety net (normally a no-op). Then HasJobOnCell re-validates (terrain removable, no blocking building,
            // reservable). No try/catch: a vanilla WorkGiver throwing is a real bug to surface.
            var jobs = new List<Job>(plan.cells.Count);
            var jobCells = new List<IntVec3>(plan.cells.Count);
            for (int i = 0; i < plan.cells.Count; i++)
            {
                var c = plan.cells[i];
                if (map.designationManager.DesignationAt(c, DesignationDefOf.RemoveFloor) == null)
                    map.designationManager.AddDesignation(new Designation(c, DesignationDefOf.RemoveFloor));
                if (!scanner.HasJobOnCell(pawn, c, forced: true))
                    continue;
                var job = scanner.JobOnCell(pawn, c, forced: true);
                if (job == null)
                    continue;
                jobs.Add(job);
                jobCells.Add(c);
            }
            if (jobs.Count == 0)
            {
                if (MultiplayerCompat.ShouldShowLocalFeedback)
                    Messages.Message("HaulersDream.PlanRoute.NoTargets".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            int queued = replace
                ? QueueReplace(pawn, scanner, jobs, jobCells)
                : QueueAppend(pawn, scanner, jobs);

            HDLog.Dbg($"{pawn} planned a {mode} remove-floor route ({(replace ? "replace" : "append")}): {queued}/{selected} cell(s), " +
                      $"cappedAmount={plan.cappedByAmount}, cappedDist={plan.cappedByDistance}.");

            if (MultiplayerCompat.ShouldShowLocalFeedback)
            {
                if (queued == 0)
                    Messages.Message("HaulersDream.PlanRoute.NoTargets".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                else if (plan.cappedByDistance)
                    Messages.Message("HaulersDream.PlanRoute.DistanceLimited".Translate(queued), pawn, MessageTypeDefOf.CautionInput, historical: false);
                else if (plan.cappedByAmount)
                    Messages.Message("HaulersDream.PlanRoute.Capped".Translate(queued), pawn, MessageTypeDefOf.CautionInput, historical: false);
                else if (queued < selected)
                    Messages.Message("HaulersDream.PlanRoute.Partial".Translate(queued, selected - queued), pawn, MessageTypeDefOf.CautionInput, historical: false);
                else
                    Messages.Message("HaulersDream.PlanRoute.Planned".Translate(queued), pawn, MessageTypeDefOf.SilentInput, historical: false);
            }
        }

        // REPLACE: interrupt current work + clear any existing queue (the lead does this synchronously), then append
        // the rest of the route. Mirrors SowRouteExecutor.QueueReplace, cell-targeted (the lead job's cell anchor).
        private static int QueueReplace(Pawn pawn, WorkGiver_ConstructRemoveFloor scanner, List<Job> jobs, List<IntVec3> cells)
        {
            pawn.jobs.ClearQueuedJobs();
            int queued = 0;
            bool leadStarted = false;
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                job.workGiverDef = scanner.def;
                if (!leadStarted)
                {
                    if (pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, scanner, cells[i]))
                    {
                        leadStarted = true;
                        queued++;
                    }
                    continue;
                }
                job.playerForced = true;
                if (job.TryMakePreToilReservations(pawn, errorOnFailed: false))
                {
                    pawn.jobs.jobQueue.EnqueueLast(job, scanner.def.tagToGive);
                    queued++;
                }
            }
            return queued;
        }

        // APPEND: never clear the queue (the pawn's existing manual route survives). Reserve + EnqueueLast every
        // cell, then nudge the pawn only if idle. Mirrors SowRouteExecutor.QueueAppend.
        private static int QueueAppend(Pawn pawn, WorkGiver_ConstructRemoveFloor scanner, List<Job> jobs)
        {
            int queued = 0;
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                job.workGiverDef = scanner.def;
                job.playerForced = true;
                if (job.TryMakePreToilReservations(pawn, errorOnFailed: false))
                {
                    pawn.jobs.jobQueue.EnqueueLast(job, scanner.def.tagToGive);
                    queued++;
                }
            }
            if (queued > 0)
            {
                var cur = pawn.jobs.curJob;
                if (cur == null)
                    pawn.jobs.CheckForJobOverride(0f, ignoreQueue: false);
                else if (cur.def.isIdle)
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            return queued;
        }
    }
}
