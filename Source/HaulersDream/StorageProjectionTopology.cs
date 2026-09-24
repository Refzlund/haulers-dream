using System;
using System.Collections.Generic;
using System.Reflection;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    // Main-thread opaque continuation. It owns no predicate scope and is valid
    // only in the synchronous scope that created it. Output pages carry values.
    internal sealed class StorageProjectionCursor
    {
        internal readonly StorageProjectionScope Owner;
        internal readonly List<IStorageGroupMember> Members;
        internal readonly ProjectionListGuard<IStorageGroupMember> MembersGuard;
        internal readonly Dictionary<ISlotGroupParent, StorageProjectionMemberGuard> Guards = new Dictionary<ISlotGroupParent, StorageProjectionMemberGuard>();
        internal readonly HashSet<IntVec3> Seen = new HashSet<IntVec3>();
        internal readonly List<ProjectionUnresolvedCell> Pending = new List<ProjectionUnresolvedCell>();
        internal int MemberIndex, CellIndex;
        internal int PendingRetryOffset, PendingOutputOffset;
        internal bool DiscoveryEnded, Invalidated;
        internal StorageProjectionCursor(StorageProjectionScope owner, StorageGroup linked)
        {
            Owner = owner;
            if (linked != null) { Members = linked.members; MembersGuard = new ProjectionListGuard<IStorageGroupMember>(Members); }
        }
    }

    internal sealed class StorageProjectionMemberGuard
    {
        private static readonly FieldInfo OccupiedCache = typeof(Building_Storage).GetField("cachedOccupiedCells", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        internal ISlotGroupParent Parent { get; }
        internal SlotGroup Slot { get; }
        internal string Key { get; }
        internal int CellCount { get; }
        private readonly Map map;
        private readonly ISlotGroup canonical;
        private readonly List<IntVec3> zoneCells;
        private readonly ProjectionListGuard<IntVec3> zoneGuard;
        private readonly ThingDef def, stuff;
        private readonly IntVec3 position;
        private readonly Rot4 rotation;
        private readonly IntVec2 size;
        private readonly CellRect rect;
        private readonly int id;
        private readonly List<IntVec3> buildingCells;
        private readonly ProjectionListGuard<IntVec3> buildingGuard;
        private readonly List<ThingComp> parentComps;
        private readonly ProjectionListGuard<ThingComp> parentCompGuard;
        private readonly Faction faction;
        private readonly PreparedProjectionZoneCells preparedZone;
        internal StorageProjectionMemberGuard(ISlotGroupParent parent, Map map, ISlotGroup canonical, PreparedProjectionZoneCells preparedZone = null)
        {
            Parent = parent; Slot = parent.GetSlotGroup(); this.map = map; this.canonical = canonical;
            if (parent is Zone_Stockpile zone)
            {
                id = zone.ID; Key = "zone:" + id; zoneCells = zone.cells;
                if (id < 0) throw new ProjectionAbort(ProjectionReason.InvalidRequest);
                this.preparedZone = preparedZone ?? throw new ProjectionAbort(ProjectionReason.ProviderInitializing);
                if (!preparedZone.Matches(zone)) throw new ProjectionAbort(ProjectionReason.GroupChanged);
                zoneGuard = new ProjectionListGuard<IntVec3>(zoneCells); CellCount = zoneCells.Count;
            }
            else if (parent is Building_Storage building)
            {
                id = building.thingIDNumber; Key = "building:" + id; def = building.def; stuff = building.Stuff;
                if (id < 0) throw new ProjectionAbort(ProjectionReason.InvalidRequest);
                position = building.Position; rotation = building.Rotation; size = building.def.size;
                if (size.x <= 0 || size.z <= 0) throw new ProjectionAbort(ProjectionReason.UnsupportedFootprint);
                faction = building.Faction; parentComps = building.AllComps; parentCompGuard = new ProjectionListGuard<ThingComp>(parentComps);
                // Direct inspected native footprint. Never touch the cache-building
                // AllSlotCellsList or any linked group's static scratch getter.
                rect = building.OccupiedRect();
                long count = checked((long)rect.Width * rect.Height);
                if (count < 0 || count > int.MaxValue) throw new ProjectionAbort(ProjectionReason.UnsupportedFootprint);
                CellCount = (int)count;
                if (OccupiedCache == null) throw new ProjectionAbort(ProjectionReason.BindingFault);
                buildingCells = (List<IntVec3>)OccupiedCache.GetValue(building);
                if (buildingCells != null)
                {
                    if (buildingCells.Count != CellCount) throw new ProjectionAbort(ProjectionReason.GroupChanged);
                    buildingGuard = new ProjectionListGuard<IntVec3>(buildingCells);
                }
            }
            else throw new ProjectionAbort(ProjectionReason.UnsupportedProvider);
        }
        internal IntVec3 CellAt(int ordinal)
        {
            if (ordinal < 0 || ordinal >= CellCount) throw new ProjectionAbort(ProjectionReason.InvalidRequest);
            // Native GenAdj.CellsOccupiedBy iterates X outside / Z inside (unlike
            // CellRect's own enumerator). Preserve that inspected cache order.
            var cell = zoneCells != null ? zoneCells[ordinal] : new IntVec3(rect.minX + ordinal / rect.Height, 0, rect.minZ + ordinal % rect.Height);
            if (buildingCells != null && buildingCells[ordinal] != cell) throw new ProjectionAbort(ProjectionReason.GroupChanged);
            return cell;
        }
        internal bool ContainsObservedCell(IntVec3 cell)
        {
            if (zoneCells != null) return preparedZone.Contains(cell);
            if (!rect.Contains(cell)) return false;
            int ordinal = (cell.x - rect.minX) * rect.Height + cell.z - rect.minZ;
            return buildingCells == null || buildingCells[ordinal] == cell;
        }
        internal bool Matches()
        {
            if (!ReferenceEquals(Parent.Map, map) || !ReferenceEquals(Parent.GetSlotGroup(), Slot)
                || !ReferenceEquals(StorageResourceProjector.Canonical(Slot), canonical)) return false;
            if (Parent is Zone_Stockpile zone) return zone.ID == id && zoneGuard.Matches(zone.cells) && preparedZone.Matches(zone);
            var building = (Building_Storage)Parent;
            var actualCache = (List<IntVec3>)OccupiedCache.GetValue(building);
            if (buildingCells == null ? actualCache != null : !buildingGuard.Matches(actualCache)) return false;
            return building.Spawned && !building.Destroyed && building.thingIDNumber == id
                && ReferenceEquals(building.Faction, faction) && parentCompGuard.Matches(building.AllComps)
                && ReferenceEquals(building.def, def) && ReferenceEquals(building.Stuff, stuff)
                && building.Position == position && building.Rotation == rotation && building.def.size == size
                && building.OccupiedRect().Equals(rect);
        }
    }

    internal sealed class PreparedProjectionZoneCells
    {
        private readonly Zone_Stockpile zone;
        private readonly ProjectionListGuard<IntVec3> guard;
        private readonly HashSet<IntVec3> cells;
        internal int Count => cells.Count;
        internal PreparedProjectionZoneCells(Zone_Stockpile zone)
        {
            this.zone = zone; guard = new ProjectionListGuard<IntVec3>(zone.cells); cells = new HashSet<IntVec3>();
            foreach (var cell in zone.cells)
                if (!cell.InBounds(zone.Map) || !ReferenceEquals(zone.Map.haulDestinationManager.SlotGroupAt(cell), zone.GetSlotGroup()) || !cells.Add(cell))
                    throw new ProjectionAbort(ProjectionReason.GroupChanged);
            if (!guard.Matches(zone.cells)) throw new ProjectionAbort(ProjectionReason.GroupChanged);
        }
        internal bool Matches(Zone_Stockpile candidate) => ReferenceEquals(zone, candidate) && guard.Matches(candidate.cells);
        internal bool Contains(IntVec3 cell) => cells.Contains(cell);
    }

    internal sealed class ProjectionThingGuard
    {
        internal Thing Thing { get; }
        internal int Id { get; }
        internal ThingDef Def { get; }
        internal int Count { get; }
        internal int StackLimit { get; }
        private readonly IntVec3 position;
        private readonly Rot4 rotation;
        private readonly IntVec2 size;
        private readonly Map map;
        private readonly object holder;
        private readonly Thing spawnedParent;
        private readonly bool spawned;
        private readonly ThingDef stuff;
        private readonly List<ThingComp> comps;
        private readonly ProjectionListGuard<ThingComp> compGuard;
        internal ProjectionThingGuard(Thing thing)
        {
            Thing = thing; Id = thing.thingIDNumber; Def = thing.def; Count = thing.stackCount; StackLimit = thing.def.stackLimit;
            position = thing.Position; rotation = thing.Rotation; size = thing.def.size; map = thing.MapHeld;
            holder = thing.ParentHolder; spawnedParent = thing.SpawnedParentOrMe; spawned = thing.Spawned;
            stuff = thing.Stuff;
            if (thing is ThingWithComps withComps) { comps = withComps.AllComps; compGuard = new ProjectionListGuard<ThingComp>(comps); }
        }
        internal bool Matches() => !Thing.Destroyed && Thing.thingIDNumber == Id && ReferenceEquals(Thing.def, Def)
            && Thing.stackCount == Count && Thing.def.stackLimit == StackLimit && Thing.Position == position
            && Thing.Rotation == rotation && Thing.def.size == size && ReferenceEquals(Thing.MapHeld, map)
            && ReferenceEquals(Thing.Stuff, stuff) && (comps == null || compGuard.Matches(((ThingWithComps)Thing).AllComps))
            && ReferenceEquals(Thing.ParentHolder, holder) && ReferenceEquals(Thing.SpawnedParentOrMe, spawnedParent) && Thing.Spawned == spawned;
    }
    internal sealed class ProjectionAbort : Exception
    {
        internal ProjectionReason Reason { get; }
        internal ProjectionAbort(ProjectionReason reason) { Reason = reason; }
    }
}
