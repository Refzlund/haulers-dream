using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    // One current trip owns one native cell, including that cell's slots and resident deficits.
    // The record lives in storageClaims; the native reservation is the physical exclusion authority.
    internal sealed class StorageExclusiveCellAllocation
    {
        internal Pawn pawn;
        internal Job job;
        internal ExplicitHaulOrder order;
        internal Building_Storage shelf;
        internal ISlotGroup group;
        internal IntVec3 cell;
        internal ThingDef def;
        internal int units;
        internal bool Live => pawn?.Spawned == true && pawn.CurJob == job
            && pawn.jobs.curDriver?.GetType() == typeof(JobDriver_ExplicitHaul)
            && !order.Terminal && order.shelfAllocation == this && ExplicitHaulCommand.Identified(pawn, job)
            && StorageCommitments.SameExplicitShelf(pawn, order)
            && pawn.Map.reservationManager.ReservedBy(cell, pawn, job);
    }

    internal static partial class StorageCommitments
    {
        internal static bool SameExplicitShelf(Pawn pawn, ExplicitHaulOrder order)
            => order.IsShelf && order.shelf is Building_Storage shelf && shelf.Spawned && !shelf.Destroyed
                && shelf.thingIDNumber == order.shelfId && shelf.Map == pawn.Map && shelf.Map.uniqueID == order.mapId
                && shelf.Position == order.shelfPosition && shelf.Rotation.AsInt == order.shelfRotation;

        // The shared observation adapter certifies the concrete provider and its live limits.
        // Selecting a linked member never grants its sibling cells.
        private static bool ShelfAccepts(Pawn pawn, Thing subject, Building_Storage shelf)
        {
            if (pawn?.Map == null || subject?.def == null || shelf == null
                || !shelf.Spawned || shelf.Map != pawn.Map || shelf.Faction != pawn.Faction
                || shelf.IsForbidden(pawn) || !shelf.HaulDestinationEnabled
                || subject.def.size.x != 1 || subject.def.size.z != 1
                || !shelf.Accepts(subject) || !shelf.GetParentStoreSettings().AllowedToAccept(subject)) return false;
            var filter = HaulersDreamMod.Settings?.storageBuildingFilter;
            if (StorageBuildingFilter.Enabled && filter != null
                && (filter.denied.Contains(shelf.def.defName)
                    || (shelf.def.modContentPack?.PackageId is string package && filter.denied.Contains(package)))) return false;
            return true;
        }

        private static bool IncomingAt(Pawn asker, IntVec3 cell)
        {
            // An existing native haul may have had its destination reservation stripped by HD.
            // Its actual current destination still has priority over this new allocation.
            foreach (var other in asker.Map.mapPawns.AllPawnsSpawned)
            {
                if (other == asker || other.CurJob == null) continue;
                var job = other.CurJob;
                if (!job.targetB.IsValid || job.targetB.HasThing || job.targetB.Cell != cell) continue;
                if (job.def == JobDefOf.HaulToCell || job.def == HaulersDreamDefOf.HaulersDream_UnloadInventory
                    || job.def == HaulersDreamDefOf.HaulersDream_UnloadTransporterInBulk
                    || ExplicitHaulCommand.IsJob(job)) return true;
            }
            return false;
        }

        private static bool ShelfCellSpace(Pawn pawn, Thing subject, Building_Storage shelf, IntVec3 cell,
            Job ownJob, out int capacity)
            => ShelfCellSpace(pawn, subject, shelf, cell, ownJob, out capacity, out _, out _);

        private static bool ShelfCellSpace(Pawn pawn, Thing subject, Building_Storage shelf, IntVec3 cell,
            Job ownJob, out int capacity, out int vacantSlots, out int compatibleDeficits)
        {
            capacity = 0; vacantSlots = 0; compatibleDeficits = 0;
            var map = pawn.Map;
            // Native shelves are PassThroughOnly furniture: valid haul destinations are walkable,
            // but Standable deliberately rejects their occupied cells.
            if (!ShelfAccepts(pawn, subject, shelf) || !cell.InBounds(map) || !cell.WalkableBy(map, pawn)
                || cell.IsForbidden(pawn) || (subject.Spawned && subject.Position == cell)
                || !ReferenceEquals(map.haulDestinationManager.SlotGroupAt(cell)?.parent, shelf)
                || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some) || IncomingAt(pawn, cell)) return false;
            if (map.reservationManager.ReservationsReadOnly.Any(r => r.Target == cell && (r.Claimant != pawn || r.Job != ownJob))) return false;
            var group = BulkHaul.BudgetGroupOf(shelf.GetSlotGroup());
            var demand = new StorageAllocationObservationDemand((object)ownJob ?? pawn, subject, subject,
                pawn, subject.stackCount, StorageFilterContext.Unload, cell, ownJob, startsRefill: false);
            var observation = StorageAllocationObservation.ObserveCells(map, group, new[] { demand }, new[] { cell });
            if (!observation.Complete || observation.Invalidated || observation.Requests.Count != 1
                || observation.Cells.Count != 1 || !observation.StillCurrent()) return false;
            var request = observation.Requests[0];
            var physical = observation.Cells[0];
            if (!request.EligibleCells.Contains(physical.Key)) return false;
            long space = 0;
            foreach (var stack in physical.Stacks)
                if (request.EligibleStacks.Contains(stack.Key)) space += stack.FreeUnits;
            compatibleDeficits = (int)Math.Min(int.MaxValue, space);
            vacantSlots = physical.VacantSlots;
            space += vacantSlots * (long)subject.def.stackLimit;
            capacity = (int)Math.Min(int.MaxValue, space);
            return capacity > 0;
        }

        private static int ExplicitShelfFreeUnits(Pawn pawn, Thing subject, ISlotGroup group, IntVec3 cell, Job ownJob)
        {
            // Price this exact destination alongside every existing commodity, using the same
            // fresh allocator as native/bulk admission. A linked sibling cannot fund this cell.
            var request = new StorageAllocationObservationDemand((object)ownJob ?? pawn, subject, subject,
                pawn, subject.stackCount, StorageFilterContext.Unload, cell, ownJob, startsRefill: false);
            return ResourceUnitsFor(pawn, group, request, null, new[] { cell }, out int allowed)
                == ResourceAllowance.Observed ? allowed : 0;
        }

        internal static bool ObserveExplicitShelf(Pawn pawn, Thing subject, Building_Storage shelf,
            out IntVec3 cell, out int capacity)
        {
            cell = IntVec3.Invalid; capacity = 0;
            if (!ActiveOn(pawn?.Map) || !ShelfAccepts(pawn, subject, shelf)) return false;
            var group = BulkHaul.BudgetGroupOf(shelf.GetSlotGroup());
            foreach (var candidate in shelf.AllSlotCells().OrderBy(c => c.DistanceToSquared(pawn.Position)).ThenBy(c => c.x).ThenBy(c => c.z))
                if (ShelfCellSpace(pawn, subject, shelf, candidate, null, out int physical))
                {
                    int shared = ExplicitShelfFreeUnits(pawn, subject, group, candidate, null);
                    if (shared <= 0) continue;
                    cell = candidate; capacity = Math.Min(shared, physical); return capacity > 0;
                }
            return false;
        }

        internal static bool AcquireExplicitShelf(Pawn pawn, Job job, ExplicitHaulOrder order, Thing subject, out int capacity)
        {
            capacity = 0;
            if (!ActiveOn(pawn?.Map) || pawn.CurJob != job || pawn.jobs.curDriver?.GetType() != typeof(JobDriver_ExplicitHaul)
                || !ExplicitHaulCommand.Identified(pawn, job) || !SameExplicitShelf(pawn, order)) return false;
            var shelf = (Building_Storage)order.shelf;
            var existing = order.shelfAllocation;
            if (existing != null && existing.Live && ReferenceEquals(existing.group, BulkHaul.BudgetGroupOf(shelf.GetSlotGroup()))
                && ShelfCellSpace(pawn, subject, shelf, existing.cell, job, out int current))
            { capacity = Math.Min(current, existing.units); return capacity > 0; }
            ReleaseExplicitShelf(pawn, order);
            // Release also handles an exact native reservation loaded without a runtime allocation.
            // No serialized capacity is trusted: price afresh, then rebuild the shared row.
            foreach (var candidate in shelf.AllSlotCells().OrderBy(c => c == order.tripDestination ? 0 : 1)
                .ThenBy(c => c.DistanceToSquared(pawn.Position)).ThenBy(c => c.x).ThenBy(c => c.z))
            {
                if (!ShelfCellSpace(pawn, subject, shelf, candidate, job, out int physical)) continue;
                var group = BulkHaul.BudgetGroupOf(shelf.GetSlotGroup());
                int shared = ExplicitShelfFreeUnits(pawn, subject, group, candidate, job);
                int wanted = order.piece != null ? order.tripUnits : Math.Min(order.Remaining, pawn.carryTracker.MaxStackSpaceEver(subject.def));
                int units = Math.Min(wanted, Math.Min(physical, shared));
                if (units <= 0 || !ExplicitHaulCommand.Reserve(pawn, job, candidate)) continue;
                var claim = new StorageExclusiveCellAllocation { pawn = pawn, job = job, order = order,
                    shelf = shelf, group = group, cell = candidate, def = subject.def, units = units };
                if (order.tripDestination != candidate) pawn.pather.StopDead();
                order.tripDestination = candidate; job.targetB = candidate; order.pathStarted = 0; order.shelfAllocation = claim;
                var rows = new List<StorageClaimRow>(HaulersDreamGameComponent.storageClaims)
                    { new StorageClaimRow(pawn, group, subject.def, units, claim) };
                HaulersDreamGameComponent.SetStorageClaims(rows.ToArray());
                capacity = units; return true;
            }
            return false;
        }

        internal static void ReleaseExplicitShelf(Pawn pawn, ExplicitHaulOrder order)
        {
            var claim = order?.shelfAllocation;
            if (claim == null)
            {
                var job = pawn?.CurJob;
                if (order?.IsShelf == true && job?.loadID == order.jobId && ExplicitHaulCommand.IsJob(job)
                    && job.targetB.Cell == order.DeliveryCell && pawn.Map?.reservationManager.ReservedBy(order.DeliveryCell, pawn, job) == true)
                {
                    pawn.Map.reservationManager.Release(order.DeliveryCell, pawn, job);
                    HaulersDreamGameComponent.InvalidateStorageClaimEvidence();
                }
                return;
            }
            order.shelfAllocation = null;
            if (pawn?.Map != null && pawn.Map.reservationManager.ReservedBy(claim.cell, pawn, claim.job))
                pawn.Map.reservationManager.Release(claim.cell, pawn, claim.job);
            HaulersDreamGameComponent.SetStorageClaims(HaulersDreamGameComponent.storageClaims
                .Where(r => !ReferenceEquals(r.ExclusiveCellAllocation, claim)).ToArray());
        }

        internal static bool ExplicitShelfCellHeldByOther(Pawn pawn, IntVec3 cell)
        {
            if (pawn?.Map == null || !cell.IsValid) return false;
            // Native reservations are also present immediately after loading, before the current
            // explicit driver has rebuilt its unscribed row. A preselected native job must honor them.
            return pawn.Map.reservationManager.ReservationsReadOnly.Any(r => r.Target == cell && r.Claimant != pawn
                && r.Claimant?.CurJob == r.Job && ExplicitHaulCommand.Identified(r.Claimant, r.Job)
                && r.Claimant.GetComp<CompHauledToInventory>().ExplicitOrder(r.Job)?.IsShelf == true);
        }

        // Only an identified current explicit order may read through its own native cell lease.
        // Native/ordinary jobs retain their separate owner route; no arbitrary reservation is waived.
        internal static bool OwnsExplicitShelfCell(Pawn pawn, Job job, Thing subject, ISlotGroupParent parent, IntVec3 cell)
        {
            if (!ExplicitHaulCommand.IsJob(job) || pawn?.CurJob != job) return false;
            var order = pawn?.GetComp<CompHauledToInventory>()?.ExplicitOrder(job);
            if (job == null || pawn.CurJob != job || !ExplicitHaulCommand.Identified(pawn, job)
                || order?.IsShelf != true || !SameExplicitShelf(pawn, order)
                || !ReferenceEquals(order.shelf, parent) || order.DeliveryCell != cell
                || !ExplicitShelfSubject(pawn, order, subject)
                || pawn.jobs.curDriver?.GetType() != typeof(JobDriver_ExplicitHaul)
                || !cell.WalkableBy(pawn.Map, pawn) || cell.IsForbidden(pawn)
                || order.shelf.IsForbidden(pawn) || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some)) return false;
            // Preserve native IsGoodStoreCell's source-to-destination reach check too.
            // Pawn-to-cell reach alone does not establish the route from a selected source.
            var spawnedParent = subject.SpawnedParentOrMe;
            var start = spawnedParent == null ? pawn.PositionHeld
                : spawnedParent == subject || !spawnedParent.def.hasInteractionCell
                    ? spawnedParent.Position : spawnedParent.InteractionCell;
            if (!pawn.Map.reachability.CanReach(start, cell, PathEndMode.ClosestTouch, TraverseParms.For(pawn))) return false;
            var reservations = pawn.Map.reservationManager;
            return reservations.ReservedBy(cell, pawn, job)
                && !reservations.ReservationsReadOnly.Any(r => r.Target == cell && (r.Claimant != pawn || r.Job != job));
        }

        private static bool ExplicitShelfSubject(Pawn pawn, ExplicitHaulOrder order, Thing subject)
        {
            if (subject == null || subject.Destroyed || subject.stackCount <= 0) return false;
            if (order.piece != null)
                return ReferenceEquals(subject, order.piece) && subject.stackCount == order.tripUnits
                    && pawn.carryTracker.CarriedThing == subject
                    && ExplicitHaulTransfer.Owns(pawn.carryTracker.innerContainer, subject);
            var remainder = order.remainders.Count > 0 ? order.remainders[0] : null;
            if (!ReferenceEquals(subject, remainder?.thing ?? order.source)
                || !subject.Spawned || subject.Map != pawn.Map
                || !NearbyHaulCommand.IsGroundTarget(pawn, subject)
                || (!order.sourceForbiddenAtIssue && subject.IsForbidden(pawn))) return false;
            return remainder == null ? subject.thingIDNumber == order.sourceId
                : subject.stackCount == remainder.observedCount && subject.Position == remainder.cell
                    && remainder.units > 0 && remainder.units <= subject.stackCount;
        }

        internal static int ExplicitShelfUnits(Pawn pawn, out Thing subject, out ISlotGroup group)
        {
            subject = null; group = null;
            var order = pawn?.GetComp<CompHauledToInventory>()?.ExplicitOrder(pawn.CurJob);
            var claim = order?.shelfAllocation;
            if (claim?.Live != true) return 0;
            subject = order.piece ?? (order.remainders.Count > 0 ? order.remainders[0].thing : order.source);
            if (subject == null || subject.Destroyed || subject.def != claim.def) return 0;
            group = claim.group;
            return Math.Min(claim.units, order.piece != null ? order.tripUnits : Math.Min(order.Remaining, subject.stackCount));
        }
    }
}
