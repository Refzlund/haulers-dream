using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    internal static partial class StorageCommitments
    {
        // A synchronous ownership handoff, never a second capacity ledger. Admission is read-only;
        // only the witnessed transfer replaces the source's portions in the authoritative rows.
        internal sealed class UnloadResourceTransfer
        {
            private readonly Pawn pawn;
            private readonly Thing source;
            private readonly ResourceResponsibilitySnapshot before;
            private readonly ISlotGroup destination;
            private readonly ISlotGroup retiredDestination;
            private readonly LoadRecoveryTicket loadTicket;
            private readonly List<ResourceResponsibility> portions = new List<ResourceResponsibility>();

            private UnloadResourceTransfer(Pawn pawn, Thing source,
                ResourceResponsibilitySnapshot before, ISlotGroup destination,
                ISlotGroup retiredDestination = null, LoadRecoveryTicket loadTicket = null)
            {
                this.pawn = pawn; this.source = source; this.before = before;
                this.destination = destination;
                this.retiredDestination = retiredDestination;
                this.loadTicket = loadTicket;
                foreach (var portion in before.Portions)
                    if (ReferenceEquals(portion.Row.Pawn, pawn)
                        && ReferenceEquals(portion.Entry.Subject, source)) portions.Add(portion);
            }

            internal bool Current => before.Current;

            internal void Complete(IReadOnlyList<Thing> descendants, Thing hands, int heldUnits)
            {
                if (!before.SameLifecycle || ResourceQueriesBlocked(loadTicket))
                    throw new InvalidOperationException("Ordinary-unload lifecycle changed during transfer.");
                // Finish provider/group reads before capturing the array to be updated. The
                // remaining publication phase uses only these local facts and exact identities.
                var liveGroups = new HashSet<object>();
                foreach (var portion in portions)
                    // Retire only the captured invalid destination. The exact-source debits
                    // below still preserve every other parcel, owner and callback-written row.
                    if (!ReferenceEquals(portion.Row.Group, retiredDestination)
                        && GroupIsLive(portion.Row.Group, pawn.Map)) liveGroups.Add(portion.Row.Group);
                var entries = new List<StorageParcelEvidence.Entry>();
                StorageParcelEvidence.Collect(pawn, entries);
                var currentRows = HaulersDreamGameComponent.storageClaims;
                // Foreign callbacks may have changed unrelated rows. Subtract only this source's
                // attributed portion from the same responsibility key; preserve every other owner.
                var rows = new List<StorageClaimRow>();
                var debits = new int[portions.Count];
                var budgets = new List<(object group, int units, HeldDestinationReason reason, ISlotGroup candidate)>();
                foreach (var portion in portions)
                {
                    int index = budgets.FindIndex(pair => ReferenceEquals(pair.group, portion.Row.Group));
                    var heldOwner = portion.Row.WorkOwner as HeldCargoOwner;
                    if (index < 0) budgets.Add((portion.Row.Group, portion.Units,
                        heldOwner?.Reason ?? HeldDestinationReason.None, heldOwner?.CandidateGroup));
                    else budgets[index] = (budgets[index].group, budgets[index].units + portion.Units,
                        budgets[index].reason, budgets[index].candidate);
                }
                foreach (var row in currentRows)
                {
                    int units = row.Units;
                    for (int i = 0; i < portions.Count && units > 0; i++)
                    {
                        var old = portions[i].Row;
                        if (row.ExclusiveCellAllocation != null || !ReferenceEquals(row.Pawn, old.Pawn)
                            || !ReferenceEquals(row.Group, old.Group) || !ReferenceEquals(row.Def, old.Def)
                            || !ReferenceEquals(row.WorkOwner, old.WorkOwner)) continue;
                        int debit = Math.Min(units, portions[i].Units - debits[i]);
                        units -= debit; debits[i] += debit;
                    }
                    if (units == row.Units) rows.Add(row);
                    else if (units > 0) rows.Add(new StorageClaimRow(row.Pawn, row.Group, row.Def,
                        units, workOwner: row.WorkOwner));
                }

                // The actual withdrawn portion takes its newly admitted destination. Consume its
                // old responsibility at that destination first, then at the other old destinations.
                int moved = hands != null ? Math.Min(heldUnits, hands.stackCount) : 0;
                int remainingDebit = moved;
                for (int phase = 0; phase < 2; phase++)
                    for (int i = 0; i < budgets.Count && remainingDebit > 0; i++)
                    {
                        var budget = budgets[i];
                        if ((ReferenceEquals(budget.group, destination) ? 0 : 1) != phase) continue;
                        int debit = Math.Min(budget.units, remainingDebit);
                        budgets[i] = (budget.group, budget.units - debit, budget.reason, budget.candidate);
                        remainingDebit -= debit;
                    }

                var available = new int[entries.Count];
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    bool owned = ReferenceEquals(entry.Subject, source);
                    for (int j = 0; !owned && j < descendants.Count; j++)
                        owned = ReferenceEquals(entry.Subject, descendants[j]);
                    if (owned && entry.Held) available[i] = entry.Units;
                }
                if (destination != null && moved > 0)
                    for (int i = 0; i < entries.Count; i++)
                        if (ReferenceEquals(entries[i].Subject, hands))
                        {
                            int units = Math.Min(moved, available[i]);
                            if (units > 0) rows.Add(new StorageClaimRow(pawn, destination, hands.def, units,
                                workOwner: new HeldCargoOwner(pawn, entries[i])));
                            available[i] -= units;
                            break;
                        }
                // A container/native exclusive reservation owns its destination externally. Its
                // hands must not also retain a stale promise to the old storage group.
                if (hands != null)
                    for (int i = 0; i < entries.Count; i++)
                        if (ReferenceEquals(entries[i].Subject, hands)) available[i] = 0;
                foreach (var budget in budgets)
                {
                    int remaining = budget.units;
                    for (int i = 0; i < entries.Count && remaining > 0; i++)
                    {
                        var entry = entries[i];
                        if (available[i] <= 0) continue;
                        bool bound = liveGroups.Contains(budget.group);
                        if (bound && entry.KnownGroup != null && !ReferenceEquals(entry.KnownGroup, budget.group))
                            continue;
                        int units = Math.Min(remaining, available[i]);
                        bool wasUnresolved = ReferenceEquals(budget.group, UnresolvedHeldDestination);
                        rows.Add(new StorageClaimRow(pawn, bound ? budget.group : UnresolvedHeldDestination,
                            entry.Subject.def, units, workOwner: new HeldCargoOwner(pawn, entry,
                                bound ? HeldDestinationReason.None : wasUnresolved ? budget.reason
                                    : HeldDestinationReason.NoDestination,
                                wasUnresolved ? budget.candidate : null)));
                        remaining -= units; available[i] -= units;
                    }
                }
                if (!before.SameLifecycle || ResourceQueriesBlocked(loadTicket)
                    || !ReferenceEquals(currentRows, HaulersDreamGameComponent.storageClaims))
                    throw new InvalidOperationException("Ordinary-unload responsibility changed during publication.");
                HaulersDreamGameComponent.SetStorageClaims(rows.ToArray());
            }

            internal static ResourceAllowance Prepare(JobDriver_UnloadHauledInventory driver,
                Thing source, int wanted, ISlotGroup destination, out UnloadResourceTransfer transfer,
                out int allowed)
            {
                transfer = null; allowed = 0;
                var pawn = driver?.pawn;
                if (pawn?.Map == null || source == null || pawn.CurJob != driver.job
                    || pawn.jobs.curDriver != driver) return ResourceAllowance.Deferred;
                var snapshot = ObserveResourceResponsibilities(pawn.Map);
                int physical = 0, already = 0;
                foreach (var entry in snapshot.EntriesFor(pawn))
                    if (entry.Held && ReferenceEquals(entry.Subject, source)) physical = entry.Units;
                if (!snapshot.Current || pawn.CurJob != driver.job || pawn.jobs.curDriver != driver)
                    return ResourceAllowance.Deferred;
                wanted = Math.Min(wanted, physical);
                if (wanted <= 0) return ResourceAllowance.Observed;
                foreach (var portion in snapshot.Portions)
                    if (ReferenceEquals(portion.Row.Pawn, pawn)
                        && ReferenceEquals(portion.Entry.Subject, source)
                        && ReferenceEquals(portion.Row.Group, destination)) already += portion.Units;
                // Ordinary delivery shared native cells only when Haul-to-Stack was enabled.
                // Bulk planning's broader ActiveOn gate must not remove that exclusivity while
                // the vanilla incoming-capacity adapters are intentionally disabled.
                int admitted = 0;
                var status = GatesVanillaStorage(pawn.Map)
                    ? ResourceUnitsFor(pawn, destination, source, Math.Max(wanted, already),
                        null, driver.job, out admitted)
                    : ResourceAllowance.Unsupported;
                if (!snapshot.Current || pawn.CurJob != driver.job || pawn.jobs.curDriver != driver
                    || status == ResourceAllowance.Deferred) return ResourceAllowance.Deferred;
                allowed = status == ResourceAllowance.Unsupported ? wanted : Math.Min(wanted, admitted);
                if (allowed > 0) transfer = new UnloadResourceTransfer(pawn, source, snapshot,
                    status == ResourceAllowance.Observed ? destination : null);
                return status;
            }

            internal static UnloadResourceTransfer CaptureReturn(Pawn pawn, Thing source,
                ISlotGroup retiredDestination = null)
                => CaptureReturnForLoad(pawn, source, retiredDestination, null);

            internal static UnloadResourceTransfer CaptureReturnForLoad(Pawn pawn, Thing source,
                ISlotGroup retiredDestination, LoadRecoveryTicket ticket)
            {
                if (!UnityData.IsInMainThread || ResourceQueriesBlocked(ticket)) return null;
                var snapshot = ObserveResourceResponsibilitiesForLoad(pawn.Map, ticket);
                return snapshot.Current
                    ? new UnloadResourceTransfer(pawn, source, snapshot, null, retiredDestination, ticket) : null;
            }
        }

        internal static void RestoreOrdinaryUnloadHands(Pawn pawn)
            => RestoreOrdinaryUnloadHandsForLoad(pawn, null);

        private static void RestoreOrdinaryUnloadHandsForLoad(Pawn pawn, LoadRecoveryTicket ticket)
        {
            if (ResourceQueriesBlocked(ticket)) return;
            if (!(pawn?.jobs?.curDriver is JobDriver_UnloadHauledInventory driver)
                || driver.job?.def != HaulersDreamDefOf.HaulersDream_UnloadInventory) return;
            driver.RestoreOrdinaryTransitAfterLoad();
            Thing held = driver.StorageBoundOrdinaryHands;
            if (held == null || pawn.Map == null || !driver.job.targetB.IsValid) return;
            var map = pawn.Map;
            var job = driver.job;
            int jobId = job.loadID;
            LocalTargetInfo destination = job.targetB;
            bool Current() => !ResourceQueriesBlocked(ticket) && ReferenceEquals(pawn.Map, map)
                && ReferenceEquals(pawn.CurJob, job) && job.loadID == jobId
                && ReferenceEquals(pawn.jobs.curDriver, driver) && job.targetB == destination
                && ReferenceEquals(driver.StorageBoundOrdinaryHands, held);
            // Native exclusivity is already charged through its physical cell reservation.
            var reservations = map.reservationManager;
            bool reserved = reservations.ReservedBy(destination, pawn, job);
            if (!Current() || reserved) return;
            bool nativeExclusive = destination.HasThing || !GatesVanillaStorage(map);
            if (!Current()) return;
            if (nativeExclusive)
            {
                Exception failure = null;
                bool attempted = false;
                try
                {
                    // Reserve's forced branch can interrupt another job. A loaded order does
                    // not authorize taking an already-conflicting destination on reconstruction.
                    bool canReserve = reservations.CanReserve(pawn, destination);
                    if (Current() && canReserve)
                    {
                        attempted = true;
                        reservations.Reserve(pawn, job, destination, errorOnFailed: false);
                    }
                }
                catch (Exception error) { failure = error; }
                try
                {
                    if (Current())
                    {
                        reserved = reservations.ReservedBy(destination, pawn, job);
                        if (Current() && !reserved)
                        {
                            // Only the receipt-owned parcel returns, with the existing transfer
                            // barrier/tag repair. Leave saved job targets/count/toil unchanged;
                            // native work can finish/fail normally with cargo safely retained.
                            driver.ReturnOrdinaryHandsAfterLoad(ticket);
                        }
                    }
                    else if (attempted && !ResourceQueriesBlocked(ticket) && job.loadID == jobId
                        && reservations.ReservedBy(destination, pawn, job))
                    {
                        // Release only this newly attempted old lease, never a replacement
                        // job's reservation or an earlier lease that this method did not add.
                        if (!ResourceQueriesBlocked(ticket) && job.loadID == jobId)
                            reservations.Release(destination, pawn, job);
                    }
                }
                catch (Exception error)
                {
                    if (failure == null) failure = error;
                    else failure.Data["HaulersDream.LoadRecovery.OrdinaryLease"] = error;
                }
                try
                {
                    // A native return may refuse without throwing. With shared hauling off,
                    // a deferred numeric query cannot stop this still-running native delivery.
                    // End only the exact old activation; native cleanup retains/tags/drops its
                    // receipt-owned cargo and does not consume the saved queue or start work.
                    if (Current())
                    {
                        reserved = reservations.ReservedBy(destination, pawn, job);
                        if (Current() && !reserved)
                            pawn.jobs.EndCurrentJob(JobCondition.Incompletable,
                                startNewJob: false, canReturnToPool: false);
                    }
                }
                catch (Exception error)
                {
                    if (failure == null) failure = error;
                    else failure.Data["HaulersDream.LoadRecovery.OrdinaryLeaseCleanup"] = error;
                }
                if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
                return;
            }
            if (!ActiveOn(map) || !Current()) return;
            var group = BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(destination.Cell));
            if (group == null) return;
            var entry = new StorageParcelEvidence.Entry { Subject = held, OwnerJob = driver.job,
                Units = driver.StorageBoundOrdinaryHandCount, KnownGroup = group,
                Destination = destination.Cell, Held = true };
            if (!Current()) return;
            HaulersDreamGameComponent.SetStorageClaims(StorageClaimLedger.AddForWork(
                HaulersDreamGameComponent.storageClaims, pawn, group, held.def, entry.Units,
                new HeldCargoOwner(pawn, entry)));
        }
    }
}
