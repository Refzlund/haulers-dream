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
        // A discarded proposal over the one authoritative ledger. Only actual startup may
        // apply it; menus, cached drivers and queued preflights never change destination owners.
        private sealed class ForcedStorageProposal
        {
            internal ResourceResponsibilitySnapshot Snapshot;
            internal StorageAllocationObservationResult Observation;
            internal StorageAllocationObservationDemand Primary;
            internal Func<bool> IncomingCurrent;
            internal Func<bool> NotAlreadyAdmitted;
            internal ForcedRawState Raw;
            internal ForcedRawState Policy;
            internal ForcedRawState IncomingRaw;
            internal readonly List<ForcedPendingReduction> Pending = new List<ForcedPendingReduction>();
            internal readonly List<Func<bool>> Guards = new List<Func<bool>>();
            internal bool Current()
            {
                if (!UnityData.IsInMainThread || ResourceQueriesBlocked() || !Snapshot.Current) return false;
                foreach (var guard in Guards) if (!guard()) return false;
                return Raw.Matches() && Policy.Matches() && !ResourceQueriesBlocked() && Snapshot.Current;
            }
        }

        private sealed class ForcedPendingReduction
        {
            internal ResourceResponsibility Portion;
            internal int Allowed;
        }

        // Unlike Reserve/ReservedBy, inspecting the actual native rows invokes no reservation
        // callbacks. Final publication guards must not start another ownership transition.
        private static bool ForcedSourceOwned(Pawn pawn, Job job, Thing source, Map map, int units = 1)
        {
            if (map == null || pawn?.Map != map || job == null || source?.Spawned != true || source.Map != map)
                return false;
            return units > 0 && StorageParcelEvidence.ReservedUnits(pawn, job, source) >= units;
        }

        private static Func<bool> ForcedWorkGuard(Pawn pawn, Job job, Thing source, int units, BulkParcelOwner bulk = null)
        {
            Map map = pawn.Map;
            JobDriver driver = pawn.jobs?.curDriver;
            int id = job.loadID, count = job.count;
            var a = job.targetA; var b = job.targetB;
            var queue = job.targetQueueB; var counts = job.countQueue;
            int index = bulk?.Index ?? -1;
            int queued = index >= 0 && counts != null && index < counts.Count ? counts[index] : -1;
            bool forced = job.playerForced;
            return () => pawn.Map == map && ReferenceEquals(pawn.CurJob, job) && job.loadID == id
                && ReferenceEquals(pawn.jobs?.curDriver, driver) && job.playerForced == forced
                && job.targetA == a && job.targetB == b && job.count == count
                && ForcedSourceOwned(pawn, job, source, map, units)
                && (bulk == null || (ReferenceEquals(job.targetQueueB, queue)
                    && ReferenceEquals(job.countQueue, counts) && index < queue.Count && index < counts.Count
                    && queue[index].Thing == source && counts[index] == queued
                    && bulk.Driver.StoragePendingStartIndex <= index));
        }

        private static bool SameForcedParcel(StorageAllocationObservationDemand left, StorageAllocationObservationDemand right)
            => ReferenceEquals(left.Owner, right.Owner) && ReferenceEquals(left.Parcel, right.Parcel)
                && ReferenceEquals(left.Subject, right.Subject);

        private static Func<bool> ForcedActivationGuard(Pawn pawn, Job job)
        {
            Map map = pawn.Map; Job current = pawn.CurJob; JobDriver driver = pawn.jobs?.curDriver;
            int id = job.loadID, count = job.count;
            var a = job.targetA; var b = job.targetB; bool forced = job.playerForced;
            return () => pawn.Map == map && ReferenceEquals(pawn.CurJob, current)
                && ReferenceEquals(pawn.jobs?.curDriver, driver) && job.loadID == id
                && job.targetA == a && job.targetB == b && job.count == count && job.playerForced == forced;
        }

        private static Func<bool> ForcedQueueGuard(Job job)
        {
            var queue = new ProjectionListGuard<LocalTargetInfo>(job.targetQueueB);
            var counts = new ProjectionListGuard<int>(job.countQueue);
            return () => queue.Matches(job.targetQueueB) && counts.Matches(job.countQueue);
        }

        internal static bool MayPrioritizeNative(JobDriver driver)
            => UnityData.IsInMainThread && !ResourceQueriesBlocked() && driver?.job?.playerForced == true
                && driver.pawn?.carryTracker?.CarriedThing == null
                && driver.job.targetA.Thing?.Spawned == true
                && !(driver.job.targetA.Thing is Corpse)
                && CurrentNativeOwner(driver.pawn, driver.job) == null;

        internal static ResourceAllowance ForcedNativeUnitsFor(JobDriver driver, ISlotGroup group,
            Thing source, int wanted, bool apply, out int allowed)
        {
            allowed = 0;
            if (!UnityData.IsInMainThread || ResourceQueriesBlocked()) return ResourceAllowance.Deferred;
            Pawn pawn = driver?.pawn; Job job = driver?.job;
            if (!ActiveOn(pawn?.Map)) return ResourceAllowance.Unsupported;
            if (!MayPrioritizeNative(driver)) return ResourceAllowance.Deferred;
            Map map = pawn.Map; var destination = job.targetB;
            var raw = new ForcedRawState(); raw.Map(map); raw.Pawn(pawn); raw.Job(job); raw.Thing(source);
            var live = ForcedActivationGuard(pawn, job);
            var sourceGuard = new ProjectionThingGuard(source);
            var request = new StorageAllocationObservationDemand(job, source, source, pawn, wanted,
                StorageFilterContext.Unload, destination.Cell, job, startsRefill: true);
            var status = ForcedStorageUnitsFor(pawn, group, request, request, null, null,
                out allowed, out var proposal);
            bool valid = live() && sourceGuard.Matches() && job.targetA.Thing == source
                && ReferenceEquals(BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(destination.Cell)), group);
            if (!valid) { allowed = 0; return ResourceAllowance.Deferred; }
            if (!apply || status != ResourceAllowance.Observed || allowed <= 0) return status;
            if (proposal == null || !ReferenceEquals(pawn.CurJob, job) || !ReferenceEquals(pawn.jobs.curDriver, driver))
            { allowed = 0; return ResourceAllowance.Deferred; }
            proposal.Guards.Add(live); proposal.Guards.Add(sourceGuard.Matches);
            proposal.IncomingCurrent = () => live() && sourceGuard.Matches()
                && ReferenceEquals(BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(destination.Cell)), group);
            proposal.NotAlreadyAdmitted = () => CurrentNativeOwner(pawn, job) == null;
            proposal.IncomingRaw = raw;
            var owner = new NativeStorageOwner(pawn, job, null, 0, source, allowed);
            if (!ApplyForcedStorage(proposal, pawn, job, driver, group, source, allowed, owner))
            { allowed = 0; return ResourceAllowance.Deferred; }
            return status;
        }

        private static bool MayPrioritizeBulk(JobDriver_BulkHaul driver, int index, Thing source)
            => UnityData.IsInMainThread && !ResourceQueriesBlocked()
                && driver?.job?.playerForced == true && index == 0 && driver.StoragePendingStartIndex == 0
                && driver.job.takeInventoryDelay <= 0 && source?.def?.category == ThingCategory.Item
                && !(source is Corpse)
                && source.Spawned && driver.job.targetA.Thing == source
                && FindBulkOwner(driver.pawn, driver, index, source) == null;

        internal static bool ForcedBulkPreflight(JobDriver_BulkHaul driver)
        {
            if (!UnityData.IsInMainThread || ResourceQueriesBlocked()) return false;
            var job = driver.job; var pawn = driver.pawn;
            var source = job.targetQueueB[0].Thing;
            if (!MayPrioritizeBulk(driver, 0, source) || job.countQueue[0] <= 0) return true;
            var live = ForcedActivationGuard(pawn, job);
            var stamp = new ProjectionThingGuard(source);
            var queueGuard = ForcedQueueGuard(job);
            var priority = StoreUtility.CurrentStoragePriorityOf(source);
            using (SuppressOwnGateForProjection())
            using (StorageBuildingFilter.PushContext(StorageFilterContext.Unload))
            {
                if (!StoreUtility.TryFindBestBetterStorageFor(source, pawn, pawn.Map, priority, pawn.Faction,
                    out IntVec3 cell, out _, needAccurateResult: false)) return false;
                if (!cell.IsValid) return live() && stamp.Matches() && queueGuard(); // true container retains its own policy
                var group = BulkHaul.BudgetGroupOf(pawn.Map.haulDestinationManager.SlotGroupAt(cell));
                var request = new StorageAllocationObservationDemand(job, source, source, pawn,
                    Math.Min(job.countQueue[0], source.stackCount), StorageFilterContext.Unload,
                    ownJob: job, priorityFloor: priority, requireBetterPriority: true, startsRefill: true);
                var status = ForcedStorageUnitsFor(pawn, group, request, request, null, new[] { cell },
                    out int units, out _);
                return live() && stamp.Matches() && queueGuard() && (status == ResourceAllowance.Unsupported
                    || status == ResourceAllowance.Observed && units > 0);
            }
        }

        // Protected quantities are committed first; the one explicitly ordered primary may then
        // displace only recognized automatic pending parcels. Extras run after those survivors.
        private static ResourceAllowance ForcedStorageUnitsFor(Pawn pawn, ISlotGroup group,
            StorageAllocationObservationDemand primary, StorageAllocationObservationDemand requested,
            IReadOnlyList<StorageAllocationObservationDemand> planned, IReadOnlyList<IntVec3> preferred,
            out int allowed, out ForcedStorageProposal proposal)
        {
            allowed = 0; proposal = null;
            if (!UnityData.IsInMainThread || ResourceQueriesBlocked() || resourceTransferDepth > 0)
                return ResourceAllowance.Deferred;
            if (seamDisabled || pawn?.Map == null || group == null || primary?.Subject?.def?.category != ThingCategory.Item)
                return ResourceAllowance.Unsupported;
            if (primary.Units <= 0 || requested == null || primary.Pawn != pawn || requested.Pawn != pawn)
                return ResourceAllowance.Deferred;
            var map = pawn.Map;
            if (HaulersDreamGameComponent.storageClaims.Length > 256) return ResourceAllowance.Deferred;
            var raw = new ForcedRawState();
            var policy = new ForcedRawState();
            var before = HaulersDreamGameComponent.storageClaims;
            raw.Map(map); raw.Pawn(pawn); raw.Job(primary.OwnJob); raw.Thing(primary.Subject);
            policy.HdPolicy(); policy.PawnPolicy(pawn);
            foreach (var row in before)
                if (ReferenceEquals(row.Group, group) && row.Pawn is Pawn carrier)
                { raw.Pawn(carrier); policy.PawnPolicy(carrier); }
            // Capture native/effective/fixed policy before any attribution/provider callback.
            policy.StoragePolicy(group);
            var snapshot = ObserveResourceResponsibilities(map);
            if (!ReferenceEquals(before, snapshot.Rows) || !snapshot.Current || snapshot.Rows.Length > 256
                || !raw.Matches() || !policy.Matches())
                return ResourceAllowance.Deferred;
            // An unresolved physical owner's candidate group is not disposable automatic
            // work. Forced source admission cannot bypass the same held obligation that
            // defers an ordinary new-source proposal while maintenance resolves its proof.
            foreach (var portion in snapshot.Portions)
                if (ReferenceEquals(portion.Row.Group, UnresolvedHeldDestination)
                    && portion.Row.WorkOwner is HeldCargoOwner unresolved
                    && ReferenceEquals(unresolved.CandidateGroup, group))
                    return ResourceAllowance.Deferred;
            var next = new ForcedStorageProposal { Snapshot = snapshot, Primary = primary, Raw = raw, Policy = policy };
            var demands = new List<StorageAllocationObservationDemand>();
            var automatic = new List<ResourceResponsibility>();
            var owners = new HashSet<object>();
            var repeated = new HashSet<object>();
            foreach (var row in snapshot.Rows)
            {
                if (row.WorkOwner != null && !owners.Add(row.WorkOwner)) repeated.Add(row.WorkOwner);
                if (!ReferenceEquals(row.Group, group) || row.Units <= 0 || row.ExclusiveCellAllocation != null) continue;
                // An unknown owner cannot be classified as dispensable merely because the
                // current collector lacks its proof. Decline the priority transfer instead.
                if (row.WorkOwner != null && !(row.WorkOwner is NativeStorageOwner)
                    && !(row.WorkOwner is BulkParcelOwner) && !(row.WorkOwner is HeldCargoOwner))
                    return ResourceAllowance.Deferred;
            }
            foreach (var portion in snapshot.Portions)
            {
                if (!ReferenceEquals(portion.Row.Group, group)) continue;
                var carrier = (Pawn)portion.Row.Pawn;
                var entry = portion.Entry;
                next.Raw.Pawn(carrier); next.Raw.Job(entry.OwnerJob); next.Raw.Thing(entry.Subject);
                var native = portion.Row.WorkOwner as NativeStorageOwner;
                var bulk = portion.Row.WorkOwner as BulkParcelOwner;
                Job job = native?.Job ?? bulk?.Job;
                bool pending = !entry.Held && carrier.Faction == pawn.Faction
                    && !ReferenceEquals(job, primary.OwnJob)
                    && job?.playerForced == false && ReferenceEquals(entry.OwnerJob, job)
                    && snapshot.RowUnits[portion.RowIndex] == portion.Row.Units
                    && !repeated.Contains(portion.Row.WorkOwner)
                    && ((native != null && native.Live(carrier) && native.Pending == entry.Subject
                        && native.PendingUnits == portion.Units && job.def == JobDefOf.HaulToCell
                        && carrier.jobs.curDriver?.GetType() == typeof(JobDriver_HaulToCell))
                        || (bulk != null && !bulk.Held && bulk.Live(carrier)
                            && job.countQueue[bulk.Index] == portion.Units));
                var stamp = new ProjectionThingGuard(entry.Subject);
                next.Guards.Add(stamp.Matches);
                if (job != null)
                {
                    next.Guards.Add(ForcedActivationGuard(carrier, job));
                    next.Guards.Add(() => native != null ? native.Live(carrier) : bulk.Live(carrier));
                }
                else if (bulk != null) next.Guards.Add(() => bulk.Live(carrier));
                else if (portion.Row.WorkOwner is HeldCargoOwner held) next.Guards.Add(() => held.Live(carrier));
                if (pending)
                {
                    next.Guards.Add(ForcedWorkGuard(carrier, job, entry.Subject, portion.Units, bulk));
                    automatic.Add(portion);
                }
                else demands.Add(ForcedDemand(portion));
            }
            int protectedCount = demands.Count;
            int primaryIndex = demands.Count;
            demands.Add(primary);
            automatic.Sort((left, right) =>
            {
                int order = ((Pawn)left.Row.Pawn).thingIDNumber.CompareTo(((Pawn)right.Row.Pawn).thingIDNumber);
                if (order == 0) order = left.Entry.OwnerJob.loadID.CompareTo(right.Entry.OwnerJob.loadID);
                if (order == 0) order = left.Entry.Subject.thingIDNumber.CompareTo(right.Entry.Subject.thingIDNumber);
                return order != 0 ? order : left.RowIndex.CompareTo(right.RowIndex);
            });
            foreach (var portion in automatic) demands.Add(ForcedDemand(portion));
            int extrasIndex = demands.Count;
            if (planned != null)
                foreach (var extra in planned)
                    if (!SameForcedParcel(extra, primary) && !SameForcedParcel(extra, requested)) demands.Add(extra);
            int requestedIndex = SameForcedParcel(primary, requested) ? primaryIndex : demands.Count;
            if (requestedIndex != primaryIndex) demands.Add(requested);
            foreach (var demand in demands)
            {
                next.Raw.Thing(demand.Subject);
                var stamp = new ProjectionThingGuard(demand.Subject);
                next.Guards.Add(stamp.Matches);
            }
            var observation = StorageAllocationObservation.Observe(map, group, demands, preferredCells: preferred);
            foreach (var issue in observation.Issues)
                if (issue.Status == StorageAllocationObservationStatus.Unsupported) return ResourceAllowance.Unsupported;
            // Priority transfer requires a complete finite result, not a guess that omitted
            // pending work or an unseen protected destination could fit somewhere else.
            if (observation.Invalidated || !observation.Complete || observation.Requests.Count != demands.Count
                || !next.Current()) return ResourceAllowance.Deferred;
            var options = new StorageAllocationOptions(observationComplete: true);
            var compatibility = new Dictionary<(object target, object subject), bool>();
            bool exhausted = false;
            bool Compatible(object target, object subject)
            {
                var key = (target, subject);
                if (compatibility.TryGetValue(key, out bool answer)) return answer;
                if (compatibility.Count >= 8192) { exhausted = true; return false; }
                answer = CompatibleResource(target, subject); compatibility.Add(key, answer); return answer;
            }
            StorageAllocationState state = StorageAllocationState.Empty;
            for (int phase = 0; phase < 4; phase++)
            {
                int start = phase == 0 ? 0 : phase == 1 ? primaryIndex : phase == 2 ? primaryIndex + 1 : extrasIndex;
                int end = phase == 0 ? protectedCount : phase == 1 ? primaryIndex + 1 : phase == 2 ? extrasIndex : demands.Count;
                var requests = new List<StorageAllocationRequest>();
                for (int i = start; i < end; i++) requests.Add(observation.Requests[i]);
                var result = StorageResourceAllocator.Allocate(observation.Cells, state, requests,
                    Compatible, observation.ObservedEligible, options);
                if (exhausted || !result.CanPublish || !next.Current()) return ResourceAllowance.Deferred;
                for (int i = start; i < end; i++)
                    if ((phase == 0 || (phase == 1 && requestedIndex != primaryIndex)
                        || (phase == 3 && i != requestedIndex))
                        && result.AdmittedUnits(observation.Requests[i]) != demands[i].Units)
                        return ResourceAllowance.Deferred;
                state = result.State;
            }
            // Compatibility is a mod seam too. Re-observe eligibility/physical resources after
            // those callbacks, then validate the complete proposal without invoking them again.
            var fresh = StorageAllocationObservation.Observe(map, group, demands, preferredCells: preferred);
            if (fresh.Invalidated || !fresh.Complete || fresh.Requests.Count != demands.Count || !next.Current())
                return ResourceAllowance.Deferred;
            next.Raw.ObservedCells(map, group, fresh);
            var confirmed = StorageResourceAllocator.Allocate(fresh.Cells, state, Array.Empty<StorageAllocationRequest>(),
                (target, subject) => compatibility.TryGetValue((target, subject), out bool answer) && answer,
                fresh.ObservedEligible, options);
            if (!confirmed.CanPublish || !next.Current()) return ResourceAllowance.Deferred;
            int primaryUnits = (int)Math.Min(int.MaxValue, state.UnitsFor(primary.Owner, primary.Parcel));
            if (primaryUnits <= 0) return ResourceAllowance.Observed;
            for (int i = 0; i < automatic.Count; i++)
            {
                var request = observation.Requests[primaryIndex + 1 + i];
                int retained = (int)Math.Min(int.MaxValue, state.UnitsFor(request.Owner, request.Parcel));
                if (retained < automatic[i].Units)
                {
                    var portion = automatic[i];
                    next.Pending.Add(new ForcedPendingReduction { Portion = portion, Allowed = retained });
                }
            }
            allowed = (int)Math.Min(int.MaxValue, state.UnitsFor(requested.Owner, requested.Parcel));
            next.Observation = fresh;
            if (next.Current()) proposal = next;
            else { allowed = 0; return ResourceAllowance.Deferred; }
            return ResourceAllowance.Observed;
        }

        private static StorageAllocationObservationDemand ForcedDemand(ResourceResponsibility portion)
        {
            var entry = portion.Entry;
            object owner = portion.Row.WorkOwner ?? (object)entry.OwnerJob ?? portion.Row.Pawn;
            object parcel = portion.Row.WorkOwner is BulkParcelOwner ? owner : entry.Subject;
            return new StorageAllocationObservationDemand(owner, parcel, entry.Subject, (Pawn)portion.Row.Pawn,
                portion.Units, StorageFilterContext.Unload, entry.Destination, entry.OwnerJob, startsRefill: false);
        }

        private static bool ApplyForcedStorage(ForcedStorageProposal proposal, Pawn pawn, Job job,
            JobDriver driver, ISlotGroup group, Thing source, int units, object incomingOwner)
        {
            var map = pawn.Map;
            var relinquishedTarget = LocalTargetInfo.Invalid;
            if (proposal == null || units <= 0 || incomingOwner == null || !proposal.Current()
                || proposal.IncomingCurrent?.Invoke() != true
                || proposal.NotAlreadyAdmitted?.Invoke() != true
                || !ReferenceEquals(pawn.CurJob, job) || !ReferenceEquals(pawn.jobs.curDriver, driver)
                || !ForcedSourceOwned(pawn, job, source, map, units)
                || proposal.Observation?.StillCurrent() != true) return false;
            var reductions = new Dictionary<int, ForcedPendingReduction>();
            foreach (var change in proposal.Pending) reductions.Add(change.Portion.RowIndex, change);
            var rows = new List<StorageClaimRow>();
            // No callback-bearing operation occurs between the last guard, exact plan edits,
            // and one immutable array publication. Victims continue at their OWN lifecycle
            // boundary; ending one here could re-enter the forcing pawn's native StartJob.
            using (BeginResourceTransfer())
            {
                // All provider/native callbacks have finished. This last pass reads only
                // captured raw fields and collections, then the exact authoritative array.
                // Do not insert Live/ReservedBy/group lookup/StillCurrent after this point.
                if (proposal.IncomingRaw == null || !proposal.Raw.Matches() || !proposal.Policy.Matches() || !proposal.IncomingRaw.Matches()
                    || !proposal.IncomingRaw.Reserved(pawn, job, source, map, units)
                    || !proposal.Snapshot.Current) return false;
                for (int i = 0; i < proposal.Snapshot.Rows.Length; i++)
                {
                    var row = proposal.Snapshot.Rows[i];
                    if (!reductions.TryGetValue(i, out var reduction)) { rows.Add(row); continue; }
                    int keep = row.Units - reduction.Portion.Units + reduction.Allowed;
                    object owner = row.WorkOwner;
                    if (owner is NativeStorageOwner native)
                        owner = new NativeStorageOwner((Pawn)row.Pawn, native.Job, native.Hands, native.HandUnits,
                            reduction.Allowed > 0 ? native.Pending : null, reduction.Allowed);
                    else if (owner is BulkParcelOwner bulk)
                    {
                        bulk.Job.countQueue[bulk.Index] = reduction.Allowed;
                        if (reduction.Allowed == 0) bulk.Job.targetQueueB[bulk.Index] = relinquishedTarget;
                    }
                    if (keep > 0) rows.Add(new StorageClaimRow(row.Pawn, row.Group, row.Def, keep, workOwner: owner));
                }
                rows.Add(new StorageClaimRow(pawn, group, source.def, units, workOwner: incomingOwner));
                HaulersDreamGameComponent.SetStorageClaims(rows.ToArray());
            }
            RouteSelection.ClearClaimedCache();
            BulkHaul.InvalidatePlanCache();
            // Native StartCarryThing re-admits before pickup (or delivers existing hands).
            // Bulk walk/pause consume the tombstone; its take toil also refuses count zero.
            // Their own native cleanup eventually releases only that job's reservations.
            // Revalidate our new promise through ordinary admission, never another priority
            // attempt. This cannot restore retired victim promises or clear their queues.
            bool accepted = false;
            try
            {
                var observedRows = HaulersDreamGameComponent.storageClaims;
                var status = ResourceUnitsFor(pawn, group, proposal.Primary, null, null, out int surviving);
                if (status == ResourceAllowance.Observed && surviving >= units && proposal.IncomingCurrent?.Invoke() == true
                    && proposal.Policy.Matches() && proposal.IncomingRaw.Matches()
                    && proposal.IncomingRaw.Reserved(pawn, job, source, map, units)
                    && ReferenceEquals(observedRows, HaulersDreamGameComponent.storageClaims))
                    foreach (var row in observedRows)
                        if (ReferenceEquals(row.WorkOwner, incomingOwner) && row.Units == units
                            && ReferenceEquals(row.Group, group)) { accepted = true; return true; }
                return false;
            }
            finally
            {
                // Refusal or an actual query exception retires only this pending token. Keep
                // the original exception; never restore victims or remove a cargo successor.
                if (!accepted)
                {
                    var survivors = new List<StorageClaimRow>();
                    var current = HaulersDreamGameComponent.storageClaims;
                    foreach (var row in current) if (!ReferenceEquals(row.WorkOwner, incomingOwner)) survivors.Add(row);
                    if (survivors.Count != current.Length) HaulersDreamGameComponent.SetStorageClaims(survivors.ToArray());
                }
            }
        }

    }
}
