using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    internal static partial class StorageCommitments
    {
        internal enum ResourceAllowance { Unsupported, Observed, Deferred }

        [ThreadStatic] private static int resourceTransferDepth;
        // A split/merge callback can query storage after the source changed but before its actual
        // recipient is known. No positive allocation is justified in that synchronous interval.
        internal static IDisposable BeginResourceTransfer()
        {
            resourceTransferDepth++;
            return new ResourceTransferScope();
        }

        private sealed class ResourceTransferScope : IDisposable
        {
            private bool disposed;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                resourceTransferDepth--;
            }
        }

        private static IEnumerable<StorageClaimRow> ResourceClaimOrder(StorageClaimRow[] rows)
        {
            // Attribute exact active work before unconverted retained-inventory rows. Otherwise an
            // older def-wide row can spend its quantity on the new source instead of its old cargo.
            foreach (var row in rows) if (row.WorkOwner != null) yield return row;
            foreach (var row in rows) if (row.WorkOwner == null) yield return row;
        }

        private static IEnumerable<StorageParcelEvidence.Entry> ResourceParcelOrder(
            List<StorageParcelEvidence.Entry> entries, bool native)
        {
            // A duplicate source may already be reserved without being admitted. Actual native
            // hands consume this activation's amount before any pending source, regardless of ID.
            if (native) foreach (var entry in entries) if (entry.Held) yield return entry;
            foreach (var entry in entries) if (!native || !entry.Held) yield return entry;
        }

        private static bool SameResourceParcel(ResourceResponsibility portion, Pawn pawn, Thing subject)
            => ReferenceEquals(portion.Row.Pawn, pawn) && ReferenceEquals(portion.Entry.Subject, subject);

        private static bool UnresolvedResourceBlocks(ResourceResponsibilitySnapshot snapshot,
            ISlotGroup group, Pawn pawn, Thing subject)
        {
            foreach (var portion in snapshot.Portions)
                if (ReferenceEquals(portion.Row.Group, UnresolvedHeldDestination)
                    && portion.Row.WorkOwner is HeldCargoOwner unresolved
                    && ReferenceEquals(unresolved.CandidateGroup, group)
                    && !(portion.Entry.Held && SameResourceParcel(portion, pawn, subject))) return true;
            return false;
        }

        // Shared by immediate admission and the synchronous native search view. Exact bulk/
        // retained owners, full physical quantities and destination restrictions stay identical.
        private static StorageAllocationObservationDemand PriorResourceDemand(ResourceResponsibility portion)
        {
            object exact = portion.Row.WorkOwner is BulkParcelOwner || portion.Row.WorkOwner is HeldCargoOwner
                ? portion.Row.WorkOwner : null;
            var entry = portion.Entry;
            return new StorageAllocationObservationDemand(exact ?? (object)entry.OwnerJob ?? portion.Row.Pawn,
                exact ?? entry.Subject, entry.Subject, (Pawn)portion.Row.Pawn, portion.Units,
                StorageFilterContext.Unload, entry.Destination, entry.OwnerJob, startsRefill: false);
        }

        // Reconstruct physical allocations from the one authoritative intent ledger and fresh
        // exact parcels. The scratch allocation never survives the observation that justifies it.
        // Existing quantities are protected; exact destinations stay in their eligibility.
        // Fresh tentative placements may be rematched only if all those quantities survive.
        // This permits a real deposit to replace a vacancy with its actual compatible stack without
        // retaining a second stale snapshot of the world or publishing during a work-giver probe.
        internal static ResourceAllowance ResourceUnitsFor(Pawn pawn, ISlotGroup group, Thing subject,
            int desired, IntVec3? destination, Job ownJob, out int allowed)
        {
            allowed = 0;
            // A deferred reader result is too late if policy/custody was already read here.
            if (!UnityData.IsInMainThread) return ResourceAllowance.Deferred;
            if (ResourceQueriesBlocked()) return ResourceAllowance.Deferred;
            if (!ActiveOn(pawn?.Map)) return ResourceAllowance.Unsupported;
            object owner = (object)ownJob ?? pawn;
            var request = new StorageAllocationObservationDemand(owner, subject, subject, pawn, desired,
                StorageFilterContext.Unload, destination, ownJob, startsRefill: !IsDelivering(pawn, subject));
            return ResourceUnitsFor(pawn, group, request, null, null, out allowed);
        }

        // Explicit bulk commands have their own established master/map setting boundary. This
        // purely observational overload is also used by those callers after their command gate.
        internal static ResourceAllowance ResourceUnitsFor(Pawn pawn, ISlotGroup group,
            StorageAllocationObservationDemand requested, IReadOnlyList<StorageAllocationObservationDemand> planned,
            IReadOnlyList<IntVec3> preferredCells, out int allowed)
            => ResourceUnitsForLoad(pawn, group, requested, planned, preferredCells, null, out allowed);

        internal static ResourceAllowance ResourceUnitsForLoad(Pawn pawn, ISlotGroup group,
            StorageAllocationObservationDemand requested, IReadOnlyList<StorageAllocationObservationDemand> planned,
            IReadOnlyList<IntVec3> preferredCells, LoadRecoveryTicket ticket, out int allowed)
        {
            allowed = 0;
            if (!UnityData.IsInMainThread) return ResourceAllowance.Deferred;
            if (ResourceQueriesBlocked(ticket)) return ResourceAllowance.Deferred;
            var map = pawn?.Map;
            var subject = requested?.Subject;
            if (seamDisabled) return ResourceAllowance.Unsupported;
            if (map == null || group == null || subject?.def?.category != ThingCategory.Item
                || !ReferenceEquals(requested.Pawn, pawn))
                return ResourceAllowance.Unsupported;
            if (resourceTransferDepth > 0) return ResourceAllowance.Deferred;
            if (requested.Units <= 0) return ResourceAllowance.Observed;

            var demands = new List<StorageAllocationObservationDemand>();
            var preferred = new List<IntVec3>();
            if (preferredCells != null) preferred.AddRange(preferredCells);
            if (requested.Destination.HasValue && !preferred.Contains(requested.Destination.Value))
                preferred.Add(requested.Destination.Value);
            var replacements = new List<StorageAllocationObservationDemand> { requested };
            if (planned != null)
                foreach (var request in planned)
                    if (request != null && ReferenceEquals(request.Pawn, pawn)) replacements.Add(request);
            var responsibility = ObserveResourceResponsibilitiesForLoad(map, ticket);
            // A marker is not proof that native exclusivity can coexist with numeric owners.
            // Its exact physical owner retains the immediate, freshly observed delivery path.
            if (UnresolvedResourceBlocks(responsibility, group, pawn, subject)) return ResourceAllowance.Deferred;
            if (!responsibility.Current) return ResourceAllowance.Deferred;
            if ((planned == null || planned.Count == 0)
                && TryHeldDeliveryAllowance(pawn, group, requested, responsibility, preferred, ticket,
                    out ResourceAllowance deliveryStatus, out allowed)) return deliveryStatus;
            foreach (var portion in responsibility.Portions)
            {
                var row = portion.Row;
                if (!ReferenceEquals(row.Group, group)) continue;
                var carrier = (Pawn)row.Pawn;
                var entry = portion.Entry;
                bool replacing = false;
                if (ReferenceEquals(carrier, pawn))
                    foreach (var request in replacements)
                        if (request.Parcel is BulkParcelOwner || request.Parcel is HeldCargoOwner
                            ? ReferenceEquals(row.WorkOwner, request.Parcel)
                            : ReferenceEquals(entry.Subject, request.Subject)) { replacing = true; break; }
                if (replacing) continue;
                demands.Add(PriorResourceDemand(portion));
                if (entry.Destination.HasValue && !preferred.Contains(entry.Destination.Value))
                    preferred.Add(entry.Destination.Value);
            }
            if (!responsibility.Current) return ResourceAllowance.Deferred;
            int existingCount = demands.Count;
            if (planned != null) demands.AddRange(planned);
            int requestedIndex = demands.Count;
            demands.Add(requested);
            var observation = StorageAllocationObservation.ObserveForLoad(map, group, demands, null, preferred, ticket);
            if (observation.Invalidated || !responsibility.Current) return ResourceAllowance.Deferred;
            // Never reinterpret an unreviewed provider as native spare capacity. Native callers keep
            // their whole-cell reservation when this seam cannot take responsibility.
            foreach (var issue in observation.Issues)
                if (issue.Status == StorageAllocationObservationStatus.Unsupported)
                    return ResourceAllowance.Unsupported;
            if (observation.Requests.Count != demands.Count) return ResourceAllowance.Deferred;
            var options = new StorageAllocationOptions(observationComplete: observation.Complete);
            var existing = new List<StorageAllocationRequest>();
            for (int i = 0; i < existingCount; i++) existing.Add(observation.Requests[i]);
            var prior = StorageResourceAllocator.Allocate(observation.Cells, StorageAllocationState.Empty,
                existing, CompatibleResource, observation.ObservedEligible, options);
            if (!prior.CanPublish) return ResourceAllowance.Deferred;
            foreach (var demand in existing)
                if (prior.AdmittedUnits(demand) != demand.Units) return ResourceAllowance.Deferred;
            var priorState = prior.State;
            if (requestedIndex > existingCount)
            {
                var overlay = new List<StorageAllocationRequest>();
                for (int i = existingCount; i < requestedIndex; i++) overlay.Add(observation.Requests[i]);
                var proposed = StorageResourceAllocator.Allocate(observation.Cells, priorState,
                    overlay, CompatibleResource, observation.ObservedEligible, options);
                if (!proposed.CanPublish) return ResourceAllowance.Deferred;
                foreach (var request in overlay)
                    if (proposed.AdmittedUnits(request) != request.Units) return ResourceAllowance.Deferred;
                priorState = proposed.State;
            }
            var finalRequest = observation.Requests[requestedIndex];
            var result = StorageResourceAllocator.Allocate(observation.Cells, priorState,
                new[] { finalRequest }, CompatibleResource, observation.ObservedEligible, options);
            // These prior slices were reconstructed only for this observation, not published
            // cell leases. A preferred exact candidate can otherwise occupy itself with a
            // flexible prior parcel before the incoming parcel is considered. Retry that
            // tentative matching together; every prior/planned quantity must survive, and
            // their real destination restrictions remain in the observed eligibility edges.
            if (requestedIndex > 0 && result.CanPublish && result.AdmittedUnits(finalRequest) < finalRequest.Units)
            {
                var rematched = StorageResourceAllocator.Allocate(observation.Cells, StorageAllocationState.Empty,
                    observation.Requests, CompatibleResource, observation.ObservedEligible, options);
                bool preservesPrior = rematched.CanPublish;
                for (int i = 0; preservesPrior && i < requestedIndex; i++)
                    preservesPrior = rematched.AdmittedUnits(observation.Requests[i]) == observation.Requests[i].Units;
                if (preservesPrior && rematched.AdmittedUnits(finalRequest) > result.AdmittedUnits(finalRequest))
                    result = rematched;
            }
            if (!result.CanPublish || !observation.StillCurrent()
                || !responsibility.Current || ResourceQueriesBlocked(ticket) || resourceTransferDepth > 0)
                return ResourceAllowance.Deferred;
            allowed = (int)Math.Min(int.MaxValue, result.AdmittedUnits(finalRequest));
            return allowed > 0 || observation.Complete ? ResourceAllowance.Observed : ResourceAllowance.Deferred;
        }

        private static bool CompatibleResource(object target, object incoming)
            => target is Thing stack && incoming is Thing subject && !stack.Destroyed && !subject.Destroyed
                && stack.CanStackWith(subject);

        // Full retained demand is not a promise that all of it still fits. An actual owner may
        // deliver its deterministic physical slice while excess stays represented in inventory.
        // New ground-source proposals continue through the stricter all-prior-demand check above.
        private static bool TryHeldDeliveryAllowance(Pawn pawn, ISlotGroup group,
            StorageAllocationObservationDemand requested, ResourceResponsibilitySnapshot snapshot,
            IReadOnlyList<IntVec3> preferred, LoadRecoveryTicket ticket,
            out ResourceAllowance status, out int allowed)
        {
            status = ResourceAllowance.Deferred; allowed = 0;
            var portions = new List<ResourceResponsibility>();
            bool owns = false;
            foreach (var portion in snapshot.Portions)
            {
                if (!ReferenceEquals(portion.Row.Group, group)) continue;
                portions.Add(portion);
                if (portion.Entry.Held && ReferenceEquals(portion.Row.Pawn, pawn)
                    && ReferenceEquals(portion.Entry.Subject, requested.Subject)) owns = true;
            }
            if (!owns) return false;
            portions.Sort((left, right) =>
            {
                // Already withdrawn hands precede retained inventory. Otherwise a partial
                // withdrawal's older source identity could take its own hand parcel's room.
                int leftPhase = left.Entry.Held ? (left.Entry.OwnerJob != null ? 0 : 1) : 2;
                int rightPhase = right.Entry.Held ? (right.Entry.OwnerJob != null ? 0 : 1) : 2;
                int phase = leftPhase.CompareTo(rightPhase);
                if (phase != 0) return phase;
                int carrier = ((Pawn)left.Row.Pawn).thingIDNumber.CompareTo(((Pawn)right.Row.Pawn).thingIDNumber);
                if (carrier != 0) return carrier;
                int subject = left.Entry.Subject.thingIDNumber.CompareTo(right.Entry.Subject.thingIDNumber);
                return subject != 0 ? subject : left.RowIndex.CompareTo(right.RowIndex);
            });
            var demands = new List<StorageAllocationObservationDemand>();
            foreach (var portion in portions)
            {
                object owner = portion.Row.WorkOwner ?? (object)portion.Entry.OwnerJob ?? portion.Row.Pawn;
                // One exact portion per row/parcel. Legacy entries can share a def-wide owner.
                object parcel = portion.Row.WorkOwner is BulkParcelOwner || portion.Row.WorkOwner is HeldCargoOwner
                    ? portion.Row.WorkOwner : (object)portion.Entry.Subject;
                bool callerParcel = portion.Entry.Held && ReferenceEquals(portion.Row.Pawn, pawn)
                    && ReferenceEquals(portion.Entry.Subject, requested.Subject);
                demands.Add(new StorageAllocationObservationDemand(owner, parcel, portion.Entry.Subject,
                    (Pawn)portion.Row.Pawn, portion.Units,
                    callerParcel ? requested.Context : StorageFilterContext.Unload,
                    callerParcel ? requested.Destination ?? portion.Entry.Destination : portion.Entry.Destination,
                    callerParcel ? requested.OwnJob : portion.Entry.OwnerJob,
                    callerParcel ? requested.PriorityFloor : StoragePriority.Unstored,
                    callerParcel && requested.RequireBetterPriority,
                    callerParcel && requested.StartsRefill));
            }
            var observation = StorageAllocationObservation.ObserveForLoad(pawn.Map, group, demands,
                null, preferred, ticket);
            if (observation.Invalidated || !snapshot.Current || ResourceQueriesBlocked(ticket)) return true;
            foreach (var issue in observation.Issues)
                if (issue.Status == StorageAllocationObservationStatus.Unsupported)
                { status = ResourceAllowance.Unsupported; return true; }
            if (!observation.Complete || observation.Requests.Count != demands.Count) return true;
            var allocation = StorageResourceAllocator.Allocate(observation.Cells, StorageAllocationState.Empty,
                observation.Requests, CompatibleResource, observation.ObservedEligible,
                new StorageAllocationOptions(observationComplete: true));
            if (!allocation.CanPublish || !observation.StillCurrent()
                || !snapshot.Current || ResourceQueriesBlocked(ticket) || resourceTransferDepth > 0) return true;
            long assigned = 0;
            for (int i = 0; i < portions.Count; i++)
                if (portions[i].Entry.Held && ReferenceEquals(portions[i].Row.Pawn, pawn)
                    && ReferenceEquals(portions[i].Entry.Subject, requested.Subject))
                    assigned += allocation.AdmittedUnits(observation.Requests[i]);
            allowed = (int)Math.Min(requested.Units, Math.Min(int.MaxValue, assigned));
            status = ResourceAllowance.Observed;
            return true;
        }

    }
}
