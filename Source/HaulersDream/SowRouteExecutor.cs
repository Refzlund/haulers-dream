using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Turns a planned SOW route into queued work: builds a vanilla <c>Sow</c> job (<see cref="JobDefOf.Sow"/>) per
    /// planned cell via the vanilla sow WorkGiver's cell path (<see cref="WorkGiver_GrowerSow.JobOnCell"/>), then
    /// either REPLACES current work with the route (interrupt + clear queue, start now) or APPENDS it to the pawn's
    /// existing manual queue. Cell-based throughout — it deliberately never enters the Thing-based
    /// <see cref="RouteExecutor"/> pipeline (no designation: sowing needs none; the zone + plant def drive it).
    /// </summary>
    public static class SowRouteExecutor
    {
        // The vanilla sow WorkGiver scanner, resolved once. Its JobOnCell self-computes the wanted plant def for the
        // cell (CalculateWantedPlantDef) when its scan-static is null, so we can call it ad hoc here.
        private static WorkGiver_GrowerSow sowScanner;
        private static bool sowScannerResolved;

        public static WorkGiver_GrowerSow SowScanner()
        {
            if (sowScannerResolved)
                return sowScanner;
            sowScannerResolved = true;
            var defs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                if (typeof(WorkGiver_GrowerSow).IsAssignableFrom(defs[i].giverClass)
                    && defs[i].Worker is WorkGiver_GrowerSow s)
                {
                    sowScanner = s;
                    break;
                }
            }
            return sowScanner;
        }

        /// <summary>
        /// Multiplayer entry point for the sow dialog's Append/Replace buttons — the cell-based sibling of
        /// <see cref="RouteExecutor.ExecuteRouteSynced"/>. Like that method, the BODY references NO Multiplayer.API
        /// type and carries NO [SyncMethod] attribute (issue #6); it is registered BY NAME from the MP-gated
        /// <see cref="MultiplayerCompat"/> shim, so a non-MP game never resolves the unshipped API assembly.
        ///
        /// <para>Args are MP-serializable only: a <see cref="Pawn"/>, an <see cref="IntVec3"/> anchor cell,
        /// primitives/enums, and <c>List&lt;IntVec3&gt;</c>. The clicked ZONE is NOT shipped (zones aren't a clean
        /// wire type); it is re-derived per client from the synced anchor cell (<c>ZoneAt(anchor)</c>), and the plan
        /// is RECOMPUTED on every client from the same synced state (deterministic — see <see cref="SowRoutePlanner"/>),
        /// matching the recompute-on-all-clients lockstep model the Thing route already uses.</para>
        /// </summary>
        public static void ExecuteSowRouteSynced(Pawn pawn, IntVec3 anchor, SowRouteMode mode, int amount, int radius,
            float maxDistance, bool smart, bool replace, List<IntVec3> mustInclude,
            HaulersDream.Core.RouteSelectionMethod selectionMethod, int exactMax)
        {
            if (pawn?.Map == null || !anchor.IsValid || !anchor.InBounds(pawn.Map))
                return;
            // Re-derive the clicked zone from the synced anchor cell (deterministic). A null/changed zone (the player
            // deleted it, or it's no longer a growing zone) means no-op rather than queue a mismatched route.
            var zone = pawn.Map.zoneManager.ZoneAt(anchor) as Zone_Growing;
            if (zone == null)
                return;
            Execute(pawn, anchor, zone, mode, amount, radius, maxDistance, smart, replace, mustInclude,
                selectionMethod, exactMax);
        }

        public static void Execute(Pawn pawn, IntVec3 anchor, Zone_Growing zone, SowRouteMode mode, int amount,
            int radius, float maxDistance, bool smart, bool replace, IReadOnlyList<IntVec3> mustInclude = null,
            HaulersDream.Core.RouteSelectionMethod selectionMethod = HaulersDream.Core.RouteSelectionMethod.MostStopsPerTravel,
            int exactMax = HaulersDream.Core.RouteOrderPolicy.ExactMax, SowRoutePlan precomputed = null)
        {
            if (pawn?.Map == null || zone == null)
                return;

            var scanner = SowScanner();
            if (scanner == null)
                return;

            // Prefer the dialog's already-computed plan so the queued route matches the preview; fall back to a
            // fresh plan (the MP path always passes null → every client recomputes deterministically).
            var plan = (precomputed != null && precomputed.cells.Count > 0)
                ? precomputed
                : SowRoutePlanner.Plan(pawn, anchor, zone, mode, amount, radius, maxDistance, smart, mustInclude,
                    selectionMethod, exactMax);
            if (plan.cells.Count == 0)
            {
                if (MultiplayerCompat.ShouldShowLocalFeedback)
                    Messages.Message("HaulersDream.PlanRoute.NoTargets".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            int selected = plan.cells.Count;

            // Build a forced Sow job per cell, in route order; drop any cell that can't be sown right now (the
            // authoritative recheck — covers a cell sown/blocked since the plan was previewed). No designation.
            var jobs = new List<Job>(plan.cells.Count);
            var jobCells = new List<IntVec3>(plan.cells.Count);
            for (int i = 0; i < plan.cells.Count; i++)
            {
                var c = plan.cells[i];
                // WorkGiver_Grower.wantedPlantDef is a SCAN-STATE static that vanilla's grower scan sets per
                // building/zone and resets to null between them. We're calling JobOnCell ad hoc (no scan), so a
                // STALE non-null value from the game's last grower scan would make JobOnCell sow the WRONG plant
                // (its self-recompute on line `if (wantedPlantDef == null)` only fires when null). Reset it to null
                // before each cell so JobOnCell recomputes CalculateWantedPlantDef(c) for THIS cell's zone/pot.
                WorkGiver_Grower.wantedPlantDef = null;
                // No try/catch: a vanilla WorkGiver throwing is a real bug to surface, not silently skip.
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

            HDLog.Dbg($"{pawn} planned a {mode} sow route ({(replace ? "replace" : "append")}): {queued}/{selected} cell(s), " +
                      $"smart={smart}, plant={plan.plantDef?.defName}, cappedAmount={plan.cappedByAmount}, cappedDist={plan.cappedByDistance}.");

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
        // the rest of the route. Mirrors RouteExecutor.QueueReplace, but cell-targeted (the lead job's cell anchor).
        private static int QueueReplace(Pawn pawn, WorkGiver_GrowerSow scanner, List<Job> jobs, List<IntVec3> cells)
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
        // cell, then nudge the pawn only if idle. Mirrors RouteExecutor.QueueAppend.
        private static int QueueAppend(Pawn pawn, WorkGiver_GrowerSow scanner, List<Job> jobs)
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
