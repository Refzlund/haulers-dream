using System;
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

        // First supported destination is the actual native Building_Storage. Subclass/provider contracts
        // remain explicit follow-up work; accepting a linked group never grants its sibling cells.
        private static bool ShelfAccepts(Pawn pawn, Thing subject, Building_Storage shelf)
        {
            if (pawn?.Map == null || subject?.def == null || shelf?.GetType() != typeof(Building_Storage)
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
            bool own = ownJob != null && map.reservationManager.ReservedBy(cell, pawn, ownJob);
            if (map.reservationManager.ReservationsReadOnly.Any(r => r.Target == cell && (r.Claimant != pawn || r.Job != ownJob))) return false;
            // IsGoodStoreCell's CanReserveNew rejects even the current owner's reservation. In that
            // branch only, use its actual physical predicate with no carrier/faction and retain the
            // explicit ownership, forbidden and reach checks above. No foreign reservation is ignored.
            using (SuppressOwnGateForProjection())
                if (!StoreUtility.IsGoodStoreCell(cell, map, subject, own ? null : pawn, own ? null : pawn.Faction)) return false;
            int maximum = cell.GetMaxItemsAllowedInCell(map), items = 0;
            if (maximum <= 0 || maximum != shelf.def.building.maxItemsInCell) return false;
            long space = 0;
            foreach (var resident in cell.GetThingList(map))
            {
                if (resident.def.category != ThingCategory.Item) continue;
                if (resident.stackCount <= 0 || resident.def.size.x != 1 || resident.def.size.z != 1) return false;
                items++;
                if (resident.CanStackWith(subject)) space += Math.Max(0L, (long)resident.def.stackLimit - resident.stackCount);
            }
            compatibleDeficits = (int)Math.Min(int.MaxValue, space);
            vacantSlots = Math.Max(0, maximum - items);
            space += vacantSlots * (long)subject.def.stackLimit;
            capacity = (int)Math.Min(int.MaxValue, space);
            return capacity > 0;
        }

        private static int ExplicitShelfFreeUnits(Pawn pawn, Thing subject, Building_Storage shelf, ISlotGroup group)
        {
            int shared = FreeUnitsFor(pawn, group, subject.def, subject, out bool truncated);
            if (!truncated || shared > 0) return shared;
            // The selected shelf can lie after 200 blocked cells in a linked group's member order.
            // Measure only its eligible cells as a second lower bound, never add it to the overlapping
            // truncated pool. Native shelves have one or two cells; keep even this fallback bounded.
            int slots = 0; long deficits = 0;
            foreach (var cell in shelf.AllSlotCells().Take(MaxSpaceScanCells))
                if (ShelfCellSpace(pawn, subject, shelf, cell, null, out _, out int empty, out int partial))
                { slots += empty; deficits += partial; }
            return SelectedShelfCapacity.Available((int)Math.Min(int.MaxValue, deficits), slots, subject.def.stackLimit,
                HaulersDreamGameComponent.storageClaims, group, subject.def, pawn, IsDelivering(pawn, subject),
                Evidence, def => def is ThingDef thingDef ? thingDef.stackLimit : 0);
        }

        internal static bool ObserveExplicitShelf(Pawn pawn, Thing subject, Building_Storage shelf,
            out IntVec3 cell, out int capacity)
        {
            cell = IntVec3.Invalid; capacity = 0;
            if (!ActiveOn(pawn?.Map) || !ShelfAccepts(pawn, subject, shelf)) return false;
            var group = BulkHaul.BudgetGroupOf(shelf.GetSlotGroup());
            int shared = ExplicitShelfFreeUnits(pawn, subject, shelf, group);
            if (shared <= 0 || shared == int.MaxValue) return false;
            foreach (var candidate in shelf.AllSlotCells().OrderBy(c => c.DistanceToSquared(pawn.Position)).ThenBy(c => c.x).ThenBy(c => c.z))
                if (ShelfCellSpace(pawn, subject, shelf, candidate, null, out int physical))
                { cell = candidate; capacity = Math.Min(shared, physical); return capacity > 0; }
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
                int shared = ExplicitShelfFreeUnits(pawn, subject, shelf, group);
                if (shared == int.MaxValue) continue;
                int wanted = order.piece != null ? order.tripUnits : Math.Min(order.Remaining, pawn.carryTracker.MaxStackSpaceEver(subject.def));
                int units = Math.Min(wanted, Math.Min(physical, shared));
                if (units <= 0 || !ExplicitHaulCommand.Reserve(pawn, job, candidate)) continue;
                var claim = new StorageExclusiveCellAllocation { pawn = pawn, job = job, order = order,
                    shelf = shelf, group = group, cell = candidate, def = subject.def, units = units };
                if (order.tripDestination != candidate) pawn.pather.StopDead();
                order.tripDestination = candidate; job.targetB = candidate; order.pathStarted = 0; order.shelfAllocation = claim;
                HaulersDreamGameComponent.SetStorageClaims(StorageClaimLedger.Add(HaulersDreamGameComponent.storageClaims,
                    pawn, group, subject.def, units, claim));
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
