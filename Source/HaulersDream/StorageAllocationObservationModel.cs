using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    internal enum StorageAllocationObservationStatus { Observed, Refused, Deferred, Unsupported, Invalidated }

    // A work limit is separate from storage truth. The owner may retain only this
    // address/retry information, never the observed cells or their eligibility.
    internal enum StorageObservationLimit { Members, Cells, GridThings, Predicates, Demands, MapWork }
    internal sealed class StorageObservationStop
    {
        internal StorageObservationLimit Dimension { get; }
        internal string Phase { get; }
        internal IntVec3? Position { get; }
        internal int RequiredWork { get; }
        internal int RemainingWork { get; }
        internal StorageObservationStop(StorageObservationLimit dimension, string phase,
            IntVec3? position, int requiredWork, int remainingWork)
        { Dimension = dimension; Phase = phase; Position = position; RequiredWork = requiredWork; RemainingWork = remainingWork; }
    }

    internal sealed class StorageAllocationObservationDemand
    {
        internal object Owner { get; }
        internal object Parcel { get; }
        internal Thing Subject { get; }
        internal Pawn Pawn { get; }
        internal int Units { get; }
        internal StorageFilterContext Context { get; }
        internal IntVec3? Destination { get; }
        internal Job OwnJob { get; }
        internal StoragePriority PriorityFloor { get; }
        internal bool RequireBetterPriority { get; }
        internal bool StartsRefill { get; }
        internal StorageAllocationObservationDemand(object owner, object parcel, Thing subject, Pawn pawn, int units,
            StorageFilterContext context, IntVec3? destination = null, Job ownJob = null,
            StoragePriority priorityFloor = StoragePriority.Unstored, bool requireBetterPriority = false, bool startsRefill = true)
        {
            Owner = owner; Parcel = parcel; Subject = subject; Pawn = pawn; Units = units; Context = context;
            Destination = destination; OwnJob = ownJob; PriorityFloor = priorityFloor;
            RequireBetterPriority = requireBetterPriority; StartsRefill = startsRefill;
        }
    }

    internal sealed class StorageAllocationObservationLimits
    {
        internal int Members { get; }
        internal int Cells { get; }
        internal int GridThings { get; }
        internal int Predicates { get; }
        internal int Demands { get; }
        internal int StartMember { get; }
        internal int StartCell { get; }
        internal StorageAllocationObservationLimits(int members = 64, int cells = 200, int gridThings = 4096,
            int predicates = 8192, int demands = 256, int startMember = 0, int startCell = 0)
        {
            if (members <= 0 || cells <= 0 || gridThings <= 0 || predicates <= 0 || demands <= 0 || startMember < 0 || startCell < 0)
                throw new ArgumentOutOfRangeException();
            Members = members; Cells = cells; GridThings = gridThings; Predicates = predicates; Demands = demands;
            StartMember = startMember; StartCell = startCell;
        }
    }

    internal sealed class StorageAllocationObservationIssue
    {
        internal IntVec3? Cell { get; }
        internal object Owner { get; }
        internal object Parcel { get; }
        internal StorageAllocationObservationStatus Status { get; }
        internal string Reason { get; }
        internal StorageAllocationObservationIssue(IntVec3? cell, StorageAllocationObservationDemand demand,
            StorageAllocationObservationStatus status, string reason)
        { Cell = cell; Owner = demand?.Owner; Parcel = demand?.Parcel; Status = status; Reason = reason; }
    }

    internal sealed class StorageAllocationObservationResult
    {
        internal IReadOnlyList<StorageAllocationCell> Cells { get; }
        internal IReadOnlyList<StorageAllocationRequest> Requests { get; }
        internal IReadOnlyDictionary<string, IntVec3> CellLocations { get; }
        internal IReadOnlyDictionary<string, Thing> StackTargets { get; }
        internal IReadOnlyList<StorageAllocationObservationIssue> Issues { get; }
        // Complete is deliberately stronger than "the observed cells had no free space".
        internal bool Complete { get; }
        // The initial census/edges/guards completed for this observed subset. This
        // does not cover absent priors or replace the final StillCurrent check.
        internal bool CertifiedSubset { get; }
        // A failed post-return freshness check updates this same synchronous result.
        // Callers copy address/dimension metadata only into persistent progress.
        internal StorageObservationStop Stop { get; private set; }
        internal void RecordStop(StorageObservationStop stop) { Stop = stop; }
        internal bool Invalidated { get; }
        internal int NextMember { get; }
        internal int NextCell { get; }
        internal int CellsInspected { get; }
        internal int GridThingsInspected { get; }
        internal int PredicatesCalled { get; }
        private readonly Func<bool> stillCurrent;
        // Immediate writer revalidation only; this closure is not persisted as capacity.
        internal bool StillCurrent() => stillCurrent != null && stillCurrent();
        internal StorageAllocationObservationResult(List<StorageAllocationCell> cells, List<StorageAllocationRequest> requests,
            Dictionary<string, IntVec3> locations, Dictionary<string, Thing> targets,
            List<StorageAllocationObservationIssue> issues, bool complete, bool invalidated, int nextMember, int nextCell,
            int cellWork, int gridWork, int predicates, Func<bool> stillCurrent = null,
            bool certifiedSubset = false, StorageObservationStop stop = null)
        {
            Cells = cells.AsReadOnly(); Requests = requests.AsReadOnly(); Issues = issues.AsReadOnly();
            CellLocations = new ReadOnlyDictionary<string, IntVec3>(locations);
            StackTargets = new ReadOnlyDictionary<string, Thing>(targets);
            Complete = complete; Invalidated = invalidated; NextMember = nextMember; NextCell = nextCell;
            CertifiedSubset = certifiedSubset; Stop = stop;
            CellsInspected = cellWork; GridThingsInspected = gridWork; PredicatesCalled = predicates;
            this.stillCurrent = stillCurrent;
        }

        // This is eligibility in THIS observation, not owner liveness or a lasting permission.
        // The game claim owner must supply genuine job/custody evidence separately.
        internal bool ObservedEligible(StorageResourceAllocation slice)
        {
            if (Invalidated || slice == null) return false;
            foreach (var request in Requests)
            {
                StorageProgressWork.Charge(StorageWorkKind.ObservedEligibility);
                if (!ReferenceEquals(request.Owner, slice.Owner) || !ReferenceEquals(request.Parcel, slice.Parcel)
                    || !ReferenceEquals(request.Subject, slice.Subject)) continue;
                bool cell = false;
                foreach (var key in request.EligibleCells)
                { StorageProgressWork.Charge(StorageWorkKind.ObservedEligibility); if (key == slice.CellKey) { cell = true; break; } }
                if (!cell) return false;
                if (slice.Kind == StorageAllocationResourceKind.VacantSlot) return true;
                foreach (var key in request.EligibleStacks)
                { StorageProgressWork.Charge(StorageWorkKind.ObservedEligibility); if (key == slice.ResourceKey) return true; }
                return false;
            }
            return false;
        }
    }
}
