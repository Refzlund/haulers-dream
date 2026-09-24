using System;
using System.Linq;
using HaulersDream.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    [DefOf]
    public static class ExplicitHaulDefOf
    {
        public static JobDef HaulersDream_ExplicitHaul;
        static ExplicitHaulDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(ExplicitHaulDefOf));
    }

    internal static class ExplicitHaulCommand
    {
        internal static bool IsJob(Job job) => job?.def != null && job.def == ExplicitHaulDefOf.HaulersDream_ExplicitHaul;
        internal static bool Identified(Pawn pawn, Job job) => IsJob(job) && job.playerForced
            && pawn?.GetComp<CompHauledToInventory>()?.ExplicitOrder(job) is ExplicitHaulOrder o
                && !o.Terminal && o.mapId == job.globalTarget.Map?.uniqueID && job.targetB.Cell == o.DeliveryCell
                && (!o.IsShelf || ReferenceEquals(job.targetC.Thing, o.shelf))
            && (pawn.CurJob?.loadID != job.loadID || ReferenceEquals(pawn.CurJob, job))
            && !pawn.jobs.jobQueue.Any(q => q?.job != null && q.job.loadID == job.loadID && !ReferenceEquals(q.job, job))
            && pawn.jobs.jobQueue.Count(q => ReferenceEquals(q?.job, job)) <= (pawn.CurJob == job ? 0 : 1);

        // Separate from Nearby.CanOffer: explicit points need neither better storage nor nearby settings.
        internal static string ActorReason(Pawn p)
        {
            if (p == null || p.Dead || p.Downed || !p.Spawned || p.Map == null || p.InMentalState
                || p.CarriedBy != null || p.Faction != Faction.OfPlayerSilentFail || p.Faction == null
                || p.IsQuestLodger() || p.jobs == null || p.carryTracker == null || p.inventory == null
                || p.GetComp<CompHauledToInventory>() == null) return "Pawn unavailable";
            var role = MiscRobotsStorageRole.Query(p).Role;
            bool robot = role != MiscRobotStorageRole.Unrelated;
            if ((!p.CanTakeOrder && (!robot || p.HostFaction != null)) || p.Drafted
                || !MapGate.HdActiveOnMap(p.Map) || !MultiplayerCompat.ExplicitHaulAvailable) return "Order unavailable";
            if (role == MiscRobotStorageRole.Unsupported || role == MiscRobotStorageRole.Unassigned
                || HaulOrderGate.BlockFor(p) != HaulOrderBlock.None) return "Pawn cannot haul";
            if (p.RaceProps.IsMechanoid && (HaulersDreamMod.Settings?.allowMechanoids != true
                || WorkCapabilityProbe.IsDisabled(p, WorkTypeDefOf.Hauling)
                || (!robot && p.RaceProps.mechEnabledWorkTypes?.Contains(WorkTypeDefOf.Hauling) != true))) return "Mech cannot haul";
            return null;
        }
        internal static bool CanIssue(Pawn pawn, Thing source, IntVec3 cell, int count, int mapId)
            => ActorReason(pawn) == null && pawn.Map.uniqueID == mapId && count > 0
                && NearbyHaulCommand.IsGroundTarget(pawn, source) && count <= source.stackCount
                && ExplicitHaulCell.Observe(pawn, source, cell, out int free, out _) && free > 0
                && source.Position != cell && pawn.CanReach(source, PathEndMode.ClosestTouch, Danger.Some)
                && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some)
                && !pawn.Map.reservationManager.ReservationsReadOnly.Any(r => r.Claimant != pawn
                    && (r.Target == source || r.Target == cell));

        internal static bool CanReserve(Pawn pawn, Job own, LocalTargetInfo target)
        {
            if (pawn?.Map == null || !target.IsValid) return false;
            var manager = pawn.Map.reservationManager;
            // Native CanReserve permits the same pawn's other jobs. Our exact-order acquisition does not.
            if (manager.ReservationsReadOnly.Any(r => r.Target == target && (r.Claimant != pawn || r.Job != own))) return false;
            return manager.CanReserve(pawn, target, 1, -1, null, false);
        }
        internal static bool Reserve(Pawn pawn, Job job, LocalTargetInfo target)
            => CanReserve(pawn, job, target) && pawn.Map.reservationManager.Reserve(pawn, job, target, 1, -1, null, false, false);

        private static bool Local(Pawn p, int mapId) => MultiplayerCompat.ExplicitHaulLocalUi
            && p?.Map != null && p.Map.uniqueID == mapId && p.Map == Find.CurrentMap && Find.Selector.SingleSelectedThing == p;
        internal static void Dispatch(Pawn pawn, Thing source, IntVec3 cell, int total, int mapId, bool queue)
        {
            if (Local(pawn, mapId) && CanIssue(pawn, source, cell, total, mapId))
                IssueSynced(pawn, source, cell, total, mapId, queue);
        }
        public static void IssueSynced(Pawn pawn, Thing source, IntVec3 cell, int total, int mapId, bool queue)
        {
            if (!MultiplayerCompat.ExplicitHaulExecuting || !CanIssue(pawn, source, cell, total, mapId)) return;
            var order = pawn.GetComp<CompHauledToInventory>().NewExplicitOrder(source, cell, total);
            if (order != null) Start(pawn, order, queue);
        }
        internal static bool CanIssueShelf(Pawn pawn, Thing source, Building_Storage shelf, int total, int mapId)
            => ActorReason(pawn) == null && pawn.Map.uniqueID == mapId && total > 0
                && NearbyHaulCommand.IsGroundTarget(pawn, source) && total <= source.stackCount
                && pawn.CanReach(source, PathEndMode.ClosestTouch, Danger.Some)
                && !pawn.Map.reservationManager.ReservationsReadOnly.Any(r => r.Claimant != pawn && r.Target == source)
                && StorageCommitments.ObserveExplicitShelf(pawn, source, shelf, out _, out _);
        internal static void DispatchShelf(Pawn pawn, Thing source, Building_Storage shelf, int total, int mapId, bool queue)
        { if (Local(pawn, mapId) && CanIssueShelf(pawn, source, shelf, total, mapId)) IssueShelfSynced(pawn, source, shelf, total, mapId, queue); }
        public static void IssueShelfSynced(Pawn pawn, Thing source, Building_Storage shelf, int total, int mapId, bool queue)
        {
            if (!MultiplayerCompat.ExplicitHaulExecuting || !CanIssueShelf(pawn, source, shelf, total, mapId)
                || !StorageCommitments.ObserveExplicitShelf(pawn, source, shelf, out var cell, out _)) return;
            var order = pawn.GetComp<CompHauledToInventory>().NewExplicitOrder(source, cell, total, shelf);
            if (order != null) Start(pawn, order, queue);
        }
        private static void Start(Pawn pawn, ExplicitHaulOrder order, bool queue)
        {
            var job = JobMaker.MakeJob(ExplicitHaulDefOf.HaulersDream_ExplicitHaul, order.source, order.DeliveryCell);
            if (order.IsShelf) job.targetC = order.shelf;
            job.playerForced = true; job.count = order.Remaining;
            job.globalTarget = new GlobalTargetInfo(order.destination, pawn.Map);
            order.jobId = job.loadID; order.state = ExplicitHaulState.Queued; order.reason = null;
            try
            {
                if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing: queue))
                { order.Block("Native job admission refused"); order.jobId = -1; }
            }
            catch { order.Block("Native job admission failed"); throw; }
        }
        internal static void DispatchResume(Pawn p, long id, int mapId, bool queue)
        { if (Local(p, mapId)) ResumeSynced(p, id, mapId, queue); }
        internal static void DispatchCancel(Pawn p, long id, int mapId)
        { if (Local(p, mapId)) CancelSynced(p, id, mapId); }
        public static void ResumeSynced(Pawn pawn, long id, int mapId, bool queue)
        {
            if (!MultiplayerCompat.ExplicitHaulExecuting || ActorReason(pawn) != null || pawn.Map.uniqueID != mapId) return;
            var o = pawn.GetComp<CompHauledToInventory>().ExplicitOrder(id);
            if (o == null || o.state != ExplicitHaulState.Blocked || o.mapId != mapId || o.Remaining <= 0) return;
            if (pawn.CurJob?.loadID == o.jobId || pawn.jobs.jobQueue.Any(q => q?.job?.loadID == o.jobId)) return;
            Start(pawn, o, queue);
        }
        public static void CancelSynced(Pawn pawn, long id, int mapId)
        {
            if (!MultiplayerCompat.ExplicitHaulExecuting || pawn?.Map?.uniqueID != mapId || pawn.Faction != Faction.OfPlayerSilentFail) return;
            var o = pawn.GetComp<CompHauledToInventory>()?.ExplicitOrder(id);
            if (o == null || o.state == ExplicitHaulState.Complete) return;
            o.state = ExplicitHaulState.Cancelled; o.reason = "Cancelled";
            StorageCommitments.ReleaseExplicitShelf(pawn, o);
            pawn.jobs.jobQueue.RemoveAll(pawn, j => IsJob(j) && j.loadID == o.jobId);
            if (IsJob(pawn.CurJob) && pawn.CurJob.loadID == o.jobId)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            // A previous failed pickup may have retained an exact parcel outside normal inventory.
            // Cancellation releases that physical parcel through native placement, never revives intent.
            ExplicitHaulLifecycle.DropRetained(pawn, o);
        }
    }
}
