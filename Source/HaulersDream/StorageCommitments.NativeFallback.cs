using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    internal sealed class NativeFallbackReservation
    {
        private readonly JobDriver_HaulToCell driver;
        private readonly Pawn pawn;
        private readonly Map map;
        private readonly Job job, previousJob;
        private readonly JobDriver previousDriver;
        private readonly int jobId, previousJobId;
        private readonly ISlotGroup group;
        private readonly LocalTargetInfo source, destination;
        private readonly bool hadSource, hadDestination;

        internal NativeFallbackReservation(JobDriver_HaulToCell driver, ISlotGroup group)
        {
            this.driver = driver; pawn = driver.pawn; map = pawn.Map; job = driver.job;
            jobId = job.loadID; this.group = group; source = job.targetA; destination = job.targetB;
            previousJob = pawn.CurJob; previousJobId = previousJob?.loadID ?? -1;
            previousDriver = pawn.jobs.curDriver;
            hadSource = map.reservationManager.ReservedBy(source, pawn, job);
            hadDestination = map.reservationManager.ReservedBy(destination, pawn, job);
        }

        private bool SameJob => ReferenceEquals(driver.pawn, pawn) && ReferenceEquals(driver.job, job)
            && job.loadID == jobId;
        private bool Current => SameJob && ReferenceEquals(pawn.Map, map)
            && job.targetA == source && job.targetB == destination
            && ReferenceEquals(pawn.CurJob, previousJob) && (previousJob?.loadID ?? -1) == previousJobId
            && ReferenceEquals(pawn.jobs.curDriver, previousDriver);

        internal bool Allowed() => Current
            && StorageCommitments.CanUseNativeExclusiveCell(pawn, group, destination.Cell, source.Thing, job)
            && Current;

        internal Exception Finish(ref bool result, Exception failure)
        {
            try { if (failure == null && result && Allowed()) return null; }
            catch (Exception error) { Preserve(ref failure, error); }
            result = false;
            ReleaseNew(source, hadSource, ref failure);
            ReleaseNew(destination, hadDestination, ref failure);
            return failure;
        }

        private void ReleaseNew(LocalTargetInfo target, bool existed, ref Exception failure)
        {
            try
            {
                if (!existed && SameJob && map.reservationManager.ReservedBy(target, pawn, job))
                    map.reservationManager.Release(target, pawn, job);
            }
            catch (Exception error) { Preserve(ref failure, error); }
        }

        private static void Preserve(ref Exception primary, Exception error)
        {
            if (primary == null) primary = error;
            else primary.Data["HaulersDream.NativeFallback.Cleanup"] = error.ToString();
        }
    }

    internal static partial class StorageCommitments
    {
        // An unsupported quantitative provider retains native whole-cell reservations.
        // Those reservations cannot see existing numerical admissions whose destination
        // reservations HD already removed. Keep that prior cargo ahead of a new lease,
        // regardless of its def. This observes ownership, not guessed provider capacity.
        internal static bool CanUseNativeExclusiveCell(Pawn pawn, ISlotGroup group, IntVec3 cell,
            Thing subject, Job job)
        {
            if (!AnyClaims || group == null) return true; // True containers have a separate native contract.
            if (!UnityData.IsInMainThread || pawn?.Map == null || !cell.IsValid
                || ResourceQueriesBlocked() || resourceTransferDepth > 0) return false;
            var map = pawn.Map;
            if (!ReferenceEquals(BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(cell)), group))
                return false;
            var snapshot = ObserveResourceResponsibilities(map);
            if (!snapshot.Current || UnresolvedResourceBlocks(snapshot, group, pawn, subject)) return false;
            foreach (var portion in snapshot.Portions)
            {
                if (!ReferenceEquals(portion.Row.Group, group)) continue;
                var entry = portion.Entry;
                // Delivery of this exact physical parcel may replace its own responsibility.
                // A different parcel of the same def, even on this pawn, is not that owner.
                if (ReferenceEquals(portion.Row.Pawn, pawn) && ReferenceEquals(entry.Subject, subject)
                    && (entry.Held || (job != null && ReferenceEquals(entry.OwnerJob, job)))) continue;
                // A fixed destination cannot overlap another cell's native lease. Flexible
                // admitted cargo may still need this cell; without quantitative evidence it
                // must finish/retire before a competing whole-cell lease can be granted.
                if (!entry.Destination.HasValue || entry.Destination.Value == cell) return false;
            }
            return snapshot.Current && !ResourceQueriesBlocked() && ReferenceEquals(pawn.Map, map)
                && ReferenceEquals(BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(cell)), group);
        }
    }
}
