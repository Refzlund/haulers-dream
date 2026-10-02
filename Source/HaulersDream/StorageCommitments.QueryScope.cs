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

        internal sealed partial class StorageAdmissionQueryScope : IDisposable
        {
            internal static StorageAdmissionQueryScope Open(Pawn pawn, Map map, Thing subject, MethodBase boundary)
            {
                if (!UnityData.IsInMainThread) return null;
                if (currentStorageQuery == null && (ResourceQueriesBlocked()
                    || resourceTransferDepth > 0 || InsideSpaceScan || InForcedOrder
                    || StorageAllocationObservation.InProgress)) return null;
                return new StorageAdmissionQueryScope(pawn, map, subject, boundary);
            }

            internal static StorageAdmissionQueryScope OpenFactory(Pawn pawn, Thing subject, MethodBase boundary)
            {
                if (!UnityData.IsInMainThread) return null;
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
            private ProjectionThingGuard sourceGuard;
            private readonly MethodBase boundary;
            private bool compositionKnown, compositionSupported;
            private StorageProjectionAsfBinding queryAsf;
            private readonly Dictionary<ISlotGroup, QueryGroupView> groups = new Dictionary<ISlotGroup, QueryGroupView>();
            private readonly List<QueryGroupView> groupOrder = new List<QueryGroupView>();
            private ResourceResponsibilitySnapshot snapshot;
            private bool disposed, invalid, operating;

            private StorageAdmissionQueryScope(Pawn pawn, Map map, Thing subject, MethodBase boundary)
            {
                parent = currentStorageQuery;
                this.pawn = pawn; this.map = map; this.subject = subject; this.boundary = boundary;
                factory = boundary?.DeclaringType == typeof(HaulAIUtility)
                    && boundary.Name == nameof(HaulAIUtility.HaulToCellStorageJob);
                if (factory)
                {
                    factoryWork = StorageProgressWork.Begin(map, null, StorageWorkLane.Mandatory, "native-factory");
                    factoryMeter = StorageProgressWork.Enter(factoryWork);
                }
                // Even an excluded nested call owns a frame, so it cannot accidentally use
                // its parent's provisional view. This check precedes every world/policy read.
                try
                {
                if (UnityData.IsInMainThread && !ResourceQueriesBlocked() && resourceTransferDepth == 0
                    && !InsideSpaceScan && !InForcedOrder && !StorageAllocationObservation.InProgress
                    && pawn?.Map == map && subject?.def?.category == ThingCategory.Item
                    && GatesVanillaStorage(map))
                {
                    game = Verse.Current.Game; epoch = activeLoadRecovery;
                    job = pawn.CurJob; jobId = job?.loadID ?? -1; driver = pawn.jobs?.curDriver;
                    context = StorageBuildingFilter.CurrentContext;
                    enabled = !factory || AnyClaims;
                }
                }
                catch (StorageWorkExhausted) { enabled = false; }
                currentStorageQuery = this;
            }

            internal bool Applies(Pawn carrier, Map world, Thing cargo)
                => enabled && ReferenceEquals(carrier, pawn) && ReferenceEquals(world, map)
                    && ReferenceEquals(cargo, subject);

            internal bool FactoryHasView(ISlotGroup group)
                => factory && Current() && group != null && groups.TryGetValue(group, out var view)
                    && view.Ready && !view.Immediate;

            private bool SupportedComposition()
            {
                if (!compositionKnown)
                {
                    compositionSupported = StorageQueryBindings.Inspected(boundary, subject)
                        || StorageQueryBindings.TryAsfQuery(boundary, subject, out queryAsf);
                    compositionKnown = true;
                }
                return compositionSupported;
            }

            private bool Current()
            {
                try
                {
                    if (StorageProgressWork.ActiveFor(map) == null) return false;
                    if (!SupportedComposition()) return false;
                    if (sourceGuard == null) sourceGuard = new ProjectionThingGuard(subject);
                    return enabled && !disposed && !invalid && UnityData.IsInMainThread
                    && ReferenceEquals(currentStorageQuery, this) && !ResourceQueriesBlocked()
                    && resourceTransferDepth == 0 && ReferenceEquals(Verse.Current.Game, game)
                    && ReferenceEquals(activeLoadRecovery, epoch) && ReferenceEquals(pawn.Map, map)
                    && ReferenceEquals(pawn.CurJob, job) && (job?.loadID ?? -1) == jobId
                    && ReferenceEquals(pawn.jobs?.curDriver, driver)
                    && StorageBuildingFilter.CurrentContext == context && sourceGuard.Matches()
                    && (snapshot == null || snapshot.Current); }
                catch (StorageWorkExhausted) { return false; }
            }

            private StorageAllocationObservationDemand Incoming(IntVec3 cell)
                => new StorageAllocationObservationDemand((object)job ?? pawn, subject, subject, pawn,
                    1, StorageFilterContext.Unload, cell, job, startsRefill: !IsDelivering(pawn, subject));

            internal bool TryCandidate(ISlotGroup group, IntVec3 cell, out ResourceAllowance status, out int allowed)
            {
                status = ResourceAllowance.Deferred; allowed = 0;
                // An unbound/foreign worker runs outside our optional frame. Keep the
                // established fresh immediate adapter for that original native path.
                if (StorageProgressWork.ActiveFor(map) == null || originalWorker != null && !originalWorker.CanShare) return false;
                if (originalWorkerDepth > 0 && (!SupportedComposition() || queryAsf == null)) return false;
                if (!Current() || operating) return !compositionKnown || compositionSupported;
                operating = true;
                bool immediate = false;
                try
                {
                    if (snapshot == null) snapshot = ObserveResourceResponsibilities(map);
                    if (!Current()) return true;
                    if (!groups.TryGetValue(group, out var view))
                    {
                        view = MakeGroup(group, cell);
                        groups.Add(group, view); groupOrder.Add(view);
                    }
                    if (view.Immediate) { immediate = true; return false; }
                    if (!view.Ready || !Current()) return true;
                    var incoming = Incoming(cell);
                    // A candidate branches from the same prior state; a successful probe does
                    // not spend capacity or become part of the next candidate's baseline.
                    var localDemands = view.DemandsAt(cell);
                    localDemands.Add(incoming);
                    var observation = StorageAllocationObservation.ObserveCells(map, group,
                        localDemands, new[] { cell }, view.Topology.Contains, StorageProgressWork.SelectedLimits());
                    if (!Usable(observation, localDemands.Count) || !InspectedResources(observation) || !Current()) return true;
                    var raw = QueryPhysicalGuard(group, observation);
                    var result = AllocateResources(observation.Cells, view.StateAt(cell),
                        new[] { observation.Requests[observation.Requests.Count - 1] },
                        CompatibleResource, observation.ObservedEligible,
                        new StorageAllocationOptions(observationComplete: true));
                    if (!result.CanPublish || !FreshProgress(observation, group, copyCursor: false)
                        || raw != null && !raw.Matches() || !view.Guard.Matches() || !Current()) return true;
                    allowed = (int)Math.Min(1, AdmittedResourceUnits(result, observation.Requests[observation.Requests.Count - 1]));
                    if (allowed > 0)
                    { view.RecordPositive(cell); originalWorker?.RecordShared(cell, group); }
                    status = ResourceAllowance.Observed;
                    return true;
                }
                catch (StorageWorkExhausted) { return true; }
                finally
                {
                    operating = false;
                    if (!immediate && status == ResourceAllowance.Deferred)
                        StorageProgressWork.NeedDiscovery(map, group, "candidate-deferred");
                }
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
                if (!StorageQueryBindings.QueryGroup(group, queryAsf)) { view.Immediate = true; return view; }
                view.Topology = QueryTopology.TryCreate(map, group);
                if (view.Topology == null) { view.Immediate = queryAsf != null; return view; }
                view.Guard.Map(map); view.Guard.Pawn(pawn); view.Guard.Thing(subject);
                view.Guard.HdPolicy(); view.Guard.StoragePolicy(group);
                if (queryAsf != null) view.Guard.AsfPolicy(group, queryAsf);
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
                var work = StorageProgressWork.Active;
                var progress = work?.Owner.Queue.Get(group);
                var hints = progress == null ? new List<IntVec3>() : work.Owner.Hints(progress);
                if (!hints.Contains(preferred)) hints.Insert(0, preferred);
                var observation = StorageAllocationObservation.Observe(map, group, view.Demands,
                    StorageProgressWork.Limits(progress), hints);
                work?.Owner.ObserveProgress(progress, observation);
                if (observation.Invalidated || !observation.CertifiedSubset || observation.Requests.Count != view.Demands.Count
                    || !InspectedResources(observation) || !Current()) return view;
                foreach (var issue in observation.Issues)
                    if (issue.Status == StorageAllocationObservationStatus.Unsupported
                        || issue.Status == StorageAllocationObservationStatus.Deferred) return view;
                var result = AllocateResources(observation.Cells, StorageAllocationState.Empty,
                    observation.Requests, CompatibleResource, observation.ObservedEligible,
                    new StorageAllocationOptions(observationComplete: observation.Complete));
                if (!result.CanPublish || !view.Topology.Current() || !FreshProgress(observation, group)
                    || !view.Guard.Matches() || !Current()) return view;
                foreach (var request in observation.Requests)
                    if (AdmittedResourceUnits(result, request) != request.Units) return view;
                work?.Owner.Remember(progress, observation.CellLocations, result.State);
                view.Initialize(result.State, observation.CellLocations);
                return view;
            }

            private static bool Usable(StorageAllocationObservationResult observation, int demands)
            {
                if (observation.Invalidated || !observation.CertifiedSubset || observation.Requests.Count != demands) return false;
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
                // An original/foreign worker result carries no optional certificate.
                // Never reserve optional work merely to finish that compatibility route.
                if (!enabled) return true;
                if (sharedSelection.HasValue && sharedSelection.Value == selected)
                {
                    var currentGroup = BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(selected));
                    if (currentGroup == null || !ReferenceEquals(currentGroup, sharedSelectionGroup)
                        || !groups.TryGetValue(currentGroup, out var sharedView) || sharedView.Immediate) return false;
                    using (var final = StorageProgressWork.Begin(map, currentGroup, StorageWorkLane.Mandatory, "original-query-final"))
                    using (StorageProgressWork.Enter(final))
                        return final != null && FinishGroup(sharedView, new[] { selected });
                }
                if (!workerSelection.HasValue || workerSelection.Value != selected) return true;
                var group = BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(selected));
                if (group == null) return false;
                if (!groupWork.TryGetValue(group, out var work)) return false;
                using (StorageProgressWork.Enter(work))
                using (work.FinalPass())
                {
                    if (!Current()) return compositionKnown && !compositionSupported;
                    if (!groups.TryGetValue(group, out var view) || view.Immediate) return true;
                    bool accepted = FinishGroup(view, new[] { selected });
                    if (!accepted) StorageProgressWork.NeedDiscovery(map, group, "final-candidate-deferred");
                    return accepted;
                }
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
                using var finalPass = StorageProgressWork.Active?.FinalPass();
                if (!view.Ready || !Current() || operating) return false;
                operating = true;
                try
                {
                    if (!view.Guard.Matches()) return false;
                    var coordinates = new List<IntVec3>(view.BaselineCells);
                    foreach (var cell in candidates) if (!coordinates.Contains(cell)) coordinates.Add(cell);
                    var demands = new List<StorageAllocationObservationDemand>(view.Demands);
                    // Each independent cell is validated against the same full prior state.
                    var observation = StorageAllocationObservation.ObserveCells(map, view.Group, demands, coordinates,
                        view.Topology.Contains, StorageProgressWork.SelectedLimits());
                    if (!Usable(observation, demands.Count) || !InspectedResources(observation) || !Current()) return false;
                    var raw = QueryPhysicalGuard(view.Group, observation);
                    var prior = AllocateResources(observation.Cells, view.State,
                        Array.Empty<StorageAllocationRequest>(), CompatibleResource, observation.ObservedEligible,
                        new StorageAllocationOptions(observationComplete: true));
                    if (!prior.CanPublish || !Current()) return false;
                    foreach (var cell in candidates)
                    {
                        var local = view.DemandsAt(cell); local.Add(Incoming(cell));
                        var fresh = StorageAllocationObservation.ObserveCells(map, view.Group, local, new[] { cell },
                            view.Topology.Contains, StorageProgressWork.SelectedLimits());
                        if (!Usable(fresh, local.Count) || !InspectedResources(fresh) || !Current()) return false;
                        var localRaw = QueryPhysicalGuard(view.Group, fresh);
                        var request = fresh.Requests[fresh.Requests.Count - 1];
                        var result = AllocateResources(fresh.Cells, view.StateAt(cell),
                            new[] { request }, CompatibleResource, fresh.ObservedEligible,
                            new StorageAllocationOptions(observationComplete: true));
                        if (!result.CanPublish || AdmittedResourceUnits(result, request) <= 0
                            || !FreshProgress(fresh, view.Group, copyCursor: false) || localRaw != null && !localRaw.Matches()
                            || !view.Guard.Matches() || !Current()) return false;
                    }
                    // Candidate policy callbacks can mutate earlier resources. Recheck the
                    // full physical certificate after the last one, before returning success.
                    return view.Topology.Current() && FreshProgress(observation, view.Group, copyCursor: false)
                        && StorageQueryBindings.QueryGroup(view.Group, queryAsf)
                        && (queryAsf == null || StorageQueryBindings.AsfQueryCurrent(boundary, subject, queryAsf))
                        && (raw == null || raw.Matches()) && view.Guard.Matches() && Current();
                }
                catch (StorageWorkExhausted) { return false; }
                finally { operating = false; }
            }

            private ForcedRawState QueryPhysicalGuard(ISlotGroup group, StorageAllocationObservationResult observation)
            {
                if (queryAsf == null) return null;
                var guard = new ForcedRawState();
                // Match the already bounded reader, without imposing the forced-writer
                // default 4096 on an original mandatory provider query that admits 32768.
                guard.ObservedCells(map, group, observation, StorageProgressWork.SelectedLimits().GridThings);
                return guard;
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
                foreach (var operation in groupWork.Values) operation.Dispose();
                groupWork.Clear(); factoryMeter?.Dispose(); factoryWork?.Dispose();
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
                        StorageProgressWork.Charge(StorageWorkKind.ObservedEligibility);
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
                        {
                            StorageProgressWork.Charge(StorageWorkKind.ObservedEligibility);
                            if (ReferenceEquals(demand.Owner, slice.Owner) && ReferenceEquals(demand.Parcel, slice.Parcel))
                            { if (!result.Contains(demand)) result.Add(demand); break; }
                        }
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
                        StorageProgressWork.Charge(StorageWorkKind.Topology);
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
                    foreach (var guard in members.Values)
                    { StorageProgressWork.Charge(StorageWorkKind.Topology); if (!guard.Matches()) return false; }
                    return true;
                }
            }
        }
    }
}
