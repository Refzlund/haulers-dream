using System;
using System.Collections.Generic;
using System.Reflection;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    internal static partial class StorageCommitments
    {
        [ThreadStatic] private static StorageAdmissionQueryScope currentStorageQuery;

        internal static bool QueryFactoryHasView(Pawn pawn, Map map, Thing subject, ISlotGroup group)
            => currentStorageQuery?.Applies(pawn, map, subject) == true
                && currentStorageQuery.FactoryHasView(group);

        // Only the inspected native search adapters consume this synchronous proposal.
        // Actual reservation/pickup/forced writers continue through ResourceUnitsFor.
        internal static ResourceAllowance SearchCellUnitsFor(Pawn pawn, Map map, ISlotGroup group,
            Thing subject, IntVec3 cell, out int allowed)
        {
            allowed = 0;
            if (!UnityData.IsInMainThread || ResourceQueriesBlocked() || resourceTransferDepth > 0)
                return ResourceAllowance.Deferred;
            var scope = currentStorageQuery;
            if (scope != null && scope.Applies(pawn, map, subject)
                && scope.TryCandidate(group, cell, out ResourceAllowance status, out allowed)) return status;
            // Standalone predicates and excluded compositions get a new complete immediate
            // observation. Never borrow the enclosing/previous query's physical certificate.
            return ResourceUnitsFor(pawn, group, subject, 1, cell, pawn.CurJob, out allowed);
        }

        internal sealed class StorageAdmissionQueryScope : IDisposable
        {
            internal static StorageAdmissionQueryScope Open(Pawn pawn, Map map, Thing subject, MethodBase boundary)
            {
                if (!UnityData.IsInMainThread) return null;
                if (currentStorageQuery == null && (!AnyClaims || ResourceQueriesBlocked()
                    || resourceTransferDepth > 0 || InsideSpaceScan || InForcedOrder
                    || StorageAllocationObservation.InProgress)) return null;
                return new StorageAdmissionQueryScope(pawn, map, subject, boundary);
            }

            internal static StorageAdmissionQueryScope OpenFactory(Pawn pawn, Thing subject, MethodBase boundary)
            {
                if (!UnityData.IsInMainThread) return null;
                if (currentStorageQuery == null && !AnyClaims) return null;
                return Open(pawn, pawn?.Map, subject, boundary);
            }

            private readonly StorageAdmissionQueryScope parent;
            private readonly Pawn pawn;
            private readonly Map map;
            private readonly Thing subject;
            private readonly Game game;
            private readonly LoadRecoveryTicket epoch;
            private readonly Job job;
            private readonly int jobId;
            private readonly JobDriver driver;
            private readonly StorageFilterContext context;
            private readonly bool enabled, factory;
            private readonly ProjectionThingGuard sourceGuard;
            private readonly Dictionary<ISlotGroup, QueryGroupView> groups = new Dictionary<ISlotGroup, QueryGroupView>();
            private readonly List<QueryGroupView> groupOrder = new List<QueryGroupView>();
            private ResourceResponsibilitySnapshot snapshot;
            private bool disposed, invalid, operating;

            private StorageAdmissionQueryScope(Pawn pawn, Map map, Thing subject, MethodBase boundary)
            {
                parent = currentStorageQuery;
                this.pawn = pawn; this.map = map; this.subject = subject;
                factory = boundary?.DeclaringType == typeof(HaulAIUtility)
                    && boundary.Name == nameof(HaulAIUtility.HaulToCellStorageJob);
                // Even an excluded nested call owns a frame, so it cannot accidentally use
                // its parent's provisional view. This check precedes every world/policy read.
                if (UnityData.IsInMainThread && !ResourceQueriesBlocked() && resourceTransferDepth == 0
                    && !InsideSpaceScan && !InForcedOrder && !StorageAllocationObservation.InProgress
                    && pawn?.Map == map && subject?.def?.category == ThingCategory.Item
                    && AnyClaims && GatesVanillaStorage(map))
                {
                    game = Verse.Current.Game; epoch = activeLoadRecovery;
                    job = pawn.CurJob; jobId = job?.loadID ?? -1; driver = pawn.jobs?.curDriver;
                    context = StorageBuildingFilter.CurrentContext;
                    enabled = StorageQueryBindings.Inspected(boundary, subject);
                    if (enabled) sourceGuard = new ProjectionThingGuard(subject);
                }
                currentStorageQuery = this;
            }

            internal bool Applies(Pawn carrier, Map world, Thing cargo)
                => enabled && ReferenceEquals(carrier, pawn) && ReferenceEquals(world, map)
                    && ReferenceEquals(cargo, subject);

            internal bool FactoryHasView(ISlotGroup group)
                => factory && Current() && group != null && groups.TryGetValue(group, out var view)
                    && view.Ready && !view.Immediate;

            private bool Current()
                => enabled && !disposed && !invalid && UnityData.IsInMainThread
                    && ReferenceEquals(currentStorageQuery, this) && !ResourceQueriesBlocked()
                    && resourceTransferDepth == 0 && ReferenceEquals(Verse.Current.Game, game)
                    && ReferenceEquals(activeLoadRecovery, epoch) && ReferenceEquals(pawn.Map, map)
                    && ReferenceEquals(pawn.CurJob, job) && (job?.loadID ?? -1) == jobId
                    && ReferenceEquals(pawn.jobs?.curDriver, driver)
                    && StorageBuildingFilter.CurrentContext == context && sourceGuard.Matches()
                    && (snapshot == null || snapshot.Current);

            private StorageAllocationObservationDemand Incoming(IntVec3 cell)
                => new StorageAllocationObservationDemand((object)job ?? pawn, subject, subject, pawn,
                    1, StorageFilterContext.Unload, cell, job, startsRefill: !IsDelivering(pawn, subject));

            internal bool TryCandidate(ISlotGroup group, IntVec3 cell, out ResourceAllowance status, out int allowed)
            {
                status = ResourceAllowance.Deferred; allowed = 0;
                if (!Current() || operating) return true;
                operating = true;
                try
                {
                    if (snapshot == null) snapshot = ObserveResourceResponsibilities(map);
                    if (!Current()) return true;
                    if (!groups.TryGetValue(group, out var view))
                    {
                        view = MakeGroup(group, cell);
                        groups.Add(group, view); groupOrder.Add(view);
                    }
                    if (view.Immediate) return false;
                    if (!view.Ready || !Current()) return true;
                    var incoming = Incoming(cell);
                    // A candidate branches from the same prior state; a successful probe does
                    // not spend capacity or become part of the next candidate's baseline.
                    var localDemands = view.DemandsAt(cell);
                    localDemands.Add(incoming);
                    var observation = StorageAllocationObservation.ObserveCells(map, group,
                        localDemands, new[] { cell }, view.Topology.Contains);
                    if (!Usable(observation, localDemands.Count) || !InspectedResources(observation) || !Current()) return true;
                    var result = StorageResourceAllocator.Allocate(observation.Cells, view.StateAt(cell),
                        new[] { observation.Requests[observation.Requests.Count - 1] },
                        CompatibleResource, observation.ObservedEligible,
                        new StorageAllocationOptions(observationComplete: true));
                    if (!result.CanPublish || !observation.StillCurrent()
                        || !view.Guard.Matches() || !Current()) return true;
                    allowed = (int)Math.Min(1, result.AdmittedUnits(observation.Requests[observation.Requests.Count - 1]));
                    if (allowed > 0) view.RecordPositive(cell);
                    status = ResourceAllowance.Observed;
                    return true;
                }
                finally { operating = false; }
            }

            private QueryGroupView MakeGroup(ISlotGroup group, IntVec3 preferred)
            {
                var view = new QueryGroupView(group);
                // An exact caller parcel keeps its established fresh replacement/partial-
                // delivery route, including unresolved or differently destined held cargo.
                // Classify it before any other owner's unresolved marker can deny this view.
                foreach (var portion in snapshot.Portions)
                    if (SameResourceParcel(portion, pawn, subject))
                    { view.Immediate = true; return view; }
                if (UnresolvedResourceBlocks(snapshot, group, pawn, subject)) return view;
                if (!StorageQueryBindings.NativeGroup(group)) { view.Immediate = true; return view; }
                view.Topology = QueryTopology.TryCreate(map, group);
                if (view.Topology == null) return view;
                view.Guard.Map(map); view.Guard.Pawn(pawn); view.Guard.Thing(subject);
                view.Guard.HdPolicy(); view.Guard.StoragePolicy(group);
                foreach (var portion in snapshot.Portions)
                {
                    if (!ReferenceEquals(portion.Row.Group, group)) continue;
                    var carrier = (Pawn)portion.Row.Pawn;
                    var entry = portion.Entry;
                    if (!StorageQueryBindings.NativeSubject(entry.Subject))
                    { view.Immediate = true; return view; }
                    view.Guard.Pawn(carrier); view.Guard.Thing(entry.Subject); view.Guard.Job(entry.OwnerJob);
                    view.Demands.Add(PriorResourceDemand(portion));
                }
                if (!Current()) return view;
                if (view.Demands.Count == 0)
                {
                    if (view.Topology.Current() && view.Guard.Matches())
                        view.Initialize(StorageAllocationState.Empty, new Dictionary<string, IntVec3>());
                    return view;
                }
                var observation = StorageAllocationObservation.Observe(map, group, view.Demands,
                    preferredCells: new[] { preferred });
                if (observation.Invalidated || observation.Requests.Count != view.Demands.Count
                    || !InspectedResources(observation) || !Current()) return view;
                foreach (var issue in observation.Issues)
                    if (issue.Status == StorageAllocationObservationStatus.Unsupported
                        || issue.Status == StorageAllocationObservationStatus.Deferred) return view;
                var result = StorageResourceAllocator.Allocate(observation.Cells, StorageAllocationState.Empty,
                    observation.Requests, CompatibleResource, observation.ObservedEligible,
                    new StorageAllocationOptions(observationComplete: observation.Complete));
                if (!result.CanPublish || !view.Topology.Current() || !observation.StillCurrent()
                    || !view.Guard.Matches() || !Current()) return view;
                foreach (var request in observation.Requests)
                    if (result.AdmittedUnits(request) != request.Units) return view;
                view.Initialize(result.State, observation.CellLocations);
                return view;
            }

            private static bool Usable(StorageAllocationObservationResult observation, int demands)
            {
                if (observation.Invalidated || !observation.Complete || observation.Requests.Count != demands) return false;
                foreach (var issue in observation.Issues)
                    if (issue.Status == StorageAllocationObservationStatus.Deferred
                        || issue.Status == StorageAllocationObservationStatus.Unsupported) return false;
                return true;
            }

            private static bool InspectedResources(StorageAllocationObservationResult observation)
            {
                foreach (var cell in observation.Cells)
                    foreach (var stack in cell.Stacks)
                        if (!(stack.Target is Thing item) || !StorageQueryBindings.NativeSubject(item)) return false;
                return true;
            }

            // Called after the inspected native body and its read-only postfixes. Candidate
            // answers are provisional until all prior slices + the selected cell are freshly
            // certified together. No scope certificate reaches an actual claim writer.
            internal bool FinishSearch(IntVec3 selected)
            {
                if (!enabled) return true;
                if (!Current()) return false;
                var group = BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(selected));
                if (!Current()) return false;
                if (group == null || !groups.TryGetValue(group, out var view) || view.Immediate) return true;
                return FinishGroup(view, new[] { selected });
            }

            internal void FinishFactory()
            {
                if (!enabled) return;
                // Preserve the original native job/count even if callbacks invalidated a
                // provisional gate answer. Reservation/start always re-admits actual cargo.
                // The original complete count loop is neither truncated nor replaced here.
                foreach (var view in groupOrder)
                    if (!view.Immediate && view.Positive.Count > 0
                        && !FinishGroup(view, new List<IntVec3>(view.Positive))) invalid = true;
            }

            private bool FinishGroup(QueryGroupView view, IReadOnlyList<IntVec3> candidates)
            {
                if (!view.Ready || !Current() || operating || !view.Guard.Matches()) return false;
                operating = true;
                try
                {
                    var coordinates = new List<IntVec3>(view.BaselineCells);
                    foreach (var cell in candidates) if (!coordinates.Contains(cell)) coordinates.Add(cell);
                    var demands = new List<StorageAllocationObservationDemand>(view.Demands);
                    // Each independent cell is validated against the same full prior state.
                    var observation = StorageAllocationObservation.ObserveCells(map, view.Group, demands, coordinates,
                        view.Topology.Contains);
                    if (!Usable(observation, demands.Count) || !InspectedResources(observation) || !Current()) return false;
                    var prior = StorageResourceAllocator.Allocate(observation.Cells, view.State,
                        Array.Empty<StorageAllocationRequest>(), CompatibleResource, observation.ObservedEligible,
                        new StorageAllocationOptions(observationComplete: true));
                    if (!prior.CanPublish || !Current()) return false;
                    foreach (var cell in candidates)
                    {
                        var local = view.DemandsAt(cell); local.Add(Incoming(cell));
                        var fresh = StorageAllocationObservation.ObserveCells(map, view.Group, local, new[] { cell },
                            view.Topology.Contains);
                        if (!Usable(fresh, local.Count) || !InspectedResources(fresh) || !Current()) return false;
                        var request = fresh.Requests[fresh.Requests.Count - 1];
                        var result = StorageResourceAllocator.Allocate(fresh.Cells, view.StateAt(cell),
                            new[] { request }, CompatibleResource, fresh.ObservedEligible,
                            new StorageAllocationOptions(observationComplete: true));
                        if (!result.CanPublish || result.AdmittedUnits(request) <= 0
                            || !fresh.StillCurrent() || !view.Guard.Matches() || !Current()) return false;
                    }
                    // Candidate policy callbacks can mutate earlier resources. Recheck the
                    // full physical certificate after the last one, before returning success.
                    return StorageQueryBindings.NativeGroup(view.Group) && view.Topology.Current()
                        && observation.StillCurrent() && view.Guard.Matches() && Current();
                }
                finally { operating = false; }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (ReferenceEquals(currentStorageQuery, this)) currentStorageQuery = parent;
                else
                {
                    // Corrupt/out-of-order nesting cannot leave a reusable permission frame.
                    for (var frame = currentStorageQuery; frame != null; frame = frame.parent) frame.invalid = true;
                    currentStorageQuery = null;
                }
                groups.Clear(); groupOrder.Clear(); snapshot = null;
            }

            private sealed class QueryGroupView
            {
                internal readonly ISlotGroup Group;
                internal readonly ForcedRawState Guard = new ForcedRawState();
                internal QueryTopology Topology;
                internal readonly List<StorageAllocationObservationDemand> Demands = new List<StorageAllocationObservationDemand>();
                internal readonly List<IntVec3> Positive = new List<IntVec3>();
                private readonly HashSet<IntVec3> positiveSet = new HashSet<IntVec3>();
                internal readonly List<IntVec3> BaselineCells = new List<IntVec3>();
                internal StorageAllocationState State;
                internal bool Immediate, Ready;
                private readonly Dictionary<IntVec3, StorageAllocationState> byCell = new Dictionary<IntVec3, StorageAllocationState>();
                internal QueryGroupView(ISlotGroup group) { Group = group; }
                internal void RecordPositive(IntVec3 cell) { if (positiveSet.Add(cell)) Positive.Add(cell); }
                internal void Initialize(StorageAllocationState state, IReadOnlyDictionary<string, IntVec3> locations)
                {
                    State = state;
                    var slices = new Dictionary<IntVec3, List<StorageResourceAllocation>>();
                    foreach (var slice in state.Slices)
                    {
                        var cell = locations[slice.CellKey];
                        if (!slices.TryGetValue(cell, out var values))
                        { values = new List<StorageResourceAllocation>(); slices.Add(cell, values); BaselineCells.Add(cell); }
                        values.Add(slice);
                    }
                    foreach (var pair in slices) byCell.Add(pair.Key, new StorageAllocationState(pair.Value));
                    Ready = true;
                }
                internal StorageAllocationState StateAt(IntVec3 cell)
                    => byCell.TryGetValue(cell, out var state) ? state : StorageAllocationState.Empty;
                internal List<StorageAllocationObservationDemand> DemandsAt(IntVec3 cell)
                {
                    var result = new List<StorageAllocationObservationDemand>();
                    foreach (var slice in StateAt(cell).Slices)
                    {
                        foreach (var demand in Demands)
                            if (ReferenceEquals(demand.Owner, slice.Owner) && ReferenceEquals(demand.Parcel, slice.Parcel))
                            { if (!result.Contains(demand)) result.Add(demand); break; }
                    }
                    return result;
                }
            }

            // Synchronous native topology only. Zone membership is indexed once instead of
            // doing a whole CellsList.Contains for every exact candidate. No physical counts
            // live here; Dispose drops this with the rest of the frame.
            private sealed class QueryTopology
            {
                private readonly Dictionary<SlotGroup, StorageProjectionMemberGuard> members
                    = new Dictionary<SlotGroup, StorageProjectionMemberGuard>();
                private StorageGroup linked;
                private ProjectionListGuard<IStorageGroupMember> linkedGuard;
                internal static QueryTopology TryCreate(Map map, ISlotGroup group)
                {
                    var result = new QueryTopology();
                    int zoneCells = 0;
                    bool Add(ISlotGroupParent parent)
                    {
                        PreparedProjectionZoneCells prepared = null;
                        if (parent is Zone_Stockpile zone)
                        {
                            if (zone.cells.Count > 4096 - zoneCells) return false;
                            zoneCells += zone.cells.Count;
                            prepared = new PreparedProjectionZoneCells(zone);
                        }
                        var guard = new StorageProjectionMemberGuard(parent, map, group, prepared);
                        if (result.members.ContainsKey(guard.Slot)) return false;
                        result.members.Add(guard.Slot, guard); return true;
                    }
                    try
                    {
                        if (group is SlotGroup slot)
                        { if (!Add(slot.parent)) return null; }
                        else if (group is StorageGroup linked)
                        {
                            if (linked.members.Count > 64) return null;
                            result.linked = linked;
                            result.linkedGuard = new ProjectionListGuard<IStorageGroupMember>(linked.members);
                            foreach (var member in linked.members)
                                if (!(member is ISlotGroupParent parent) || !Add(parent)) return null;
                        }
                        else return null;
                        return result.Current() ? result : null;
                    }
                    catch (ProjectionAbort) { return null; }
                }
                internal bool Contains(SlotGroup slot, IntVec3 cell)
                    => (linked == null || linkedGuard.Matches(linked.members))
                        && members.TryGetValue(slot, out var guard) && guard.Matches() && guard.ContainsObservedCell(cell);
                internal bool Current()
                {
                    if (linked != null && !linkedGuard.Matches(linked.members)) return false;
                    foreach (var guard in members.Values) if (!guard.Matches()) return false;
                    return true;
                }
            }
        }
    }
}
