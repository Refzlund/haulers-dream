using System;
using System.Collections.Generic;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    internal static partial class StorageCommitments
    {
        // One actual queue responsibility, or one witnessed physical inventory receipt. Distinct
        // same-def parcels retain distinct destinations in the existing authoritative ledger.
        private sealed class BulkParcelOwner
        {
            internal readonly Thing Subject;
            internal readonly Job Job;
            internal readonly JobDriver_BulkHaul Driver;
            internal readonly int Index;
            private readonly int jobId;
            internal bool Held => Job == null;

            internal BulkParcelOwner(Thing subject, JobDriver_BulkHaul driver = null, int index = -1)
            {
                Subject = subject; Driver = driver; Job = driver?.job; Index = index;
                jobId = Job?.loadID ?? -1;
            }

            internal bool Live(Pawn pawn)
            {
                if (pawn?.Map == null || Subject == null || Subject.Destroyed || Subject.stackCount <= 0)
                    return false;
                if (Held)
                    return !Subject.Spawned && ReferenceEquals(Subject.holdingOwner, pawn.inventory?.innerContainer)
                        && pawn.GetComp<CompHauledToInventory>()?.PeekHashSet().Contains(Subject) == true;
                return ReferenceEquals(pawn.CurJob, Job) && jobId == Job.loadID
                    && ReferenceEquals(pawn.jobs.curDriver, Driver) && Driver.StoragePendingStartIndex <= Index
                    && Job.targetQueueB != null && Index >= 0 && Index < Job.targetQueueB.Count
                    && ReferenceEquals(Job.targetQueueB[Index].Thing, Subject)
                    && Job.countQueue != null && Index < Job.countQueue.Count && Job.countQueue[Index] > 0
                    && Subject.Spawned && Subject.Map == pawn.Map
                    && pawn.Map.reservationManager.ReservedBy(Subject, pawn, Job);
            }
        }

        internal static bool AdmitBulkParcel(JobDriver_BulkHaul driver, int index, Thing subject,
            int wanted, out int admitted, bool prioritizeOriginal = false)
        {
            admitted = 0;
            Pawn pawn = driver?.pawn;
            Job job = driver?.job;
            if (pawn?.Map == null || subject == null || wanted <= 0
                || !ReferenceEquals(pawn.CurJob, job) || !ReferenceEquals(pawn.jobs.curDriver, driver)) return false;
            // Pick-up-to-inventory is an explicit command with no storage precondition. Corpses
            // and true containers retain their separate native enroute policy.
            if (job.takeInventoryDelay > 0 || subject.def.category != ThingCategory.Item)
            { admitted = wanted; return true; }
            var owner = FindBulkOwner(pawn, driver, index, subject) ?? new BulkParcelOwner(subject, driver, index);
            if (!owner.Live(pawn)) return false;
            bool priorityTransfer = prioritizeOriginal && MayPrioritizeBulk(driver, index, subject);
            var raw = priorityTransfer ? new ForcedRawState() : null;
            if (raw != null) { raw.Map(pawn.Map); raw.Pawn(pawn); raw.Job(job); raw.Thing(subject); }
            var activation = priorityTransfer ? ForcedActivationGuard(pawn, job) : null;
            var queueGuard = priorityTransfer ? ForcedQueueGuard(job) : null;
            var sourceGuard = priorityTransfer ? new ProjectionThingGuard(subject) : null;
            var priority = StoreUtility.CurrentStoragePriorityOf(subject);
            using (priorityTransfer ? SuppressOwnGateForProjection() : null)
            using (StorageBuildingFilter.PushContext(StorageFilterContext.Unload))
            {
                if (!StoreUtility.TryFindBestBetterStorageFor(subject, pawn, pawn.Map, priority,
                    pawn.Faction, out IntVec3 cell, out _, needAccurateResult: false)) return false;
                if (!cell.IsValid) { admitted = wanted; return true; }
                var group = BulkHaul.BudgetGroupOf(pawn.Map.haulDestinationManager.SlotGroupAt(cell));
                if (group == null) return false;
                var request = new StorageAllocationObservationDemand(owner, owner, subject, pawn,
                    Math.Min(wanted, subject.stackCount), StorageFilterContext.Unload, ownJob: job,
                    priorityFloor: priority, requireBetterPriority: true, startsRefill: true);
                int allowed;
                ForcedStorageProposal proposal = null;
                var status = priorityTransfer
                    ? ForcedStorageUnitsFor(pawn, group, request, request, null, new[] { cell }, out allowed, out proposal)
                    : ResourceUnitsFor(pawn, group, request, null, new[] { cell }, out allowed);
                if (status != ResourceAllowance.Observed || allowed <= 0 || !owner.Live(pawn)) return false;
                admitted = Math.Min(wanted, allowed);
                if (priorityTransfer)
                {
                    if (!activation() || !queueGuard() || !sourceGuard.Matches() || proposal == null) return false;
                    proposal.Guards.Add(activation); proposal.Guards.Add(queueGuard); proposal.Guards.Add(sourceGuard.Matches);
                    proposal.IncomingCurrent = () => activation() && queueGuard() && sourceGuard.Matches()
                        && ReferenceEquals(BulkHaul.BudgetGroupOf(pawn.Map.haulDestinationManager.SlotGroupAt(cell)), group);
                    proposal.NotAlreadyAdmitted = () => FindBulkOwner(pawn, driver, index, subject) == null;
                    proposal.IncomingRaw = raw;
                    return ApplyForcedStorage(proposal, pawn, job, driver, group, subject, admitted, owner);
                }
                HaulersDreamGameComponent.SetStorageClaims(StorageClaimLedger.AddForWork(
                    HaulersDreamGameComponent.storageClaims, pawn, group, subject.def, admitted, owner));
                return true;
            }
        }

        private static BulkParcelOwner FindBulkOwner(Pawn pawn, JobDriver_BulkHaul driver, int index, Thing subject)
        {
            foreach (var row in HaulersDreamGameComponent.storageClaims)
                if (ReferenceEquals(row.Pawn, pawn) && row.WorkOwner is BulkParcelOwner owner && !owner.Held
                    && ReferenceEquals(owner.Driver, driver) && owner.Index == index
                    && ReferenceEquals(owner.Subject, subject) && owner.Live(pawn)) return owner;
            return null;
        }

        internal static BulkResourceTransfer BeginBulkTransfer(JobDriver_BulkHaul driver, int index, Thing source,
            int requested, Action<Thing, int> recordCargo)
        {
            var owner = FindBulkOwner(driver.pawn, driver, index, source);
            foreach (var row in HaulersDreamGameComponent.storageClaims)
                if (owner != null && ReferenceEquals(row.WorkOwner, owner))
                    return new BulkResourceTransfer(driver.pawn, source, requested, row, recordCargo);
            return new BulkResourceTransfer(driver.pawn, source, requested, default, recordCargo);
        }

        internal sealed class BulkResourceTransfer : IDisposable
        {
            [ThreadStatic] internal static BulkResourceTransfer Current;
            private readonly BulkResourceTransfer previous;
            private readonly Pawn pawn;
            private readonly Thing source;
            private readonly int requested;
            private bool splitEntered;
            private readonly List<Thing> fragments = new List<Thing>();
            private readonly StorageClaimRow pending;
            private readonly IDisposable barrier;
            private readonly List<Thing> recipients = new List<Thing>();
            private readonly List<int> received = new List<int>();
            private readonly Action<Thing, int> recordCargo;
            private bool disposed;
            internal bool MovedAnything { get; private set; }

            internal BulkResourceTransfer(Pawn pawn, Thing source, int requested,
                StorageClaimRow pending, Action<Thing, int> recordCargo)
            {
                this.pawn = pawn; this.source = source; this.requested = requested;
                this.pending = pending; this.recordCargo = recordCargo;
                previous = Current; Current = this;
                barrier = BeginResourceTransfer();
            }

            internal bool AdmitSplit(Thing item, int count)
            {
                if (item == null || count <= 0 || count > item.stackCount) return false;
                if (ReferenceEquals(item, source))
                {
                    if (splitEntered || count != requested) return false;
                    splitEntered = true;
                    // A partial pickup does not own the original remainder. Only a whole-source
                    // split can authorize recovery of the original itself if Despawn throws.
                    if (count == item.stackCount) RecordSplit(item);
                    return true;
                }
                return fragments.Contains(item);
            }

            internal void RecordSplit(Thing fragment)
            {
                if (fragment != null && !fragments.Contains(fragment)) fragments.Add(fragment);
            }

            internal static void PreserveFailure(ref Exception primary, Exception recovery, string operation)
            {
                if (primary == null) primary = recovery;
                else primary.Data["HaulersDream.BulkPickup." + operation] = recovery.ToString();
            }

            internal void RetainDetached(ref Exception primary)
            {
                // Native/provider callbacks can split another known fragment while it is being
                // retained; inspect newly captured descendants without invalidating an enumerator.
                for (int i = 0; i < fragments.Count; i++)
                {
                    var fragment = fragments[i];
                    if (fragment == null || fragment.Destroyed || fragment.Spawned || fragment.holdingOwner != null) continue;
                    try
                    {
                        var inventory = pawn.inventory?.innerContainer;
                        // Never retry the failed merge or adopt another same-def item. Keep only
                        // this pickup's exact surviving split, then let ordinary unload recover it.
                        if (inventory == null || (!Add(fragment, inventory) && !InInventory(fragment)))
                            throw new InvalidOperationException("Could not retain the exact bulk-pickup fragment.");
                    }
                    catch (Exception recovery)
                    { PreserveFailure(ref primary, recovery, "remainder." + fragment.thingIDNumber); }
                }
            }

            internal void Absorb(Thing source, Thing recipient)
            {
                int sourceBefore = source.stackCount;
                int targetBefore = InInventory(recipient) ? recipient.stackCount : 0;
                try { recipient.TryAbsorbStack(source, respectStackLimit: true); }
                finally
                {
                    int removed = sourceBefore - (source.Destroyed ? 0 : source.stackCount);
                    int added = InInventory(recipient) ? recipient.stackCount - targetBefore : 0;
                    Record(recipient, Math.Max(0, Math.Min(removed, added)));
                }
            }

            internal bool Add(Thing source, ThingOwner inventory)
            {
                int before = source.stackCount;
                bool alreadyHeld = InInventory(source);
                try { return inventory.TryAdd(source, canMergeWithExistingStacks: false); }
                finally
                {
                    // Exact object custody, not TryAdd's bool, proves a plain inventory transfer.
                    if (!alreadyHeld && InInventory(source)) Record(source, Math.Min(before, source.stackCount));
                }
            }

            private void Record(Thing recipient, int moved)
            {
                if (moved <= 0) return;
                int index = recipients.IndexOf(recipient);
                if (index < 0) { recipients.Add(recipient); received.Add(moved); }
                else received[index] = DestinationEnroutePolicy.SaturatingAdd(received[index], moved);
                MovedAnything = true;
                // Bind the explicit nearby manifest before tag/provider notifications can throw.
                recordCargo?.Invoke(recipient, moved);
            }

            private bool InInventory(Thing thing) => thing != null && !thing.Destroyed && !thing.Spawned
                && ReferenceEquals(thing.holdingOwner, pawn.inventory?.innerContainer);

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    var rows = new List<StorageClaimRow>();
                    foreach (var row in HaulersDreamGameComponent.storageClaims)
                        if (pending.WorkOwner == null || !ReferenceEquals(row.WorkOwner, pending.WorkOwner)) rows.Add(row);
                    int remaining = pending.Units;
                    var comp = pawn.GetComp<CompHauledToInventory>();
                    var repairTags = new List<(Thing target, int units)>();
                    for (int i = 0; i < recipients.Count; i++)
                    {
                        Thing target = recipients[i];
                        int moved = InInventory(target) ? Math.Min(target.stackCount, received[i]) : 0;
                        if (moved <= 0) continue;
                        MovedAnything = true;
                        // A callback may have thrown after movement but before the normal tag call.
                        // Recover only the exact observed recipient, never arbitrary same-def stock.
                        if (comp != null && !comp.PeekHashSet().Contains(target)) repairTags.Add((target, moved));
                        int units = Math.Min(remaining, moved);
                        if (units <= 0 || pending.Group == null) continue;
                        rows.Add(new StorageClaimRow(pawn, pending.Group, target.def, units,
                            workOwner: new BulkParcelOwner(target)));
                        remaining -= units;
                    }
                    if (pending.WorkOwner != null) HaulersDreamGameComponent.SetStorageClaims(rows.ToArray());
                    // Publish every physical receipt before notifying providers. Each registration
                    // tags its recipient before notifying CE. One notification failure must not skip
                    // the other exact recipients; keep the barrier until all repairs were attempted.
                    Exception repairFailure = null;
                    foreach (var repair in repairTags)
                    {
                        if (!InInventory(repair.target)) continue;
                        try { comp.RegisterHauledItem(repair.target, repair.units); }
                        catch (Exception failure)
                        { PreserveFailure(ref repairFailure, failure, "tag." + repair.target.thingIDNumber); }
                    }
                    if (repairFailure != null)
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(repairFailure).Throw();
                }
                finally { Current = previous; barrier.Dispose(); }
            }
        }
    }

    // The base result exists before ThingWithComps.PostSplitOff can throw. The active scope
    // admits only this exact pickup and its descendants, following the quantity-drop precedent.
    [HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    internal static class Patch_BulkStorageSplitReceipt
    {
        static void Prefix(Thing __instance, int count, out StorageCommitments.BulkResourceTransfer __state)
        {
            var scope = StorageCommitments.BulkResourceTransfer.Current;
            __state = scope != null && scope.AdmitSplit(__instance, count) ? scope : null;
        }
        static void Postfix(Thing __result, StorageCommitments.BulkResourceTransfer __state)
        { __state?.RecordSplit(__result); }
    }
}
