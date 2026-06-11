using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Meet-in-the-middle: when a worker is dispatched to fetch from a carrier, nudge an IDLE carrier
    /// to walk toward the worker so they converge (the worker's haul job already follows the carrier).
    /// Never interrupts a carrier doing real work.
    /// </summary>
    public static class SharedInventoryApproach
    {
        public static void MaybeApproach(Thing carriedStack, Pawn worker)
        {
            // JobOnThing / ResourceDeliverJobFor are also run speculatively to build right-click float menus;
            // don't dispatch a real Goto from a menu preview — only from an actual work assignment.
            if (RimWorld.FloatMenuMakerMap.makingFor != null)
                return;
            var carrier = (carriedStack?.ParentHolder as Pawn_InventoryTracker)?.pawn;
            if (carrier == null || worker == null || carrier == worker || carrier.Drafted)
                return;
            if (!IsIdle(carrier))
                return; // don't pull a busy carrier off real work (the "actively-using" respect)
            if (!carrier.Spawned || !carrier.CanReach(worker, PathEndMode.Touch, Danger.Some))
                return;

            var job = JobMaker.MakeJob(JobDefOf.Goto, worker);
            job.locomotionUrgency = LocomotionUrgency.Walk;
            job.collideWithPawns = false;
            carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static bool IsIdle(Pawn p)
        {
            if (p.jobs == null)
                return false;
            // QUEUED jobs are real work too (a planned route!) — TryTakeOrderedJob would WIPE the queue,
            // destroying e.g. a 12-stop prioritized harvest route just to walk toward a fetcher.
            if (p.jobs.jobQueue != null && p.jobs.jobQueue.Count > 0)
                return false;
            if (p.CurJob == null)
                return true;
            var def = p.CurJobDef;
            return def == JobDefOf.Wait
                || def == JobDefOf.Wait_Wander
                || def == JobDefOf.GotoWander
                || def == JobDefOf.Wait_MaintainPosture;
        }
    }
}
