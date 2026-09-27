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
        [ThreadStatic] private static bool reconcilingResources;
        [ThreadStatic] private static bool attributingResources;
        [ThreadStatic] private static int resourceEvidenceDepth;
        // An explicit custody record with no justified slot destination. It is deliberately
        // not an ISlotGroup and never consumes imaginary room in every future storage group.
        private static readonly object UnresolvedHeldDestination = new object();
        private enum HeldDestinationReason { None, NoDestination, NativeContainer, Unsupported, Deferred }
        // Janitor adoption owns one actual parcel, never every Thing with the same def.
        // Hands are additionally bound to the current activation; inventory remains tagged
        // cargo across job changes. Admission still uses the collector's bounded quantity.
        private sealed class HeldCargoOwner
        {
            internal readonly Thing Subject;
            internal readonly HeldDestinationReason Reason;
            internal readonly ISlotGroup CandidateGroup;
            private readonly ThingOwner holder;
            private readonly Job job;
            private readonly JobDriver driver;
            private readonly int jobId;
            internal HeldCargoOwner(Pawn pawn, StorageParcelEvidence.Entry entry,
                HeldDestinationReason reason = HeldDestinationReason.None, ISlotGroup candidateGroup = null)
            {
                Subject = entry.Subject; holder = Subject.holdingOwner;
                Reason = reason; CandidateGroup = candidateGroup;
                if (ReferenceEquals(holder, pawn.carryTracker?.innerContainer))
                { job = pawn.CurJob; driver = pawn.jobs?.curDriver; jobId = job?.loadID ?? -1; }
            }
            internal bool Live(Pawn pawn)
            {
                if (pawn?.Map == null || Subject == null || Subject.Destroyed || Subject.Spawned
                    || Subject.stackCount <= 0 || !ReferenceEquals(Subject.holdingOwner, holder)
                    || holder?.Contains(Subject) != true) return false;
                if (ReferenceEquals(holder, pawn.inventory?.innerContainer))
                    return pawn.GetComp<CompHauledToInventory>()?.PeekHashSet().Contains(Subject) == true;
                return ReferenceEquals(holder, pawn.carryTracker?.innerContainer) && job != null
                    && ReferenceEquals(pawn.CurJob, job) && job.loadID == jobId
                    && ReferenceEquals(pawn.jobs?.curDriver, driver);
            }
            internal bool Matches(StorageParcelEvidence.Entry entry)
                => entry.Held && ReferenceEquals(entry.Subject, Subject)
                    && (job == null || ReferenceEquals(entry.OwnerJob, job));
        }

        private readonly struct ResourceResponsibility
        {
            internal readonly int RowIndex;
            internal readonly StorageClaimRow Row;
            internal readonly StorageParcelEvidence.Entry Entry;
            internal readonly int Units;
            internal ResourceResponsibility(int index, StorageClaimRow row,
                StorageParcelEvidence.Entry entry, int units)
            { RowIndex = index; Row = row; Entry = entry; Units = units; }
        }

        // An owned read result, shared by admission and maintenance. It is valid only while its
        // captured ledger remains current; no per-tick cache or write is hidden in attribution.
        private sealed class ResourceResponsibilitySnapshot
        {
            internal readonly StorageClaimRow[] Rows;
            internal readonly List<ResourceResponsibility> Portions = new List<ResourceResponsibility>();
            internal readonly int[] RowUnits;
            private readonly Dictionary<Pawn, List<StorageParcelEvidence.Entry>> cargo
                = new Dictionary<Pawn, List<StorageParcelEvidence.Entry>>();
            private readonly Dictionary<(Pawn, Thing), int> attributed = new Dictionary<(Pawn, Thing), int>();
            private readonly Game game;
            private readonly LoadRecoveryTicket epoch;
            internal bool Complete;
            internal bool SameLifecycle => ReferenceEquals(game, Verse.Current.Game)
                && ReferenceEquals(epoch, activeLoadRecovery);
            internal bool Current => Complete && ReferenceEquals(Rows, HaulersDreamGameComponent.storageClaims)
                && SameLifecycle;
            internal ResourceResponsibilitySnapshot(StorageClaimRow[] rows)
            {
                Rows = rows; RowUnits = new int[rows.Length];
                game = UnityData.IsInMainThread ? Verse.Current.Game : null;
                epoch = activeLoadRecovery;
            }
            internal List<StorageParcelEvidence.Entry> EntriesFor(Pawn pawn)
            {
                if (!cargo.TryGetValue(pawn, out var entries))
                {
                    entries = new List<StorageParcelEvidence.Entry>();
                    resourceEvidenceDepth++;
                    try { StorageParcelEvidence.Collect(pawn, entries); }
                    finally { resourceEvidenceDepth--; }
                    cargo.Add(pawn, entries);
                }
                return entries;
            }
            internal int Remaining(Pawn pawn, StorageParcelEvidence.Entry entry)
            {
                attributed.TryGetValue((pawn, entry.Subject), out int used);
                return Math.Max(0, entry.Units - used);
            }
            internal void Attribute(int index, StorageClaimRow row, Pawn pawn,
                StorageParcelEvidence.Entry entry, int units)
            {
                var key = (pawn, entry.Subject);
                attributed.TryGetValue(key, out int used);
                attributed[key] = used + units;
                RowUnits[index] += units;
                Portions.Add(new ResourceResponsibility(index, row, entry, units));
            }
        }

        private static ResourceResponsibilitySnapshot ObserveResourceResponsibilities(Map map)
            => ObserveResourceResponsibilitiesForLoad(map, null);

        private static ResourceResponsibilitySnapshot ObserveResourceResponsibilitiesForLoad(Map map,
            LoadRecoveryTicket ticket)
        {
            if (!UnityData.IsInMainThread || ResourceQueriesBlocked(ticket))
                return new ResourceResponsibilitySnapshot(StorageClaimLedger.Empty);
            var snapshot = new ResourceResponsibilitySnapshot(HaulersDreamGameComponent.storageClaims);
            if (attributingResources || resourceEvidenceDepth > 0 || resourceTransferDepth > 0) return snapshot;
            attributingResources = true;
            try
            {
                // Actual owners consume physical quantities before legacy def-wide rows. Among native
                // entries, the held parcel consumes its amount before a newly reserved duplicate.
                for (int phase = 0; phase < 2; phase++)
                    for (int index = 0; index < snapshot.Rows.Length; index++)
                    {
                        var row = snapshot.Rows[index];
                        if ((row.WorkOwner != null ? 0 : 1) != phase || row.ExclusiveCellAllocation != null
                            || !(row.Pawn is Pawn carrier) || carrier.Map != map || row.Units <= 0
                            || (!ReferenceEquals(row.Group, UnresolvedHeldDestination)
                                && !GroupIsLive(row.Group, map))) continue;
                        var native = row.WorkOwner as NativeStorageOwner;
                        var bulk = row.WorkOwner as BulkParcelOwner;
                        var held = row.WorkOwner as HeldCargoOwner;
                        bool unresolved = ReferenceEquals(row.Group, UnresolvedHeldDestination);
                        if (unresolved && held == null) continue;
                        if (row.WorkOwner != null && !(native?.Live(carrier) == true
                            || bulk?.Live(carrier) == true || held?.Live(carrier) == true)) continue;
                        int remaining = row.Units;
                        foreach (var entry in ResourceParcelOrder(snapshot.EntriesFor(carrier), native != null))
                        {
                            if (remaining <= 0) break;
                            if (entry.Subject?.def != row.Def || entry.Units <= 0
                                || (!unresolved && entry.KnownGroup != null && !ReferenceEquals(entry.KnownGroup, row.Group))
                                || (native != null && !ReferenceEquals(native.Job, entry.OwnerJob))
                                || (bulk != null && (!ReferenceEquals(bulk.Subject, entry.Subject)
                                    || (!bulk.Held && !ReferenceEquals(bulk.Job, entry.OwnerJob))))
                                || (held != null && !held.Matches(entry))
                                // A reservation/queue is not admission. Legacy records may account for
                                // real retained cargo, never resurrect unpicked native/bulk quantities.
                                || (row.WorkOwner == null && !entry.Held)) continue;
                            int units = Math.Min(remaining, snapshot.Remaining(carrier, entry));
                            if (units <= 0) continue;
                            remaining -= units;
                            snapshot.Attribute(index, row, carrier, entry, units);
                        }
                    }
                snapshot.Complete = true;
                return snapshot;
            }
            finally { attributingResources = false; }
        }

        private static bool ReconcileResourceRows(Map map)
        {
            var snapshot = ObserveResourceResponsibilities(map);
            var survivors = new List<StorageClaimRow>(snapshot.Rows.Length);
            bool changed = false;
            for (int i = 0; i < snapshot.Rows.Length; i++)
            {
                var row = snapshot.Rows[i];
                if (!(row.Pawn is Pawn pawn) || pawn.Map == null)
                { changed = true; continue; }
                if (pawn.Map != map) { survivors.Add(row); continue; }
                if (row.ExclusiveCellAllocation != null)
                {
                    // Its native cell reservation is already the physical exclusion. No numeric
                    // debt or def-wide evidence is substituted for this lease's exact lifecycle.
                    if (row.ExclusiveCellAllocation is StorageExclusiveCellAllocation exclusive && exclusive.Live)
                        survivors.Add(row);
                    else changed = true;
                    continue;
                }
                int units = snapshot.RowUnits[i];
                if (units <= 0) { changed = true; continue; }
                if (units == row.Units) survivors.Add(row);
                else
                {
                    changed = true;
                    survivors.Add(new StorageClaimRow(row.Pawn, row.Group, row.Def, units,
                        workOwner: row.WorkOwner));
                }
            }
            // A provider callback may have changed another responsibility during collection.
            // Never replace newer authoritative rows with a snapshot derived from the old array.
            if (!snapshot.Current) return false;
            if (changed) HaulersDreamGameComponent.SetStorageClaims(survivors.ToArray());
            return true;
        }

        private static void AdoptResidualCargo(Pawn pawn, ref ResourceResponsibilitySnapshot snapshot,
            LoadRecoveryTicket ticket = null)
        {
            if (ResourceQueriesBlocked(ticket)) return;
            if (!snapshot.Current) snapshot = ObserveResourceResponsibilitiesForLoad(pawn.Map, ticket);
            if (!snapshot.Current) return;
            var entries = snapshot.EntriesFor(pawn);
            // The collector orders actual Things by ID; two same-def parcels with different
            // stuff/quality therefore retain their own destination and admission predicate.
            foreach (var entry in entries)
            {
                if (!entry.Held || entry.Subject?.def?.category != ThingCategory.Item) continue;
                // A preceding adoption or provider callback changed the ledger. Recollect before
                // computing this parcel's residual instead of spending an old whole-stack amount.
                if (!snapshot.Current) snapshot = ObserveResourceResponsibilitiesForLoad(pawn.Map, ticket);
                var currentEntries = snapshot.EntriesFor(pawn);
                StorageParcelEvidence.Entry current = default;
                foreach (var candidate in currentEntries)
                    if (ReferenceEquals(candidate.Subject, entry.Subject)) { current = candidate; break; }
                if (!snapshot.Current) return;
                if (!current.Held) continue;
                int residual = snapshot.Remaining(pawn, current);
                if (residual <= 0) continue;
                object destination = ResolveHeldDestination(pawn, current, ticket,
                    out HeldDestinationReason reason, out ISlotGroup candidateGroup);
                var owner = new HeldCargoOwner(pawn, current, reason, candidateGroup);
                if (!snapshot.Current || !owner.Live(pawn) || ResourceQueriesBlocked(ticket))
                { snapshot.Complete = false; continue; }
                // Acceptance callbacks can alter keep-stock rules without changing a Thing's
                // physical stamp. Recollect the actual surplus before publishing this receipt.
                var final = ObserveResourceResponsibilitiesForLoad(pawn.Map, ticket);
                if (!final.Current) { snapshot.Complete = false; continue; }
                int stillResidual = 0;
                foreach (var actual in final.EntriesFor(pawn))
                    if (owner.Matches(actual)) { stillResidual = final.Remaining(pawn, actual); break; }
                if (!final.Current || !snapshot.Current || !owner.Live(pawn) || ResourceQueriesBlocked(ticket))
                { snapshot = final; continue; }
                if (!ReferenceEquals(destination, UnresolvedHeldDestination) && !GroupIsLive(destination, pawn.Map))
                {
                    destination = UnresolvedHeldDestination;
                    owner = new HeldCargoOwner(pawn, current, HeldDestinationReason.NoDestination);
                }
                int units = Math.Min(residual, stillResidual);
                if (units <= 0) { snapshot = final; continue; }
                // Group lifetime checks above can call provider getters. Publish only after
                // their callbacks and every ownership/ticket check, against this exact array.
                if (!owner.Live(pawn) || ResourceQueriesBlocked(ticket)
                    || !final.Current || !snapshot.Current) { snapshot = final; continue; }
                HaulersDreamGameComponent.SetStorageClaims(StorageClaimLedger.AddForWork(
                    final.Rows, pawn, destination, current.Subject.def, units, owner));
            }
        }

        private static object ResolveHeldDestination(Pawn pawn, StorageParcelEvidence.Entry entry,
            LoadRecoveryTicket ticket, out HeldDestinationReason reason, out ISlotGroup candidateGroup)
        {
            reason = HeldDestinationReason.None;
            candidateGroup = entry.KnownGroup;
            // A witnessed ordinary/container hand parcel already has an actual destination.
            // Do not predict a different shelf merely because a container has no slot group.
            if (entry.Held && entry.OwnerJob != null && entry.OwnerJob.targetB.HasThing
                && ReferenceEquals(pawn.CurJob, entry.OwnerJob)
                && ReferenceEquals(pawn.carryTracker?.CarriedThing, entry.Subject))
            {
                reason = pawn.Map.reservationManager.ReservedBy(entry.OwnerJob.targetB, pawn, entry.OwnerJob)
                    ? HeldDestinationReason.NativeContainer : HeldDestinationReason.Deferred;
                candidateGroup = null;
                return UnresolvedHeldDestination;
            }
            if (candidateGroup == null || !GroupIsLive(candidateGroup, pawn.Map))
            {
                candidateGroup = null;
                // This owned maintenance probe must see physical destinations, not its own
                // pending-load gate or other numeric promises. Public callback queries retain
                // their missing-ticket guard; this scope confers no resource-reader privilege.
                using (SuppressOwnGateForProjection())
                using (StorageBuildingFilter.PushContext(StorageFilterContext.Unload))
                {
                    bool found = StoreUtility.TryFindBestBetterStorageFor(entry.Subject, pawn, pawn.Map,
                        StoragePriority.Unstored, pawn.Faction, out IntVec3 cell, out _, needAccurateResult: false);
                    if (!found) reason = HeldDestinationReason.NoDestination;
                    else if (!cell.IsValid) reason = HeldDestinationReason.NativeContainer;
                    else candidateGroup = BulkHaul.BudgetGroupOf(pawn.Map.haulDestinationManager.SlotGroupAt(cell));
                }
            }
            if (candidateGroup == null)
            {
                if (reason == HeldDestinationReason.None) reason = HeldDestinationReason.NoDestination;
                return UnresolvedHeldDestination;
            }
            var demand = new StorageAllocationObservationDemand(pawn, entry.Subject, entry.Subject, pawn,
                entry.Units, StorageFilterContext.Unload, entry.Destination, entry.OwnerJob, startsRefill: false);
            var observation = StorageAllocationObservation.ObserveForLoad(pawn.Map, candidateGroup,
                new[] { demand }, null, null, ticket);
            foreach (var issue in observation.Issues)
                if (issue.Status == StorageAllocationObservationStatus.Unsupported)
                { reason = HeldDestinationReason.Unsupported; return UnresolvedHeldDestination; }
            if (observation.Invalidated || !observation.Complete)
            { reason = HeldDestinationReason.Deferred; return UnresolvedHeldDestination; }
            // This records physical demand, not certified spare capacity. A full/denied group
            // may have zero current room; exact-owner delivery will obtain a fresh bounded slice.
            return candidateGroup;
        }

        private static void ResolveUnboundHeldCargo(Map map, LoadRecoveryTicket ticket = null)
        {
            var snapshot = ObserveResourceResponsibilitiesForLoad(map, ticket);
            if (!snapshot.Current) return;
            foreach (var portion in snapshot.Portions)
            {
                if (!ReferenceEquals(portion.Row.Group, UnresolvedHeldDestination)
                    || !(portion.Row.WorkOwner is HeldCargoOwner previous)) continue;
                var pawn = (Pawn)portion.Row.Pawn;
                object destination = ResolveHeldDestination(pawn, portion.Entry, ticket,
                    out HeldDestinationReason reason, out ISlotGroup candidateGroup);
                if (!snapshot.Current || !previous.Live(pawn) || ResourceQueriesBlocked(ticket)) return;
                var final = ObserveResourceResponsibilitiesForLoad(map, ticket);
                int units = 0;
                foreach (var actual in final.Portions)
                    if (ReferenceEquals(actual.Row.WorkOwner, previous)
                        && ReferenceEquals(actual.Entry.Subject, portion.Entry.Subject)) units += actual.Units;
                if (!final.Current || !snapshot.Current || !previous.Live(pawn) || ResourceQueriesBlocked(ticket)) return;
                if (units <= 0) { snapshot = final; continue; }
                if (!ReferenceEquals(destination, UnresolvedHeldDestination) && !GroupIsLive(destination, map))
                { destination = UnresolvedHeldDestination; reason = HeldDestinationReason.NoDestination; candidateGroup = null; }
                // Replace only this exact token, including its reason when still unresolved.
                var owner = new HeldCargoOwner(pawn, portion.Entry, reason, candidateGroup);
                var rows = StorageClaimLedger.AddForWork(final.Rows, pawn, null,
                    portion.Entry.Subject.def, 0, previous);
                rows = StorageClaimLedger.AddForWork(rows, pawn, destination, portion.Entry.Subject.def,
                    units, owner);
                if (!owner.Live(pawn) || ResourceQueriesBlocked(ticket)
                    || !final.Current || !snapshot.Current) return;
                HaulersDreamGameComponent.SetStorageClaims(rows);
                // Recollect before another replacement; callbacks may change physical surplus.
                snapshot = ObserveResourceResponsibilitiesForLoad(map, ticket);
                if (!snapshot.Current) return;
            }
        }
    }
}
