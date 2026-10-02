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
        // Immutable admission/physical receipt. Publishing a successor replaces the exact old
        // token in a new row array, invalidating any observation that captured its predecessor.
        internal sealed class NativeStorageOwner
        {
            internal readonly Job Job;
            internal readonly Thing Hands, Pending;
            internal readonly int HandUnits, PendingUnits;
            private readonly int jobId;
            private readonly JobDriver driver;
            internal NativeStorageOwner(Pawn pawn, Job job, Thing hands, int handUnits,
                Thing pending, int pendingUnits)
            {
                Job = job; jobId = job.loadID; driver = pawn.jobs.curDriver;
                Hands = hands; HandUnits = handUnits; Pending = pending; PendingUnits = pendingUnits;
            }
            internal bool Live(Pawn pawn) => pawn != null && ReferenceEquals(pawn.CurJob, Job)
                && Job.loadID == jobId && ReferenceEquals(pawn.jobs.curDriver, driver);
        }

        internal static NativeStorageOwner CurrentNativeOwner(Pawn pawn, Job job)
        {
            foreach (var row in HaulersDreamGameComponent.storageClaims)
                if (ReferenceEquals(row.Pawn, pawn) && row.WorkOwner is NativeStorageOwner owner
                    && ReferenceEquals(owner.Job, job) && owner.Live(pawn)) return owner;
            return null;
        }

        internal static bool NativeAdmissionCurrent(Pawn pawn, NativeStorageOwner owner, ISlotGroup group, int units)
        {
            if (owner == null) return false;
            foreach (var row in HaulersDreamGameComponent.storageClaims)
                if (ReferenceEquals(row.Pawn, pawn) && ReferenceEquals(row.WorkOwner, owner)
                    && ReferenceEquals(row.Group, group) && row.Units == units) return true;
            return false;
        }

        internal static bool NativeParcels(Pawn pawn, Job job, out Thing hands, out int held,
            out Thing pending, out int unpicked)
        {
            hands = pending = null; held = unpicked = 0;
            foreach (var row in HaulersDreamGameComponent.storageClaims)
            {
                if (!ReferenceEquals(row.Pawn, pawn) || !(row.WorkOwner is NativeStorageOwner owner)
                    || !ReferenceEquals(owner.Job, job) || !owner.Live(pawn)) continue;
                if (OrdinaryUnloadTransit.Owns(pawn.carryTracker?.innerContainer, owner.Hands)
                    && ReferenceEquals(pawn.carryTracker.CarriedThing, owner.Hands))
                { hands = owner.Hands; held = Math.Min(row.Units, Math.Min(owner.HandUnits, hands.stackCount)); }
                pending = owner.Pending;
                unpicked = Math.Min(Math.Max(0, row.Units - held), owner.PendingUnits);
                return true;
            }
            return false;
        }

        internal static void CommitNativeResource(Pawn pawn, Job job, ISlotGroup group, Thing subject, int units)
            => CommitNativeResourceForLoad(pawn, job, group, subject, units, null);

        private static void CommitNativeResourceForLoad(Pawn pawn, Job job, ISlotGroup group, Thing subject,
            int units, LoadRecoveryTicket ticket)
        {
            if (ResourceQueriesBlocked(ticket)) return;
            if (pawn?.CurJob != job || group == null || subject == null || units <= 0
                || resourceTransferDepth > 0) return;
            bool known = NativeParcels(pawn, job, out Thing hands, out int handUnits, out _, out _);
            if (!known)
            {
                hands = NativeStoragePickup.NativeHands(pawn, job);
                handUnits = hands?.stackCount ?? 0;
            }
            if (hands?.def != subject.def) { hands = null; handUnits = 0; }
            handUnits = Math.Min(units, handUnits);
            Thing pending = ReferenceEquals(subject, hands) ? null : subject;
            int pendingUnits = pending == null ? 0 : Math.Min(subject.stackCount, units - handUnits);
            // Unknown original-save hands were established above from native custody. A known
            // receipt stays bounded even if callbacks grew that same physical Thing identity.
            var previous = CurrentNativeOwner(pawn, job);
            var owner = new NativeStorageOwner(pawn, job, hands, handUnits, pending, pendingUnits);
            var rows = new List<StorageClaimRow>();
            foreach (var row in HaulersDreamGameComponent.storageClaims)
                if (!ReferenceEquals(row.WorkOwner, previous) || previous == null) rows.Add(row);
            if (handUnits + pendingUnits > 0)
                rows.Add(new StorageClaimRow(pawn, group, subject.def, handUnits + pendingUnits, workOwner: owner));
            if (ResourceQueriesBlocked(ticket)) return;
            HaulersDreamGameComponent.SetStorageClaims(rows.ToArray());
            Trace("haul-to-cell", pawn, group, subject.def, handUnits + pendingUnits);
        }

        internal static bool SettleNativePickup(Pawn pawn, Job job, JobDriver driver, int jobId, Map map,
            IntVec3 destination, ISlotGroup group, NativeStorageOwner previous, int admitted, Thing hands, int handUnits,
            IReadOnlyList<(Thing thing, int units)> owned, out Exception evidenceFailure)
        {
            // Provider/keep-stock reads precede the authoritative array capture. The barrier is
            // still held, but unrelated legacy writers in callbacks must also be preserved.
            var entries = new List<StorageParcelEvidence.Entry>();
            evidenceFailure = null;
            if (owned.Count > 0)
                try { StorageParcelEvidence.Collect(pawn, entries); }
                catch (Exception error) { entries.Clear(); evidenceFailure = error; }
            // Shelf linking/removal can change during those policy callbacks without changing
            // the job's coordinates. Recheck both group lifetime and this cell's canonical key.
            bool liveGroup = pawn.Map == map && GroupIsLive(group, map)
                && ReferenceEquals(BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(destination)), group);
            bool same = ReferenceEquals(pawn.CurJob, job) && job.loadID == jobId
                && ReferenceEquals(pawn.jobs.curDriver, driver);
            bool currentAdmission = liveGroup && same && pawn.Map == map && job.targetB.IsValid && !job.targetB.HasThing
                && job.targetB.Cell == destination && NativeAdmissionCurrent(pawn, previous, group, admitted);
            // Returning false leaves the captured owner untouched until the caller recovers its
            // exact hands. A second, hands-free settlement can retire it without reviving a
            // superseded admission or publishing any promise at a callback-selected destination.
            if (hands != null && !currentAdmission) return false;
            if (!same || !OrdinaryUnloadTransit.Owns(pawn.carryTracker?.innerContainer, hands)
                || !ReferenceEquals(pawn.carryTracker.CarriedThing, hands)) { hands = null; handUnits = 0; }
            else handUnits = Math.Min(handUnits, hands.stackCount);

            var current = HaulersDreamGameComponent.storageClaims;
            var rows = new List<StorageClaimRow>();
            foreach (var row in current)
                if (previous == null || !ReferenceEquals(row.WorkOwner, previous)) rows.Add(row);
            int remaining = currentAdmission ? admitted : 0;
            handUnits = Math.Min(handUnits, remaining);
            if (liveGroup && hands != null && handUnits > 0)
                rows.Add(new StorageClaimRow(pawn, group, hands.def, handUnits,
                    workOwner: new NativeStorageOwner(pawn, job, hands, handUnits, null, 0)));
            remaining -= handUnits;
            if (liveGroup && currentAdmission)
                foreach (var parcel in owned)
                {
                    if (!OrdinaryUnloadTransit.Owns(pawn.inventory?.innerContainer, parcel.thing)) continue;
                    foreach (var entry in entries)
                    {
                        if (!entry.Held || !ReferenceEquals(entry.Subject, parcel.thing)) continue;
                        int units = Math.Min(remaining, Math.Min(parcel.units, entry.Units));
                        if (units > 0) rows.Add(new StorageClaimRow(pawn, group, parcel.thing.def, units,
                            workOwner: new HeldCargoOwner(pawn, entry)));
                        remaining -= units;
                        break;
                    }
                }
            if (!ReferenceEquals(current, HaulersDreamGameComponent.storageClaims))
                throw new InvalidOperationException("Native pickup responsibility changed during publication.");
            // Deliberately publish zero settlement too: no reserved source remainder can revive
            // the attempted admission merely because native error cleanup has not run yet.
            HaulersDreamGameComponent.SetStorageClaims(rows.ToArray());
            // A surplus/provider error cannot prevent publication of already witnessed hands.
            // Its inventory remains tagged but unallocated, and the caller preserves the primary
            // pickup error while native cleanup handles the failed active operation.
            return true;
        }
    }
}
