using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    // Changed-build native projector adapter fixture. No allocator, claims, job
    // injection, tick advancement, or published-build equivalence is involved.
    internal sealed class StorageProjectionScenario
    {
        private static readonly FieldInfo DisallowedSpecials = typeof(ThingFilter).GetField("disallowedSpecialFilters", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Map map;
        private readonly StorageProjectionResult result;
        private readonly List<Scene> scenes = new List<Scene>();
        private readonly Dictionary<string, string> resourceOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        private StorageProjectionBridge api;
        private Pawn actor;
        private bool setupValid;
        private bool behaviorPassed = true;

        private StorageProjectionScenario(Map map, string expectedBehavior)
        {
            this.map = map;
            result = new StorageProjectionResult { caseId = "CAP02", expectedBehavior = expectedBehavior,
                contract = "native-physical-projector-v1", status = "inconclusive", startedTick = Find.TickManager.TicksGame,
                mainThread = Thread.CurrentThread.ManagedThreadId,
                scope = "Changed-build native physical/eligibility adapter; no allocation, claims, executed jobs, or report resolution." };
        }
        internal static StorageProjectionResult Run(Map map, string expectedBehavior)
        {
            var test = new StorageProjectionScenario(map, expectedBehavior);
            try
            {
                test.Setup();
                var catalogStatus = test.api.CreateCatalog();
                test.Operation("catalog", "create", catalogStatus);
                if (test.Check("catalog-created", catalogStatus.usable, Json.Stringify(catalogStatus)))
                {
                    test.result.nativeIdentity = test.api.NativeIdentity;
                    test.result.patchInventory = test.api.PatchInventory;
                    test.Check("native-semantics-reviewed", test.api.NativeReviewed, test.result.nativeIdentity);
                    foreach (var scene in test.scenes.Where(x => x.shelf != null)) test.Shelf(scene);
                    test.Zone(test.scenes.Single(x => x.zone != null));
                }
                foreach (var scene in test.scenes)
                {
                    var finalState = test.PhysicalState(scene);
                    test.result.finalPhysical.Add(finalState);
                    HarnessSession.Event("storage-projection-physical-final", Json.Stringify(finalState));
                    test.Require(scene.id + "-physical-unchanged", Json.Stringify(finalState) == scene.initial,
                        "Before=" + scene.initial + "; after=" + Json.Stringify(finalState));
                }
                test.Require("same-tick", Find.TickManager.TicksGame == test.result.startedTick, "No simulation ticks advanced during the synchronous adapter fixture.");
                test.result.finalActor = test.ThingState(test.actor);
                test.Require("actor-unchanged", ActorStill(test.actor)
                    && Json.Stringify(test.result.initialActor) == Json.Stringify(test.result.finalActor),
                    "Actual actor identity/map/position/faction and custody match setup; empty inventory/carry and no current job.");
                test.Check("all-scopes-disposed", !test.api.HasOpenScope, "Actual projector HasOpenScope after all using/finally boundaries.");
                test.result.fixtureValid = test.setupValid;
                test.result.requestedBehaviorSatisfied = test.setupValid && test.behaviorPassed;
                test.result.expectationMatched = test.result.requestedBehaviorSatisfied && expectedBehavior == "satisfied";
                test.result.status = test.result.expectationMatched ? "passed" : "failed";
            }
            catch (Exception error)
            {
                var cause = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                test.result.error = cause.ToString();
                test.Record("fixture-exception", "fixture", false, cause.ToString());
                test.result.fixtureValid = false; test.result.requestedBehaviorSatisfied = false;
                test.result.expectationMatched = false; test.result.status = "inconclusive";
            }
            finally
            {
                test.result.finishedTick = Find.TickManager.TicksGame;
                HarnessSession.Event("storage-projection-result", Json.Stringify(test.result));
            }
            return test.result;
        }
        private static bool ActorStill(Pawn pawn) => pawn != null && pawn.Spawned && pawn.inventory.innerContainer.Count == 0
            && pawn.carryTracker.CarriedThing == null && pawn.CurJob == null;

        private void Setup()
        {
            Require("expectation", result.expectedBehavior == "satisfied", "CAP02 supports only explicit satisfied; no published comparator or expected-gap pass.");
            Require("map", map != null && map.IsPlayerHome && Current.Game != null && UnityData.IsInMainThread, "Real isolated initialized player-home map on the Unity main thread.");
            Require("native-provider-only", !AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "AdaptiveStorageFramework"), "ASF is a separately pending fixture matrix.");
            Require("native-filter-state-binding", DisallowedSpecials?.FieldType == typeof(List<SpecialThingFilterDef>), "Exact native filter list binding preserves order and duplicate entries in fixture state evidence.");
            api = new StorageProjectionBridge(); result.assemblies = api.Assemblies; result.bindings = api.Bindings;
            result.sessionId = api.SessionId.ToString("N");
            HarnessSession.Event("storage-projection-bind", Json.Stringify(new ProjectionFixtureBinding { assemblies = result.assemblies, bindings = result.bindings }));
            var generated = map.listerThings.AllThings.Where(x => x is Pawn || x is Skyfaller || x is ActiveTransporter).ToList();
            foreach (var thing in generated) if (thing.Spawned) thing.DeSpawn();
            var rect = new CellRect(map.Center.x - 20, map.Center.z - 9, 41, 19);
            Require("fixture-bounds", rect.Cells.All(x => x.InBounds(map)), rect.ToString());
            Require("no-zones-overwritten", rect.Cells.All(x => map.zoneManager.ZoneAt(x) == null), "Only empty generated-map ground is replaced.");
            GenDebug.ClearArea(rect, map);
            foreach (var cell in rect.Cells)
            {
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.roofGrid.SetRoof(cell, null);
                map.areaManager.Home[cell] = true; map.fogGrid.Unfog(cell);
            }
            Rand.PushState(12002);
            try { actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); }
            finally { Rand.PopState(); }
            Require("normal-actor", actor != null && actor.RaceProps.Humanlike && !actor.Downed && actor.inventory != null && actor.carryTracker != null, "Actual normal human pawn.");
            actor.inventory.innerContainer.ClearAndDestroyContents(); actor.workSettings.EnableAndInitializeIfNotAlreadyInitialized(); actor.workSettings.DisableAll();
            GenSpawn.Spawn(actor, map.Center + new IntVec3(0, 0, -7), map);
            Require("only-fixture-actor", map.mapPawns.AllPawnsSpawned.Count == 1 && ActorStill(actor), "No generated actor may interfere; no ticks advance.");
            result.initialActor = ThingState(actor);
            HarnessSession.Event("storage-projection-actor-setup", Json.Stringify(result.initialActor));
            foreach (var def in new[] { ThingDefOf.Steel, ThingDefOf.WoodLog, ThingDefOf.Silver, ThingDefOf.Cloth, DefDatabase<ThingDef>.GetNamed("Uranium") })
                Require("stack-limit-" + def.defName, def.category == ThingCategory.Item && def.stackLimit > 7, "Actual stackLimit=" + def.stackLimit);
            MakeScene("occupied-two-full-one-vacant", 0, 2, false);
            MakeScene("empty-three-vacant", 1, 0, false);
            MakeScene("full-seven-unit-silver-top-up", 2, 3, true);
            MakeScene("full-incompatible", 3, 3, false);
            MakeScene("one-cell-stockpile", 4, 0, false, true);
            setupValid = true;
            HarnessSession.Event("storage-projection-fixture-setup", "Despawned generated actors/landing objects=" + generated.Count
                + "; four actual unlinked ShelfSmall and one actual Zone_Stockpile; floor parcels; no claims/allocations/jobs.");
        }
        private void MakeScene(string id, int index, int occupied, bool partial, bool zone = false)
        {
            var cell = map.Center + new IntVec3(-16 + index * 8, 0, 0);
            var scene = new Scene { id = id, cell = cell, expectedItems = occupied, partial = partial, expectedSlots = zone ? 1 : 3 };
            StorageSettings settings;
            if (zone)
            {
                scene.zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                map.zoneManager.RegisterZone(scene.zone); scene.zone.AddCell(cell);
                scene.parent = scene.zone; settings = scene.zone.settings;
            }
            else
            {
                var def = DefDatabase<ThingDef>.GetNamed("ShelfSmall");
                var made = ThingMaker.MakeThing(def, ThingDefOf.WoodLog);
                scene.shelf = (Building_Storage)GenSpawn.Spawn(made, cell, map, Rot4.North); scene.shelf.SetFaction(Faction.OfPlayer);
                scene.parent = scene.shelf; settings = scene.shelf.GetStoreSettings();
                Require(id + "-actual-native-shelf", scene.shelf.GetType() == typeof(Building_Storage) && def.size.x == 1 && def.size.z == 1
                    && scene.shelf.storageGroup == null, "Actual unlinked vanilla ShelfSmall; no custom provider class.");
            }
            settings.filter.SetDisallowAll();
            foreach (var accepted in new[] { ThingDefOf.Steel, ThingDefOf.WoodLog, ThingDefOf.Silver, ThingDefOf.Cloth, DefDatabase<ThingDef>.GetNamed("Uranium") }) settings.filter.SetAllow(accepted, true);
            settings.Priority = StoragePriority.Critical;
            scene.group = scene.parent.GetSlotGroup();
            Require(id + "-registered", ReferenceEquals(map.haulDestinationManager.SlotGroupAt(cell), scene.group), "Exact actual map slot registration.");
            Require(id + "-actual-slot-limit", cell.GetMaxItemsAllowedInCell(map) == scene.expectedSlots, "GetMaxItemsAllowedInCell=" + cell.GetMaxItemsAllowedInCell(map));
            if (occupied >= 1) Spawn(ThingDefOf.Steel, ThingDefOf.Steel.stackLimit, cell);
            if (occupied >= 2) Spawn(ThingDefOf.WoodLog, ThingDefOf.WoodLog.stackLimit, cell);
            if (occupied == 3)
            {
                var third = partial ? ThingDefOf.Silver : DefDatabase<ThingDef>.GetNamed("Uranium");
                scene.target = Spawn(third, third.stackLimit - (partial ? 7 : 0), cell);
            }
            scene.silver = Spawn(ThingDefOf.Silver, 7, cell + new IntVec3(-1, 0, 4));
            scene.cloth = Spawn(ThingDefOf.Cloth, 7, cell + new IntVec3(1, 0, 4));
            foreach (var subject in new[] { scene.silver, scene.cloth })
                Require(id + "-floor-" + subject.ThingID, subject.Spawned && subject.Map == map && ReferenceEquals(subject.ParentHolder, map)
                    && subject.stackCount == 7 && settings.AllowedToAccept(subject), "Actual floor Thing and effective Thing filter accepts " + subject.ThingID);
            Require(id + "-physical-item-count", Items(cell).Count == occupied, "Actual item count=" + Items(cell).Count);
            if (partial)
                Require(id + "-actual-directional-controls", scene.target.CanStackWith(scene.silver) && !scene.target.CanStackWith(scene.cloth),
                    "Actual target.CanStackWith(Silver)=true and target.CanStackWith(Cloth)=false; deficit seven.");
            scene.settings = settings; scene.filter = settings.filter;
            scene.fixedSettings = scene.parent.GetParentStoreSettings(); scene.fixedFilter = scene.fixedSettings.filter;
            var initial = PhysicalState(scene);
            scene.initial = Json.Stringify(initial); scenes.Add(scene); result.initialPhysical.Add(initial);
            HarnessSession.Event("storage-projection-physical-setup", scene.initial);
        }
        private Thing Spawn(ThingDef def, int count, IntVec3 cell)
        {
            var thing = ThingMaker.MakeThing(def); thing.stackCount = count;
            var spawned = GenSpawn.Spawn(thing, cell, map);
            Require("spawn-" + thing.ThingID, ReferenceEquals(thing, spawned) && thing.stackCount == count && thing.Position == cell, "Fixture spawn preserved actual identity/count/cell.");
            return spawned;
        }
        private List<Thing> Items(IntVec3 cell) => map.thingGrid.ThingsListAt(cell).Where(x => x.def.category == ThingCategory.Item).OrderBy(x => x.thingIDNumber).ToList();
        private ProjectionFixtureThing ThingState(Thing thing) => new ProjectionFixtureThing
        {
            thingId = thing.thingIDNumber, def = thing.def.defName, count = thing.stackCount, stackLimit = thing.def.stackLimit,
            cell = thing.Position.ToString(), spawned = thing.Spawned, mapId = thing.Map?.uniqueID,
            faction = thing.Faction?.GetUniqueLoadID(), holderType = thing.ParentHolder?.GetType().FullName,
            heldByFixtureMap = ReferenceEquals(thing.ParentHolder, map), destroyed = thing.Destroyed,
            stuff = thing.Stuff?.defName, hitPoints = thing.HitPoints
        };
        private ProjectionFixtureFilter FilterState(ThingFilter filter) => new ProjectionFixtureFilter
        {
            type = filter.GetType().FullName, onlySpecial = filter.OnlySpecialFilters,
            allowedDefs = filter.AllowedThingDefs.Select(x => x.defName).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            disallowedSpecials = ((List<SpecialThingFilterDef>)DisallowedSpecials.GetValue(filter)).Select(x => x?.defName).ToList(),
            hitPointsMin = filter.AllowedHitPointsPercents.min, hitPointsMax = filter.AllowedHitPointsPercents.max,
            mentalBreakMin = filter.AllowedMentalBreakChance.min, mentalBreakMax = filter.AllowedMentalBreakChance.max,
            qualities = filter.AllowedQualityLevels.ToString()
        };
        private ProjectionFixturePhysical PhysicalState(Scene scene)
        {
            var settings = scene.parent.GetStoreSettings(); var fixedSettings = scene.parent.GetParentStoreSettings();
            return new ProjectionFixturePhysical
            {
                scene = scene.id, session = result.sessionId, mapId = map.uniqueID, tick = Find.TickManager.TicksGame,
                cell = scene.cell.ToString(), parentKey = ParentKey(scene), parentId = scene.shelf?.thingIDNumber ?? scene.zone.ID,
                parentType = scene.parent.GetType().FullName, parentThing = scene.shelf == null ? null : ThingState(scene.shelf),
                maximumSlots = scene.cell.GetMaxItemsAllowedInCell(map), items = Items(scene.cell).Select(ThingState).ToList(),
                silver = ThingState(scene.silver), cloth = ThingState(scene.cloth),
                registered = ReferenceEquals(map.haulDestinationManager.SlotGroupAt(scene.cell), scene.group),
                zoneRegistered = scene.zone == null || ReferenceEquals(map.zoneManager.ZoneAt(scene.cell), scene.zone),
                zoneCells = scene.zone?.cells.Select(x => x.ToString()).ToList(), priority = settings.Priority.ToString(),
                settingsOwnerIsParent = ReferenceEquals(settings.owner, scene.parent),
                settingsIdentityUnchanged = ReferenceEquals(settings, scene.settings) && ReferenceEquals(settings.filter, scene.filter),
                fixedIdentityUnchanged = ReferenceEquals(fixedSettings, scene.fixedSettings) && ReferenceEquals(fixedSettings.filter, scene.fixedFilter),
                filter = FilterState(settings.filter), fixedFilter = FilterState(fixedSettings.filter)
            };
        }
        private string Physical(Scene scene) => Json.Stringify(PhysicalState(scene));
        private static string ParentKey(Scene scene) => scene.shelf != null ? "building:" + scene.shelf.thingIDNumber : "zone:" + scene.zone.ID;
        private static string ParcelId(Scene scene, Thing subject) => scene.id + "/" + subject.ThingID;
        private bool Prepare(Scene scene, string stage, int indexedCells)
        {
            // Explicit complete preparation allowance: fixture data only, no
            // caller-built fake capacity. Formula is recorded and finite.
            long allowance = checked(8L * (DefDatabase<ThingDef>.AllDefsListForReading.Count + 1)
                * (DefDatabase<ThingCategoryDef>.AllDefsListForReading.Count + 1)
                * (DefDatabase<SpecialThingFilterDef>.AllDefsListForReading.Count + 1) + 1000000L);
            var prepared = api.Prepare(scene.parent, allowance, scene.shelf != null);
            var status = api.ResultStatus(prepared);
            Operation(scene.id, stage, status, "allowance=" + allowance + "; charged=" + api.Get<long>(prepared, "ChargedWork")
                + "; ready=" + api.Get<bool>(prepared, "Ready") + "; indexed=" + api.Get<int>(prepared, "IndexedZoneCells")
                + "; explicitFootprintWarmup=" + api.Get<bool>(prepared, "FootprintWarmupRequested"));
            bool okay = Check(scene.id + "-" + stage, status.usable && api.Get<bool>(prepared, "Ready")
                && api.Get<int>(prepared, "IndexedZoneCells") == indexedCells, Json.Stringify(status));
            Check(scene.id + "-" + stage + "-work", api.Get<long>(prepared, "ChargedWork") > 0 && api.Get<long>(prepared, "ChargedWork") <= allowance,
                "Preparation declares finite work and records its charge.");
            return okay;
        }
        private object Open(Scene scene, string stage)
        {
            var opened = api.Open(map, scene.group, "CAP02/" + scene.id + "/" + stage);
            var status = api.ResultStatus(opened); Operation(scene.id, stage + "-open", status);
            var scope = api.Scope(opened);
            if (!Check(scene.id + "-" + stage + "-open", status.usable && scope != null, Json.Stringify(status)))
            { (scope as IDisposable)?.Dispose(); return null; }
            return scope;
        }
        private void Shelf(Scene scene)
        {
            if (!Prepare(scene, "prepare", 0)) return;
            var first = ProbeOrder(scene, false);
            var reverse = ProbeOrder(scene, true);
            Check(scene.id + "-reverse-order-invariant", first != null && reverse != null && first == reverse,
                "Canonical physical resource keys and per-subject eligibility exclude observation/work IDs. forward=" + first + "; reverse=" + reverse);
        }
        private string ProbeOrder(Scene scene, bool reverse)
        {
            string stage = reverse ? "cloth-then-silver" : "silver-then-cloth";
            var scope = Open(scene, stage); if (scope == null) return null;
            using ((IDisposable)scope)
            {
                var rawCell = api.Observe(scope, scene.cell); var cell = Cell(scene, stage, rawCell);
                if (!Check(scene.id + "-" + stage + "-physical-complete", cell.status.usable, Json.Stringify(cell.status))) return null;
                CheckCell(scene, cell);
                var fingerprints = new List<string>();
                foreach (var subject in reverse ? new[] { scene.cloth, scene.silver } : new[] { scene.silver, scene.cloth })
                {
                    var parcel = api.Parcel(ParcelId(scene, subject), subject, actor);
                    var rawEligibility = api.Eligibility(scope, parcel, rawCell);
                    var row = Eligibility(scene, stage, rawEligibility); CheckEligibility(scene, subject, cell, row);
                    var validation = api.Recheck(scope, parcel, rawCell, rawEligibility);
                    var status = api.ResultStatus(validation); Operation(scene.id, stage + "-recheck-" + subject.def.defName, status);
                    var fresh = api.Fresh(validation);
                    Check(scene.id + "-" + stage + "-recheck-" + subject.def.defName, status.usable && fresh != null,
                        "Unchanged actual observation must support a fresh eligibility evaluation: " + Json.Stringify(status));
                    if (fresh != null)
                    {
                        var freshRow = Eligibility(scene, stage + "-fresh", fresh);
                        CheckEligibility(scene, subject, cell, freshRow);
                        Check(scene.id + "-" + stage + "-fresh-equal-" + subject.def.defName, EligibilityKey(row) == EligibilityKey(freshRow), "No caller booleans or simulated parcels used.");
                    }
                    fingerprints.Add(subject.ThingID + ":" + EligibilityKey(row));
                }
                var after = Cell(scene, stage + "-after-probes", api.Observe(scope, scene.cell));
                if (after.status.usable) CheckCell(scene, after);
                Check(scene.id + "-" + stage + "-resource-not-consumed", after.status.usable && CellKey(cell) == CellKey(after), "Eligibility probes do not allocate or consume shared physical vacancies.");
                fingerprints.Sort(StringComparer.Ordinal);
                return CellKey(cell) + ";" + string.Join(";", fingerprints);
            }
        }
        private void CheckCell(Scene scene, ProjectionFixtureCell cell)
        {
            var actual = Items(scene.cell);
            string prefix = result.sessionId + "/" + map.uniqueID + "/" + ParentKey(scene) + "/" + scene.cell.x + "," + scene.cell.z;
            Check(scene.id + "-physical-resource-counts", cell.maximumSlots == scene.expectedSlots && cell.itemCount == scene.expectedItems
                && cell.vacantSlots == scene.expectedSlots - scene.expectedItems && cell.stacks.Count == actual.Count,
                "Actual single physical slot pool and actual item rows; " + Json.Stringify(cell));
            Check(scene.id + "-physical-resource-identities", cell.stacks.All(x => actual.Any(t => t.thingIDNumber == x.thingId && t.def.defName == x.def
                && t.stackCount == x.count && t.def.stackLimit == x.stackLimit && x.deficit == Math.Max((long)t.def.stackLimit - t.stackCount, 0)))
                && cell.stacks.All(x => x.key == prefix + "/stack:" + x.thingId) && cell.stacks.Select(x => x.thingId).Distinct().Count() == cell.stacks.Count
                && cell.stacks.Select(x => x.key).Distinct().Count() == cell.stacks.Count && cell.vacantKey == prefix + "/vacant"
                && cell.parentKey == ParentKey(scene) && cell.groupKey == "concrete", "Resource keys bind the actual session/map/parent/cell and physical Thing IDs.");
            foreach (var key in cell.stacks.Select(x => x.key).Concat(new[] { cell.vacantKey }))
            {
                bool valid = !string.IsNullOrEmpty(key) && (!resourceOwners.TryGetValue(key, out var owner) || owner == scene.id);
                Check(scene.id + "-cross-scene-resource-identity", valid, "Resource=" + key + "; actual scene=" + scene.id);
                if (valid) resourceOwners[key] = scene.id;
            }
            Check(scene.id + "-physical-provenance", cell.session == result.sessionId && cell.mapId == map.uniqueID && cell.tick == result.startedTick
                && cell.generation == 1 && cell.cell == scene.cell.ToString() && cell.observationId > 0, "Actual scope/session/map/tick/cell provenance must accompany every usable physical row.");
        }
        private void CheckEligibility(Scene scene, Thing subject, ProjectionFixtureCell cell, ProjectionFixtureEligibility row)
        {
            Check(scene.id + "-eligibility-identity-" + subject.def.defName, row.observationId == cell.observationId
                && row.parcelId == ParcelId(scene, subject), "Every initial/fresh row names the actual observed cell and requested physical parcel.");
            bool vacancy = scene.expectedItems < scene.expectedSlots;
            bool topUp = scene.partial && subject.def == ThingDefOf.Silver;
            bool expectedEligible = vacancy || topUp;
            Check(scene.id + "-eligibility-" + subject.def.defName, row.status.usable && row.state == (expectedEligible ? "Eligible" : "Refused")
                && row.vacantEligible == vacancy && row.unitsPerNewStack == (vacancy ? (int?)subject.def.stackLimit : null), Json.Stringify(row));
            Check(scene.id + "-top-up-" + subject.def.defName, topUp
                ? row.topUps.Count == 1 && row.topUps[0].targetId == scene.target.thingIDNumber && row.topUps[0].units == 7
                    && cell.stacks.Any(x => x.thingId == scene.target.thingIDNumber && x.key == row.topUps[0].key)
                : row.topUps.Count == 0, "Only the actual directional compatible seven-unit deficit may become a top-up edge.");
            var native = row.predicates.Where(x => x.name == "native-IsGoodStoreCell").ToList();
            Check(scene.id + "-actual-native-predicate-" + subject.def.defName, native.Count == 1 && native[0].state == (expectedEligible ? "Eligible" : "Refused")
                && (expectedEligible || native[0].reason == "NativeCellRefused"), "The native predicate must execute; unsupported or deferred is not a refusal.");
            if (topUp)
                Check(scene.id + "-actual-directional-predicate", row.predicates.Any(x => x.name == "directional-CanStackWith"
                    && x.targetId == scene.target.thingIDNumber && x.state == "Eligible"), "Actual target-directed stack predicate must be observed.");
        }
        private void Zone(Scene scene)
        {
            if (!Prepare(scene, "prepare-valid", 1)) return;
            var scope = Open(scene, "valid-before-mutation"); if (scope == null) return;
            bool removed = false;
            try
            {
                using ((IDisposable)scope)
                {
                    var raw = api.Observe(scope, scene.cell); var row = Cell(scene, "valid-before-mutation", raw);
                    if (!Check(scene.id + "-initial-valid", row.status.usable, Json.Stringify(row.status))) return;
                    CheckCell(scene, row);
                    var parcel = api.Parcel(ParcelId(scene, scene.silver), scene.silver, actor);
                    var eligible = api.Eligibility(scope, parcel, raw); CheckEligibility(scene, scene.silver, row, Eligibility(scene, "valid", eligible));
                    var validRecheck = api.Recheck(scope, parcel, raw, eligible);
                    var fresh = api.Fresh(validRecheck);
                    Check(scene.id + "-initial-recheck", api.ResultStatus(validRecheck).usable && fresh != null, Json.Stringify(api.ResultStatus(validRecheck)));
                    if (fresh != null) CheckEligibility(scene, scene.silver, row, Eligibility(scene, "valid-fresh", fresh));
                    // Deliberate test control: omit native RemoveCell notifications
                    // to keep the map registration pointing to a now-absent member.
                    removed = scene.zone.cells.Remove(scene.cell);
                    Require(scene.id + "-stale-control-established", removed && scene.zone.cells.Count == 0
                        && ReferenceEquals(map.haulDestinationManager.SlotGroupAt(scene.cell), scene.group)
                        && ReferenceEquals(map.zoneManager.ZoneAt(scene.cell), scene.zone), "Fixture alone removed zone.cells entry; both native registries deliberately retain the zone.");
                    HarnessSession.Event("storage-projection-fixture-mutation", "Removed exactly one zone.cells entry without notifications; " + Physical(scene));
                    var stale = Cell(scene, "retained-index-stale", api.Observe(scope, scene.cell));
                    Check(scene.id + "-retained-index-invalidated", !stale.status.usable && stale.status.reason == "GroupChanged", Json.Stringify(stale.status));
                    var staleRecheck = api.ResultStatus(api.Recheck(scope, parcel, raw, eligible));
                    Operation(scene.id, "stale-recheck", staleRecheck);
                    Check(scene.id + "-prior-observation-invalidated", !staleRecheck.usable && staleRecheck.reason == "GroupChanged", Json.Stringify(staleRecheck));
                }
                // Preparing an empty list can index zero members; it must still
                // never make the missing requested cell a complete observation.
                if (Prepare(scene, "prepare-stale-empty-list", 0))
                {
                    var freshScope = Open(scene, "fresh-stale-index");
                    if (freshScope != null) using ((IDisposable)freshScope)
                    {
                        var missing = Cell(scene, "fresh-stale-index", api.Observe(freshScope, scene.cell));
                        Check(scene.id + "-grid-is-not-membership-proof", !missing.status.usable && missing.status.reason == "GroupChanged", Json.Stringify(missing.status));
                    }
                }
            }
            finally
            {
                // Dispose is idempotent; also covers exceptions before the using.
                try { ((IDisposable)scope).Dispose(); }
                catch (Exception error)
                {
                    Record(scene.id + "-dispose-failure", "fixture", false, error.ToString());
                    throw;
                }
                finally
                {
                    if (removed)
                    {
                        // Restore even when disposal or the precondition fails.
                        // This list belongs solely to this disposable fixture.
                        bool expected = scene.zone.cells.Count == 0;
                        scene.zone.cells.Clear(); scene.zone.cells.Add(scene.cell);
                        HarnessSession.Event("storage-projection-fixture-repair", "Restored original sole zone.cells entry; registries were untouched; " + Physical(scene));
                        Require(scene.id + "-repair-precondition", expected, "No unrelated zone edits occurred during this synchronous control.");
                    }
                }
            }
            Require(scene.id + "-repaired-native-membership", scene.zone.cells.Count == 1 && scene.zone.cells[0] == scene.cell
                && ReferenceEquals(map.haulDestinationManager.SlotGroupAt(scene.cell), scene.group), "Fixture restores exact original list contents and registration.");
            if (Prepare(scene, "prepare-repaired", 1))
            {
                var recovered = ProbeOrder(scene, false);
                Check(scene.id + "-recovery-complete", recovered != null, "New preparation and fresh scope recover valid physical observation/eligibility/recheck after the deliberate control.");
            }
        }
        private ProjectionFixtureCell Cell(Scene scene, string stage, object raw)
        {
            var row = api.Cell(raw); row.fixtureScene = scene.id; row.fixtureStage = stage; result.cells.Add(row);
            HarnessSession.Event("storage-projection-cell", Json.Stringify(new ProjectionFixtureCellEvent { scene = scene.id, stage = stage, value = row })); return row;
        }
        private ProjectionFixtureEligibility Eligibility(Scene scene, string stage, object raw)
        {
            var row = api.EligibilityRow(raw); row.fixtureScene = scene.id; row.fixtureStage = stage; result.eligibility.Add(row);
            HarnessSession.Event("storage-projection-eligibility", Json.Stringify(new ProjectionFixtureEligibilityEvent { scene = scene.id, stage = stage, value = row })); return row;
        }
        private static string CellKey(ProjectionFixtureCell cell) => cell.session + "/" + cell.mapId + "/" + cell.parentKey + "/" + cell.groupKey
            + "/" + cell.cell + "/" + cell.vacantKey + ":" + cell.vacantSlots + ":" + cell.maximumSlots
            + ";" + string.Join("|", cell.stacks.OrderBy(x => x.thingId).Select(x => x.key + ":" + x.thingId + ":" + x.def + ":" + x.count + ":" + x.stackLimit + ":" + x.deficit));
        private static string EligibilityKey(ProjectionFixtureEligibility row) => row.status.observation + ":" + row.status.capability + ":" + row.status.reason
            + ":" + row.state + ":" + row.vacantEligible + ":" + row.unitsPerNewStack + ";"
            + string.Join("|", row.topUps.OrderBy(x => x.targetId).Select(x => x.key + ":" + x.targetId + ":" + x.units)) + ";"
            + string.Join("|", row.predicates.Select(x => x.name + ":" + x.targetId + ":" + x.state + ":" + x.reason).OrderBy(x => x, StringComparer.Ordinal));
        private void Operation(string scene, string stage, ProjectionFixtureStatus status, string detail = null)
        {
            var row = new ProjectionFixtureOperation { scene = scene, stage = stage, status = status, detail = detail };
            result.operations.Add(row); HarnessSession.Event("storage-projection-operation", Json.Stringify(row));
        }
        private void Require(string id, bool okay, string detail)
        { Record(id, "fixture", okay, detail); if (!okay) throw new InvalidOperationException(id + ": " + detail); }
        private bool Check(string id, bool okay, string detail)
        { Record(id, "behavior", okay, detail); if (!okay) behaviorPassed = false; return okay; }
        private void Record(string id, string kind, bool okay, string detail)
        {
            var row = new ProjectionFixtureAssertion { sequence = result.assertions.Count + 1, id = id, kind = kind, passed = okay, detail = detail };
            result.assertions.Add(row); HarnessSession.Event("storage-projection-assertion", Json.Stringify(row));
        }
        private sealed class Scene
        {
            internal string id, initial;
            internal IntVec3 cell;
            internal Building_Storage shelf;
            internal Zone_Stockpile zone;
            internal ISlotGroupParent parent;
            internal SlotGroup group;
            internal StorageSettings settings, fixedSettings;
            internal ThingFilter filter, fixedFilter;
            internal Thing silver, cloth, target;
            internal int expectedItems, expectedSlots;
            internal bool partial;
        }
    }

    [DataContract] internal sealed class StorageProjectionResult
    {
        [DataMember] public string caseId { get; set; }
        [DataMember] public string expectedBehavior { get; set; }
        [DataMember] public string contract { get; set; }
        [DataMember] public string scope { get; set; }
        [DataMember] public string status { get; set; }
        [DataMember] public string sessionId { get; set; }
        [DataMember] public string nativeIdentity { get; set; }
        [DataMember] public string error { get; set; }
        [DataMember] public bool fixtureValid { get; set; }
        [DataMember] public bool requestedBehaviorSatisfied { get; set; }
        [DataMember] public bool expectationMatched { get; set; }
        [DataMember] public int startedTick { get; set; }
        [DataMember] public int finishedTick { get; set; }
        [DataMember] public int mainThread { get; set; }
        [DataMember] public List<ProjectionFixtureAssembly> assemblies { get; set; } = new List<ProjectionFixtureAssembly>();
        [DataMember] public List<string> bindings { get; set; } = new List<string>();
        [DataMember] public List<string> patchInventory { get; set; } = new List<string>();
        [DataMember] public List<ProjectionFixtureAssertion> assertions { get; set; } = new List<ProjectionFixtureAssertion>();
        [DataMember] public List<ProjectionFixtureOperation> operations { get; set; } = new List<ProjectionFixtureOperation>();
        [DataMember] public List<ProjectionFixtureCell> cells { get; set; } = new List<ProjectionFixtureCell>();
        [DataMember] public List<ProjectionFixtureEligibility> eligibility { get; set; } = new List<ProjectionFixtureEligibility>();
        [DataMember] public ProjectionFixtureThing initialActor { get; set; }
        [DataMember] public ProjectionFixtureThing finalActor { get; set; }
        [DataMember] public List<ProjectionFixturePhysical> initialPhysical { get; set; } = new List<ProjectionFixturePhysical>();
        [DataMember] public List<ProjectionFixturePhysical> finalPhysical { get; set; } = new List<ProjectionFixturePhysical>();
    }
    [DataContract] internal sealed class ProjectionFixtureThing
    {
        [DataMember] public int thingId { get; set; }
        [DataMember] public string def { get; set; }
        [DataMember] public int count { get; set; }
        [DataMember] public int stackLimit { get; set; }
        [DataMember] public string cell { get; set; }
        [DataMember] public bool spawned { get; set; }
        [DataMember] public int? mapId { get; set; }
        [DataMember] public string faction { get; set; }
        [DataMember] public string holderType { get; set; }
        [DataMember] public bool heldByFixtureMap { get; set; }
        [DataMember] public bool destroyed { get; set; }
        [DataMember] public string stuff { get; set; }
        [DataMember] public int hitPoints { get; set; }
    }
    [DataContract] internal sealed class ProjectionFixtureFilter
    {
        [DataMember] public string type { get; set; }
        [DataMember] public bool onlySpecial { get; set; }
        [DataMember] public List<string> allowedDefs { get; set; }
        [DataMember] public List<string> disallowedSpecials { get; set; }
        [DataMember] public float hitPointsMin { get; set; }
        [DataMember] public float hitPointsMax { get; set; }
        [DataMember] public float mentalBreakMin { get; set; }
        [DataMember] public float mentalBreakMax { get; set; }
        [DataMember] public string qualities { get; set; }
    }
    [DataContract] internal sealed class ProjectionFixturePhysical
    {
        [DataMember] public string scene { get; set; }
        [DataMember] public string session { get; set; }
        [DataMember] public int mapId { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public string cell { get; set; }
        [DataMember] public string parentKey { get; set; }
        [DataMember] public int parentId { get; set; }
        [DataMember] public string parentType { get; set; }
        [DataMember] public ProjectionFixtureThing parentThing { get; set; }
        [DataMember] public int maximumSlots { get; set; }
        [DataMember] public List<ProjectionFixtureThing> items { get; set; }
        [DataMember] public ProjectionFixtureThing silver { get; set; }
        [DataMember] public ProjectionFixtureThing cloth { get; set; }
        [DataMember] public bool registered { get; set; }
        [DataMember] public bool zoneRegistered { get; set; }
        [DataMember] public List<string> zoneCells { get; set; }
        [DataMember] public string priority { get; set; }
        [DataMember] public bool settingsOwnerIsParent { get; set; }
        [DataMember] public bool settingsIdentityUnchanged { get; set; }
        [DataMember] public bool fixedIdentityUnchanged { get; set; }
        [DataMember] public ProjectionFixtureFilter filter { get; set; }
        [DataMember] public ProjectionFixtureFilter fixedFilter { get; set; }
    }
    [DataContract] internal sealed class ProjectionFixtureAssembly
    { [DataMember] public string name { get; set; } [DataMember] public string path { get; set; } [DataMember] public string mvid { get; set; } [DataMember] public string sha256 { get; set; } }
    [DataContract] internal sealed class ProjectionFixtureBinding
    { [DataMember] public List<ProjectionFixtureAssembly> assemblies { get; set; } [DataMember] public List<string> bindings { get; set; } }
    [DataContract] internal sealed class ProjectionFixtureAssertion
    { [DataMember] public int sequence { get; set; } [DataMember] public string id { get; set; } [DataMember] public string kind { get; set; } [DataMember] public bool passed { get; set; } [DataMember] public string detail { get; set; } }
    [DataContract] internal sealed class ProjectionFixtureStatus
    { [DataMember] public bool usable { get; set; } [DataMember] public string observation { get; set; } [DataMember] public string capability { get; set; } [DataMember] public string reason { get; set; } }
    [DataContract] internal sealed class ProjectionFixtureOperation
    { [DataMember] public string scene { get; set; } [DataMember] public string stage { get; set; } [DataMember] public ProjectionFixtureStatus status { get; set; } [DataMember] public string detail { get; set; } }
    [DataContract] internal sealed class ProjectionFixtureWork
    { [DataMember] public string kind { get; set; } [DataMember] public long value { get; set; } }
    [DataContract] internal sealed class ProjectionFixtureCell
    {
        [DataMember] public string fixtureScene { get; set; }
        [DataMember] public string fixtureStage { get; set; }
        [DataMember] public long observationId { get; set; }
        [DataMember] public string query { get; set; }
        [DataMember] public string session { get; set; }
        [DataMember] public int mapId { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public long generation { get; set; }
        [DataMember] public string cell { get; set; }
        [DataMember] public string parentKey { get; set; }
        [DataMember] public string groupKey { get; set; }
        [DataMember] public string provider { get; set; }
        [DataMember] public string vacantKey { get; set; }
        [DataMember] public int? maximumSlots { get; set; }
        [DataMember] public int? itemCount { get; set; }
        [DataMember] public long? vacantSlots { get; set; }
        [DataMember] public int? gridEntries { get; set; }
        [DataMember] public ProjectionFixtureStatus status { get; set; }
        [DataMember] public List<ProjectionFixtureStack> stacks { get; set; }
        [DataMember] public List<ProjectionFixtureWork> work { get; set; }
    }
    [DataContract] internal sealed class ProjectionFixtureStack
    {
        [DataMember] public string key { get; set; } [DataMember] public int thingId { get; set; } [DataMember] public string def { get; set; }
        [DataMember] public int count { get; set; } [DataMember] public int stackLimit { get; set; } [DataMember] public long deficit { get; set; }
        [DataMember] public bool? providerTargetValid { get; set; }
    }
    [DataContract] internal sealed class ProjectionFixtureEligibility
    {
        [DataMember] public string fixtureScene { get; set; }
        [DataMember] public string fixtureStage { get; set; }
        [DataMember] public long observationId { get; set; } [DataMember] public string parcelId { get; set; }
        [DataMember] public ProjectionFixtureStatus status { get; set; } [DataMember] public string state { get; set; }
        [DataMember] public bool vacantEligible { get; set; } [DataMember] public int? unitsPerNewStack { get; set; }
        [DataMember] public List<ProjectionFixturePredicate> predicates { get; set; } [DataMember] public List<ProjectionFixtureEdge> topUps { get; set; }
        [DataMember] public List<ProjectionFixtureWork> work { get; set; }
    }
    [DataContract] internal sealed class ProjectionFixturePredicate
    { [DataMember] public string name { get; set; } [DataMember] public string state { get; set; } [DataMember] public string reason { get; set; } [DataMember] public int? targetId { get; set; } }
    [DataContract] internal sealed class ProjectionFixtureEdge
    { [DataMember] public string key { get; set; } [DataMember] public int targetId { get; set; } [DataMember] public long units { get; set; } }
    [DataContract] internal sealed class ProjectionFixtureCellEvent
    { [DataMember] public string scene { get; set; } [DataMember] public string stage { get; set; } [DataMember] public ProjectionFixtureCell value { get; set; } }
    [DataContract] internal sealed class ProjectionFixtureEligibilityEvent
    { [DataMember] public string scene { get; set; } [DataMember] public string stage { get; set; } [DataMember] public ProjectionFixtureEligibility value { get; set; } }
}
