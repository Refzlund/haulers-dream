using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    public partial class HaulersDreamGameComponent
    {
        private static void RepairOrphanedRecipeJobsAfterLoad()
        {
            var maps = Find.Maps;
            if (maps == null)
                return;
            int ended = 0, removed = 0;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m]?.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                    continue;
                for (int p = 0; p < pawns.Count; p++)
                {
                    var pawn = pawns[p];
                    var jobs = pawn?.jobs;
                    if (jobs == null)
                        continue;

                    // Capture identities before any cleanup: driver finish actions may enqueue other work.
                    var current = jobs.curJob;
                    bool endCurrent = IsOrphanedRecipeJob(current)
                        && jobs.curDriver?.GetType() == current.def.driverClass;
                    List<Job> orphanedQueue = null;
                    if (jobs.jobQueue != null)
                        for (int q = 0; q < jobs.jobQueue.Count; q++)
                        {
                            var queued = jobs.jobQueue[q]?.job;
                            if (IsOrphanedRecipeJob(queued))
                                (orphanedQueue ??= new List<Job>()).Add(queued);
                        }

                    // Remove these first, before a current-job finish action could start or pool/reuse one.
                    // Native queue cleanup releases only the removed jobs' reservations.
                    if (orphanedQueue != null)
                        jobs.jobQueue.RemoveAll(pawn, job =>
                        {
                            if (!orphanedQueue.Contains(job))
                                return false;
                            removed++;
                            return true;
                        });
                    if (endCurrent && jobs.curJob == current)
                    {
                        // Native cleanup runs finish actions, releases reservations and handles carried items.
                        // Do not choose new work while the rest of the loaded game is still finalizing.
                        jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: false);
                        ended++;
                    }
                }
            }
            if (ended > 0 || removed > 0)
                HDLog.Msg($"Save repair: ended {ended} current and removed {removed} queued recipe job(s) "
                    + "whose unfinished-item bill had no bill stack, using normal job cleanup.");
        }

        private static bool IsOrphanedRecipeJob(Job job)
        {
            if (job?.def == null || !(job.bill is Bill_ProductionWithUft bill) || bill.billStack != null)
                return false;
            var def = job.def;
            // Exact known def/driver pairs only: do not invoke arbitrary foreign drivers' cleanup or touch
            // medical/other bills. The retired InventoryDoBill already ends itself before reading its bill.
            return (def == JobDefOf.DoBill && def.driverClass == typeof(JobDriver_DoBill))
                || (def == HaulersDreamDefOf.HaulersDream_BillPrepGather
                    && def.driverClass == typeof(JobDriver_BillPrepGather))
                || (def == HaulersDreamDefOf.HaulersDream_GatherBillIngredients
                    && def.driverClass == typeof(JobDriver_GatherBillIngredients))
                || (def == HaulersDreamDefOf.HaulersDream_BatchCraft
                    && def.driverClass == typeof(JobDriver_BatchCraft));
        }
    }
}
