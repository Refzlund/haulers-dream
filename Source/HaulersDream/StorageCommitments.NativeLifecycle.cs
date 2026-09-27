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
        private sealed class NativeRestoreWork
        {
            internal Pawn Pawn;
            internal Job Job;
            internal JobDriver Driver;
            internal int JobId;
            internal ISlotGroup Group;
            internal Map Map;
            internal IntVec3 Destination;
            internal readonly List<StorageParcelEvidence.Entry> Entries = new List<StorageParcelEvidence.Entry>();
            internal bool Live => ReferenceEquals(Pawn?.Map, Map) && NativeStoragePickup.Applies(Pawn, out Job current)
                && ReferenceEquals(current, Job) && Job.loadID == JobId
                && ReferenceEquals(Pawn.jobs.curDriver, Driver) && Job.targetB.Cell == Destination;
        }

        /// <summary>Restore actual custody and admitted native intent before a new work scan.</summary>
        internal static void RebuildNativeResourceClaimsAfterLoad()
            => RebuildResourceClaimsForLoad(null);

        private static void RebuildResourceClaimsForLoad(LoadRecoveryTicket ticket)
        {
            if (!UnityData.IsInMainThread || ResourceQueriesBlocked(ticket)) return;
            TrimInactiveResourceClaims();
            var loadedMaps = Find.Maps;
            if (loadedMaps == null) return;
            var maps = new List<Map>(loadedMaps);
            maps.Sort((left, right) => left.uniqueID.CompareTo(right.uniqueID));
            var pawns = new List<Pawn>();
            var work = new List<NativeRestoreWork>();
            var carriers = new List<Pawn>();
            var component = HaulersDreamGameComponent.Instance;
            bool hasAdmittedReceipts = component != null && component.NativeStorageIntentVersion >= 1;
            var receipts = component?.NativeStorageIntents;
            foreach (Map map in maps)
            {
                if (map.reservationManager == null || map.mapPawns == null) continue;
                pawns.Clear();
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    if (pawn != null) pawns.Add(pawn);
                pawns.Sort(ByThingId);
                foreach (Pawn pawn in pawns)
                {
                    if (ticket != null && !ticket.Current) return;
                    // Restore ordinary physical hands outside the native-job filter, before
                    // ANY pending native source is re-admitted below.
                    RestoreOrdinaryUnloadHandsForLoad(pawn, ticket);
                    if (pawn.Map == map && ActiveOn(map)) carriers.Add(pawn);
                    if (!GatesVanillaStorage(map) || pawn.Map != map
                        || !NativeStoragePickup.Applies(pawn, out Job job)) continue;
                    ISlotGroup group = BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(job.targetB.Cell));
                    if (group == null) continue;
                    var restored = new NativeRestoreWork { Pawn = pawn, Job = job, JobId = job.loadID,
                        Driver = pawn.jobs.curDriver, Destination = job.targetB.Cell, Group = group, Map = map };
                    StorageParcelEvidence.Collect(pawn, restored.Entries);
                    restored.Entries.RemoveAll(entry => !ReferenceEquals(entry.OwnerJob, job)
                        || entry.Subject?.def?.category != ThingCategory.Item || entry.Units <= 0
                        || !ReferenceEquals(entry.KnownGroup, group)
                        || !entry.Destination.HasValue || entry.Destination.Value != restored.Destination
                        || !map.reservationManager.ReservedBy(entry.Subject, pawn, job));
                    work.Add(restored);
                }
            }

            // Restore every physically owned hand parcel first. An older save has no saved
            // admission amounts, but exact native reservation + actual hand custody is proof
            // of a real delivery. Its responsibility precedes re-admission of unpicked work.
            foreach (var restored in work)
                foreach (var entry in restored.Entries)
                    if ((ticket == null || ticket.Current) && restored.Live && entry.Held)
                        CommitNativeResourceForLoad(restored.Pawn, restored.Job, restored.Group,
                            entry.Subject, entry.Units, ticket);

            // Inventory survived the save too. Publish its exact held surplus before ANY
            // pending native source; no space/unknown destination remains explicit custody.
            foreach (Pawn pawn in carriers)
            {
                if (ticket != null && !ticket.Current) return;
                var snapshot = ObserveResourceResponsibilitiesForLoad(pawn.Map, ticket);
                AdoptResidualCargo(pawn, ref snapshot, ticket);
                var covered = ObserveResourceResponsibilitiesForLoad(pawn.Map, ticket);
                var actual = covered.EntriesFor(pawn);
                if (ticket != null && !ticket.Current) return;
                foreach (var entry in actual)
                    if (entry.Held && covered.Remaining(pawn, entry) > 0)
                        throw new InvalidOperationException("Loaded held cargo could not be represented: "
                            + pawn.thingIDNumber + "/" + entry.Subject.thingIDNumber);
                if (!covered.Current || (ticket != null && !ticket.Current))
                    throw new InvalidOperationException("Storage load custody changed during reconstruction.");
            }

            // New saves retain the actual admitted source amount separately from job.count.
            // An absent or stale record must not turn a reserved 50-unit source admitted for
            // 30 units into a 50-unit promise. Fresh held obligations may have filled the
            // destination, so the saved amount is an upper bound, not saved capacity truth.
            if (hasAdmittedReceipts)
            {
                foreach (var restored in work)
                    foreach (var entry in restored.Entries)
                    {
                        if ((ticket != null && !ticket.Current) || !restored.Live || entry.Held) continue;
                        int pending = SavedNativePendingUnits(receipts, restored.Pawn, restored.Job, entry);
                        if (pending <= 0) continue;
                        var allowance = NativeStoragePickup.ObserveForLoad(restored.Pawn, restored.Job,
                            entry.Subject, pending, ticket, out ISlotGroup observedGroup, out int admitted);
                        if ((ticket != null && !ticket.Current) || !restored.Live
                            || allowance != ResourceAllowance.Observed || admitted <= 0
                            || !ReferenceEquals(observedGroup, restored.Group)) continue;
                        Thing hands = NativeStoragePickup.OwnedHands(restored.Pawn, restored.Job);
                        int held = hands?.def == entry.Subject.def ? NativeStoragePickup.OwnedHandUnits(restored.Pawn, restored.Job) : 0;
                        CommitNativeResourceForLoad(restored.Pawn, restored.Job, restored.Group, entry.Subject,
                            (int)Math.Min(int.MaxValue, (long)held + Math.Min(pending, admitted)), ticket);
                    }
                return;
            }

            // Migration from saves without admitted receipts is a new deterministic proposal,
            // never inferred capacity from a native source reservation. All actual hands are
            // already represented. Preserve native's full trip budget; the pickup hook will
            // re-check any pending source whose load-time observation is deferred/refused.
            foreach (var restored in work)
                foreach (var entry in restored.Entries)
                {
                    if ((ticket != null && !ticket.Current) || !restored.Live || entry.Held) continue;
                    var allowance = NativeStoragePickup.ObserveForLoad(restored.Pawn, restored.Job,
                        entry.Subject, entry.Units, ticket, out ISlotGroup observedGroup, out int admitted);
                    if ((ticket != null && !ticket.Current) || !restored.Live
                        || allowance != ResourceAllowance.Observed || admitted <= 0
                        || !ReferenceEquals(observedGroup, restored.Group)) continue;
                    Thing hands = NativeStoragePickup.OwnedHands(restored.Pawn, restored.Job);
                    int held = hands?.def == entry.Subject.def ? NativeStoragePickup.OwnedHandUnits(restored.Pawn, restored.Job) : 0;
                    CommitNativeResourceForLoad(restored.Pawn, restored.Job, restored.Group, entry.Subject,
                        (int)Math.Min(int.MaxValue, (long)held + admitted), ticket);
                }
        }

        /// <summary>Retire expired actual work/cargo owners without changing legacy or exclusive rows.</summary>
        internal static void TrimInactiveResourceClaims()
        {
            var rows = HaulersDreamGameComponent.storageClaims;
            List<StorageClaimRow> survivors = null;
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                bool expired = row.ExclusiveCellAllocation == null
                    && ((row.WorkOwner is NativeStorageOwner owner && !owner.Live(row.Pawn as Pawn))
                        || (row.WorkOwner is BulkParcelOwner bulk && !bulk.Live(row.Pawn as Pawn)));
                if (expired)
                {
                    if (survivors != null) continue;
                    survivors = new List<StorageClaimRow>(rows.Length - 1);
                    for (int previous = 0; previous < i; previous++) survivors.Add(rows[previous]);
                }
                else survivors?.Add(row);
            }
            if (survivors != null) HaulersDreamGameComponent.SetStorageClaims(survivors.ToArray());
        }
    }
}
