using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    internal sealed partial class Cap03FilterScenario
    {
        private void RunScene(string id, bool unknown, bool asfFirst, IntVec3 anchor)
        {
            var scene = new Cap03FilterScene { id = id, unknownWorker = unknown, asfFirst = asfFirst, startedTick = Find.TickManager.TicksGame,
                storageGroupsBefore = map.storageGroups.StorageGroupsForReading.Count };
            result.scenes.Add(scene);
            var local = new List<Thing>(); StorageGroup linked = null;
            try
            {
                // Parcels exist before the unknown XML filter is a registered destination.
                var rice = Spawn(rawRice, 1, anchor + new IntVec3(-3, 0, 4), local);
                var wood = Spawn(ThingDefOf.WoodLog, 1, anchor + new IntVec3(-1, 0, 4), local);
                var steel = Spawn(ThingDefOf.Steel, 1, anchor + new IntVec3(1, 0, 4), local);
                var parcels = new[] { rice, wood, steel };
                var rottable = rice.TryGetComp<CompRottable>();
                scene.riceRottableType = rottable?.GetType().FullName; scene.riceRotStage = rottable?.Stage.ToString();
                scene.freshWorkerType = fresh.Worker.GetType().FullName;
                Require(id + "-fresh-rice", rottable != null && rottable.Stage == RotStage.Fresh && fresh.Worker.Matches(rice)
                    && !fresh.Worker.Matches(wood) && !fresh.Worker.Matches(steel), "Actual native fresh worker and live rottable stage discriminate the three actual parcels.");
                Capture("freshness", id, new[] { scene.riceRottableType, scene.riceRotStage, scene.freshWorkerType });
                var def = DefDatabase<ThingDef>.GetNamed(unknown ? "HDHarness_CAP03A_Unreviewed" : "HDHarness_CAP03A_Fresh");
                Require(id + "-xml-class", def.thingClass == api.AsfParentType && def.size.x == 1 && def.size.z == 1
                    && def.modContentPack.assemblies.loadedAssemblies.Contains(GetType().Assembly), "Actual test-only ASF XML definition from disposable harness package.");
                var extension = def.modExtensions?.SingleOrDefault(x => x.GetType().FullName == "AdaptiveStorage.Extension");
                var lockField = extension?.GetType().GetField("lockStorageSettingsToStuff", Fields);
                Require(id + "-xml-stuff-lock", lockField?.FieldType == typeof(bool) && (bool)lockField.GetValue(extension), "Actual installed ASF extension, no compiled subclass or shared def mutation.");
                var asf = SpawnBuilding(def, anchor, local);
                var shelf = SpawnBuilding(DefDatabase<ThingDef>.GetNamed("Shelf"), anchor + new IntVec3(5, 0, 0), local);
                Require(id + "-native-shelf", shelf.GetType() == typeof(Building_Storage) && shelf.def.modContentPack.PackageId.ToLowerInvariant() == "ludeon.rimworld", "Actual native Shelf runtime class/definition.");
                foreach (var member in new[] { asf, shelf })
                {
                    var settings = member.GetStoreSettings(); settings.filter.SetDisallowAll();
                    foreach (var subject in parcels) settings.filter.SetAllow(subject.def, true);
                    settings.Priority = StoragePriority.Critical;
                    Require(id + "-initial-clean-" + member.ThingID, !member.GetStoreSettings().filter.OnlySpecialFilters
                        && ((List<SpecialThingFilterDef>)specials.GetValue(settings.filter)).Count == 0,
                        "Both effective filters start with identical native def allowances and no disallowed special worker.");
                }
                scene.unlinked = Snapshot(null, asf, shelf, parcels); Capture("unlinked", id, scene.unlinked);
                Require(id + "-initial-equivalent", Same(scene.unlinked.members[0].effective.contents, scene.unlinked.members[1].effective.contents), "InitFrom will copy equivalent uncontaminated settings in either order.");
                var first = asfFirst ? asf : shelf; var second = asfFirst ? shelf : asf;
                linked = map.storageGroups.NewGroup("CAP03-A " + id);
                linked.InitFrom(first); first.SetStorageGroup(linked); second.SetStorageGroup(linked);
                scene.linked = Snapshot(linked, asf, shelf, parcels); Capture("linked", id, scene.linked);
                ValidateScene(scene, asf, shelf, first, second);
                scene.beforeProtected = Counter(); Capture("protected-begin", id, scene.beforeProtected);
                foreach (var parent in new[] { first, second })
                {
                    Prepare(scene, parent, 0, "zero-allowance");
                    Trial(scene, linked, parent, rice, "unprepared", asf);
                }
                foreach (var parent in new[] { first, second }) Prepare(scene, parent, PreparationCost(parent), "complete-allowance");
                foreach (var parent in new[] { first, second }) foreach (var subject in parcels) Trial(scene, linked, parent, subject, "prepared", asf);
                scene.afterProjection = Counter(); scene.afterProtected = Snapshot(linked, asf, shelf, parcels);
                Capture("protected-end", id, scene.afterProjection); Capture("physical-after-protected", id, scene.afterProtected);
                Require(id + "-physical-unchanged", Same(scene.linked, scene.afterProtected), "Projection does not alter actual group order, settings/filter contents or physical cargo/custody.");
                Check(id + "-no-custom-Matches", scene.afterProjection.matches == scene.beforeProtected.matches,
                    "Absolute construction/AlwaysMatches counters are recorded separately; zero Matches delta throughout the protected preparation/projection interval.");

                // Explicit native controls occur only AFTER the complete protected interval.
                // They establish actual recursive filter semantics and custom worker reachability.
                foreach (var parent in new[] { asf, shelf }) foreach (var subject in parcels)
                {
                    bool isRice = subject == rice, isWood = subject == wood;
                    bool groupAccepts = !(asfFirst && isRice);
                    Native(scene, "effective-recursive", parent, subject, groupAccepts,
                        () => parent.GetStoreSettings().AllowedToAccept(subject), unknown && asfFirst);
                    Native(scene, "interface-fixed", parent, subject, parent == shelf || !isRice,
                        () => ((IStorageGroupMember)parent).ParentStoreSettings.AllowedToAccept(subject), unknown && parent == asf);
                    Native(scene, "declared-fixed", parent, subject, parent == shelf || isWood,
                        () => (parent == asf ? api.DeclaredFixed(parent) : parent.GetParentStoreSettings()).AllowedToAccept(subject), false);
                }
                scene.afterControl = Counter(); scene.afterNativeControl = Snapshot(linked, asf, shelf, parcels);
                Capture("physical-after-native-control", id, scene.afterNativeControl); Capture("native-control-end", id, scene.afterControl);
                Require(id + "-native-controls-physical-unchanged", Same(scene.linked, scene.afterNativeControl), "Native predicate controls are read-only with respect to physical/filter evidence.");
                if (unknown) Check(id + "-counter-reachable-after-boundary", scene.afterControl.matches > scene.afterProjection.matches && scene.afterControl.workerExists
                    && scene.afterControl.actualWorkerType == typeof(Cap03FilterUnknownWorker).FullName, "Only explicit post-boundary native recursion invokes the real custom predicate.");
                scene.completed = true;
            }
            catch (Exception error) { scene.error = error.ToString(); Capture("scene-exception", id, scene.error); throw; }
            finally
            {
                // No list reordering, def repairs or settings injection. Retire each complete scene
                // through native Destroy, attempting every owned Thing even if another fails.
                bool restored = true;
                foreach (var thing in local.AsEnumerable().Reverse())
                {
                    try { if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish); scene.retiredThingIds.Add(thing.thingIDNumber); }
                    catch (Exception error) { restored = false; scene.error = (scene.error ?? "") + "\nRetirement: " + error; }
                }
                scene.storageGroupsAfter = map.storageGroups.StorageGroupsForReading.Count;
                scene.restored = restored && local.All(x => x.Destroyed && !x.Spawned)
                    && (linked == null || (linked.members.Count == 0 && !map.storageGroups.HasStorageGroup(linked))) && scene.storageGroupsAfter == scene.storageGroupsBefore;
                scene.finishedTick = Find.TickManager.TicksGame;
                Check(id + "-retired", scene.restored, "Every test building/parcel retired; native linked membership empty and group manager restored.");
                Capture("scene-result", id, scene);
            }
        }

        private Thing Spawn(ThingDef def, int count, IntVec3 cell, List<Thing> local)
        {
            var thing = ThingMaker.MakeThing(def); thing.stackCount = count; owned.Add(thing); local.Add(thing);
            var actual = GenSpawn.Spawn(thing, cell, map);
            Require("spawn-" + thing.ThingID, ReferenceEquals(actual, thing) && thing.stackCount == count && thing.Position == cell
                && thing.Spawned && ReferenceEquals(thing.ParentHolder, map), "Actual physical ID/count/map/custody receipt.");
            return thing;
        }
        private Building_Storage SpawnBuilding(ThingDef def, IntVec3 cell, List<Thing> local)
        {
            var thing = (Building_Storage)ThingMaker.MakeThing(def, ThingDefOf.WoodLog); owned.Add(thing); local.Add(thing);
            GenSpawn.Spawn(thing, cell, map, Rot4.North); thing.SetFaction(Faction.OfPlayer);
            Require("spawn-" + thing.ThingID, thing.Spawned && thing.Position == cell && ReferenceEquals(thing.ParentHolder, map) && thing.Stuff == ThingDefOf.WoodLog, "Normal wooden building spawn and actual native registration.");
            return thing;
        }
        private void ValidateScene(Cap03FilterScene scene, Building_Storage asf, Building_Storage shelf, Building_Storage first, Building_Storage second)
        {
            var s = scene.linked; var a = s.members.Single(x => x.thing.thingId == asf.thingIDNumber); var b = s.members.Single(x => x.thing.thingId == shelf.thingIDNumber);
            Require(scene.id + "-real-group-order", s.groupRegistered && s.memberOrder.SequenceEqual(new[] { first.thingIDNumber, second.thingIDNumber }) && s.members.All(x => x.groupIdentity == s.groupIdentity
                && x.groupTag == "Shelf" && x.registered && x.effective.settingsIdentity == s.groupEffective.settingsIdentity && x.effective.ownerIdentity == s.groupIdentity), "Actual native NewGroup/InitFrom/SetStorageGroup membership, not a manually edited member list.");
            Require(scene.id + "-distinct-fixed-dispatch", a.declaredFixed.settingsIdentity != a.interfaceFixed.settingsIdentity && a.declaredFixed.filterIdentity != a.interfaceFixed.filterIdentity
                && a.declaredFixed.ownerIdentity == null && a.interfaceFixed.ownerIdentity == null
                && a.declaredFixed.contents.allowedDefs.SequenceEqual(new[] { "WoodLog" }) && a.declaredFixed.contents.disallowedSpecials.Count == 0
                && a.interfaceFixed.contents.allowedDefs.SequenceEqual(new[] { "RawRice", "Steel", "WoodLog" })
                && a.interfaceFixed.contents.disallowedSpecials.SequenceEqual(new[] { scene.unknownWorker ? UnknownDef : "AllowFresh" }), "Declared ASF wood-only filter differs from inherited interface XML fixed filter.");
            Require(scene.id + "-first-member-interface-owner", s.groupFixed.settingsIdentity == (scene.asfFirst ? a.interfaceFixed.settingsIdentity : b.interfaceFixed.settingsIdentity), "Actual group's fixed settings reference is first member's interface result.");
            Require(scene.id + "-effective-uncontaminated", s.groupEffective.contents.disallowedSpecials.Count == 0 && s.groupEffective.contents.allowedDefs.SequenceEqual(new[] { "RawRice", "Steel", "WoodLog" })
                && b.interfaceFixed.contents.disallowedSpecials.SequenceEqual(new[] { "AllowLargeCorpses" })
                && b.interfaceFixed.settingsIdentity == b.declaredFixed.settingsIdentity && b.interfaceFixed.filterIdentity == b.declaredFixed.filterIdentity
                && b.interfaceFixed.ownerIdentity == null && new[] { "RawRice", "Steel", "WoodLog" }.All(x => b.interfaceFixed.contents.allowedDefs.Contains(x)),
                "Custom/fresh special occurs only in ASF XML fixed settings; actual unchanged native Shelf retains its distinct AllowLargeCorpses restriction.");
            Require(scene.id + "-physical-empty-members", a.nativeMaximum == 6 && a.definitionMaximum == 6 && a.registryCount == 0 && !a.contentsPacked
                && a.cells.Count == 1 && b.cells.Count == 2 && b.nativeMaximum == 3 && b.runtimeType == typeof(Building_Storage).FullName
                && s.members.All(m => m.grid.All(g => g.things.Count == 1 && g.things[0].thingId == m.thing.thingId))
                && s.parcels.All(p => p.count == 1 && p.spawned && !p.destroyed && p.heldByFixtureMap && !s.members.Any(m => m.cells.Contains(p.cell))) && s.actorIdle,
                "Complete native slot/grid/ASF empty-registry census plus distinct external physical parcels and idle actor.");
        }

        private ProjectionFixtureThing ThingState(Thing thing) => new ProjectionFixtureThing { thingId = thing.thingIDNumber, def = thing.def.defName, count = thing.stackCount,
            stackLimit = thing.def.stackLimit, cell = thing.Position.ToString(), spawned = thing.Spawned, destroyed = thing.Destroyed, mapId = thing.Map?.uniqueID,
            heldByFixtureMap = ReferenceEquals(thing.ParentHolder, map), holderType = thing.ParentHolder?.GetType().FullName, faction = thing.Faction?.GetUniqueLoadID(), stuff = thing.Stuff?.defName, hitPoints = thing.HitPoints };
        private Cap03FilterSettings Settings(StorageSettings settings) => settings == null ? null : new Cap03FilterSettings
        {
            settingsIdentity = Id(settings), filterIdentity = Id(settings.filter), ownerIdentity = Id(settings.owner), ownerType = settings.owner?.GetType().FullName, priority = settings.Priority.ToString(),
            contents = new ProjectionFixtureFilter { type = settings.filter.GetType().FullName, onlySpecial = settings.filter.OnlySpecialFilters,
                allowedDefs = settings.filter.AllowedThingDefs.Select(x => x.defName).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                disallowedSpecials = ((List<SpecialThingFilterDef>)specials.GetValue(settings.filter)).Select(x => x?.defName).ToList(),
                hitPointsMin = settings.filter.AllowedHitPointsPercents.min, hitPointsMax = settings.filter.AllowedHitPointsPercents.max,
                mentalBreakMin = settings.filter.AllowedMentalBreakChance.min, mentalBreakMax = settings.filter.AllowedMentalBreakChance.max, qualities = settings.filter.AllowedQualityLevels.ToString() }
        };
        private Cap03FilterSnapshot Snapshot(StorageGroup group, Building_Storage asf, Building_Storage shelf, Thing[] parcels)
        {
            var s = new Cap03FilterSnapshot { tick = Find.TickManager.TicksGame, mapId = map.uniqueID, groupLoadId = group?.loadID ?? -1, groupIdentity = Id(group),
                memberOrder = group?.members.Select(x => ((Thing)x).thingIDNumber).ToList() ?? new List<int>(), groupEffective = Settings(group?.GetStoreSettings()), groupFixed = Settings(group?.GetParentStoreSettings()),
                actor = ThingState(actor), actorIdle = ActorIdle(), groupRegistered = group != null && map.storageGroups.HasStorageGroup(group),
                parcels = parcels.Select(ThingState).ToList(), members = new List<Cap03FilterMember>() };
            foreach (var parent in new[] { asf, shelf })
            {
                var cells = parent.OccupiedRect().Cells.ToList(); var collection = parent == asf ? api.RegistryIdentity(asf) : null;
                s.members.Add(new Cap03FilterMember { thing = ThingState(parent), runtimeType = parent.GetType().FullName, defPackage = parent.def.modContentPack?.PackageId,
                    groupTag = ((IStorageGroupMember)parent).StorageGroupTag, slotIdentity = Id(parent.GetSlotGroup()), groupIdentity = Id(parent.storageGroup), ordinal = group?.members.IndexOf(parent) ?? -1,
                    definitionMaximum = parent.def.building.maxItemsInCell, nativeMaximum = cells[0].GetMaxItemsAllowedInCell(map), registered = cells.All(c => ReferenceEquals(map.haulDestinationManager.SlotGroupAt(c), parent.GetSlotGroup())),
                    registryCount = collection == null ? (int?)null : (int)registryCount.GetValue(collection, null), contentsPacked = parent == asf && (bool)packed.GetValue(asf, null),
                    compTypes = parent.AllComps.Select(c => c.GetType().FullName).ToList(), cells = cells.Select(c => c.ToString()).ToList(),
                    grid = cells.Select(c => new Cap03FilterGrid { cell = c.ToString(), things = map.thingGrid.ThingsListAt(c).Select(ThingState).ToList() }).ToList(),
                    local = Settings(parent.settings), effective = Settings(parent.GetStoreSettings()), declaredFixed = Settings(parent == asf ? api.DeclaredFixed(parent) : parent.GetParentStoreSettings()), interfaceFixed = Settings(((IStorageGroupMember)parent).ParentStoreSettings) });
            }
            return s;
        }
    }
}
