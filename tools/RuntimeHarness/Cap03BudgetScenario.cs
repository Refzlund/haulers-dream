using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    // CAP03 component B only. Real spawned objects, normal ASF registration,
    // public projector operations; no jobs, fake providers or Harmony observers.
    internal sealed class Cap03BudgetScenario
    {
        private static readonly FieldInfo Specials = typeof(ThingFilter).GetField("disallowedSpecialFilters", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Map map;
        private readonly Cap03BudgetResult result;
        private readonly List<object> identities = new List<object>();
        private readonly List<Thing> residents = new List<Thing>();
        private Cap03BudgetBridge api;
        private Pawn actor;
        private Building_Storage crate;
        private Thing novel, cargo;
        private IntVec3 cell;
        private SlotGroup group;
        private bool setupValid, behaviorPassed = true;
        private Cap03BudgetScenario(Map map, string expected)
        {
            this.map = map; result = new Cap03BudgetResult { expectedBehavior = expected,
                startedTick = Find.TickManager.TicksGame, mainThread = Thread.CurrentThread.ManagedThreadId, mapId = map?.uniqueID ?? -1 };
        }
        internal static Cap03BudgetResult Run(Map map, string expectedBehavior)
        {
            var test = new Cap03BudgetScenario(map, expectedBehavior);
            try
            {
                test.Setup();
                test.result.catalogStatus = test.api.CreateCatalog();
                test.Capture("catalog", null, test.result.catalogStatus);
                if (test.Check("catalog-created", Complete(test.result.catalogStatus), Json.Stringify(test.result.catalogStatus)))
                {
                    test.result.nativeIdentity = test.api.NativeIdentity; test.result.asfIdentity = test.api.AsfIdentity;
                    test.result.patchInventory = test.api.PatchInventory;
                    test.Check("native-semantics-reviewed", test.api.NativeReviewed, test.result.nativeIdentity);
                    test.Capture("patch-inventory", null, test.result.patchInventory);
                    test.result.preparationAllowance = checked(8L * (DefDatabase<ThingDef>.AllDefsListForReading.Count + 1)
                        * (DefDatabase<ThingCategoryDef>.AllDefsListForReading.Count + 1)
                        * (DefDatabase<SpecialThingFilterDef>.AllDefsListForReading.Count + 1) + 1000000L);
                    var prepared = test.api.Prepare(test.crate, test.result.preparationAllowance, true);
                    test.result.preparationStatus = test.api.ResultStatus(prepared);
                    test.result.preparationCharged = test.api.Get<long>(prepared, "ChargedWork");
                    test.result.preparationReady = test.api.Get<bool>(prepared, "Ready");
                    test.result.indexedZoneCells = test.api.Get<int>(prepared, "IndexedZoneCells");
                    test.result.footprintWarmup = test.api.Get<bool>(prepared, "FootprintWarmupRequested");
                    test.Capture("preparation", null, new Cap03BudgetPreparation { status = test.result.preparationStatus,
                        allowance = test.result.preparationAllowance, charged = test.result.preparationCharged,
                        ready = test.result.preparationReady, indexed = test.result.indexedZoneCells, warmup = test.result.footprintWarmup });
                    if (test.Check("member-prepared", Complete(test.result.preparationStatus) && test.result.preparationReady
                        && test.result.indexedZoneCells == 0 && test.result.footprintWarmup && test.result.preparationCharged > 0
                        && test.result.preparationCharged <= test.result.preparationAllowance, Json.Stringify(test.result.preparationStatus)))
                    {
                        test.Trial("novel-low", test.novel, 36, true);
                        test.Trial("novel-sufficient", test.novel, 8192, false);
                        test.Trial("steel-sufficient", test.cargo, 8192, false);
                    }
                }
                test.result.finalPhysical = test.Physical(); test.Capture("physical-final", null, test.result.finalPhysical);
                test.Require("physical-final-unchanged", Same(test.result.initialPhysical, test.result.finalPhysical), "Exact physical/settings/registry/actor snapshots are unchanged.");
                test.Require("same-tick", Find.TickManager.TicksGame == test.result.startedTick, "Synchronous fixture: no simulation tick advancement.");
                test.Check("all-scopes-closed", !test.api.HasOpenScope, "Actual projector entry scope is closed.");
                test.Check("three-fresh-trials-complete", test.result.trials.Count == 3 && test.result.trials.All(x => x.completed && x.disposed)
                    && test.result.trials.Select(x => x.scopeIdentity).Distinct().Count() == 3, "Each trial used a distinct actual scope with the same unchanged scene.");
                test.result.fixtureValid = test.setupValid;
                test.result.requestedBehaviorSatisfied = test.setupValid && test.behaviorPassed;
                test.result.expectationMatched = test.result.requestedBehaviorSatisfied && expectedBehavior == "satisfied";
                test.result.status = test.result.expectationMatched ? "passed" : "failed";
            }
            catch (Exception error)
            {
                var cause = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                test.result.error = cause.ToString(); test.Record("fixture-exception", "fixture", false, cause.ToString());
                test.result.fixtureValid = false; test.result.requestedBehaviorSatisfied = false; test.result.expectationMatched = false;
                test.result.status = "inconclusive";
            }
            finally
            {
                test.result.finishedTick = Find.TickManager.TicksGame;
                HarnessSession.Event("cap03-b-result", Json.Stringify(test.result));
            }
            return test.result;
        }
        private void Setup()
        {
            Require("expectation", result.expectedBehavior == "satisfied", "Only explicit satisfied for the corrected component; no baseline-gap alias.");
            Require("map", map != null && map.IsPlayerHome && Current.Game != null && UnityData.IsInMainThread, "Real isolated initialized player-home map.");
            Require("native-filter-field", Specials?.FieldType == typeof(List<SpecialThingFilterDef>), "Exact read-only filter special-list binding.");
            result.gameMapIds = Current.Game.Maps.Select(x => x.uniqueID).ToList();
            api = new Cap03BudgetBridge(); api.InitializeBudgetBindings(); result.sessionId = api.SessionId.ToString("N");
            result.assemblies = api.Assemblies; result.bindings = api.Bindings;
            result.bindings.Add(typeof(ThingFilter).FullName + ".disallowedSpecialFilters; field-token=" + Specials.MetadataToken + "; mvid=" + Specials.Module.ModuleVersionId);
            Require("reviewed-asf-assembly", result.assemblies.Single(x => x.name.StartsWith("AdaptiveStorageFramework,", StringComparison.Ordinal)).sha256
                == "28DAA37ADE4144CAD2B7669EDFDA2201F9C0E8E99D4639853131E066026935B9", "Actual loaded ASF file is the independently inspected installed1.2.4 build.");
            Capture("bindings", null, new ProjectionFixtureBinding { assemblies = result.assemblies, bindings = result.bindings });
            var mods = LoadedModManager.RunningModsListForReading;
            var neat = mods.Single(x => string.Equals(x.PackageId, "sbz.NeatStorage", StringComparison.OrdinalIgnoreCase));
            var asf = mods.Single(x => x.assemblies.loadedAssemblies.Any(a => a.GetName().Name == "AdaptiveStorageFramework"));
            var core = mods.Single(x => string.Equals(x.PackageId, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase));
            Input(neat, "1.6/Defs/ThingDefs_Buildings/Buildings_CrateAndPallet.xml", "9B9F757E13A16327250B5F62BA15DC457F40F4B45DD7500CA9DEA0F56BC4D582");
            Input(asf, "Defs/ThingDefBase.xml", "DECE4A55D724F4D1EE23B6F21C531BB4F5EF627EC93E4A4D02C7565FE73A242B");
            Input(core, "Defs/Books/BookDefs.xml", "86F29BB5FDB9991E52865D78686BD7F98CB5FB1744BAB1B8719720CF6D20829D");
            var generated = map.listerThings.AllThings.Where(x => x is Pawn || x is Skyfaller || x is ActiveTransporter).ToList();
            foreach (var thing in generated) if (thing.Spawned) thing.DeSpawn();
            var rect = new CellRect(map.Center.x - 10, map.Center.z - 8, 21, 17);
            Require("fixture-bounds", rect.Cells.All(x => x.InBounds(map)), rect.ToString());
            Require("no-zones-overwritten", rect.Cells.All(x => map.zoneManager.ZoneAt(x) == null), "Only disposable generated-map ground.");
            GenDebug.ClearArea(rect, map);
            foreach (var address in rect.Cells) { map.terrainGrid.SetTerrain(address, TerrainDefOf.Concrete); map.roofGrid.SetRoof(address, null); map.areaManager.Home[address] = true; map.fogGrid.Unfog(address); }
            Rand.PushState(12003);
            try { actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); }
            finally { Rand.PopState(); }
            Require("normal-actor", actor != null && actor.RaceProps.Humanlike && !actor.Downed && actor.inventory != null && actor.carryTracker != null, "Actual normal human actor.");
            actor.inventory.innerContainer.ClearAndDestroyContents(); actor.workSettings.EnableAndInitializeIfNotAlreadyInitialized(); actor.workSettings.DisableAll();
            GenSpawn.Spawn(actor, map.Center + new IntVec3(0, 0, -5), map);
            Require("only-fixture-actor", map.mapPawns.AllPawnsSpawned.Count == 1 && ActorStill(), "No running jobs or generated actors.");
            cell = map.Center; var def = DefDatabase<ThingDef>.GetNamed("sbz_SmallCrate");
            Require("actual-neat-def", def.thingClass == api.AsfParentType && def.size.x == 1 && def.size.z == 1
                && string.Equals(def.modContentPack?.PackageId, neat.PackageId, StringComparison.OrdinalIgnoreCase), "Actual patched Neat Storage one-cell definition/class.");
            crate = (Building_Storage)GenSpawn.Spawn(ThingMaker.MakeThing(def, ThingDefOf.WoodLog), cell, map, Rot4.North);
            crate.SetFaction(Faction.OfPlayer); group = crate.GetSlotGroup();
            var defs = new[] { "Steel", "WoodLog", "Plasteel", "Uranium", "Jade", "Gold" }.Select(x => DefDatabase<ThingDef>.GetNamed(x)).ToArray();
            var settings = crate.GetStoreSettings(); settings.filter.SetDisallowAll();
            foreach (var accepted in defs.Concat(new[] { DefDatabase<ThingDef>.GetNamed("Novel") })) settings.filter.SetAllow(accepted, true);
            settings.Priority = StoragePriority.Critical;
            foreach (var material in defs)
            {
                Require("stack-limit-" + material.defName, material.category == ThingCategory.Item && material.stackLimit > 7, "Actual native stack limit=" + material.stackLimit);
                residents.Add(Spawn(material, material.stackLimit - (material == ThingDefOf.Steel ? 7 : 0), cell));
            }
            cargo = Spawn(ThingDefOf.Steel, 7, cell + new IntVec3(2, 0, 3));
            novel = Spawn(DefDatabase<ThingDef>.GetNamed("Novel"), 1, cell + new IntVec3(-2, 0, 3));
            Require("actual-book", novel.GetType() == typeof(Book) && novel.def.stackLimit == 1, "Actual native Verse.Book Novel; unchanged stackLimit1.");
            ((Book)novel).GenerateBook();
            Require("book-generated", !string.IsNullOrEmpty(((Book)novel).Title), "Normal native GenerateBook creates readable metadata; no private book fields seeded.");
            result.initialPhysical = Physical(); Capture("physical-setup", null, result.initialPhysical);
            VerifyScene(result.initialPhysical);
            var steel = residents.Single(x => x.def == ThingDefOf.Steel);
            Require("directional-commodity-control", steel.CanStackWith(cargo) && !steel.CanStackWith(novel) && !novel.CanStackWith(cargo),
                "Explicit setup-only actual predicates; these are not low-budget native-call instrumentation.");
            setupValid = true;
        }
        private Thing Spawn(ThingDef def, int count, IntVec3 at)
        {
            var made = ThingMaker.MakeThing(def); made.stackCount = count; var spawned = GenSpawn.Spawn(made, at, map);
            Require("spawn-" + made.ThingID, ReferenceEquals(made, spawned) && spawned.stackCount == count && spawned.Position == at
                && spawned.Spawned && ReferenceEquals(spawned.ParentHolder, map), "Normal spawn preserves actual ID/count/map/custody; ASF registration is not manually seeded.");
            return spawned;
        }
        private void Input(ModContentPack mod, string relative, string expectedHash)
        {
            var path = Path.Combine(mod.RootDir, relative.Replace('/', Path.DirectorySeparatorChar));
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
            {
                var row = new Cap03BudgetFile { packageId = mod.PackageId, root = mod.RootDir, relativePath = relative,
                    sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") };
                result.inputs.Add(row); Capture("input", null, row); Require("reviewed-input-" + mod.PackageId, row.sha256 == expectedHash, Json.Stringify(row));
            }
        }
        private string Identity(object value)
        {
            if (value == null) return null;
            for (int i = 0; i < identities.Count; i++) if (ReferenceEquals(identities[i], value)) return "ref:" + (i + 1);
            if (identities.Count >= 128) throw new InvalidOperationException("Fixture identity census bound exceeded.");
            identities.Add(value); return "ref:" + identities.Count;
        }
        private bool ActorStill() => actor != null && actor.Spawned && actor.CurJob == null && actor.inventory.innerContainer.Count == 0 && actor.carryTracker.CarriedThing == null;
        private ProjectionFixtureThing ThingState(Thing thing) => new ProjectionFixtureThing { thingId = thing.thingIDNumber, def = thing.def.defName,
            count = thing.stackCount, stackLimit = thing.def.stackLimit, cell = thing.Position.ToString(), spawned = thing.Spawned, mapId = thing.Map?.uniqueID,
            faction = thing.Faction?.GetUniqueLoadID(), holderType = thing.ParentHolder?.GetType().FullName, heldByFixtureMap = ReferenceEquals(thing.ParentHolder, map),
            destroyed = thing.Destroyed, stuff = thing.Stuff?.defName, hitPoints = thing.HitPoints };
        private ProjectionFixtureFilter Filter(ThingFilter filter) => new ProjectionFixtureFilter { type = filter.GetType().FullName, onlySpecial = filter.OnlySpecialFilters,
            allowedDefs = filter.AllowedThingDefs.Select(x => x.defName).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            disallowedSpecials = ((List<SpecialThingFilterDef>)Specials.GetValue(filter)).Select(x => x?.defName).ToList(),
            hitPointsMin = filter.AllowedHitPointsPercents.min, hitPointsMax = filter.AllowedHitPointsPercents.max,
            mentalBreakMin = filter.AllowedMentalBreakChance.min, mentalBreakMax = filter.AllowedMentalBreakChance.max, qualities = filter.AllowedQualityLevels.ToString() };
        private Cap03BudgetPhysical Physical()
        {
            var settings = crate.GetStoreSettings(); var declared = api.DeclaredFixed(crate); var inherited = ((IStorageGroupMember)crate).ParentStoreSettings;
            var maximum = crate.def.building.GetType().GetField("maxItemsInCell", BindingFlags.Instance | BindingFlags.Public);
            if (maximum?.FieldType != typeof(int)) throw new MissingFieldException("Native maxItemsInCell binding differs.");
            var grid = map.thingGrid.ThingsListAt(cell).OrderBy(x => x.thingIDNumber).ToList();
            return new Cap03BudgetPhysical { mapId = map.uniqueID, tick = Find.TickManager.TicksGame, cell = cell.ToString(), parentKey = "building:" + crate.thingIDNumber,
                parent = ThingState(crate), parentEverStorable = crate.def.EverStorable(false), actor = ThingState(actor), novel = ThingState(novel), cargo = ThingState(cargo),
                allGrid = grid.Select(ThingState).ToList(), gridCount = grid.Count, items = grid.Where(x => x.def.category == ThingCategory.Item).Select(ThingState).ToList(),
                registered = ReferenceEquals(map.haulDestinationManager.SlotGroupAt(cell), group), groupIdentity = Identity(group), unlinked = crate.storageGroup == null,
                registryIdentity = Identity(api.RegistryIdentity(crate)), settingsOwnerIsParent = ReferenceEquals(settings.owner, crate), priority = settings.Priority.ToString(),
                settingsIdentity = Identity(settings), filterIdentity = Identity(settings.filter), declaredFixedIdentity = Identity(declared), declaredFilterIdentity = Identity(declared.filter),
                interfaceFixedIdentity = Identity(inherited), interfaceFilterIdentity = Identity(inherited.filter), effectiveFilter = Filter(settings.filter),
                declaredFixedFilter = Filter(declared.filter), interfaceFixedFilter = Filter(inherited.filter),
                nativeMaximum = cell.GetMaxItemsAllowedInCell(map), defMaximum = (int)maximum.GetValue(crate.def.building),
                storageGroupTag = ((IStorageGroupMember)crate).StorageGroupTag, actualCrateClass = crate.GetType().FullName,
                crateDefSource = crate.def.modContentPack?.PackageId, crateWidth = crate.def.size.x, crateDepth = crate.def.size.z,
                bookClass = novel.GetType().FullName, bookTitle = ((Book)novel).Title, novelShortHash = novel.def.shortHash, cargoShortHash = cargo.def.shortHash,
                novelEffectiveAccepted = settings.AllowedToAccept(novel), novelDeclaredAccepted = api.FixedAllows(crate, novel), novelInterfaceAccepted = inherited.AllowedToAccept(novel),
                cargoEffectiveAccepted = settings.AllowedToAccept(cargo), cargoDeclaredAccepted = api.FixedAllows(crate, cargo), cargoInterfaceAccepted = inherited.AllowedToAccept(cargo),
                actorStill = ActorStill(), parentCompTypes = crate.AllComps.Select(x => x.GetType().FullName).ToList(),
                novelCompTypes = ((ThingWithComps)novel).AllComps.Select(x => x.GetType().FullName).ToList(),
                cargoCompTypes = (cargo as ThingWithComps)?.AllComps.Select(x => x.GetType().FullName).ToList() ?? new List<string>(),
                registry = api.Registry(crate, cell, novel, cargo) };
        }
        private void VerifyScene(Cap03BudgetPhysical p)
        {
            Require("six-real-asf-slots", p.nativeMaximum == 6 && p.defMaximum == 6 && p.crateWidth == 1 && p.crateDepth == 1
                && p.actualCrateClass == "AdaptiveStorage.ThingClass" && p.storageGroupTag == "Shelf" && p.unlinked && p.registered
                && p.settingsOwnerIsParent && p.priority == "Critical", "Actual loaded class/def/native capacity/owner/group registration.");
            Require("complete-physical-census", p.items.Count == 6 && p.allGrid.Count == 7 && p.gridCount == 7
                && p.items.Select(x => x.thingId).OrderBy(x => x).SequenceEqual(residents.Select(x => x.thingIDNumber).OrderBy(x => x))
                && p.allGrid.All(x => x.spawned && !x.destroyed && x.heldByFixtureMap && x.holderType == "Verse.Map" && x.mapId == map.uniqueID && x.cell == cell.ToString())
                && p.allGrid.Select(x => x.thingId).Distinct().Count() == 7, "Every item plus actual crate occupies the real map cell, with distinct physical IDs.");
            foreach (var item in residents)
                Require("resident-quantity-" + item.ThingID, item.stackCount == item.def.stackLimit - (item.def == ThingDefOf.Steel ? 7 : 0), "Exactly one seven-unit commodity deficit; fillers full.");
            var outside = new[] { p.novel, p.cargo };
            Require("real-outside-parcels", outside.All(x => x.spawned && !x.destroyed && x.heldByFixtureMap && x.holderType == "Verse.Map"
                && x.mapId == map.uniqueID && x.cell != p.cell) && p.novel.def == "Novel" && p.novel.count == 1 && p.novel.stackLimit == 1
                && p.cargo.def == "Steel" && p.cargo.count == 7 && p.novel.thingId != p.cargo.thingId
                && !p.allGrid.Any(x => x.thingId == p.novel.thingId || x.thingId == p.cargo.thingId), "No requested parcel is inside the occupied rectangle or an existing target.");
            Require("actual-filter-acceptance", p.novelEffectiveAccepted && p.novelDeclaredAccepted && p.novelInterfaceAccepted
                && p.cargoEffectiveAccepted && p.cargoDeclaredAccepted && p.cargoInterfaceAccepted
                && p.declaredFixedIdentity == p.interfaceFixedIdentity, "Actual unlinked Neat fixed filter accepts Novel and Steel; distinct stuff-lock dispatch is separate component A.");
            var r = p.registry;
            Require("registry-full-six", r.count == 6 && r.members.Count == 6 && r.cellCount == 6 && r.cellWiseCount == 6 && r.slotLimit == 6
                && !r.anyFree && !r.packed && !r.performanceFish && r.occupied.Count == 1 && r.occupied[0] == p.cell,
                "Normal ASF valid registry is full, unpacked, one occupied cell; PerformanceFish inactive.");
            Require("registry-identities-valid", r.members.Select(x => x.thingId).OrderBy(x => x).SequenceEqual(p.items.Select(x => x.thingId).OrderBy(x => x))
                && r.members.All(x => x.index == x.indexOf && x.contains && x.memberValid && x.targetValid && x.spawned && x.holderType == "Verse.Map"
                    && x.mapId == map.uniqueID && x.mapCell == p.cell && x.mapPosition == p.cell && x.storingParentId == crate.thingIDNumber
                    && p.items.Any(t => t.thingId == x.thingId && t.count == x.count && t.stackLimit == x.stackLimit && t.def == x.def))
                && r.members.Select(x => x.index).SequenceEqual(Enumerable.Range(0, 6)), "Actual collection index/Thing/grid/map-position/storing-parent/validity all reconcile.");
            Require("accepted-def-set-real", r.acceptedShortHashes.Count == 1 && r.acceptedShortHashes[0] == cargo.def.shortHash
                && r.acceptedDefs.SequenceEqual(new[] { "Steel" }) && r.acceptsCargoDef && !r.acceptsNovelDef,
                "Read-only real accepted-definition set contains only the partial Steel, without seeding private provider state.");
            Require("actual-custom-classification", r.novelOverridesStack && !r.cargoOverridesStack && p.bookClass == "Verse.Book",
                "Installed ASF OverridesCanStackWith classifies actual Novel custom and ordinary Steel noncustom.");
            Require("actor-still", p.actorStill && p.actor.cell != p.cell && p.actor.thingId != p.parent.thingId
                && p.actor.spawned && p.actor.heldByFixtureMap, "Only stationary ordinary actor, no jobs/carry/inventory changes.");
        }
        private void Trial(string id, Thing subject, long providerLimit, bool low)
        {
            var trial = new Cap03BudgetTrial { id = id, query = "CAP03-B/" + id, subjectId = subject.thingIDNumber,
                parcelId = id + "/" + subject.ThingID, before = Physical() };
            result.trials.Add(trial); Capture("trial-before", id, trial.before);
            Require(id + "-equivalent-scene", Same(trial.before, result.initialPhysical), "Fresh scope starts from unchanged physical/settings/registry/actor state.");
            object scope = null;
            try
            {
                var defaults = api.MakeLimits(providerLimit); var generous = api.MakeLimits(8192);
                trial.defaultAllowance = api.LimitsWork(defaults); trial.pageAllowance = api.LimitsWork(generous);
                Capture("default-allowance", id, trial.defaultAllowance); Capture("page-allowance", id, trial.pageAllowance);
                var opened = api.OpenBudget(map, group, trial.query, defaults);
                trial.openStatus = api.ResultStatus(opened); trial.openWork = api.OpenWork(opened);
                scope = api.Scope(opened); trial.scopeIdentity = Identity(scope);
                Capture("open", id, new Cap03BudgetOpen { status = trial.openStatus, scopeIdentity = trial.scopeIdentity, work = trial.openWork, query = trial.query });
                if (!Check(id + "-open", Complete(trial.openStatus) && scope != null, Json.Stringify(trial.openStatus))) return;
                var initialUsed = Used(trial, scope, "after-open");
                Check(id + "-open-used", Same(initialUsed, trial.openWork), "Cumulative actual scope.Used starts with exported Open.Work.");
                var openExpected = ZeroWork(); openExpected["GuardChecks"] = result.gameMapIds.Count;
                CheckCharges(id, "open", trial.openWork, openExpected);
                var rawPage = api.GroupPage(scope, generous); trial.page = api.PageRow(rawPage);
                foreach (var row in trial.page.cells) { row.fixtureScene = "CAP03-B"; row.fixtureStage = id + "/group-page"; }
                Capture("page", id, trial.page);
                var pageUsed = Used(trial, scope, "after-page");
                Check(id + "-page-used-delta", Adds(initialUsed, trial.page.operationWork, pageUsed), "Only one group-page operation: actual cumulative delta equals Page.Work.");
                var cells = api.RawPageCells(rawPage);
                if (!Check(id + "-complete-real-page", Complete(trial.page.status) && trial.page.requestedCellsComplete && trial.page.wholeGroupComplete
                    && trial.page.unresolved.Count == 0 && trial.page.unresolvedTotal == 0 && trial.page.unresolvedSampleCount == 0
                    && trial.page.unresolvedSampleStart == 0 && trial.page.nextCell == null && trial.page.nextMember == null
                    && trial.page.hasCursor && cells.Count == 1 && trial.page.cells.Count == 1, Json.Stringify(trial.page.status))) return;
                var rawCell = cells[0]; var cellRow = trial.page.cells[0];
                CheckCell(id, cellRow);
                CheckPageCharges(trial);
                Check(id + "-page-provider-fifty", Work(trial.page.operationWork, "ProviderVisits") == 50
                    && Work(cellRow.work, "ProviderVisits") == 50, "Six residents: two declared ASF state reads, 2*(1+4*6)=50 under explicit generous page allowance.");
                Check(id + "-cell-prefix-accounting", Same(cellRow.work, trial.page.operationWork),
                    "Single-cell page's embedded Cell.Work is the page accounting prefix, not a separate operation to add a second time.");
                var parcel = api.Parcel(trial.parcelId, subject, actor);
                var rawEligibility = api.Eligibility(scope, parcel, rawCell);
                trial.eligibility = api.EligibilityRow(rawEligibility); trial.eligibility.fixtureScene = "CAP03-B"; trial.eligibility.fixtureStage = id;
                Capture("eligibility", id, trial.eligibility);
                var eligibilityUsed = Used(trial, scope, "after-eligibility");
                Check(id + "-eligibility-used-delta", Adds(pageUsed, trial.eligibility.work, eligibilityUsed),
                    "Actual cumulative scope.Used includes exactly the returned eligibility operation charge, including typed refusal.");
                Check(id + "-retained-observation-identity", trial.eligibility.observationId == cellRow.observationId
                    && trial.eligibility.parcelId == trial.parcelId, "Eligibility uses the exact real CellProjection returned by this scope's page.");
                CheckOutcome(trial, low, subject == novel);
                trial.completed = true;
            }
            catch (Exception error)
            {
                trial.error = (error is TargetInvocationException && error.InnerException != null ? error.InnerException : error).ToString();
                Capture("trial-exception", id, trial.error); throw;
            }
            finally
            {
                if (scope != null)
                {
                    // Keep cleanup guaranteed even if the before-disposal read fails.
                    List<ProjectionFixtureWork> before = null;
                    try { before = Used(trial, scope, "before-dispose"); }
                    finally { ((IDisposable)scope).Dispose(); trial.disposed = true; }
                    var after = Used(trial, scope, "after-dispose");
                    Check(id + "-dispose-accounting", before != null && Same(before, after) && !api.HasOpenScope,
                        "Actual scope disposal closes entry without changing accumulated work.");
                }
                trial.after = Physical(); Capture("trial-after", id, trial.after);
                Require(id + "-physical-unchanged", Same(trial.before, trial.after), "Scope lifecycle did not mutate items, settings, registry, actor or material quantities.");
            }
            Capture("trial-result", id, trial);
        }
        private void CheckCell(string id, ProjectionFixtureCell row)
        {
            string prefix = result.sessionId + "/" + map.uniqueID + "/building:" + crate.thingIDNumber + "/" + cell.x + "," + cell.z;
            Check(id + "-cell-identity", Complete(row.status) && row.observationId > 0 && row.session == result.sessionId && row.mapId == map.uniqueID
                && row.tick == result.startedTick && row.generation == 1 && row.cell == cell.ToString() && row.parentKey == "building:" + crate.thingIDNumber
                && row.query == "CAP03-B/" + id && row.groupKey == "concrete" && row.provider == api.AsfIdentity && row.vacantKey == prefix + "/vacant",
                "Actual session/map/scope query/ASF provider/cell/parent identity.");
            Check(id + "-cell-physical-resources", row.maximumSlots == 6 && row.itemCount == 6 && row.vacantSlots == 0 && row.gridEntries == 7
                && row.stacks.Count == 6 && row.stacks.Select(x => x.thingId).Distinct().Count() == 6
                && row.stacks.All(x => residents.Any(t => t.thingIDNumber == x.thingId && t.def.defName == x.def && t.stackCount == x.count
                    && t.def.stackLimit == x.stackLimit && x.deficit == t.def.stackLimit - t.stackCount && x.providerTargetValid == true
                    && x.key == prefix + "/stack:" + t.thingIDNumber)), "No generated/fake cell: all six actual resource IDs, limits, deficits and ASF validity match the independent scene.");
        }
        private void CheckOutcome(Cap03BudgetTrial trial, bool low, bool book)
        {
            string id = trial.id; var row = trial.eligibility;
            long provider = Work(row.work, "ProviderVisits");
            CheckEligibilityCharges(trial, low, book);
            Check(id + "-exact-predicate-catalog", row.predicates.Count == (low || book ? 9 : 13), "All initial predicate slots plus exactly the four ordinary target predicates; no hidden extra/missing verdicts.");
            Check(id + "-filter-path-reached", new[] { "destination-enabled", "destination-faction", "selected-priority", "effective-thing-filter", "concrete-fixed-filter", "asf-declared-fixed-filter" }
                .All(x => Predicate(row, x, "Eligible", "None")), "Each preceding actual filter/owner/provider binding reached and accepted the real parcel.");
            Check(id + "-no-phantom-vacancies", !row.vacantEligible && row.unitsPerNewStack == null, "Full six-slot member has no new-stack vacancy for either subject.");
            if (low)
            {
                Check(id + "-typed-scan-refusal", !row.status.usable && row.status.observation == "Deferred" && row.status.capability == "Supported"
                    && row.status.reason == "ProviderScanRequired" && row.state == "NotEvaluated" && row.topUps.Count == 0,
                    "Insufficient default allowance stays a typed budget refusal, not a provider rejection or success.");
                Check(id + "-atomic-provider-precharge", provider == 25 && Work(row.work, "Compatibility") == 12
                    && Work(row.work, "NativeCalls") == 0 && Work(trial.defaultAllowance, "ProviderVisits") == 36
                    && 25 + 2 * result.initialPhysical.registry.count == 37, "Declared first validation25; atomic next12 does not fit36 and is wholly uncharged. Source interpretation, not independent native-call counts.");
                Check(id + "-later-predicates-not-evaluated", new[] { "asf-actual-member-capacity", "native-IsGoodStoreCell", "hd-explicit-context-filter" }
                    .All(x => Predicate(row, x, "NotEvaluated", "None")), "No returned later predicate verdict before the refused reservation.");
            }
            else if (book)
            {
                Check(id + "-actual-provider-refusal", Complete(row.status) && row.state == "Refused" && row.topUps.Count == 0
                    && Predicate(row, "asf-actual-member-capacity", "Refused", "ProviderRefused"), "Actual Novel custom branch finds no matching partial commodity.");
                Check(id + "-provider-sixty-two", provider == 62 && Work(row.work, "NativeCalls") == 0,
                    "Declared 25 validation +12 two-pass allowance +25 final validation=62; six indexed residents, not six predicate calls.");
                Check(id + "-native-not-reached", Predicate(row, "native-IsGoodStoreCell", "NotEvaluated", "None")
                    && Predicate(row, "hd-explicit-context-filter", "NotEvaluated", "None"), "Provider refusal precedes native/context calls.");
            }
            else
            {
                var steel = residents.Single(x => x.def == ThingDefOf.Steel);
                Check(id + "-ordinary-success", Complete(row.status) && row.state == "Eligible" && row.topUps.Count == 1
                    && row.topUps[0].targetId == steel.thingIDNumber && row.topUps[0].units == 7
                    && trial.page.cells[0].stacks.Any(x => x.thingId == steel.thingIDNumber && x.key == row.topUps[0].key)
                    && Predicate(row, "asf-actual-member-capacity", "Eligible", "None") && Predicate(row, "native-IsGoodStoreCell", "Eligible", "None")
                    && Predicate(row, "hd-explicit-context-filter", "Eligible", "None"), "Actual ordinary compatible Steel retains its real seven-unit top-up.");
                Check(id + "-ordinary-provider-seventy-seven", provider == 77 && Work(row.work, "NativeCalls") == 1
                    && Work(row.work, "NativeGridVisits") == 28, "Declared25+12+ASF native-call allowance(2*7+1)+25=77; native4N reserve28.");
                foreach (var name in new[] { "different-target", "target-ever-storable", "directional-CanStackWith", "asf-individual-target" })
                    Check(id + "-" + name, row.predicates.Count(x => x.name == name && x.targetId == steel.thingIDNumber && x.state == "Eligible" && x.reason == "None") == 1,
                        "Actual target-specific positive edge uses only the partial Steel identity.");
            }
        }
        private static readonly string[] WorkKinds = { "Members", "Coordinates", "GridEntries", "NativeGridVisits", "NativeCalls", "Filters", "Compatibility", "ProviderVisits", "CellLimitCalls", "Reservations", "Reachability", "GuardChecks", "Preparation", "OutputRecords" };
        private static Dictionary<string, long> ZeroWork() => WorkKinds.ToDictionary(x => x, x => 0L, StringComparer.Ordinal);
        private void CheckCharges(string id, string operation, List<ProjectionFixtureWork> actual, Dictionary<string, long> expected)
        {
            var sourceExpected = WorkKinds.Select(x => new ProjectionFixtureWork { kind = x, value = expected[x] }).ToList();
            Capture("source-derived-" + operation + "-charges", id, sourceExpected);
            Check(id + "-" + operation + "-all-charges", actual.Count == 14 && actual.Select(x => x.kind).Distinct().Count() == 14
                && actual.All(x => expected.TryGetValue(x.kind, out var value) && x.value == value),
                "Source-derived conservative per-operation reservations from actual physical/filter/comp census: " + Json.Stringify(sourceExpected));
        }
        private void CheckPageCharges(Cap03BudgetTrial trial)
        {
            var p = result.initialPhysical; long i = p.items.Count, n = p.gridCount, k = p.parentCompTypes.Count;
            var expected = ZeroWork(); expected["Members"] = 1; expected["Coordinates"] = 2; expected["GridEntries"] = 2 * n;
            expected["Compatibility"] = 2 * k + 4 * i; expected["ProviderVisits"] = 2 * (1 + 4 * i);
            expected["CellLimitCalls"] = 2; expected["GuardChecks"] = 6;
            expected["OutputRecords"] = 2 * Work(trial.pageAllowance, "Coordinates") + 130 + 3 * i + 2;
            CheckCharges(trial.id, "page", trial.page.operationWork, expected);
        }
        private void CheckEligibilityCharges(Cap03BudgetTrial trial, bool low, bool book)
        {
            // These expected rows are derived from initial physical data, not
            // from the production-reported Work values being checked.
            var p = result.initialPhysical; long i = p.items.Count, n = p.gridCount;
            var expected = ZeroWork(); long validation = 1 + 4 * i;
            long memberPreflight = p.registry.members.Sum(x => 1L + 2L * x.compTypes.Count);
            expected["GridEntries"] = (low ? 1 : 2) * n;
            expected["Filters"] = 4L + p.effectiveFilter.disallowedSpecials.Count + 3L * p.declaredFixedFilter.disallowedSpecials.Count;
            expected["Compatibility"] = low ? 2 * i : 4 * i + i + memberPreflight;
            expected["ProviderVisits"] = low ? validation : 2 * validation + 2 * i;
            expected["CellLimitCalls"] = low ? 1 : 2; expected["GuardChecks"] = low ? 2 : 3;
            expected["OutputRecords"] = 10 + 5 * i;
            if (!low && !book)
            {
                long nativePreflight = p.registry.members.Where(x => x.everStorable).Sum(x => 1L + 2L * x.compTypes.Count)
                    + (p.parentEverStorable ? 1L + 2L * p.parentCompTypes.Count : 0);
                var steel = p.registry.members.Single(x => x.def == "Steel");
                expected["Compatibility"] += 3 * n + nativePreflight + 3 + steel.compTypes.Count;
                expected["ProviderVisits"] += 2 * n + 1; expected["NativeGridVisits"] = 4 * n; expected["NativeCalls"] = 1;
                expected["CellLimitCalls"] += n + 1; expected["Reservations"] = 1; expected["Reachability"] = 1; expected["Filters"]++;
            }
            CheckCharges(trial.id, "eligibility", trial.eligibility.work, expected);
        }
        private List<ProjectionFixtureWork> Used(Cap03BudgetTrial trial, object scope, string stage)
        {
            var value = api.ScopeUsed(scope); trial.used.Add(new Cap03BudgetUsed { stage = stage, cumulative = value });
            Capture("scope-used", trial.id, trial.used.Last()); return value;
        }
        private static long Work(List<ProjectionFixtureWork> rows, string kind) => rows.Single(x => x.kind == kind).value;
        private static bool Adds(List<ProjectionFixtureWork> before, List<ProjectionFixtureWork> operation, List<ProjectionFixtureWork> after)
        {
            if (before.Count != 14 || operation.Count != 14 || after.Count != 14 || before.Select(x => x.kind).Distinct().Count() != 14) return false;
            return before.All(x => Work(after, x.kind) == checked(x.value + Work(operation, x.kind)));
        }
        private static bool Predicate(ProjectionFixtureEligibility row, string name, string state, string reason) => row.predicates.Count(x => x.name == name
            && x.state == state && x.reason == reason && x.targetId == null) == 1;
        private static bool Complete(ProjectionFixtureStatus status) => status.usable && status.observation == "Complete" && status.capability == "Supported" && status.reason == "None";
        private static bool Same<T>(T left, T right) => Json.Stringify(left) == Json.Stringify(right);
        private void Require(string id, bool okay, string detail) { Record(id, "fixture", okay, detail); if (!okay) throw new InvalidOperationException(id + ": " + detail); }
        private bool Check(string id, bool okay, string detail) { Record(id, "behavior", okay, detail); if (!okay) behaviorPassed = false; return okay; }
        private void Record(string id, string kind, bool okay, string detail)
        {
            var row = new ProjectionFixtureAssertion { sequence = result.assertions.Count + 1, id = id, kind = kind, passed = okay, detail = detail };
            result.assertions.Add(row); Capture("assertion", null, row);
        }
        private void Capture<T>(string kind, string trial, T value)
        {
            var row = new Cap03BudgetRecord { sequence = result.records.Count + 1, tick = Find.TickManager.TicksGame, kind = kind, trial = trial, data = Json.Stringify(value) };
            result.records.Add(row); HarnessSession.Event("cap03-b-" + kind, Json.Stringify(row));
        }
    }
    [System.Runtime.Serialization.DataContract] internal sealed class Cap03BudgetPreparation
    {
        [System.Runtime.Serialization.DataMember] public ProjectionFixtureStatus status;
        [System.Runtime.Serialization.DataMember] public long allowance, charged;
        [System.Runtime.Serialization.DataMember] public bool ready, warmup;
        [System.Runtime.Serialization.DataMember] public int indexed;
    }
    [System.Runtime.Serialization.DataContract] internal sealed class Cap03BudgetOpen
    {
        [System.Runtime.Serialization.DataMember] public ProjectionFixtureStatus status;
        [System.Runtime.Serialization.DataMember] public string scopeIdentity, query;
        [System.Runtime.Serialization.DataMember] public List<ProjectionFixtureWork> work;
    }
}