using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>Admission at native's real pickup boundary; job.count remains its trip budget.</summary>
    internal static class NativeStoragePickup
    {
        internal static bool Applies(Pawn pawn, out Job job)
        {
            job = pawn?.CurJob;
            var driver = pawn?.jobs?.curDriver;
            return StorageCommitments.GatesVanillaStorage(pawn?.Map)
                && job?.def == JobDefOf.HaulToCell && job.haulMode == HaulMode.ToCellStorage
                && driver?.GetType() == typeof(JobDriver_HaulToCell)
                && ReferenceEquals(driver.job, job) && ReferenceEquals(driver.pawn, pawn)
                && job.targetB.IsValid && !job.targetB.HasThing
                // An unsupported provider retained native's exclusive destination reservation.
                && !pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, job);
        }

        internal static Thing NativeHands(Pawn pawn, Job job)
        {
            Thing held = pawn.carryTracker?.CarriedThing;
            return held != null && !held.Destroyed && held.stackCount > 0
                && ReferenceEquals(held.holdingOwner, pawn.carryTracker.innerContainer)
                && (ReferenceEquals(job.targetA.Thing, held)
                    || pawn.Map.reservationManager.ReservedBy(held, pawn, job)) ? held : null;
        }

        internal static Thing OwnedHands(Pawn pawn, Job job)
        {
            if (StorageCommitments.NativeParcels(pawn, job, out Thing held, out int units, out _, out _))
                return units > 0 ? held : null;
            return NativeHands(pawn, job); // Actual native custody on an original save before restoration.
        }
        internal static int OwnedHandUnits(Pawn pawn, Job job)
        {
            if (StorageCommitments.NativeParcels(pawn, job, out _, out int units, out _, out _)) return units;
            return NativeHands(pawn, job)?.stackCount ?? 0;
        }

        internal static StorageCommitments.ResourceAllowance Observe(Pawn pawn, Job job, Thing source,
            int requested, out ISlotGroup group, out int allowed)
            => ObserveForLoad(pawn, job, source, requested, null, out group, out allowed);

        internal static StorageCommitments.ResourceAllowance ObserveForLoad(Pawn pawn, Job job, Thing source,
            int requested, StorageCommitments.LoadRecoveryTicket ticket, out ISlotGroup group, out int allowed)
        {
            allowed = 0;
            group = null;
            if (!UnityData.IsInMainThread) return StorageCommitments.ResourceAllowance.Deferred;
            if (StorageCommitments.ResourceQueriesBlocked(ticket)) return StorageCommitments.ResourceAllowance.Deferred;
            group = BulkHaul.BudgetGroupOf(pawn.Map.haulDestinationManager.SlotGroupAt(job.targetB.Cell));
            if (source == null || source.Destroyed || source.stackCount <= 0 || requested <= 0)
                return StorageCommitments.ResourceAllowance.Deferred;
            Thing held = OwnedHands(pawn, job);
            if (held != null && ReferenceEquals(source, held))
                return StorageCommitments.ResourceAllowance.Deferred;
            var overlays = new List<StorageAllocationObservationDemand>();
            if (held != null)
                overlays.Add(new StorageAllocationObservationDemand(job, held, held, pawn, OwnedHandUnits(pawn, job),
                    StorageFilterContext.Unload, job.targetB.Cell, job, startsRefill: false));
            // Explicitly name both actual parcels. Merely excluding source from a def-wide row
            // can consume its old amount before reaching the hands when source has the lower ID.
            var request = new StorageAllocationObservationDemand(job, source, source, pawn,
                Math.Min(requested, source.stackCount), StorageFilterContext.Unload,
                job.targetB.Cell, job, startsRefill: held == null);
            return StorageCommitments.ResourceUnitsForLoad(pawn, group, request, overlays, null, ticket, out allowed);
        }

        internal static void DeliverOwnedHands(Pawn pawn, Job job, Thing rejectedSource)
        {
            Thing held = OwnedHands(pawn, job);
            if (held == null)
            {
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                return;
            }
            // The original StartCarryThing action must not see a refused duplicate as a zero
            // pickup: native would end the whole job and drop the previously admitted cargo.
            // This toil now proceeds to the native post-carry label and ordinary delivery.
            job.targetA = held;
            job.count = 0;
            if (rejectedSource != null && !ReferenceEquals(rejectedSource, held)
                && pawn.Map.reservationManager.ReservedBy(rejectedSource, pawn, job))
                pawn.Map.reservationManager.Release(rejectedSource, pawn, job);
            ISlotGroup group = BulkHaul.BudgetGroupOf(pawn.Map.haulDestinationManager.SlotGroupAt(job.targetB.Cell));
            StorageCommitments.CommitNativeResource(pawn, job, group, held, OwnedHandUnits(pawn, job));
        }

        internal static bool Current(Pawn pawn, Job job, JobDriver driver, int jobId)
            => ReferenceEquals(pawn?.CurJob, job) && job?.loadID == jobId
                && ReferenceEquals(pawn?.jobs?.curDriver, driver);

        // This is control flow, not a gameplay failure. It crosses the unfinished native
        // StartCarryThing action so that action cannot EndCurrentJob on the replacement.
        private sealed class AbandonedPickup : Exception
        {
            internal readonly Pawn Pawn;
            internal readonly Job Job;
            internal readonly JobDriver Driver;
            internal readonly int JobId;
            internal AbandonedPickup(Pawn pawn, Job job, JobDriver driver, int jobId)
            { Pawn = pawn; Job = job; Driver = driver; JobId = jobId; }
        }
        internal static Exception Aborted(Pawn pawn, Job job, JobDriver driver, int jobId)
            => new AbandonedPickup(pawn, job, driver, jobId);
        internal static bool IsBenignAbort(Exception error)
            => error is AbandonedPickup abort && error.Data.Count == 0
                && !Current(abort.Pawn, abort.Job, abort.Driver, abort.JobId);
        internal static bool IsAbort(Exception error, Pawn pawn, Job job, JobDriver driver, int jobId)
            => IsBenignAbort(error) && error is AbandonedPickup abort && ReferenceEquals(abort.Pawn, pawn)
                && ReferenceEquals(abort.Job, job) && ReferenceEquals(abort.Driver, driver) && abort.JobId == jobId;

        internal static void ReportAbandoned(Exception failure)
        {
            string message = "Native storage pickup failed after its activation was replaced; kept replacement work. Original failure:\n";
            try
            {
                message += HDFault.Render(failure);
                foreach (DictionaryEntry entry in failure.Data)
                    if (entry.Key is string key && key.StartsWith("HaulersDream.NativePickup.", StringComparison.Ordinal))
                        message += "\n" + key + ": " + (entry.Value is Exception error ? HDFault.Render(error) : entry.Value);
                HDLog.Err(message);
            }
            catch (Exception reportFailure)
            {
                try { HDDebugLog.Enqueue("ERR [native-pickup] " + message + "\nReporting also failed: " + HDFault.Render(reportFailure)); }
                catch { /* Reporting an abandoned operation must not end its replacement. */ }
            }
        }

        internal sealed class Pickup : StorageSplitScope
        {
            private readonly Pawn pawn;
            private readonly Job job;
            private readonly JobDriver driver;
            private readonly int jobId, admitted;
            private readonly Map map;
            private readonly IntVec3 destination;
            private readonly ISlotGroup group;
            private readonly ThingOwner hands;
            private readonly StorageCommitments.NativeStorageOwner owner;
            private readonly Dictionary<Thing, int> quantities = new Dictionary<Thing, int>();
            private readonly HashSet<Thing> handReceipts = new HashSet<Thing>();

            internal Pickup(Pawn pawn, Job job, ISlotGroup group, Thing source, int count,
                Thing priorHands, int priorUnits) : base(source, count)
            {
                this.pawn = pawn; this.job = job; this.group = group;
                driver = pawn.jobs.curDriver; jobId = job.loadID; map = pawn.Map;
                destination = job.targetB.Cell; hands = pawn.carryTracker.innerContainer;
                owner = StorageCommitments.CurrentNativeOwner(pawn, job);
                admitted = (int)Math.Min(int.MaxValue, (long)count + priorUnits);
                quantities[source] = count;
                if (priorHands != null && priorUnits > 0)
                { quantities[priorHands] = priorUnits; Record(priorHands); handReceipts.Add(priorHands); }
            }
            private int Raw(Thing thing) => thing != null && quantities.TryGetValue(thing, out int units) ? units : 0;
            private int Units(Thing thing) => thing == null || thing.Destroyed ? 0 : Math.Min(Raw(thing), thing.stackCount);
            private bool Same => NativeStoragePickup.Current(pawn, job, driver, jobId);
            private bool AdmissionCurrent => Same && pawn.Map == map && job.targetB.IsValid
                && !job.targetB.HasThing && job.targetB.Cell == destination
                && StorageCommitments.NativeAdmissionCurrent(pawn, owner, group, admitted);

            internal override void SplitReturned(Thing item, int before, int count, Thing result)
            {
                base.SplitReturned(item, before, count, result);
                if (result == null || ReferenceEquals(item, result)) return;
                int available = Math.Min(Raw(item), before);
                int moved = Math.Min(available, Math.Min(count, result.stackCount));
                quantities[item] = available - moved;
                quantities[result] = Raw(result) + moved;
            }
            internal void Inserted(ThingOwner container, Thing item)
            {
                if (!ReferenceEquals(container, hands) || !Contains(item) || Units(item) <= 0
                    || !OrdinaryUnloadTransit.Owns(hands, item)) return;
                handReceipts.Add(item);
            }
            internal void Merged(Thing recipient, Thing source, int count)
            {
                if (count <= 0 || !Contains(source)) return;
                int moved = Math.Min(Raw(source), count);
                if (moved <= 0) return;
                quantities[source] = Raw(source) - moved;
                bool carrying = OrdinaryUnloadTransit.Owns(hands, recipient);
                // An actual outgoing merge into foreign custody consumes this fragment's
                // receipt, but never licenses recovery of the foreign recipient.
                if (!carrying && !Contains(recipient)) return;
                quantities[recipient] = (int)Math.Min(int.MaxValue, (long)Raw(recipient) + moved);
                Record(recipient);
                if (carrying) handReceipts.Add(recipient);
            }
            private Thing ProvenHands()
            {
                Thing held = pawn.carryTracker?.CarriedThing;
                return handReceipts.Contains(held) && Units(held) > 0
                    && OrdinaryUnloadTransit.Owns(hands, held) ? held : null;
            }
            private static void Preserve(ref Exception primary, Exception secondary, string operation)
            {
                if (primary == null) primary = secondary;
                else
                {
                    string key = "HaulersDream.NativePickup." + operation;
                    string available = key;
                    for (int n = 2; primary.Data.Contains(available); n++) available = key + "." + n;
                    primary.Data[available] = secondary;
                }
            }
            private bool ReplacementOwns(Thing held)
            {
                Job current = pawn.CurJob;
                if (current == null || Same) return false;
                int currentId = current.loadID;
                JobDriver currentDriver = pawn.jobs.curDriver;
                if (ReferenceEquals(current.targetA.Thing, held) || ReferenceEquals(current.targetB.Thing, held)
                    || ReferenceEquals(current.targetC.Thing, held)
                    || pawn.Map?.reservationManager?.ReservedBy(held, pawn, current) == true) return true;
                var parcels = new List<StorageParcelEvidence.Entry>();
                StorageParcelEvidence.Collect(pawn, parcels);
                foreach (var parcel in parcels)
                    if (parcel.Held && parcel.Units > 0 && ReferenceEquals(parcel.Subject, held)
                        && ReferenceEquals(parcel.OwnerJob, current)) return true;
                return !NativeStoragePickup.Current(pawn, current, currentDriver, currentId);
            }
            private Thing BoundedPiece(Thing thing)
            {
                int units = Units(thing);
                return units <= 0 ? null : units == thing.stackCount ? thing : thing.SplitOff(units);
            }
            private void ReturnAbandonedHands(ref Exception failure)
            {
                Thing held = ProvenHands();
                if (held == null) return;
                try
                {
                    if (ReplacementOwns(held)) return;
                    if (!ReferenceEquals(ProvenHands(), held)) return;
                    Thing piece = BoundedPiece(held);
                    if (piece == null) return;
                    if (OrdinaryUnloadTransit.Owns(hands, piece))
                        hands.TryTransferToContainer(piece, pawn.inventory.innerContainer, Units(piece), out _,
                            canMergeWithExistingStacks: false);
                    // A bounded partial split may be detached. RetainDetached handles it below.
                }
                catch (Exception error) { Preserve(ref failure, error, "return-hands"); }
            }
            private void RetainDetached(ref Exception failure)
            {
                for (int i = 0; i < Fragments.Count; i++)
                {
                    Thing fragment = Fragments[i];
                    if (Units(fragment) <= 0 || fragment.Spawned || fragment.holdingOwner != null) continue;
                    try
                    {
                        Thing piece = BoundedPiece(fragment);
                        if (piece == null || piece.Spawned || piece.holdingOwner != null) continue;
                        var inventory = pawn.inventory?.innerContainer;
                        if (inventory == null || (!inventory.TryAdd(piece, canMergeWithExistingStacks: false)
                            && !OrdinaryUnloadTransit.Owns(inventory, piece)))
                            throw new InvalidOperationException("Could not retain the exact native-pickup fragment.");
                    }
                    catch (Exception error) { Preserve(ref failure, error, "fragment." + fragment.thingIDNumber); }
                }
            }
            private List<(Thing thing, int units)> OwnedInventory(ref Exception failure)
            {
                var owned = new List<(Thing thing, int units)>();
                var comp = pawn.GetComp<CompHauledToInventory>();
                for (int i = 0; i < Fragments.Count; i++)
                {
                    Thing fragment = Fragments[i];
                    int units = Units(fragment);
                    if (units <= 0 || !OrdinaryUnloadTransit.Owns(pawn.inventory?.innerContainer, fragment)) continue;
                    // Recovery is no-merge. Only a fully receipt-owned piece receives a new tag;
                    // mixed foreign growth is split above before it is moved into inventory.
                    if (units != fragment.stackCount)
                    {
                        Preserve(ref failure, new InvalidOperationException(
                            "Native pickup recovery found unrelated growth in the retained inventory parcel; left it untouched."),
                            "mixed-inventory." + fragment.thingIDNumber);
                        continue;
                    }
                    try { comp?.RegisterHauledItem(fragment); }
                    catch (Exception error) { Preserve(ref failure, error, "tag." + fragment.thingIDNumber); }
                    owned.Add((fragment, units));
                }
                return owned;
            }
            internal Exception Finish(Exception failure)
            {
                try
                {
                    if (!AdmissionCurrent)
                    {
                        if (Same) Preserve(ref failure, new InvalidOperationException(
                            "Native pickup admission or destination changed during transfer."), "admission");
                        ReturnAbandonedHands(ref failure);
                    }
                    RetainDetached(ref failure);
                    var owned = OwnedInventory(ref failure);
                    Thing held = ProvenHands();
                    Exception evidenceFailure = null;
                    bool settled;
                    try
                    {
                        settled = StorageCommitments.SettleNativePickup(pawn, job, driver, jobId, map,
                            destination, group, owner, admitted, held, Units(held), owned, out evidenceFailure);
                    }
                    finally { if (evidenceFailure != null) Preserve(ref failure, evidenceFailure, "evidence"); }
                    if (!settled)
                    {
                        if (Same) Preserve(ref failure, new InvalidOperationException(
                            "Native pickup ownership was superseded before settlement."), "settlement");
                        ReturnAbandonedHands(ref failure);
                        RetainDetached(ref failure);
                        owned = OwnedInventory(ref failure);
                        evidenceFailure = null;
                        try
                        {
                            StorageCommitments.SettleNativePickup(pawn, job, driver, jobId, map,
                                destination, group, owner, admitted, null, 0, owned, out evidenceFailure);
                        }
                        finally { if (evidenceFailure != null) Preserve(ref failure, evidenceFailure, "recovery-evidence"); }
                    }
                }
                catch (Exception error) { Preserve(ref failure, error, "reconcile"); }
                finally
                {
                    try { Dispose(); }
                    catch (Exception error) { Preserve(ref failure, error, "barrier"); }
                }
                return failure ?? (!Same ? Aborted(pawn, job, driver, jobId) : null);
            }
        }
    }

    [HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.CheckForGetOpportunityDuplicate))]
    public static class Patch_NativeStorageDuplicate_Admission
    {
        static void Prefix(Toil getHaulTargetToil, ref Predicate<Thing> extraValidator)
        {
            Predicate<Thing> original = extraValidator;
            extraValidator = source =>
            {
                if (original != null && !original(source)) return false;
                Pawn pawn = getHaulTargetToil.actor;
                if (!NativeStoragePickup.Applies(pawn, out Job job)) return true;
                return NativeStoragePickup.Observe(pawn, job, source, job.count,
                    out _, out int allowed) == StorageCommitments.ResourceAllowance.Observed && allowed > 0;
            };
        }
    }

    [HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.StartCarryThing))]
    public static class Patch_NativeStorageCarryToil_Admission
    {
        static void Postfix(TargetIndex haulableInd, bool subtractNumTakenFromJobCount, Toil __result)
        {
            if (haulableInd != TargetIndex.A || !subtractNumTakenFromJobCount || __result == null) return;
            Action original = __result.initAction;
            if (original == null) return;
            Toil toil = __result;
            toil.initAction = () =>
            {
                Pawn pawn = toil.actor;
                Job captured = pawn?.CurJob;
                JobDriver driver = pawn?.jobs?.curDriver;
                int jobId = captured?.loadID ?? -1;
                bool native = NativeStoragePickup.Applies(pawn, out Job job);
                try
                {
                    if (native)
                    {
                        Thing source = job.targetA.Thing;
                        if (NativeStoragePickup.Observe(pawn, job, source, job.count,
                            out _, out int allowed) != StorageCommitments.ResourceAllowance.Observed || allowed <= 0)
                        {
                            if (NativeStoragePickup.Current(pawn, captured, driver, jobId))
                                NativeStoragePickup.DeliverOwnedHands(pawn, job, source);
                            return;
                        }
                        if (!NativeStoragePickup.Current(pawn, captured, driver, jobId)) return;
                    }
                    original();
                }
                catch (Exception failure)
                {
                    if (!native || NativeStoragePickup.Current(pawn, captured, driver, jobId)) throw;
                    if (NativeStoragePickup.IsAbort(failure, pawn, captured, driver, jobId)) return;
                    NativeStoragePickup.ReportAbandoned(failure);
                }
            };
        }
    }

    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry),
        typeof(Thing), typeof(int), typeof(bool))]
    public static class Patch_NativeStorageActualPickup_Admission
    {
        // Other pickup prefixes (including HD's corpse stripping) may change capacity. The
        // final admission belongs after those callbacks and immediately before native transfer.
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(Pawn_CarryTracker __instance, Thing item, ref int count, ref int __result,
            out NativeStoragePickup.Pickup __state)
        {
            __state = null;
            Pawn pawn = __instance.pawn;
            if (!NativeStoragePickup.Applies(pawn, out Job job)
                || !ReferenceEquals(job.targetA.Thing, item)) return true;
            JobDriver driver = pawn.jobs.curDriver;
            int jobId = job.loadID;
            Map map = pawn.Map;
            IntVec3 destination = job.targetB.Cell;
            bool reserved = pawn.Map.reservationManager.ReservedBy(item, pawn, job);
            if (!NativeStoragePickup.Current(pawn, job, driver, jobId))
                throw NativeStoragePickup.Aborted(pawn, job, driver, jobId);
            if (!reserved)
            {
                __result = 0;
                return false;
            }
            var allowance = NativeStoragePickup.Observe(pawn, job, item, count,
                out ISlotGroup group, out int allowed);
            if (!NativeStoragePickup.Current(pawn, job, driver, jobId))
                throw NativeStoragePickup.Aborted(pawn, job, driver, jobId);
            if (!ReferenceEquals(pawn.Map, map) || !job.targetB.IsValid || job.targetB.HasThing
                || job.targetB.Cell != destination || !ReferenceEquals(job.targetA.Thing, item)
                || !pawn.Map.reservationManager.ReservedBy(item, pawn, job))
                throw new InvalidOperationException("Native pickup activation changed during admission.");
            if (allowance != StorageCommitments.ResourceAllowance.Observed || allowed <= 0)
            { __result = 0; return false; }
            count = Math.Min(count, allowed);
            Thing held = NativeStoragePickup.OwnedHands(pawn, job);
            int priorUnits = NativeStoragePickup.OwnedHandUnits(pawn, job);
            StorageCommitments.CommitNativeResource(pawn, job, group, item,
                (int)Math.Min(int.MaxValue, (long)priorUnits + count));
            if (!NativeStoragePickup.Current(pawn, job, driver, jobId))
                throw NativeStoragePickup.Aborted(pawn, job, driver, jobId);
            if (!ReferenceEquals(pawn.Map, map) || !job.targetB.IsValid || job.targetB.HasThing
                || job.targetB.Cell != destination || !ReferenceEquals(job.targetA.Thing, item))
                throw new InvalidOperationException("Native pickup activation changed while committing admission.");
            __state = new NativeStoragePickup.Pickup(pawn, job, group, item, count, held, priorUnits);
            return true;
        }

        static Exception Finalizer(int __result, Exception __exception, NativeStoragePickup.Pickup __state)
        {
            return __state == null ? __exception : __state.Finish(__exception);
        }
    }
}
