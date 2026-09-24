using System;
using System.Collections.Generic;
using System.Threading;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    // Unconnected observation API. No production allocator/gate calls this class.
    internal static class StorageResourceProjector
    {
        [ThreadStatic] private static int entryDepth;
        internal static bool HasOpenScope => entryDepth != 0;
        internal static ProjectionOpenResult Open(StorageProjectionRequest request, StorageProjectionLimits limits, StorageProviderCatalog providers)
        {
            if (request?.Environment == null || limits == null || providers == null) return Failed(ProjectionReason.InvalidRequest);
            var thread = request.Environment.CheckThread();
            if (thread != ProjectionReason.None) return Failed(thread);
            if (entryDepth != 0) return Failed(ProjectionReason.NestedProjection);
            if (!Enum.IsDefined(typeof(ProjectionPurpose), request.Purpose) || !Enum.IsDefined(typeof(ProjectionPriorityMode), request.PriorityMode)
                || !Enum.IsDefined(typeof(StorageFilterContext), request.FilterContext) || !Enum.IsDefined(typeof(StoragePriority), request.PriorityFloor)
                || string.IsNullOrWhiteSpace(request.QueryId) || request.MapSessionId == Guid.Empty || request.Map == null
                || (request.Group?.GetType() != typeof(SlotGroup) && request.Group?.GetType() != typeof(StorageGroup))) return Failed(ProjectionReason.InvalidRequest);
            var life = providers.CheckLifetime(request.Environment);
            if (life != ProjectionReason.None) return Failed(life);
            int mapCount = Current.Game?.Maps.Count ?? 0;
            if (mapCount > limits.Work[ProjectionWorkKind.GuardChecks]) return Failed(ProjectionReason.BudgetExhausted);
            if (request.Environment.Game == null || !ReferenceEquals(Current.Game, request.Environment.Game)
                || request.MapSessionId != request.Environment.SessionId || !Current.Game.Maps.Contains(request.Map)) return Failed(ProjectionReason.SessionChanged);
            if (request.ProviderCatalogGeneration != providers.Generation) return Failed(ProjectionReason.CatalogChanged);
            if (request.Environment.TransferInProgress) return Failed(ProjectionReason.TransferInProgress);
            if (request.Group is StorageGroup linked && !ReferenceEquals(linked.Map, request.Map)) return Failed(ProjectionReason.InvalidRequest);
            if (request.Group is SlotGroup candidate)
            {
                var shape = providers.ShapeReason(candidate.parent);
                if (shape != ProjectionReason.None) return Failed(shape);
            }
            if (request.Group is SlotGroup slot && (!ReferenceEquals(slot.parent?.Map, request.Map) || !ReferenceEquals(Canonical(slot), request.Group)))
                return Failed(ProjectionReason.InvalidRequest);
            entryDepth++;
            var openWork = ProjectionWork.Cost(ProjectionWorkKind.GuardChecks, mapCount);
            try { return new ProjectionOpenResult(ProjectionStatus.Complete, new StorageProjectionScope(request, limits, providers, openWork), openWork); }
            catch { entryDepth--; return Failed(ProjectionReason.BindingFault); }
        }
        private static ProjectionOpenResult Failed(ProjectionReason reason) => new ProjectionOpenResult(ProjectionStatus.Failure(reason));
        internal static ISlotGroup Canonical(SlotGroup slot)
        {
            if (slot?.parent is Building_Storage building && building.storageGroup != null) return building.storageGroup;
            return slot;
        }
        internal static IDisposable EnterPreparation()
        {
            if (entryDepth != 0) throw new ProjectionAbort(ProjectionReason.NestedProjection);
            entryDepth++; return new Entry();
        }
        internal static void CloseEntry() { if (entryDepth <= 0) throw new InvalidOperationException("Unbalanced projection entry."); entryDepth--; }
        private sealed class Entry : IDisposable
        {
            private readonly int thread = Thread.CurrentThread.ManagedThreadId;
            private bool disposed;
            public void Dispose()
            {
                if (thread != Thread.CurrentThread.ManagedThreadId) throw new InvalidOperationException("Projection entry thread mismatch.");
                if (disposed) return; CloseEntry(); disposed = true;
            }
        }
    }

    internal sealed class StorageProjectionScope : IDisposable
    {
        private readonly StorageProjectionRequest request;
        private readonly StorageProjectionLimits limits;
        private readonly StorageProviderCatalog providers;
        private readonly Game game;
        private readonly int tick, mapId, thread;
        private readonly string groupKey;
        private readonly ProjectionListGuard<IStorageGroupMember> linkedTopology;
        private readonly ProjectionListGuard<Map> gameMaps;
        private readonly HashSet<ISlotGroupParent> linkedMembership = new HashSet<ISlotGroupParent>();
        private readonly Dictionary<long, CellHandle> cells = new Dictionary<long, CellHandle>();
        private readonly Dictionary<string, ParcelHandle> parcels = new Dictionary<string, ParcelHandle>(StringComparer.Ordinal);
        private readonly HashSet<CellEligibility> eligibilityHandles = new HashSet<CellEligibility>();
        private readonly HashSet<StorageProjectionCursor> cursors = new HashSet<StorageProjectionCursor>();
        private long sequence;
        private long retainedRecords;
        private bool disposed, operating;
        private ProjectionWorkBudget budget;
        private StorageProjectionLimits operationLimits;
        private HashSet<ISlotGroupParent> operationMembers;
        internal ProjectionWork Used { get; private set; } = ProjectionWork.Zero;
        internal StorageProjectionScope(StorageProjectionRequest request, StorageProjectionLimits limits, StorageProviderCatalog providers, ProjectionWork openWork)
        {
            this.request = request; this.limits = limits; this.providers = providers;
            game = Current.Game; tick = game.tickManager.TicksGame; mapId = request.Map.uniqueID; thread = Thread.CurrentThread.ManagedThreadId;
            gameMaps = new ProjectionListGuard<Map>(game.Maps); Used = openWork;
            groupKey = request.Group is StorageGroup linked ? "linked:" + linked.loadID : "concrete";
            if (request.Group is StorageGroup group) linkedTopology = new ProjectionListGuard<IStorageGroupMember>(group.members);
        }

        private ProjectionReason CheckOperation()
        {
            var reason = request.Environment.CheckThread();
            if (reason != ProjectionReason.None) return reason;
            if (disposed) return ProjectionReason.Disposed;
            if (operating) return ProjectionReason.OperationReentry;
            reason = providers.CheckLifetime(request.Environment);
            if (reason != ProjectionReason.None) return reason;
            if (!ReferenceEquals(Current.Game, game) || request.Environment.SessionId != request.MapSessionId
                || !gameMaps.Matches(game.Maps)) return ProjectionReason.SessionChanged;
            if (request.Environment.CatalogGeneration != request.ProviderCatalogGeneration) return ProjectionReason.CatalogChanged;
            if (request.Environment.TransferInProgress) return ProjectionReason.TransferInProgress;
            if (request.Group is StorageGroup linked && !linkedTopology.Matches(linked.members)) return ProjectionReason.GroupChanged;
            return game.tickManager.TicksGame == tick ? ProjectionReason.None : ProjectionReason.TickChanged;
        }
        private T Operation<T>(Func<T> body, Func<ProjectionReason, T> failure, StorageProjectionLimits allowance = null)
        {
            var preflight = CheckOperation();
            if (preflight != ProjectionReason.None) return failure(preflight);
            operating = true;
            operationLimits = allowance ?? limits;
            budget = new ProjectionWorkBudget(operationLimits.Work);
            operationMembers = new HashSet<ISlotGroupParent>();
            try
            {
                using (StorageCommitments.SuppressOwnGateForProjection())
                using (StorageBuildingFilter.PushContext(request.FilterContext))
                {
                    Charge(ProjectionWorkKind.GuardChecks, 1);
                    var result = body();
                    // Predicate callbacks may replace/load the game or advance an
                    // external generation even without reentering this object.
                    var after = CheckLiveDuringOperation();
                    return after == ProjectionReason.None ? result : failure(after);
                }
            }
            catch (ProjectionAbort abort) { return failure(abort.Reason); }
            catch (Exception) { return failure(ProjectionReason.BindingFault); }
            finally { Used = Used.Plus(budget.Used); budget = null; operationLimits = null; operationMembers = null; operating = false; }
        }
        private ProjectionReason CheckLiveDuringOperation()
        {
            var reason = providers.CheckLifetime(request.Environment);
            if (reason != ProjectionReason.None) return reason;
            if (!ReferenceEquals(Current.Game, game) || request.Environment.SessionId != request.MapSessionId) return ProjectionReason.SessionChanged;
            if (!gameMaps.Matches(game.Maps)) return ProjectionReason.SessionChanged;
            if (request.Environment.CatalogGeneration != request.ProviderCatalogGeneration) return ProjectionReason.CatalogChanged;
            if (request.Environment.TransferInProgress) return ProjectionReason.TransferInProgress;
            if (request.Group is StorageGroup linked && !linkedTopology.Matches(linked.members)) return ProjectionReason.GroupChanged;
            return game.tickManager.TicksGame == tick ? ProjectionReason.None : ProjectionReason.TickChanged;
        }
        private void RequireLive()
        {
            var reason = CheckLiveDuringOperation();
            if (reason != ProjectionReason.None) throw new ProjectionAbort(reason);
        }
        private void Charge(ProjectionWorkKind kind, long amount)
        { if (!budget.TryCharge(ProjectionWork.Cost(kind, amount))) throw new ProjectionAbort(ProjectionReason.BudgetExhausted); }
        private void Charge(ProjectionWork cost, ProjectionReason failure = ProjectionReason.BudgetExhausted)
        { if (!budget.TryCharge(cost)) throw new ProjectionAbort(failure); }
        private ProjectionWork CurrentWork => budget?.Used ?? ProjectionWork.Zero;

        internal CellProjection ObserveCell(IntVec3 cell) => Operation(() => ObserveCellCore(cell), r => FailedCell(cell, r));

        private CellProjection ObserveCellCore(IntVec3 cell)
        {
            if (!cell.IsValid || !cell.InBounds(request.Map)) throw new ProjectionAbort(ProjectionReason.InvalidRequest);
            if (cells.Count >= limits.RetainedCells) throw new ProjectionAbort(ProjectionReason.RetainedStateLimit);
            Charge(ProjectionWorkKind.Coordinates, 1);
            var slot = request.Map.haulDestinationManager.SlotGroupAt(cell);
            StorageProjectionMemberGuard member;
            try { member = ResolveMember(slot); }
            catch (ProjectionAbort abort) { return FailedCell(cell, abort.Reason, ParentKey(slot?.parent)); }
            if (!member.ContainsObservedCell(cell)) throw new ProjectionAbort(ProjectionReason.GroupChanged);
            bool asf = providers.Asf != null && member.Parent.GetType() == providers.Asf.ParentType;
            var grid = request.Map.thingGrid.ThingsListAt(cell);
            long retainedCost = 7L * grid.Count + 2;
            if (retainedCost > limits.RetainedRecords - retainedRecords) throw new ProjectionAbort(ProjectionReason.RetainedStateLimit);
            Charge(ProjectionWorkKind.GridEntries, grid.Count);
            var gridVersion = new ProjectionListGuard<Thing>(grid);
            var all = new ProjectionThingGuard[grid.Count];
            var items = new List<ProjectionThingGuard>();
            var itemIds = new HashSet<int>();
            for (int i = 0; i < grid.Count; i++)
            {
                var thing = grid[i];
                if (thing?.def == null || !thing.Spawned || thing.Destroyed || thing.Map != request.Map)
                    throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
                var stamp = new ProjectionThingGuard(thing); all[i] = stamp;
                if (thing.def.category != ThingCategory.Item) continue;
                if (thing.stackCount <= 0 || thing.def.stackLimit <= 0) throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
                if (thing.def.size.x != 1 || thing.def.size.z != 1) return FailedCell(cell, ProjectionReason.UnsupportedFootprint, member.Key);
                if (thing.Position != cell || !ReferenceEquals(thing.ParentHolder, request.Map)) throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
                if (!itemIds.Add(thing.thingIDNumber)) throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
                items.Add(stamp);
            }
            Charge(ProjectionWorkKind.CellLimitCalls, 1);
            int maximum = cell.GetMaxItemsAllowedInCell(request.Map);
            RequireLive();
            if (maximum < 0) throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
            AsfProjectionState providerState = null;
            if (asf)
            {
                Charge(ProjectionWorkKind.ProviderVisits, 1L + 4L * items.Count);
                providerState = providers.Asf.Read((Building_Storage)member.Parent, cell);
                RequireLive();
                if (providerState.Packed || providerState.CellLimit != maximum || providerState.CellCount != items.Count
                    || providerState.Count < items.Count || providerState.CellWiseCount < providerState.Count || providerState.SlotLimit < 0
                    || providerState.AnyFree != (providerState.CellWiseCount < providerState.SlotLimit))
                    throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
            }
            string prefix = request.MapSessionId.ToString("N") + "/" + mapId + "/" + member.Key + "/" + cell.x + "," + cell.z;
            var stacks = new List<StorageStackResource>();
            foreach (var stamp in items)
            {
                bool? target = null;
                if (asf)
                {
                    bool registered = providers.Asf.RegisteredExactly(providerState, (Building_Storage)member.Parent, stamp.Thing, cell);
                    RequireLive();
                    if (!registered)
                        throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
                    Charge(ProjectionWorkKind.Compatibility, 2);
                    target = providers.Asf.TargetValid((Building_Storage)member.Parent, stamp.Thing);
                    RequireLive();
                }
                stacks.Add(new StorageStackResource(prefix + "/stack:" + stamp.Id, stamp.Id, stamp.Def.defName, stamp.Count, stamp.StackLimit, target));
            }
            long id = ++sequence;
            Charge(ProjectionWorkKind.OutputRecords, 3L * items.Count + 2);
            CellProjection Row() => new CellProjection(id, request, mapId, tick, cell, member.Key, groupKey,
                asf ? providers.Asf.Identity : "RimWorld physical slots", prefix + "/vacant", maximum, items.Count,
                stacks, ProjectionStatus.Complete, CurrentWork, providerState?.CellCount, providerState?.Count,
                providerState?.CellWiseCount, providerState?.SlotLimit, providerState?.PerformanceFish, grid.Count, providerState?.AnyFree, providerState?.Packed);
            var handle = new CellHandle(Row(), member, grid, gridVersion, all, items, providerState);
            ValidateCell(handle);
            handle.Value = Row();
            cells.Add(id, handle);
            retainedRecords += retainedCost;
            return handle.Value;
        }

        private StorageProjectionMemberGuard ResolveMember(SlotGroup slot)
        {
            if (slot == null || slot.GetType() != typeof(SlotGroup)) throw new ProjectionAbort(ProjectionReason.UnsupportedProvider);
            var parentShape = providers.ShapeReason(slot.parent);
            if (parentShape != ProjectionReason.None) throw new ProjectionAbort(parentShape);
            if (slot?.parent == null || !ReferenceEquals(slot.parent.Map, request.Map) || !ReferenceEquals(StorageResourceProjector.Canonical(slot), request.Group))
                throw new ProjectionAbort(ProjectionReason.GroupChanged);
            if (!operationMembers.Contains(slot.parent))
            { Charge(ProjectionWorkKind.Members, 1); operationMembers.Add(slot.parent); }
            if (request.Group is StorageGroup linked && !linkedMembership.Contains(slot.parent))
            {
                if (linkedMembership.Count >= limits.RetainedMembers) throw new ProjectionAbort(ProjectionReason.RetainedStateLimit);
                Charge(ProjectionWorkKind.GuardChecks, linked.members.Count);
                bool found = false;
                for (int i = 0; i < linked.members.Count; i++) if (ReferenceEquals(linked.members[i], slot.parent)) { found = true; break; }
                if (!found) throw new ProjectionAbort(ProjectionReason.GroupChanged);
                linkedMembership.Add(slot.parent);
            }
            if (slot.parent is Building_Storage building) Charge(ProjectionWorkKind.Compatibility, building.AllComps.Count);
            var reason = providers.Classify(slot.parent, out _);
            if (reason != ProjectionReason.None) throw new ProjectionAbort(reason);
            PreparedProjectionZoneCells zoneCells = null;
            if (slot.parent is Zone_Stockpile zone)
            {
                reason = providers.ZoneCells(zone, out zoneCells);
                if (reason != ProjectionReason.None) throw new ProjectionAbort(reason);
            }
            var member = new StorageProjectionMemberGuard(slot.parent, request.Map, request.Group, zoneCells);
            if (!member.Matches()) throw new ProjectionAbort(ProjectionReason.GroupChanged);
            return member;
        }
        private void ValidateCell(CellHandle handle)
        {
            RequireLive();
            Charge(ProjectionWorkKind.GuardChecks, 1);
            if (request.Group is StorageGroup linked && !linkedTopology.Matches(linked.members)) throw new ProjectionAbort(ProjectionReason.GroupChanged);
            if (!handle.Member.Matches() || !handle.Member.ContainsObservedCell(handle.Value.Cell)
                || !ReferenceEquals(request.Map.haulDestinationManager.SlotGroupAt(handle.Value.Cell), handle.Member.Slot))
                throw new ProjectionAbort(ProjectionReason.GroupChanged);
            var actual = request.Map.thingGrid.ThingsListAt(handle.Value.Cell);
            Charge(ProjectionWorkKind.GridEntries, actual.Count);
            if (!handle.GridGuard.Matches(actual) || actual.Count != handle.All.Length) throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
            for (int i = 0; i < actual.Count; i++)
                if (!ReferenceEquals(actual[i], handle.All[i].Thing) || !handle.All[i].Matches()) throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
            Charge(ProjectionWorkKind.CellLimitCalls, 1);
            int actualLimit = handle.Value.Cell.GetMaxItemsAllowedInCell(request.Map);
            RequireLive();
            if (actualLimit != handle.Value.MaximumSlots) throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
            if (handle.Asf != null)
            {
                Charge(ProjectionWorkKind.ProviderVisits, 1L + 4L * handle.Items.Count);
                var current = providers.Asf.Read((Building_Storage)handle.Member.Parent, handle.Value.Cell);
                RequireLive();
                if (!handle.Asf.Same(current)) throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
                for (int i = 0; i < handle.Items.Count; i++)
                {
                    var item = handle.Items[i].Thing;
                    bool registered = providers.Asf.RegisteredExactly(current, (Building_Storage)handle.Member.Parent, item, handle.Value.Cell);
                    RequireLive();
                    if (!registered)
                        throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
                    Charge(ProjectionWorkKind.Compatibility, 2);
                    bool targetValid = providers.Asf.TargetValid((Building_Storage)handle.Member.Parent, item);
                    RequireLive();
                    if (targetValid != handle.Value.Stacks[i].ProviderTargetValid)
                        throw new ProjectionAbort(ProjectionReason.ProviderStateMismatch);
                }
            }
        }

        private CellProjection FailedCell(IntVec3 cell, ProjectionReason reason, string member = null) =>
            new CellProjection(0, request, mapId, tick, cell, member, groupKey, null, null, null, null, null, ProjectionStatus.Failure(reason),
                operating && Thread.CurrentThread.ManagedThreadId == thread ? CurrentWork : ProjectionWork.Zero);

        internal CellEligibility ObserveEligibility(StorageParcelProbe parcel, CellProjection cell) =>
            Operation(() => EligibilityCore(parcel, GetCell(cell)), r => FailedEligibility(parcel, cell, r));
        internal ProjectionValidation Recheck(StorageParcelProbe parcel, CellProjection cell, CellEligibility eligibility) => Operation(() =>
        {
            if (eligibility == null || !eligibilityHandles.Contains(eligibility) || eligibility.ObservationId != cell?.ObservationId
                || eligibility.ParcelId != parcel?.ParcelId) throw new ProjectionAbort(ProjectionReason.WrongScope);
            var fresh = EligibilityCore(parcel, GetCell(cell));
            return new ProjectionValidation(fresh.Status, fresh);
        }, r => new ProjectionValidation(ProjectionStatus.Failure(r)));

        private CellHandle GetCell(CellProjection value)
        {
            if (value == null || !cells.TryGetValue(value.ObservationId, out var handle) || !ReferenceEquals(handle.Value, value))
                throw new ProjectionAbort(ProjectionReason.WrongScope);
            return handle;
        }
        private ParcelHandle GetParcel(StorageParcelProbe probe)
        {
            if (probe == null || string.IsNullOrWhiteSpace(probe.ParcelId) || probe.RequestedUnits <= 0 || probe.Subject?.def == null
                || probe.Subject.Destroyed || probe.Subject.stackCount <= 0 || probe.RequestedUnits > probe.Subject.stackCount
                || probe.Subject.def.category != ThingCategory.Item || probe.Subject.def.stackLimit <= 0 || probe.Faction == null)
                throw new ProjectionAbort(ProjectionReason.InvalidRequest);
            if (probe.Subject.def.size.x != 1 || probe.Subject.def.size.z != 1) throw new ProjectionAbort(ProjectionReason.UnsupportedFootprint);
            // ThingFilter.Allows unwraps these before evaluating the filter.
            // Reject the unbound shape before any such predicate is invoked.
            if (probe.Subject is MinifiedThing) throw new ProjectionAbort(ProjectionReason.UnreviewedPredicate);
            if (probe.Carrier == null && request.Purpose != ProjectionPurpose.AvailabilityObservation) throw new ProjectionAbort(ProjectionReason.InvalidRequest);
            if (probe.Carrier != null && (!probe.Carrier.Spawned || probe.Carrier.Map != request.Map || probe.Carrier.Faction != probe.Faction))
                throw new ProjectionAbort(ProjectionReason.InvalidRequest);
            // This first adapter has inspected floor/pawn custody only. Avoid an
            // unbounded or custom holder-chain walk/materialization probe.
            if (!probe.Subject.Spawned && !(probe.Subject.ParentHolder is Pawn_InventoryTracker) && !(probe.Subject.ParentHolder is Pawn_CarryTracker))
                throw new ProjectionAbort(ProjectionReason.UnsupportedSourceHolder);
            if (probe.Subject.MapHeld != request.Map || !probe.Subject.SpawnedOrAnyParentSpawned) throw new ProjectionAbort(ProjectionReason.SubjectChanged);
            if (parcels.TryGetValue(probe.ParcelId, out var known))
            {
                if (!ReferenceEquals(known.Probe.Subject, probe.Subject) || !ReferenceEquals(known.Probe.Carrier, probe.Carrier)
                    || !ReferenceEquals(known.Probe.Faction, probe.Faction) || known.Probe.RequestedUnits != probe.RequestedUnits)
                    throw new ProjectionAbort(ProjectionReason.InvalidRequest);
                if (!known.Subject.Matches()) throw new ProjectionAbort(ProjectionReason.SubjectChanged);
                return known;
            }
            if (parcels.Count >= limits.RetainedParcels) throw new ProjectionAbort(ProjectionReason.RetainedStateLimit);
            var result = new ParcelHandle(probe); parcels.Add(probe.ParcelId, result); return result;
        }

        private CellEligibility EligibilityCore(StorageParcelProbe probe, CellHandle handle)
        {
            var parcel = GetParcel(probe);
            ValidateCell(handle);
            long retainedCost = 20L + 10L * handle.Items.Count;
            if (retainedCost > limits.RetainedRecords - retainedRecords) throw new ProjectionAbort(ProjectionReason.RetainedStateLimit);
            Charge(ProjectionWorkKind.OutputRecords, 10L + 5L * handle.Items.Count);
            var predicates = new List<ProjectionPredicateResult>();
            foreach (var name in new[] { "destination-enabled", "destination-faction", "selected-priority", "effective-thing-filter", "concrete-fixed-filter",
                "asf-declared-fixed-filter", "asf-actual-member-capacity", "native-IsGoodStoreCell", "hd-explicit-context-filter" })
                predicates.Add(new ProjectionPredicateResult(name, ProjectionEligibilityState.NotEvaluated, ProjectionReason.None));
            var edges = new List<StorageTopUpEdge>();
            bool refused = false;
            bool Verdict(string name, bool result, ProjectionReason reason, int? target = null)
            {
                var record = new ProjectionPredicateResult(name, result ? ProjectionEligibilityState.Eligible : ProjectionEligibilityState.Refused,
                    result ? ProjectionReason.None : reason, target);
                int index = target.HasValue ? -1 : predicates.FindIndex(p => p.Predicate == name && !p.TargetId.HasValue);
                if (index < 0) predicates.Add(record); else predicates[index] = record;
                if (!result && target == null) refused = true;
                RequireLive();
                return result;
            }
            CellEligibility Finish(ProjectionReason reason = ProjectionReason.None)
            {
                if (reason == ProjectionReason.None)
                {
                    try
                    {
                        RequireLive();
                        if (!parcel.Subject.Matches()) throw new ProjectionAbort(ProjectionReason.SubjectChanged);
                        ValidateCell(handle);
                    }
                    catch (ProjectionAbort abort) { reason = abort.Reason; }
                    catch (Exception) { reason = ProjectionReason.BindingFault; }
                }
                var status = reason == ProjectionReason.None ? ProjectionStatus.Complete : ProjectionStatus.Failure(reason);
                bool available = status.Usable && !refused;
                var result = new CellEligibility(handle.Value.ObservationId, probe.ParcelId, status,
                    refused ? ProjectionEligibilityState.Refused : available ? ProjectionEligibilityState.Eligible : ProjectionEligibilityState.NotEvaluated,
                    predicates, available ? edges : new List<StorageTopUpEdge>(), available && handle.Value.VacantSlots > 0,
                    available && handle.Value.VacantSlots > 0 ? (int?)probe.Subject.def.stackLimit : null, CurrentWork);
                if (eligibilityHandles.Count >= limits.RetainedCells) throw new ProjectionAbort(ProjectionReason.RetainedStateLimit);
                eligibilityHandles.Add(result); retainedRecords += retainedCost; return result;
            }
            try
            {
            var parent = handle.Member.Parent;
            if (!Verdict("destination-enabled", parent.HaulDestinationEnabled, ProjectionReason.DestinationDisabled)) return Finish();
            if (parent is Thing destination && !Verdict("destination-faction", destination.Faction == probe.Faction, ProjectionReason.WrongFaction)) return Finish();
            var settings = handle.Member.Slot.Settings;
            var priority = settings.Priority;
            bool better = request.PriorityMode == ProjectionPriorityMode.WithinSelectedGroup
                || (request.PriorityMode == ProjectionPriorityMode.StrictlyBetter ? (int)priority > (int)request.PriorityFloor : (int)priority >= (int)request.PriorityFloor);
            if (!Verdict("selected-priority", better, ProjectionReason.BelowPriority)) return Finish();
            if (!providers.TryReady(parent, out var memberFixed)) return Finish(ProjectionReason.ProviderInitializing);
            ISlotGroupParent effectiveFixedParent = parent;
            if (request.Group is StorageGroup linked)
            {
                if (linked.members.Count == 0 || !(linked.members[0] is ISlotGroupParent first) || !providers.TryReady(first, out _))
                    return Finish(ProjectionReason.ProviderInitializing);
                if (!ReferenceEquals(settings.owner, linked)) return Finish(ProjectionReason.GroupChanged);
                effectiveFixedParent = first;
            }
            else if (!ReferenceEquals(settings.owner, parent)) return Finish(ProjectionReason.GroupChanged);
            var effectiveFixed = request.Group is StorageGroup ? providers.BaseFixed(effectiveFixedParent) : memberFixed;
            foreach (var filter in new[] { settings, effectiveFixed, memberFixed })
            {
                var filterReason = providers.CheckFilter(filter, budget);
                if (filterReason != ProjectionReason.None) return Finish(filterReason);
            }
            if (!Verdict("effective-thing-filter", settings.AllowedToAccept(probe.Subject), ProjectionReason.ThingFilterRefused)) return Finish();
            if (!Verdict("concrete-fixed-filter", memberFixed.AllowedToAccept(probe.Subject), ProjectionReason.MemberFixedFilterRefused)) return Finish();
            if (handle.Asf != null)
            {
                var filterReason = providers.CheckFilter(memberFixed, budget);
                if (filterReason != ProjectionReason.None) return Finish(filterReason);
                if (!Verdict("asf-declared-fixed-filter", providers.Asf.FixedAllows((Building_Storage)parent, probe.Subject), ProjectionReason.MemberFixedFilterRefused)) return Finish();
                // Reserve both this indexed preflight and HasCapacityForThing's
                // possible full-member scan atomically before reading any item.
                bool outside = !probe.Subject.Spawned || !handle.Asf.Occupied.Contains(probe.Subject.Position);
                long potential = !handle.Asf.AnyFree && outside && !handle.Asf.PerformanceFish ? handle.Asf.Count : 0;
                if (potential > 0 && !operationLimits.AllowMemberScans) return Finish(ProjectionReason.ProviderScanRequired);
                Charge(ProjectionWork.ProviderMemberScan((int)potential), ProjectionReason.ProviderScanRequired);
                for (int i = 0; i < potential; i++)
                {
                    var target = providers.Asf.ItemAt(handle.Asf, i);
                    if (target?.def == null) return Finish(ProjectionReason.ProviderStateMismatch);
                    // One type check, each comp's preflight, and each possible
                    // comp callback in the later opaque stack predicate.
                    Charge(ProjectionWorkKind.Compatibility, target is ThingWithComps tc ? 2L * tc.AllComps.Count + 1 : 1);
                    if (!providers.StackPredicateReviewed(target)) return Finish(ProjectionReason.UnreviewedPredicate);
                }
                if (!Verdict("asf-actual-member-capacity", providers.Asf.HasCapacity((Building_Storage)parent, probe.Subject), ProjectionReason.ProviderRefused)) return Finish();
            }
            // Native obstruction can call directional CanStackWith on every
            // storable grid Thing, including one that is not an eligible resource.
            foreach (var candidate in handle.All)
            {
                Charge(ProjectionWorkKind.Compatibility, 1);
                if (!candidate.Def.EverStorable(false)) continue;
                Charge(ProjectionWorkKind.Compatibility, candidate.Thing is ThingWithComps tc ? 2L * tc.AllComps.Count + 1 : 1);
                if (!providers.StackPredicateReviewed(candidate.Thing)) return Finish(ProjectionReason.UnreviewedPredicate);
            }
            var nativeCost = ProjectionWork.NativeCellPredicate(handle.All.Length, probe.Carrier != null);
            if (providers.Asf != null) nativeCost = nativeCost.Plus(ProjectionWork.Cost(ProjectionWorkKind.Compatibility, handle.All.Length))
                .Plus(ProjectionWork.Cost(ProjectionWorkKind.ProviderVisits, 2L * handle.All.Length + 1));
            Charge(nativeCost);
            if (!Verdict("native-IsGoodStoreCell", StoreUtility.IsGoodStoreCell(handle.Value.Cell, request.Map, probe.Subject, probe.Carrier, probe.Faction), ProjectionReason.NativeCellRefused)) return Finish();
            if (request.FilterContext != StorageFilterContext.Unload)
            {
                Charge(ProjectionWorkKind.Filters, 1);
                bool allowed = HaulersDreamMod.Settings?.storageBuildingFilter?.IsGroupAllowed(handle.Member.Slot) ?? true;
                if (!Verdict("hd-explicit-context-filter", allowed, ProjectionReason.HdContextRefused)) return Finish();
            }
            for (int i = 0; i < handle.Items.Count; i++)
            {
                var item = handle.Items[i];
                var resource = handle.Value.Stacks[i];
                if (resource.Deficit == 0) continue;
                Charge(ProjectionWorkKind.Compatibility, 3L + (item.Thing is ThingWithComps wc ? wc.AllComps.Count : 0));
                if (!Verdict("different-target", !ReferenceEquals(item.Thing, probe.Subject), ProjectionReason.SelfTarget, item.Id)) continue;
                if (!Verdict("target-ever-storable", item.Def.EverStorable(false), ProjectionReason.InvalidStackTarget, item.Id)) continue;
                if (!Verdict("directional-CanStackWith", item.Thing.CanStackWith(probe.Subject), ProjectionReason.InvalidStackTarget, item.Id)) continue;
                if (handle.Asf != null && !Verdict("asf-individual-target", providers.Asf.TargetValid((Building_Storage)parent, item.Thing), ProjectionReason.InvalidStackTarget, item.Id)) continue;
                edges.Add(new StorageTopUpEdge(resource.ResourceKey, item.Id, Math.Min(resource.Deficit, probe.RequestedUnits)));
            }
            return Finish();
            }
            catch (ProjectionAbort abort) { return Finish(abort.Reason); }
            catch (Exception) { return Finish(ProjectionReason.BindingFault); }
        }

        private CellEligibility FailedEligibility(StorageParcelProbe probe, CellProjection cell, ProjectionReason reason) =>
            new CellEligibility(cell?.ObservationId ?? 0, probe?.ParcelId, ProjectionStatus.Failure(reason), ProjectionEligibilityState.NotEvaluated,
                new List<ProjectionPredicateResult>(), new List<StorageTopUpEdge>(), false, null,
                operating && Thread.CurrentThread.ManagedThreadId == thread ? CurrentWork : ProjectionWork.Zero);

        internal StorageProjectionPage ObserveGroupPage(StorageProjectionCursor cursor = null, StorageProjectionLimits pageAllowance = null) =>
            Operation(() => PageCore(ref cursor), r => FailedPage(cursor, r), pageAllowance);

        private StorageProjectionPage PageCore(ref StorageProjectionCursor cursor)
        {
            if (cursor == null)
            {
                if (cursors.Count != 0) throw new ProjectionAbort(ProjectionReason.RetainedStateLimit);
                cursor = new StorageProjectionCursor(this, request.Group as StorageGroup); cursors.Add(cursor);
            }
            else if (!ReferenceEquals(cursor.Owner, this) || !cursors.Contains(cursor)) throw new ProjectionAbort(ProjectionReason.WrongScope);
            // Reserve copying each page row plus a bounded pending sample twice
            // (owned list and immutable DTO). Never copy the retained whole ledger.
            int maximumPageRows = (int)Math.Min(operationLimits.Work[ProjectionWorkKind.Coordinates], limits.RetainedCells);
            Charge(ProjectionWorkKind.OutputRecords, 2L * maximumPageRows + 130);
            Charge(ProjectionWorkKind.GuardChecks, 2L * (cursor.Guards.Count + 1));
            ValidateCursor(cursor);
            var output = new List<CellProjection>();
            int pendingAtStart = cursor.Pending.Count;
            // Once discovery ends, an explicitly larger page allowance can revisit
            // named oversized cells. Unsupported members stay named and incomplete.
            if (cursor.DiscoveryEnded)
            {
                for (int attempt = 0; attempt < pendingAtStart && attempt < maximumPageRows && cursor.Pending.Count > 0; attempt++)
                {
                    if (!budget.TryCharge(ProjectionWork.Cost(ProjectionWorkKind.GuardChecks, 1))) break;
                    int i = cursor.PendingRetryOffset % cursor.Pending.Count;
                    var pending = cursor.Pending[i];
                    cursor.PendingRetryOffset = (i + 1) % cursor.Pending.Count;
                    if (!pending.Cell.IsValid) continue;
                    CellProjection retry;
                    try { retry = ObserveCellCore(pending.Cell); }
                    catch (ProjectionAbort abort) { retry = FailedCell(pending.Cell, abort.Reason, pending.MemberKey); }
                    output.Add(retry);
                    if (retry.Status.Usable)
                    {
                        // Constant-time removal from this private diagnostic list.
                        // No interior List.RemoveAt shift hidden in a capped page.
                        int last = cursor.Pending.Count - 1;
                        if (i != last) cursor.Pending[i] = cursor.Pending[last];
                        cursor.Pending.RemoveAt(last); cursor.PendingRetryOffset = i;
                    }
                    if (retry.Status.Reason == ProjectionReason.BudgetExhausted) break;
                }
            }
            else
            {
                int members = cursor.Members?.Count ?? 1;
                while (cursor.MemberIndex < members)
                {
                    ISlotGroupParent parent = cursor.Members == null ? ((SlotGroup)request.Group).parent : cursor.Members[cursor.MemberIndex] as ISlotGroupParent;
                    if (parent == null)
                    {
                        if (cursor.Pending.Count >= limits.RetainedCells || !budget.TryCharge(ProjectionWork.Cost(ProjectionWorkKind.Members, 1))) break;
                        cursor.Pending.Add(new ProjectionUnresolvedCell("non-slot-member", IntVec3.Invalid, ProjectionReason.UnsupportedProvider, cursor.MemberIndex));
                        cursor.MemberIndex++; cursor.CellIndex = 0; continue;
                    }
                    if (!cursor.Guards.TryGetValue(parent, out var guard))
                    {
                        if (cursor.Guards.Count >= limits.RetainedMembers) break;
                        try
                        {
                            if (!operationMembers.Contains(parent))
                            { Charge(ProjectionWorkKind.Members, 1); operationMembers.Add(parent); }
                            Charge(ProjectionWorkKind.GuardChecks, 2);
                            var parentShape = providers.ShapeReason(parent);
                            if (parentShape != ProjectionReason.None) throw new ProjectionAbort(parentShape);
                            guard = ResolveMember(parent.GetSlotGroup());
                        }
                        catch (ProjectionAbort abort)
                        {
                            if (abort.Reason == ProjectionReason.BudgetExhausted) break;
                            if (cursor.Pending.Count >= limits.RetainedCells) break;
                            cursor.Pending.Add(new ProjectionUnresolvedCell(ParentKey(parent), IntVec3.Invalid, abort.Reason, cursor.MemberIndex));
                            cursor.MemberIndex++; cursor.CellIndex = 0; continue;
                        }
                        cursor.Guards.Add(parent, guard);
                    }
                    bool pageEnded = false;
                    while (cursor.CellIndex < guard.CellCount)
                    {
                        if (cursor.Seen.Count >= limits.RetainedCells || cursor.Pending.Count >= limits.RetainedCells) { pageEnded = true; break; }
                        var address = guard.CellAt(cursor.CellIndex);
                        if (!budget.TryCharge(ProjectionWork.Cost(ProjectionWorkKind.Coordinates, 1))) { pageEnded = true; break; }
                        if (!cursor.Seen.Add(address)) throw new ProjectionAbort(ProjectionReason.GroupChanged);
                        cursor.CellIndex++;
                        CellProjection cell;
                        // Coordinate discovery has a distinct charge from the cell
                        // observation/recheck. A failed census advances discovery but
                        // remains explicitly unresolved, allowing later usable cells.
                        try { cell = ObserveCellCore(address); }
                        catch (ProjectionAbort abort) { cell = FailedCell(address, abort.Reason, guard.Key); }
                        output.Add(cell);
                        if (!cell.Status.Usable) cursor.Pending.Add(new ProjectionUnresolvedCell(guard.Key, address, cell.Status.Reason));
                    }
                    if (pageEnded) break;
                    cursor.MemberIndex++; cursor.CellIndex = 0;
                }
                cursor.DiscoveryEnded = cursor.MemberIndex >= members;
            }
            ValidateCursor(cursor);
            bool complete = cursor.DiscoveryEnded && cursor.Pending.Count == 0;
            bool requested = complete || output.Count > 0;
            foreach (var row in output) if (!row.Status.Usable) requested = false;
            var continuation = cursor.Guards.Count >= limits.RetainedMembers || cursor.Seen.Count >= limits.RetainedCells
                ? ProjectionReason.RetainedStateLimit : ProjectionReason.BudgetExhausted;
            var unresolved = new List<ProjectionUnresolvedCell>();
            int sampleCount = Math.Min(cursor.Pending.Count, 64);
            int sampleStart = cursor.Pending.Count == 0 ? 0 : cursor.PendingOutputOffset % cursor.Pending.Count;
            for (int i = 0; i < sampleCount; i++) unresolved.Add(cursor.Pending[(sampleStart + i) % cursor.Pending.Count]);
            cursor.PendingOutputOffset = cursor.Pending.Count == 0 ? 0 : (sampleStart + sampleCount) % cursor.Pending.Count;
            if (!cursor.DiscoveryEnded)
            {
                ISlotGroupParent nextParent = cursor.Members == null ? ((SlotGroup)request.Group).parent : cursor.Members[cursor.MemberIndex] as ISlotGroupParent;
                var nextAddress = IntVec3.Invalid;
                if (nextParent != null && cursor.Guards.TryGetValue(nextParent, out var nextGuard) && cursor.CellIndex < nextGuard.CellCount)
                    nextAddress = nextGuard.CellAt(cursor.CellIndex);
                unresolved.Add(new ProjectionUnresolvedCell(ParentKey(nextParent), nextAddress, continuation, cursor.MemberIndex));
            }
            else if (cursor.Pending.Count > 0) continuation = cursor.Pending[0].Reason;
            var status = complete ? ProjectionStatus.Complete : ProjectionStatus.Failure(continuation);
            return new StorageProjectionPage(status, output, unresolved, cursor, requested, complete, CurrentWork,
                cursor.DiscoveryEnded ? (int?)null : cursor.MemberIndex, cursor.DiscoveryEnded ? (int?)null : cursor.CellIndex,
                cursor.Pending.Count, sampleStart, sampleCount);
        }
        private void ValidateCursor(StorageProjectionCursor cursor)
        {
            if (cursor.Invalidated) throw new ProjectionAbort(ProjectionReason.GroupChanged);
            if (cursor.Members != null && !cursor.MembersGuard.Matches(((StorageGroup)request.Group).members))
            { cursor.Invalidated = true; throw new ProjectionAbort(ProjectionReason.GroupChanged); }
            foreach (var guard in cursor.Guards.Values)
                if (!guard.Matches()) { cursor.Invalidated = true; throw new ProjectionAbort(ProjectionReason.GroupChanged); }
        }
        private static string ParentKey(ISlotGroupParent parent) => parent is Thing thing ? "building:" + thing.thingIDNumber
            : parent is Zone zone ? "zone:" + zone.ID : "unsupported-parent";
        private StorageProjectionPage FailedPage(StorageProjectionCursor cursor, ProjectionReason reason) => new StorageProjectionPage(
            ProjectionStatus.Failure(reason), new List<CellProjection>(), new List<ProjectionUnresolvedCell>(),
            reason == ProjectionReason.WrongScope ? null : cursor, false, false,
            operating && Thread.CurrentThread.ManagedThreadId == thread ? CurrentWork : ProjectionWork.Zero);

        public void Dispose()
        {
            if (thread != Thread.CurrentThread.ManagedThreadId) throw new InvalidOperationException("Projection scope must be disposed on its owning thread.");
            if (disposed) return;
            if (operating) throw new InvalidOperationException("Cannot dispose a projection during a predicate callback.");
            cells.Clear(); parcels.Clear(); eligibilityHandles.Clear(); cursors.Clear();
            disposed = true; StorageResourceProjector.CloseEntry();
        }
        private sealed class ParcelHandle
        {
            internal StorageParcelProbe Probe { get; }
            internal ProjectionThingGuard Subject { get; }
            internal ParcelHandle(StorageParcelProbe probe) { Probe = probe; Subject = new ProjectionThingGuard(probe.Subject); }
        }
        private sealed class CellHandle
        {
            internal CellProjection Value { get; set; }
            internal StorageProjectionMemberGuard Member { get; }
            internal List<Thing> Grid { get; }
            internal ProjectionListGuard<Thing> GridGuard { get; }
            internal ProjectionThingGuard[] All { get; }
            internal List<ProjectionThingGuard> Items { get; }
            internal AsfProjectionState Asf { get; }
            internal CellHandle(CellProjection value, StorageProjectionMemberGuard member, List<Thing> grid, ProjectionListGuard<Thing> guard,
                ProjectionThingGuard[] all, List<ProjectionThingGuard> items, AsfProjectionState asf)
            { Value = value; Member = member; Grid = grid; GridGuard = guard; All = all; Items = items; Asf = asf; }
        }
    }
}
