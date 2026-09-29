using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    // Fresh, owned observations for one immediate allocation transaction. No claim writes,
    // reservations, janitor/adoption calls, static physical cache or linked CellsList getter.
    internal static partial class StorageAllocationObservation
    {
        [ThreadStatic] private static bool observing;
        internal static bool InProgress => observing;

        // A scope-owned candidate/final check. Observe only these concrete coordinates;
        // never rebuild the group's ordinary page inside every native cell predicate.
        internal static StorageAllocationObservationResult ObserveCells(Map map, ISlotGroup group,
            IReadOnlyList<StorageAllocationObservationDemand> demands, IReadOnlyList<IntVec3> coordinates,
            Func<SlotGroup, IntVec3, bool> membership = null)
        {
            if (!UnityData.IsInMainThread) return Unavailable("main-thread-required");
            if (StorageCommitments.ResourceQueriesBlocked()) return Unavailable("load-restoration-pending");
            if (observing) return Unavailable("nested-observation");
            var reader = new Reader(map, group, demands, new StorageAllocationObservationLimits(), coordinates, true, membership);
            observing = true;
            try
            {
                using (StorageCommitments.SuppressOwnGateForProjection()) return reader.Read();
            }
            finally { observing = false; }
        }
        internal static StorageAllocationObservationResult Observe(Map map, ISlotGroup group,
            IReadOnlyList<StorageAllocationObservationDemand> demands, StorageAllocationObservationLimits limits = null,
            IReadOnlyList<IntVec3> preferredCells = null)
            => ObserveForLoad(map, group, demands, limits, preferredCells, null);

        internal static StorageAllocationObservationResult ObserveForLoad(Map map, ISlotGroup group,
            IReadOnlyList<StorageAllocationObservationDemand> demands, StorageAllocationObservationLimits limits,
            IReadOnlyList<IntVec3> preferredCells, StorageCommitments.LoadRecoveryTicket ticket)
        {
            // Reader captures the current game/tick. Refuse before constructing it or reading
            // caller-owned world collections; even an unavailable result must stay observational.
            if (!UnityData.IsInMainThread) return Unavailable("main-thread-required");
            if (StorageCommitments.ResourceQueriesBlocked(ticket)) return Unavailable("load-restoration-pending");
            if (observing) return Unavailable("nested-observation");
            var reader = new Reader(map, group, demands, limits ?? new StorageAllocationObservationLimits(), preferredCells);
            observing = true;
            try
            {
                using (StorageCommitments.SuppressOwnGateForProjection()) return reader.Read();
            }
            finally { observing = false; }
        }

        private static StorageAllocationObservationResult Unavailable(string reason)
            => new StorageAllocationObservationResult(new List<StorageAllocationCell>(),
                new List<StorageAllocationRequest>(), new Dictionary<string, IntVec3>(StringComparer.Ordinal),
                new Dictionary<string, Thing>(StringComparer.Ordinal),
                new List<StorageAllocationObservationIssue>
                { new StorageAllocationObservationIssue(null, null, StorageAllocationObservationStatus.Deferred, reason) },
                false, false, -1, -1, 0, 0, 0);

        internal static ISlotGroup Canonical(SlotGroup slot)
            => slot?.parent is Building_Storage b && b.storageGroup != null ? b.storageGroup : (ISlotGroup)slot;
        internal static string CellKey(Map map, ISlotGroupParent parent, IntVec3 cell)
            => "map:" + map.uniqueID + "/" + (parent is Thing thing ? "thing:" + thing.thingIDNumber
                : parent is Zone_Stockpile zone ? "zone:" + zone.ID : "unsupported") + "/" + cell.x + "," + cell.z;
        internal static string StackKey(string cellKey, Thing thing) => cellKey + "/stack:" + thing.thingIDNumber;
        internal static bool CanStack(object target, object incoming)
            => target is Thing resident && incoming is Thing subject && !resident.Destroyed && !subject.Destroyed
                && resident.def?.category == ThingCategory.Item && subject.def?.category == ThingCategory.Item
                && resident.CanStackWith(subject);

        private sealed class LimitReached : Exception { }
        private sealed class ObservationChanged : Exception { }
        private sealed class OwnedCell
        {
            internal IntVec3 Position;
            internal SlotGroup Slot;
            internal string Key;
            internal List<Thing> Grid;
            internal ProjectionListGuard<Thing> GridGuard;
            internal ProjectionThingGuard[] Items;
            internal StorageAllocationCell Value;
            internal StorageProjectionAsfBinding Asf;
            internal AsfProjectionState AsfState;
            internal ProjectionThingGuard ParentGuard;
            internal int Limit;
        }
        private sealed partial class Reader
        {
            private readonly Map map;
            private readonly ISlotGroup group;
            private readonly IReadOnlyList<StorageAllocationObservationDemand> demands;
            private readonly StorageAllocationObservationLimits limits;
            private readonly IReadOnlyList<IntVec3> preferred;
            private readonly bool selectedCellsOnly;
            private readonly Func<SlotGroup, IntVec3, bool> selectedMembership;
            private readonly Game game = Current.Game;
            private readonly int tick = Find.TickManager?.TicksGame ?? -1;
            private readonly List<StorageAllocationCell> cells = new List<StorageAllocationCell>();
            private readonly List<StorageAllocationRequest> requests = new List<StorageAllocationRequest>();
            private readonly Dictionary<string, IntVec3> locations = new Dictionary<string, IntVec3>(StringComparer.Ordinal);
            private readonly Dictionary<string, Thing> targets = new Dictionary<string, Thing>(StringComparer.Ordinal);
            private readonly List<StorageAllocationObservationIssue> issues = new List<StorageAllocationObservationIssue>();
            private readonly List<OwnedCell> owned = new List<OwnedCell>();
            private readonly List<Func<bool>> topology = new List<Func<bool>>();
            private readonly HashSet<IntVec3> seen = new HashSet<IntVec3>();
            private readonly Dictionary<Thing, ProjectionThingGuard> subjects = new Dictionary<Thing, ProjectionThingGuard>();
            private readonly List<SlotGroup> members = new List<SlotGroup>();
            private bool complete = true, invalidated, validated;
            private int nextMember = -1, nextCell = -1, gridWork, predicates;
            internal Reader(Map map, ISlotGroup group, IReadOnlyList<StorageAllocationObservationDemand> demands,
                StorageAllocationObservationLimits limits, IReadOnlyList<IntVec3> preferred, bool selectedCellsOnly = false,
                Func<SlotGroup, IntVec3, bool> selectedMembership = null)
            {
                this.map = map; this.group = group; this.demands = demands;
                this.limits = limits; this.preferred = preferred; this.selectedCellsOnly = selectedCellsOnly;
                this.selectedMembership = selectedMembership;
            }
            internal StorageAllocationObservationResult Unavailable(string reason)
            { Problem(null, null, StorageAllocationObservationStatus.Deferred, reason); return Finish(); }
            private bool Problem(IntVec3? cell, StorageAllocationObservationDemand demand, StorageAllocationObservationStatus status, string reason)
            {
                issues.Add(new StorageAllocationObservationIssue(cell, demand, status, reason));
                if (status != StorageAllocationObservationStatus.Refused) complete = false;
                if (status == StorageAllocationObservationStatus.Invalidated) invalidated = true;
                return false;
            }
            private void Predicate()
            { if (predicates >= limits.Predicates) throw new LimitReached(); predicates++; }
            private void GridWork(int count)
            { if (count < 0 || count > limits.GridThings - gridWork) throw new LimitReached(); gridWork += count; }
            private void Live()
            {
                if (!ReferenceEquals(Current.Game, game) || Find.TickManager?.TicksGame != tick || map == null
                    || game == null || !game.Maps.Contains(map)) throw new ObservationChanged();
            }

            internal StorageAllocationObservationResult Read()
            {
                try
                {
                    if (map == null || group == null || demands == null || demands.Count > limits.Demands)
                        return Unavailable("invalid-or-oversized-request");
                    Live();
                    if (!selectedCellsOnly) CopyMembers();
                    // Existing leases and exact destinations are observed first, before the bounded
                    // ordinary page. A late shelf is not hidden by 200 earlier blocked cells.
                    if (preferred != null)
                        for (int i = 0; i < preferred.Count; i++) { Predicate(); AddPreferred(preferred[i]); }
                    if (!selectedCellsOnly)
                    {
                        foreach (var demand in demands)
                        { Predicate(); if (demand?.Destination is IntVec3 exact) AddPreferred(exact); }
                        CopyPage();
                    }
                    // Every concrete coordinate is owned before invoking any storage predicate.
                    foreach (var cell in owned) ObservePhysical(cell);
                    foreach (var demand in demands) ObserveDemand(demand);
                    CheckUnchanged();
                    validated = true;
                }
                catch (LimitReached) { Problem(null, null, StorageAllocationObservationStatus.Deferred, "observation-budget"); }
                catch (ObservationChanged) { Problem(null, null, StorageAllocationObservationStatus.Invalidated, "world-changed-during-observation"); }
                catch (Exception e)
                { Problem(null, null, StorageAllocationObservationStatus.Deferred, "observation-callback:" + e.GetType().FullName); }
                return Finish();
            }
            private StorageAllocationObservationResult Finish()
            {
                // A callback or work limit can interrupt the final freshness check. In that
                // case no earlier positive edge is certified for publication. A normal bounded
                // cell page still completes validation and retains its known usable resources.
                if (invalidated || !validated)
                { cells.Clear(); requests.Clear(); locations.Clear(); targets.Clear(); }
                return new StorageAllocationObservationResult(cells, requests, locations, targets, issues,
                    complete && limits.StartMember == 0 && limits.StartCell == 0, invalidated,
                    nextMember, nextCell, owned.Count, gridWork, predicates, StillCurrent);
            }

            // Reuse the same physical/topology guards and remaining work budget when a
            // priority writer must validate after compatibility callbacks, just before apply.
            private bool StillCurrent()
            {
                if (!UnityData.IsInMainThread || observing || !validated || invalidated) return false;
                observing = true;
                try
                {
                    using (StorageCommitments.SuppressOwnGateForProjection()) CheckUnchanged();
                    return true;
                }
                catch { return false; }
                finally { observing = false; }
            }

            private void CopyMembers()
            {
                if (group is SlotGroup concrete)
                {
                    if (!ReferenceEquals(Canonical(concrete), group) || concrete.parent?.Map != map)
                        throw new ObservationChanged();
                    if (limits.StartMember != 0) throw new ObservationChanged();
                    members.Add(concrete); return;
                }
                if (!(group is StorageGroup linked) || linked.Map != map)
                { Problem(null, null, StorageAllocationObservationStatus.Unsupported, "unhandled-storage-group"); return; }
                var source = linked.members;
                var guard = new ProjectionListGuard<IStorageGroupMember>(source);
                topology.Add(() => ReferenceEquals(linked.members, source) && guard.Matches(linked.members));
                if (limits.StartMember > source.Count) throw new ObservationChanged();
                int end = (int)Math.Min(source.Count, (long)limits.StartMember + limits.Members);
                for (int i = limits.StartMember; i < end; i++)
                {
                    Predicate();
                    var parent = source[i] as ISlotGroupParent;
                    if (parent == null || parent.Map != map || !ReferenceEquals(Canonical(parent.GetSlotGroup()), group))
                        throw new ObservationChanged();
                    members.Add(parent.GetSlotGroup());
                }
                if (end < source.Count) { complete = false; nextMember = end; nextCell = 0; }
            }
            private void AddPreferred(IntVec3 cell)
            {
                if (!cell.IsValid || !cell.InBounds(map) || seen.Contains(cell)) return;
                var slot = map.haulDestinationManager.SlotGroupAt(cell);
                if (slot?.parent == null || !ReferenceEquals(Canonical(slot), group)) return;
                if (selectedCellsOnly && selectedMembership != null)
                {
                    Predicate();
                    if (!selectedMembership(slot, cell)) throw new ObservationChanged();
                    topology.Add(() => selectedMembership(slot, cell));
                    AddCell(slot, cell);
                    return;
                }
                // Native building footprints are an O(1) membership witness. A zone list must be
                // inspected within the work bound; the map registration alone is not its membership.
                if (slot.parent is Building_Storage building && building.GetType() == typeof(Building_Storage))
                {
                    if (!building.OccupiedRect().Contains(cell)) throw new ObservationChanged();
                }
                else
                {
                    if (!Provider(slot.parent, cell, out _)) return;
                    var list = slot.CellsList;
                    GridWork(list.Count);
                    if (!list.Contains(cell)) throw new ObservationChanged();
                    var guard = new ProjectionListGuard<IntVec3>(list);
                    topology.Add(() => ReferenceEquals(slot.parent.GetSlotGroup(), slot)
                        && ReferenceEquals(Canonical(slot), group) && guard.Matches(slot.CellsList));
                }
                AddCell(slot, cell);
            }
            private void CopyPage()
            {
                for (int ordinal = 0; ordinal < members.Count; ordinal++)
                {
                    int member = limits.StartMember + ordinal;
                    var slot = members[ordinal];
                    if (slot == null) continue;
                    var parent = slot.parent;
                    if (!Provider(parent, nullCell, out _)) continue;
                    var source = slot.CellsList; // concrete SlotGroup only; never StorageGroup.CellsList
                    var guard = new ProjectionListGuard<IntVec3>(source);
                    topology.Add(() => ReferenceEquals(parent.GetSlotGroup(), slot) && parent.Map == map
                        && ReferenceEquals(Canonical(slot), group) && guard.Matches(slot.CellsList));
                    int start = member == limits.StartMember ? limits.StartCell : 0;
                    for (int i = start; i < source.Count; i++)
                    {
                        Predicate();
                        var position = source[i];
                        if (seen.Contains(position)) continue;
                        if (owned.Count >= limits.Cells)
                        { complete = false; nextMember = member; nextCell = i; return; }
                        if (!position.InBounds(map) || !ReferenceEquals(map.haulDestinationManager.SlotGroupAt(position), slot))
                            throw new ObservationChanged();
                        AddCell(slot, position);
                    }
                }
            }
            private static readonly IntVec3 nullCell = IntVec3.Invalid;
            private void AddCell(SlotGroup slot, IntVec3 position)
            {
                if (seen.Contains(position)) return;
                if (owned.Count >= limits.Cells) throw new LimitReached();
                seen.Add(position);
                owned.Add(new OwnedCell { Position = position, Slot = slot, Key = CellKey(map, slot.parent, position),
                    ParentGuard = slot.parent is Thing parentThing ? new ProjectionThingGuard(parentThing) : null });
            }

            private void ObservePhysical(OwnedCell cell)
            {
                Live();
                if (!Provider(cell.Slot.parent, cell.Position, out cell.Asf)) return;
                var grid = map.thingGrid.ThingsListAt(cell.Position);
                GridWork(grid.Count);
                cell.Grid = grid; cell.GridGuard = new ProjectionListGuard<Thing>(grid);
                var guards = new List<ProjectionThingGuard>();
                var stacks = new List<StorageAllocationStack>();
                int occupied = 0;
                foreach (var thing in grid)
                {
                    if (thing?.def == null || !thing.Spawned || thing.Map != map || thing.Destroyed) throw new ObservationChanged();
                    guards.Add(new ProjectionThingGuard(thing));
                    if (thing.def.category != ThingCategory.Item) continue;
                    if (thing.def.size.x != 1 || thing.def.size.z != 1 || thing.stackCount <= 0 || thing.def.stackLimit <= 0)
                    { Problem(cell.Position, null, StorageAllocationObservationStatus.Unsupported, "unhandled-resident-footprint-or-count"); return; }
                    occupied++;
                }
                Predicate(); cell.Limit = cell.Position.GetMaxItemsAllowedInCell(map);
                if (cell.Limit < 0) throw new ObservationChanged();
                if (cell.Asf != null)
                {
                    Predicate(); cell.AsfState = cell.Asf.Read((Building_Storage)cell.Slot.parent, cell.Position);
                    var state = cell.AsfState;
                    if (state.Packed || state.CellLimit != cell.Limit || state.CellCount != occupied || state.Count < occupied
                        || state.CellWiseCount < state.Count || state.SlotLimit < 0
                        || state.AnyFree != (state.CellWiseCount < state.SlotLimit)) throw new ObservationChanged();
                }
                foreach (var stamp in guards)
                {
                    var thing = stamp.Thing;
                    if (thing.def.category != ThingCategory.Item) continue;
                    bool valid = true;
                    if (cell.Asf != null)
                    {
                        Predicate();
                        if (!cell.Asf.RegisteredExactly(cell.AsfState, (Building_Storage)cell.Slot.parent, thing, cell.Position))
                            throw new ObservationChanged();
                        Predicate(); valid = cell.Asf.TargetValid((Building_Storage)cell.Slot.parent, thing);
                    }
                    string key = StackKey(cell.Key, thing);
                    int free = valid ? Math.Max(0, thing.def.stackLimit - thing.stackCount) : 0;
                    stacks.Add(new StorageAllocationStack(key, thing, free)); targets.Add(key, thing);
                }
                cell.Items = guards.ToArray();
                cell.Value = new StorageAllocationCell(cell.Key, Math.Max(0, cell.Limit - occupied), stacks);
                cells.Add(cell.Value); locations.Add(cell.Key, cell.Position);
            }

            private bool ValidDemand(StorageAllocationObservationDemand demand)
            {
                if (demand?.Owner == null || demand.Parcel == null || demand.Subject?.def == null || demand.Pawn == null
                    || !demand.Pawn.Spawned || demand.Pawn.Map != map || demand.Pawn.Faction == null
                    || demand.Subject.Destroyed || demand.Subject.stackCount <= 0 || demand.Units <= 0
                    || demand.Units > demand.Subject.stackCount || demand.Subject.def.stackLimit <= 0
                    || demand.Subject.def.category != ThingCategory.Item || demand.Subject.MapHeld != map
                    || !Enum.IsDefined(typeof(StorageFilterContext), demand.Context))
                    return Problem(null, demand, StorageAllocationObservationStatus.Invalidated, "invalid-demand-or-custody");
                if (demand.Subject.def.size.x != 1 || demand.Subject.def.size.z != 1)
                    return Problem(null, demand, StorageAllocationObservationStatus.Unsupported, "unhandled-source-footprint");
                if (!demand.Subject.Spawned && !(demand.Subject.ParentHolder is Pawn_InventoryTracker)
                    && !(demand.Subject.ParentHolder is Pawn_CarryTracker))
                    return Problem(null, demand, StorageAllocationObservationStatus.Unsupported, "unhandled-source-holder");
                if (!subjects.ContainsKey(demand.Subject)) subjects.Add(demand.Subject, new ProjectionThingGuard(demand.Subject));
                return true;
            }
            private void ObserveDemand(StorageAllocationObservationDemand demand)
            {
                if (!ValidDemand(demand)) return;
                var eligibleCells = new List<string>(); var eligibleStacks = new List<string>();
                using (StorageBuildingFilter.PushContext(demand.Context))
                    foreach (var cell in owned)
                    {
                        if (cell.Value == null || (demand.Destination.HasValue && demand.Destination.Value != cell.Position)) continue;
                        if (!Eligible(demand, cell)) continue;
                        eligibleCells.Add(cell.Key);
                        foreach (var stack in cell.Value.Stacks)
                        {
                            if (stack.FreeUnits <= 0 || ReferenceEquals(stack.Target, demand.Subject)) continue;
                            Predicate();
                            if (CanStack(stack.Target, demand.Subject)) eligibleStacks.Add(stack.Key);
                        }
                    }
                requests.Add(new StorageAllocationRequest(demand.Owner, demand.Parcel, demand.Subject,
                    demand.Units, demand.Subject.def.stackLimit, eligibleCells, eligibleStacks));
            }
            private bool Eligible(StorageAllocationObservationDemand demand, OwnedCell cell)
            {
                var parent = cell.Slot.parent; var pawn = demand.Pawn; var subject = demand.Subject;
                bool Refuse(string reason) => Problem(cell.Position, demand, StorageAllocationObservationStatus.Refused, reason);
                Predicate();
                if (!parent.HaulDestinationEnabled || parent is Thing destination && destination.Faction != pawn.Faction) return Refuse("destination-disabled-or-faction");
                var settings = cell.Slot.Settings;
                if (settings == null || (demand.RequireBetterPriority ? (int)settings.Priority <= (int)demand.PriorityFloor
                    : (int)settings.Priority < (int)demand.PriorityFloor))
                    return Refuse("destination-priority");
                Predicate(); if (!settings.AllowedToAccept(subject)) return Refuse("effective-filter");
                Predicate(); var fixedSettings = parent.GetParentStoreSettings();
                Predicate(); if (fixedSettings != null && !fixedSettings.AllowedToAccept(subject)) return Refuse("concrete-fixed-filter");
                Predicate(); if (!parent.Accepts(subject)) return Refuse("destination-provider-acceptance");
                if (demand.StartsRefill)
                { Predicate(); if (!StorageRefillHysteresisCompat.AllowsRefill(cell.Slot)) return Refuse("refill-provider"); }
                Predicate();
                if (StorageBuildingFilter.Enabled && HaulersDreamMod.Settings?.storageBuildingFilter?.IsGroupAllowed(cell.Slot) == false)
                    return Refuse("hd-context-filter");
                if (subject.Spawned && subject.Position == cell.Position) return Refuse("source-cell");
                if (cell.Asf != null)
                {
                    var building = (Building_Storage)parent;
                    Predicate(); if (!cell.Asf.FixedAllows(building, subject)) return Refuse("asf-fixed-filter");
                    bool outside = !subject.Spawned || !cell.AsfState.Occupied.Contains(subject.Position);
                    int possible = !cell.AsfState.AnyFree && outside && !cell.AsfState.PerformanceFish ? cell.AsfState.Count : 0;
                    GridWork(possible); Predicate();
                    if (!cell.Asf.HasCapacity(building, subject)) return Refuse("asf-member-capacity");
                }
                var ownJob = demand.OwnJob ?? pawn.CurJob;
                if (ownJob != null && pawn.CurJob == ownJob && map.reservationManager.ReservedBy(cell.Position, pawn, ownJob))
                    return Problem(cell.Position, demand, StorageAllocationObservationStatus.Deferred, "owned-native-reservation-needs-owner-route");
                // Keep the real carrier: native forbiddance, reach, source start, reservations,
                // fire and provider blockers must keep their exact native semantics.
                GridWork(cell.Grid.Count); Predicate();
                if (!StoreUtility.IsGoodStoreCell(cell.Position, map, subject, pawn, pawn.Faction)) return Refuse("native-cell-predicate");
                if (StorageCommitments.ExplicitShelfCellHeldByOther(pawn, cell.Position)) return Refuse("explicit-shelf-owner");
                Live();
                return true;
            }
            private void CheckUnchanged()
            {
                Live();
                foreach (var check in topology) { Predicate(); if (!check()) throw new ObservationChanged(); }
                foreach (var stamp in subjects.Values) { Predicate(); if (!stamp.Matches()) throw new ObservationChanged(); }
                foreach (var cell in owned)
                {
                    if (cell.Value == null) continue;
                    Predicate();
                    if (!ReferenceEquals(map.haulDestinationManager.SlotGroupAt(cell.Position), cell.Slot)
                        || !ReferenceEquals(Canonical(cell.Slot), group) || !cell.GridGuard.Matches(map.thingGrid.ThingsListAt(cell.Position)))
                        throw new ObservationChanged();
                    if (cell.ParentGuard != null && !cell.ParentGuard.Matches()) throw new ObservationChanged();
                    GridWork(cell.Items.Length);
                    foreach (var stamp in cell.Items) if (!stamp.Matches()) throw new ObservationChanged();
                    Predicate(); if (cell.Position.GetMaxItemsAllowedInCell(map) != cell.Limit) throw new ObservationChanged();
                    if (cell.Asf != null)
                    { Predicate(); if (!cell.AsfState.Same(cell.Asf.Read((Building_Storage)cell.Slot.parent, cell.Position))) throw new ObservationChanged(); }
                }
                // The last native/provider capacity callback can mutate an earlier
                // subject or minified inner. Finish with only raw field/list checks.
                foreach (var stamp in subjects.Values)
                { Predicate(); if (!stamp.AttributesMatch()) throw new ObservationChanged(); }
                foreach (var cell in owned)
                {
                    if (cell.Value == null) continue;
                    if (cell.ParentGuard != null)
                    { Predicate(); if (!cell.ParentGuard.AttributesMatch()) throw new ObservationChanged(); }
                    GridWork(cell.Items.Length);
                    foreach (var stamp in cell.Items)
                        if (!stamp.AttributesMatch()) throw new ObservationChanged();
                }
            }
        }
    }
}
