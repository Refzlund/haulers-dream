using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HaulersDream.Core
{
    // These values describe a caller-owned, freshly observed transaction. They do not inspect
    // the world, establish a claim lifetime, or turn an incomplete observation into capacity.
    public sealed class StorageAllocationStack
    {
        public string Key { get; }
        public object Target { get; }
        public int FreeUnits { get; }
        public StorageAllocationStack(string key, object target, int freeUnits)
        {
            Key = StorageAllocationData.Key(key); Target = target ?? throw new ArgumentNullException(nameof(target));
            if (freeUnits < 0) throw new ArgumentOutOfRangeException(nameof(freeUnits));
            FreeUnits = freeUnits;
        }
    }

    public sealed class StorageAllocationCell
    {
        public string Key { get; }
        public int VacantSlots { get; }
        public IReadOnlyList<StorageAllocationStack> Stacks { get; }
        public StorageAllocationCell(string key, int vacantSlots, IEnumerable<StorageAllocationStack> stacks = null)
        {
            Key = StorageAllocationData.Key(key);
            if (vacantSlots < 0) throw new ArgumentOutOfRangeException(nameof(vacantSlots));
            VacantSlots = vacantSlots; Stacks = StorageAllocationData.Copy(stacks);
        }
    }

    public sealed class StorageAllocationRequest
    {
        public object Owner { get; }
        public object Parcel { get; }
        public object Subject { get; }
        // Total desired outstanding quantity, including any unchanged baseline slices for this
        // exact owner/parcel. Repeating admission therefore cannot silently double a lease.
        public int Units { get; }
        public int StackLimit { get; }
        public IReadOnlyList<string> EligibleCells { get; }
        public IReadOnlyList<string> EligibleStacks { get; }
        public StorageAllocationRequest(object owner, object parcel, object subject, int units, int stackLimit,
            IEnumerable<string> eligibleCells, IEnumerable<string> eligibleStacks = null)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Parcel = parcel ?? throw new ArgumentNullException(nameof(parcel));
            Subject = subject ?? throw new ArgumentNullException(nameof(subject));
            if (units < 0 || stackLimit <= 0) throw new ArgumentOutOfRangeException();
            Units = units; StackLimit = stackLimit;
            EligibleCells = StorageAllocationData.Copy(eligibleCells?.Select(StorageAllocationData.Key));
            EligibleStacks = StorageAllocationData.Copy(eligibleStacks?.Select(StorageAllocationData.Key));
        }
    }

    public enum StorageAllocationResourceKind { ExistingStack, VacantSlot }

    public sealed class StorageResourceAllocation
    {
        public object Owner { get; }
        public object Parcel { get; }
        public object Subject { get; }
        public string CellKey { get; }
        public string ResourceKey { get; }
        public StorageAllocationResourceKind Kind { get; }
        public int Units { get; }
        // A virtual slot retains its original directional merge target even when that owner's
        // slice is released. The game adapter must rebind it from actual placement receipts.
        public object Target { get; }
        public int SlotLimit { get; }
        public StorageResourceAllocation(object owner, object parcel, object subject, string cellKey,
            string resourceKey, StorageAllocationResourceKind kind, int units, object target, int slotLimit = 0)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Parcel = parcel ?? throw new ArgumentNullException(nameof(parcel));
            Subject = subject ?? throw new ArgumentNullException(nameof(subject));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            CellKey = StorageAllocationData.Key(cellKey); ResourceKey = StorageAllocationData.Key(resourceKey);
            if (units <= 0 || !Enum.IsDefined(typeof(StorageAllocationResourceKind), kind)
                || (kind == StorageAllocationResourceKind.VacantSlot && slotLimit <= 0)) throw new ArgumentOutOfRangeException();
            Kind = kind; Units = units; SlotLimit = slotLimit;
        }
        internal StorageResourceAllocation WithUnits(int units) => new StorageResourceAllocation(
            Owner, Parcel, Subject, CellKey, ResourceKey, Kind, units, Target, SlotLimit);
    }

    public sealed class StorageAllocationState
    {
        public static readonly StorageAllocationState Empty = new StorageAllocationState(null);
        public IReadOnlyList<StorageResourceAllocation> Slices { get; }
        public StorageAllocationState(IEnumerable<StorageResourceAllocation> slices)
            => Slices = StorageAllocationData.Copy(slices);
        public long UnitsFor(object owner, object parcel = null)
        {
            long total = 0;
            foreach (var slice in Slices)
                if (ReferenceEquals(slice.Owner, owner) && (parcel == null || ReferenceEquals(slice.Parcel, parcel)))
                    total = checked(total + slice.Units);
            return total;
        }
        public IReadOnlyList<StorageResourceAllocation> SlicesFor(object owner, object parcel = null)
            => StorageAllocationData.Copy(Slices.Where(s => ReferenceEquals(s.Owner, owner)
                && (parcel == null || ReferenceEquals(s.Parcel, parcel))));
    }

    public enum StorageAllocationStatus
    {
        Complete, CapacityLimited, IncompleteObservation, BudgetExhausted, NeedsReconciliation, InvalidRequest
    }

    public sealed class StorageAllocationOptions
    {
        public bool ObservationComplete { get; }
        public int MaximumWork { get; }
        public int MaximumNewSlots { get; }
        public int MaximumRequests { get; }
        public StorageAllocationOptions(bool observationComplete = true, int maximumWork = 100000,
            int maximumNewSlots = 4096, int maximumRequests = 256)
        {
            if (maximumWork <= 0 || maximumNewSlots <= 0 || maximumRequests <= 0) throw new ArgumentOutOfRangeException();
            ObservationComplete = observationComplete; MaximumWork = maximumWork;
            MaximumNewSlots = maximumNewSlots; MaximumRequests = maximumRequests;
        }
    }

    public sealed class StorageAllocationResult
    {
        public StorageAllocationStatus Status { get; }
        public StorageAllocationState State { get; }
        public int Work { get; }
        // Invalid baseline, budget exhaustion and exceptions never publish an intermediate proposal.
        public bool CanPublish => Status == StorageAllocationStatus.Complete
            || Status == StorageAllocationStatus.CapacityLimited || Status == StorageAllocationStatus.IncompleteObservation;
        internal StorageAllocationResult(StorageAllocationStatus status, StorageAllocationState state, int work)
        { Status = status; State = state; Work = work; }
        public long AdmittedUnits(StorageAllocationRequest request) => State.UnitsFor(request.Owner, request.Parcel);
    }

    internal static class StorageAllocationData
    {
        internal static string Key(string key) => !string.IsNullOrWhiteSpace(key) ? key : throw new ArgumentException("A resource key is required.");
        internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
        {
            var result = values == null ? new List<T>() : new List<T>(values);
            if (result.Any(v => ReferenceEquals(v, null))) throw new ArgumentException("Null entries are not resources.");
            return new ReadOnlyCollection<T>(result);
        }
    }
}
