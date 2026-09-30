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
        // The final publication pass must not call a virtual/native getter, a provider, or
        // an ownership query. These short-lived guards read fields and BCL collections only.
        // They are not another capacity source; the observer/allocator still decide quantities.
        private sealed class ForcedRawState
        {
            private readonly List<Func<bool>> checks = new List<Func<bool>>();
            private readonly HashSet<object> captured = new HashSet<object>();
            private readonly HashSet<Pawn> capturedPawns = new HashSet<Pawn>();
            private bool valid = true;
            private int containerItems;
            private static readonly BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            private static FieldInfo Field(Type type, string name, bool stat = false)
                => type.GetField(name, stat ? BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly : instance)
                    ?? throw new MissingFieldException(type.FullName, name);
            private static readonly FieldInfo mapState = Field(typeof(Thing), "mapIndexOrState"),
                position = Field(typeof(Thing), "positionInt"), rotation = Field(typeof(Thing), "rotationInt"),
                stuff = Field(typeof(Thing), "stuffInt"), faction = Field(typeof(Thing), "factionInt"),
                comps = Field(typeof(ThingWithComps), "comps"), ownerList = Field(typeof(ThingOwner<Thing>), "innerList"),
                tags = Field(typeof(CompHauledToInventory), "takenToInventory"), bulkIndex = Field(typeof(JobDriver_BulkHaul), "loadIndex"),
                keeps = Field(typeof(CompHauledToInventory), "keptCounts"),
                queuedJobs = Field(typeof(JobQueue), "jobs"),
                currentGame = Field(typeof(Current), "gameInt", true), gameMaps = Field(typeof(Game), "maps"),
                reservations = Field(typeof(ReservationManager), "reservations"),
                claimant = Field(typeof(ReservationManager.Reservation), "claimant"), reservationJob = Field(typeof(ReservationManager.Reservation), "job"),
                target = Field(typeof(ReservationManager.Reservation), "target"), layer = Field(typeof(ReservationManager.Reservation), "layer"),
                maximum = Field(typeof(ReservationManager.Reservation), "maxPawns"), amount = Field(typeof(ReservationManager.Reservation), "stackCount"),
                targetThing = Field(typeof(LocalTargetInfo), "thingInt"), targetCell = Field(typeof(LocalTargetInfo), "cellInt"),
                rotationValue = Field(typeof(Rot4), "rotInt"),
                groupGrid = Field(typeof(HaulDestinationManager), "groupGrid"), thingGrid = Field(typeof(ThingGrid), "thingGrid"),
                sizeX = Field(typeof(CellIndices), "sizeX"), sizeZ = Field(typeof(CellIndices), "sizeZ"),
                hdSettings = Field(typeof(HaulersDreamMod), "<Settings>k__BackingField", true),
                rules = Field(typeof(HaulersDreamSettings), "ruleMap"),
                linkedSettings = Field(typeof(StorageGroup), "settings"), storagePriority = Field(typeof(StorageSettings), "priorityInt"),
                everSettings = Field(typeof(StorageSettings), "cachedEverStorableFixedSettings", true),
                allowedDefs = Field(typeof(ThingFilter), "allowedDefs"), specialFilters = Field(typeof(ThingFilter), "disallowedSpecialFilters"),
                onlySpecial = Field(typeof(ThingFilter), "onlySpecialFilters"), hitPoints = Field(typeof(ThingFilter), "allowedHitPointsPercents"),
                mentalBreak = Field(typeof(ThingFilter), "allowedMentalBreakChance"), qualities = Field(typeof(ThingFilter), "allowedQualities");

            internal bool Matches()
            {
                if (!valid) return false;
                foreach (var check in checks)
                { StorageProgressWork.Charge(StorageWorkKind.RawGuard); if (!check()) return false; }
                return true;
            }

            private void Value(FieldInfo field, object instance)
            {
                StorageProgressWork.Charge(StorageWorkKind.RawGuard);
                object before = field.GetValue(instance);
                // Only native primitive/value structs and identity references occur here.
                if (before is IntVec3 cell)
                    checks.Add(() => SameCell((IntVec3)field.GetValue(instance), cell));
                else if (before is Rot4 rot)
                {
                    byte value = (byte)rotationValue.GetValue(rot);
                    checks.Add(() => (byte)rotationValue.GetValue(field.GetValue(instance)) == value);
                }
                else checks.Add(field.FieldType.IsValueType ? (Func<bool>)(() => Equals(field.GetValue(instance), before))
                    : () => ReferenceEquals(field.GetValue(instance), before));
            }

            private void List<T>(List<T> list, Func<List<T>> read)
            {
                if (list == null) { checks.Add(() => read() == null); return; }
                var guard = new ProjectionListGuard<T>(list);
                checks.Add(() => guard.Matches(read()));
            }

            private void Set<T>(HashSet<T> set, Func<HashSet<T>> read)
            {
                if (set == null) { checks.Add(() => read() == null); return; }
                var guard = set.GetEnumerator();
                checks.Add(() =>
                {
                    if (!ReferenceEquals(read(), set)) return false;
                    try { var copy = guard; copy.MoveNext(); return true; }
                    catch (InvalidOperationException) { return false; }
                });
            }

            private void Dictionary<K, V>(Dictionary<K, V> dictionary, Func<Dictionary<K, V>> read)
            {
                if (dictionary == null) { checks.Add(() => read() == null); return; }
                var guard = dictionary.GetEnumerator();
                checks.Add(() =>
                {
                    if (!ReferenceEquals(read(), dictionary)) return false;
                    try { var copy = guard; copy.MoveNext(); return true; }
                    catch (InvalidOperationException) { return false; }
                });
            }

            private static bool SameTarget(LocalTargetInfo a, LocalTargetInfo b)
                => ReferenceEquals(targetThing.GetValue(a), targetThing.GetValue(b))
                    && SameCell((IntVec3)targetCell.GetValue(a), (IntVec3)targetCell.GetValue(b));
            private static bool SameCell(IntVec3 a, IntVec3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

            internal void Thing(Thing thing)
            {
                if (thing == null || !captured.Add(thing)) return;
                var def = thing.def; int id = thing.thingIDNumber, count = thing.stackCount;
                var holder = thing.holdingOwner;
                if (def == null) { valid = false; return; }
                int limit = def.stackLimit; var size = def.size;
                var attributes = new StorageSubjectAttributeGuard(thing);
                checks.Add(attributes.Matches);
                checks.Add(() => ReferenceEquals(thing.def, def) && thing.thingIDNumber == id && thing.stackCount == count
                    && ReferenceEquals(thing.holdingOwner, holder) && def.stackLimit == limit
                    && def.size.x == size.x && def.size.z == size.z);
                Value(mapState, thing); Value(position, thing); Value(rotation, thing); Value(stuff, thing); Value(faction, thing);
                if (thing is ThingWithComps withComps)
                    List((List<ThingComp>)comps.GetValue(withComps), () => (List<ThingComp>)comps.GetValue(withComps));
            }

            private void Container(ThingOwner<Thing> owner)
            {
                if (owner == null || !captured.Add(owner)) return;
                var items = (List<Thing>)ownerList.GetValue(owner);
                List(items, () => (List<Thing>)ownerList.GetValue(owner));
                containerItems += items.Count;
                if (containerItems > 4096) { valid = false; return; }
                // Surplus depends on untagged same-def siblings as well as the attributed stack.
                foreach (var item in items) { StorageProgressWork.Charge(StorageWorkKind.RawGuard); Thing(item); }
            }

            internal void Pawn(Pawn pawn)
            {
                if (pawn == null) { valid = false; return; }
                Thing(pawn);
                if (!capturedPawns.Add(pawn)) return;
                var jobs = pawn.jobs; var job = jobs?.curJob; var driver = jobs?.curDriver;
                var queue = jobs?.jobQueue;
                var inventory = pawn.inventory; var carry = pawn.carryTracker;
                var inventoryOwner = inventory?.innerContainer; var hands = carry?.innerContainer;
                checks.Add(() => ReferenceEquals(pawn.jobs, jobs) && ReferenceEquals(jobs?.curJob, job)
                    && ReferenceEquals(jobs?.curDriver, driver) && ReferenceEquals(jobs?.jobQueue, queue)
                    && ReferenceEquals(pawn.inventory, inventory)
                    && ReferenceEquals(pawn.carryTracker, carry) && ReferenceEquals(inventory?.innerContainer, inventoryOwner)
                    && ReferenceEquals(carry?.innerContainer, hands));
                Container(inventoryOwner); Container(hands);
                if (queue != null)
                {
                    var pending = (List<QueuedJob>)queuedJobs.GetValue(queue);
                    List(pending, () => (List<QueuedJob>)queuedJobs.GetValue(queue));
                    if (pending.Count > 256) { valid = false; return; }
                    foreach (var entry in pending)
                    {
                        if (entry == null) continue;
                        var queued = entry.job; var tag = entry.tag;
                        checks.Add(() => ReferenceEquals(entry.job, queued) && entry.tag == tag);
                        Job(queued);
                    }
                }
                Job(job);
                if (driver != null)
                {
                    var boundJob = driver.job; var boundPawn = driver.pawn;
                    checks.Add(() => ReferenceEquals(driver.job, boundJob) && ReferenceEquals(driver.pawn, boundPawn));
                    if (driver is JobDriver_BulkHaul) Value(bulkIndex, driver);
                }
                PawnPolicy(pawn);
            }

            internal void PawnPolicy(Pawn pawn)
            {
                var components = (List<ThingComp>)comps.GetValue(pawn);
                List(components, () => (List<ThingComp>)comps.GetValue(pawn));
                if (components == null) return;
                foreach (var component in components)
                {
                    if (!(component is CompHauledToInventory comp)) continue;
                    Set((HashSet<Thing>)tags.GetValue(comp), () => (HashSet<Thing>)tags.GetValue(comp));
                    Dictionary((Dictionary<ThingDef, int>)keeps.GetValue(comp), () => (Dictionary<ThingDef, int>)keeps.GetValue(comp));
                }
            }

            internal void HdPolicy()
            {
                var settings = (HaulersDreamSettings)hdSettings.GetValue(null);
                Value(hdSettings, null);
                if (settings == null) return;
                bool enabled = settings.storageFiltersEnabled, defaults = settings.storageFilterUseDefaults;
                bool denyLwm = settings.storageFilterDenyLwmForOpportunistic;
                var filter = settings.storageBuildingFilter;
                checks.Add(() => settings.storageFiltersEnabled == enabled && settings.storageFilterUseDefaults == defaults
                    && settings.storageFilterDenyLwmForOpportunistic == denyLwm && ReferenceEquals(settings.storageBuildingFilter, filter));
                List(settings.itemUnloadRules, () => settings.itemUnloadRules);
                var decoded = (Dictionary<string, ItemUnloadRule>)rules.GetValue(settings);
                // A null decoded cache may be built by the first normal read. Its authoritative
                // string rules remain guarded; SetItemRules always replaces that source list.
                if (decoded != null) Dictionary(decoded, () => (Dictionary<string, ItemUnloadRule>)rules.GetValue(settings));
                if (filter != null)
                { Set(filter.denied, () => filter.denied); Set(filter.allowed, () => filter.allowed); }
            }

            internal void StoragePolicy(ISlotGroup group)
            {
                // Resolve the native lazy default before capturing policy. No such factory runs
                // during Matches. External provider-private policy is still its own obligation.
                var defaults = StorageSettings.EverStorableFixedSettings();
                Value(everSettings, null); Settings(defaults);
                if (group is SlotGroup slot) ParentPolicy(slot.parent);
                else if (group is StorageGroup linked)
                {
                    Value(linkedSettings, linked); Settings((StorageSettings)linkedSettings.GetValue(linked));
                    List(linked.members, () => linked.members);
                    if (linked.members.Count > 256) { valid = false; return; }
                    foreach (var member in linked.members)
                        if (member is ISlotGroupParent parent) ParentPolicy(parent);
                        else { valid = false; return; }
                }
                else valid = false;
            }

            private void ParentPolicy(ISlotGroupParent parent)
            {
                if (parent is Building_Storage building)
                {
                    Thing(building);
                    var settings = building.settings; var linked = building.storageGroup;
                    var definition = building.def.building; var fixedSettings = definition?.fixedStorageSettings;
                    var slot = building.slotGroup;
                    checks.Add(() => ReferenceEquals(building.settings, settings) && ReferenceEquals(building.storageGroup, linked)
                        && ReferenceEquals(building.slotGroup, slot) && ReferenceEquals(building.def.building, definition)
                        && ReferenceEquals(definition?.fixedStorageSettings, fixedSettings));
                    Settings(settings); Settings(fixedSettings);
                    if (linked != null) { Value(linkedSettings, linked); Settings((StorageSettings)linkedSettings.GetValue(linked)); }
                }
                else if (parent is Zone_Stockpile zone)
                {
                    var settings = zone.settings; var slot = zone.slotGroup;
                    checks.Add(() => ReferenceEquals(zone.settings, settings) && ReferenceEquals(zone.slotGroup, slot));
                    Settings(settings);
                }
                else valid = false;
            }

            private void Settings(StorageSettings settings)
            {
                if (settings == null || !captured.Add(settings)) return;
                var filter = settings.filter; var owner = settings.owner;
                checks.Add(() => ReferenceEquals(settings.filter, filter) && ReferenceEquals(settings.owner, owner));
                Value(storagePriority, settings);
                if (filter == null || filter.GetType() != typeof(ThingFilter)) { valid = false; return; }
                Set((HashSet<ThingDef>)allowedDefs.GetValue(filter), () => (HashSet<ThingDef>)allowedDefs.GetValue(filter));
                List((List<SpecialThingFilterDef>)specialFilters.GetValue(filter), () => (List<SpecialThingFilterDef>)specialFilters.GetValue(filter));
                Value(onlySpecial, filter);
                var hp = (FloatRange)hitPoints.GetValue(filter); var mental = (FloatRange)mentalBreak.GetValue(filter);
                var quality = (QualityRange)qualities.GetValue(filter);
                checks.Add(() =>
                {
                    var h = (FloatRange)hitPoints.GetValue(filter); var m = (FloatRange)mentalBreak.GetValue(filter);
                    var q = (QualityRange)qualities.GetValue(filter);
                    return h.min == hp.min && h.max == hp.max && m.min == mental.min && m.max == mental.max
                        && q.min == quality.min && q.max == quality.max;
                });
            }

            internal void Job(Job job)
            {
                if (job == null || !captured.Add(job)) return;
                int id = job.loadID, count = job.count; var def = job.def; bool forced = job.playerForced;
                int inventoryDelay = job.takeInventoryDelay;
                var a = job.targetA; var b = job.targetB; var c = job.targetC;
                checks.Add(() => job.loadID == id && job.count == count && ReferenceEquals(job.def, def)
                    && job.playerForced == forced && job.takeInventoryDelay == inventoryDelay
                    && SameTarget(job.targetA, a) && SameTarget(job.targetB, b) && SameTarget(job.targetC, c));
                List(job.targetQueueA, () => job.targetQueueA); List(job.targetQueueB, () => job.targetQueueB);
                List(job.countQueue, () => job.countQueue);
            }

            internal void Map(Map map)
            {
                if (map == null || !captured.Add(map)) { if (map == null) valid = false; return; }
                var game = (Game)currentGame.GetValue(null);
                Value(currentGame, null);
                if (game == null) { valid = false; return; }
                List((List<Map>)gameMaps.GetValue(game), () => (List<Map>)gameMaps.GetValue(game));
                var manager = map.reservationManager;
                var rows = (List<ReservationManager.Reservation>)reservations.GetValue(manager);
                checks.Add(() => ReferenceEquals(map.reservationManager, manager));
                List(rows, () => (List<ReservationManager.Reservation>)reservations.GetValue(manager));
                if (rows.Count > 4096) { valid = false; return; }
                foreach (var row in rows)
                {
                    Value(claimant, row); Value(reservationJob, row); Value(layer, row); Value(maximum, row); Value(amount, row);
                    var savedTarget = (LocalTargetInfo)target.GetValue(row);
                    checks.Add(() => SameTarget((LocalTargetInfo)target.GetValue(row), savedTarget));
                }
            }

            internal bool Reserved(Pawn pawn, Job job, Thing source, Map map, int units)
            {
                int sourceMap = (sbyte)mapState.GetValue(source);
                var game = (Game)currentGame.GetValue(null);
                var maps = game == null ? null : (List<Map>)gameMaps.GetValue(game);
                if (sourceMap < 0 || sourceMap != (sbyte)mapState.GetValue(pawn)
                    || maps == null || sourceMap >= maps.Count || !ReferenceEquals(maps[sourceMap], map)) return false;
                int reserved = 0;
                var manager = map.reservationManager;
                foreach (var row in (List<ReservationManager.Reservation>)reservations.GetValue(manager))
                {
                    if (!ReferenceEquals(claimant.GetValue(row), pawn) || !ReferenceEquals(reservationJob.GetValue(row), job)
                        || !ReferenceEquals(targetThing.GetValue(target.GetValue(row)), source) || layer.GetValue(row) != null) continue;
                    int count = (int)amount.GetValue(row);
                    reserved = Math.Max(reserved, Math.Min(source.stackCount, count == -1 ? source.stackCount : count));
                }
                return units > 0 && reserved >= units;
            }

            internal void ObservedCells(Map map, ISlotGroup group, StorageAllocationObservationResult observation)
            {
                var manager = map.haulDestinationManager; var grid = map.thingGrid; var indices = map.cellIndices;
                var groups = (SlotGroup[,,])groupGrid.GetValue(manager);
                var cells = (List<Thing>[])thingGrid.GetValue(grid);
                int width = (int)sizeX.GetValue(indices), height = (int)sizeZ.GetValue(indices);
                checks.Add(() => ReferenceEquals(map.haulDestinationManager, manager) && ReferenceEquals(map.thingGrid, grid)
                    && ReferenceEquals(groupGrid.GetValue(manager), groups) && ReferenceEquals(thingGrid.GetValue(grid), cells)
                    && (int)sizeX.GetValue(map.cellIndices) == width && (int)sizeZ.GetValue(map.cellIndices) == height);
                if (group is StorageGroup linked) List(linked.members, () => linked.members);
                int inspected = 0;
                foreach (var cell in observation.CellLocations.Values)
                {
                    int index = cell.x + cell.z * width;
                    var slot = groups[cell.x, cell.y, cell.z]; var parent = slot?.parent;
                    var items = cells[index]; inspected += items.Count;
                    if (inspected > 4096) { valid = false; return; }
                    checks.Add(() => ReferenceEquals(groups[cell.x, cell.y, cell.z], slot) && ReferenceEquals(slot?.parent, parent));
                    List(items, () => cells[index]);
                    foreach (var item in items) { StorageProgressWork.Charge(StorageWorkKind.RawGuard); Thing(item); }
                    if (parent is Building_Storage building)
                    {
                        Thing(building); var linkedGroup = building.storageGroup;
                        checks.Add(() => ReferenceEquals(building.storageGroup, linkedGroup));
                    }
                    else if (parent is Zone_Stockpile zone) List(zone.cells, () => zone.cells);
                }
            }
        }
    }
}
