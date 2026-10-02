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
    // CAP03-A only. No observers, Harmony changes, jobs, native out-argument writes,
    // HD/ASF compile references or definition mutations. Root integrates separately.
    internal sealed partial class Cap03FilterScenario
    {
        private const string UnknownDef = "HDHarness_CAP03A_Unknown";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly Map map;
        private readonly Cap03FilterResult result;
        private readonly List<object> identities = new List<object>();
        private readonly List<Thing> owned = new List<Thing>();
        private readonly Cap03BudgetBridge api;
        private readonly FieldInfo specials, worker;
        private readonly PropertyInfo registryCount, packed;
        private Pawn actor;
        private ThingDef rawRice;
        private SpecialThingFilterDef custom, fresh;
        private bool failed;

        private Cap03FilterScenario(Map map, string expected)
        {
            this.map = map;
            result = new Cap03FilterResult { expectedBehavior = expected, startedTick = Find.TickManager.TicksGame,
                mapId = map?.uniqueID ?? -1, mainThread = Thread.CurrentThread.ManagedThreadId };
            api = new Cap03BudgetBridge(); api.InitializeBudgetBindings();
            result.session = api.SessionId.ToString("N"); result.assemblies = api.Assemblies; result.bindings = api.Bindings;
            specials = typeof(ThingFilter).GetField("disallowedSpecialFilters", Fields);
            worker = typeof(SpecialThingFilterDef).GetField("workerInt", Fields);
            if (specials?.FieldType != typeof(List<SpecialThingFilterDef>) || worker?.FieldType != typeof(SpecialThingFilterWorker))
                throw new MissingFieldException("Exact passive native special-filter field bindings differ.");
            foreach (var field in new[] { specials, worker }) Bind(field);
            var registryType = api.AsfParentType.Assembly.GetType("AdaptiveStorage.ThingCollection", true);
            registryCount = registryType.GetProperty("Count", Fields); packed = api.AsfParentType.GetProperty("ContentsPacked", Fields);
            if (registryCount?.PropertyType != typeof(int) || packed?.PropertyType != typeof(bool))
                throw new MissingMemberException("ASF passive registry/packed properties differ.");
            foreach (var method in new MethodBase[] { registryCount.GetGetMethod(true), packed.GetGetMethod(true),
                typeof(StorageGroupManager).GetMethod("NewGroup", new[] { typeof(string) }),
                typeof(StorageGroupManager).GetMethod("HasStorageGroup", new[] { typeof(StorageGroup) }),
                typeof(StorageGroupManager).GetProperty("StorageGroupsForReading").GetGetMethod(),
                typeof(StorageGroup).GetMethod("InitFrom", new[] { typeof(IStorageGroupMember) }),
                typeof(StorageGroup).GetMethod("GetParentStoreSettings", Type.EmptyTypes),
                typeof(StorageGroupUtility).GetMethod("SetStorageGroup", new[] { typeof(IStorageGroupMember), typeof(StorageGroup), typeof(bool) }),
                typeof(Building_Storage).GetMethod("GetStoreSettings", Type.EmptyTypes),
                typeof(Building_Storage).GetMethod("GetParentStoreSettings", Type.EmptyTypes),
                typeof(StorageSettings).GetMethod("AllowedToAccept", new[] { typeof(Thing) }),
                typeof(ThingFilter).GetMethod("Allows", new[] { typeof(Thing) }),
                typeof(SpecialThingFilterWorker_Fresh).GetMethod("Matches", new[] { typeof(Thing) }),
                typeof(Cap03FilterUnknownWorker).GetConstructor(Type.EmptyTypes),
                typeof(Cap03FilterUnknownWorker).GetMethod("Matches", new[] { typeof(Thing) }),
                typeof(Cap03FilterUnknownWorker).GetMethod("AlwaysMatches", new[] { typeof(ThingDef) }),
                typeof(Cap03FilterUnknownWorker).GetMethod("CanEverMatch", new[] { typeof(ThingDef) }) }) Bind(method);
        }

        internal static Cap03FilterResult Run(Map map, string expectedBehavior)
        {
            Cap03FilterScenario test = null;
            try
            {
                test = new Cap03FilterScenario(map, expectedBehavior); test.Setup();
                test.RunScene("native-asf-first", false, true, map.Center + new IntVec3(-14, 0, -8));
                test.RunScene("native-shelf-first", false, false, map.Center + new IntVec3(10, 0, -8));
                test.RunScene("unknown-asf-first", true, true, map.Center + new IntVec3(-14, 0, 8));
                test.RunScene("unknown-shelf-first", true, false, map.Center + new IntVec3(10, 0, 8));
                test.Check("four-complete-scenes", test.result.scenes.Count == 4 && test.result.scenes.All(s => s.completed && s.restored), "Both member orders are independently created and retired for each worker kind.");
                test.Check("scenes-disjoint", test.result.scenes.SelectMany(s => s.linked.members.Select(m => m.thing.thingId).Concat(s.linked.parcels.Select(p => p.thingId))).Distinct().Count() == 20,
                    "Four independent pairs and twelve distinct parcel IDs, not a reordered accepted list.");
                test.Check("all-scopes-closed", !test.api.HasOpenScope, "Every actual projection scope was disposed.");
                test.Require("same-tick", Find.TickManager.TicksGame == test.result.startedTick, "No simulated tick advancement or executed hauling.");
                test.result.fixtureValid = true; test.result.requestedBehaviorSatisfied = !test.failed;
                test.result.expectationMatched = !test.failed && expectedBehavior == "satisfied";
                test.result.status = test.result.expectationMatched ? "passed" : "failed";
            }
            catch (Exception error)
            {
                var cause = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                if (test == null)
                {
                    var failedBinding = new Cap03FilterResult { expectedBehavior = expectedBehavior, error = cause.ToString(), finishedTick = Find.TickManager.TicksGame };
                    HarnessSession.Event("cap03-a-result", Json.Stringify(failedBinding)); return failedBinding;
                }
                test.result.error = cause.ToString(); test.result.status = "inconclusive"; test.result.fixtureValid = false;
                test.result.requestedBehaviorSatisfied = false; test.result.expectationMatched = false;
                test.Record("fixture-exception", "fixture", false, cause.ToString());
            }
            finally
            {
                if (test != null)
                {
                    // Complete cleanup independently for every owned test object, even on failed setup.
                    foreach (var thing in test.owned.AsEnumerable().Reverse())
                        try { if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish); }
                        catch (Exception error) { test.result.error = (test.result.error ?? "") + "\nCleanup: " + error; test.result.status = "inconclusive"; test.result.fixtureValid = false; test.result.expectationMatched = false; test.result.requestedBehaviorSatisfied = false; }
                    test.result.finishedTick = Find.TickManager.TicksGame;
                    test.result.finalCounter = test.custom == null ? null : test.Counter();
                    test.Capture("counter-final", null, test.result.finalCounter);
                    HarnessSession.Event("cap03-a-result", Json.Stringify(test.result));
                }
            }
            return test.result;
        }

        private void Setup()
        {
            Require("expectation", result.expectedBehavior == "satisfied", "CAP03-A accepts only explicit satisfied; unsupported custom-worker containment is a requested result.");
            Require("actual-main-thread-map", map != null && map.IsPlayerHome && Current.Game != null && UnityData.IsInMainThread, "Initialized disposable player-home map on actual main simulation thread.");
            rawRice = DefDatabase<ThingDef>.GetNamed("RawRice");
            Capture("rice-definition", null, new[] { rawRice.defName, rawRice.thingClass.FullName, rawRice.modContentPack.PackageId,
                rawRice.stackLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            custom = DefDatabase<SpecialThingFilterDef>.GetNamed(UnknownDef); fresh = DefDatabase<SpecialThingFilterDef>.GetNamed("AllowFresh");
            Require("test-worker-definition", custom.workerClass == typeof(Cap03FilterUnknownWorker) && custom.allowedByDefault && custom.configurable && custom.parentCategory.defName == "Root",
                "Custom worker is allowed by default and unreviewed; no global predicate patch or catalog exemption.");
            Require("native-fresh-definition", fresh.workerClass == typeof(SpecialThingFilterWorker_Fresh) && fresh.allowedByDefault && fresh.parentCategory.defName == "Root", "Exact installed native freshness worker.");
            result.initialCounter = Counter(); Capture("counter-initial", null, result.initialCounter);
            Capture("bindings", null, new ProjectionFixtureBinding { assemblies = result.assemblies, bindings = result.bindings });
            Require("reviewed-asf", result.assemblies.Single(x => x.name.StartsWith("AdaptiveStorageFramework,", StringComparison.Ordinal)).sha256 == "28DAA37ADE4144CAD2B7669EDFDA2201F9C0E8E99D4639853131E066026935B9", "Actual inspected ASF1.2.4 binary.");
            var mods = LoadedModManager.RunningModsListForReading;
            var harness = mods.Single(x => x.assemblies.loadedAssemblies.Contains(GetType().Assembly));
            var asf = mods.Single(x => x.assemblies.loadedAssemblies.Contains(api.AsfParentType.Assembly));
            var neat = mods.Single(x => string.Equals(x.PackageId, "sbz.NeatStorage", StringComparison.OrdinalIgnoreCase));
            var core = mods.Single(x => string.Equals(x.PackageId, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase));
            Input(harness, "Defs/Cap03FilterDefs.xml", "470E370CDD776D73564EB66CEDE2C545D084C76AF80D39521CB82E095A866CCF");
            Input(asf, "Defs/ThingDefBase.xml", "DECE4A55D724F4D1EE23B6F21C531BB4F5EF627EC93E4A4D02C7565FE73A242B");
            Input(neat, "1.6/Defs/ThingDefs_Buildings/Buildings_CrateAndPallet.xml", "9B9F757E13A16327250B5F62BA15DC457F40F4B45DD7500CA9DEA0F56BC4D582");
            Input(core, "Defs/Misc/SpecialThingFilterDefs/SpecialThingFilters.xml", "A3D81DA38D9DE4145D46CFB46AB8891F3AC7648CB73B481763BE88B48A79806F");
            Input(core, "Defs/ThingDefs_Buildings/Buildings_Furniture.xml", "CEA362CA9451F0762F8A104B2344BD540B5F6E8663DD9A4E75F3C39B46607C55");
            foreach (var thing in map.listerThings.AllThings.Where(x => x is Pawn || x is Skyfaller || x is ActiveTransporter).ToList()) if (thing.Spawned) thing.DeSpawn();
            var rect = new CellRect(map.Center.x - 20, map.Center.z - 22, 44, 39);
            Require("private-scene-bounds", rect.Cells.All(c => c.InBounds(map) && map.zoneManager.ZoneAt(c) == null), "Only separate generated-map cells; no existing zone overwritten.");
            GenDebug.ClearArea(rect, map);
            foreach (var cell in rect.Cells) { map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.roofGrid.SetRoof(cell, null); map.areaManager.Home[cell] = true; map.fogGrid.Unfog(cell); }
            Rand.PushState(12031); try { actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); } finally { Rand.PopState(); }
            Require("normal-actor", actor != null && actor.RaceProps.Humanlike && !actor.Downed && actor.inventory != null && actor.carryTracker != null, "Actual normal human actor.");
            actor.inventory.innerContainer.ClearAndDestroyContents(); actor.workSettings.EnableAndInitializeIfNotAlreadyInitialized(); actor.workSettings.DisableAll();
            owned.Add(actor); GenSpawn.Spawn(actor, map.Center + new IntVec3(0, 0, -20), map);
            Require("one-idle-actor", map.mapPawns.AllPawnsSpawned.Count == 1 && ActorIdle(), "No generated actors or executing jobs.");
            result.thingDefs = DefDatabase<ThingDef>.AllDefsListForReading.Count; result.categoryDefs = DefDatabase<ThingCategoryDef>.AllDefsListForReading.Count;
            result.specialDefs = DefDatabase<SpecialThingFilterDef>.AllDefsListForReading.Count; result.gameMaps = Current.Game.Maps.Count;
            var before = Counter(); result.catalogStatus = api.CreateCatalog(); Capture("catalog", null, result.catalogStatus);
            Require("catalog-created", Complete(result.catalogStatus) && api.NativeReviewed, Json.Stringify(result.catalogStatus));
            result.nativeIdentity = api.NativeIdentity; result.asfIdentity = api.AsfIdentity; result.patchInventory = api.PatchInventory;
            Capture("patch-inventory", null, result.patchInventory);
            Check("catalog-no-custom-predicate", Counter().matches == before.matches, "Catalog class discovery does not call custom Matches.");
        }

        private void Bind(MemberInfo member)
        {
            if (member == null) throw new MissingMemberException("CAP03-A required native binding absent.");
            result.bindings.Add(member.DeclaringType.FullName + "." + member.Name + ";token=" + member.MetadataToken + ";mvid=" + member.Module.ModuleVersionId);
        }
        private void Input(ModContentPack mod, string relative, string expected)
        {
            using (var stream = File.OpenRead(Path.Combine(mod.RootDir, relative.Replace('/', Path.DirectorySeparatorChar))))
            using (var sha = SHA256.Create())
            {
                var row = new Cap03BudgetFile { packageId = mod.PackageId, root = mod.RootDir, relativePath = relative,
                    sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") };
                result.inputs.Add(row); Capture("input", null, row);
                Require("input-" + result.inputs.Count, expected == null || row.sha256 == expected, Json.Stringify(row));
            }
        }
        private string Id(object value)
        {
            if (value == null) return null;
            for (int i = 0; i < identities.Count; i++) if (ReferenceEquals(identities[i], value)) return "ref:" + (i + 1);
            if (identities.Count >= 256) throw new InvalidOperationException("Bounded fixture identity census exhausted.");
            identities.Add(value); return "ref:" + identities.Count;
        }
        private bool ActorIdle() => actor.Spawned && actor.CurJob == null && actor.inventory.innerContainer.Count == 0 && actor.carryTracker.CarriedThing == null;
        private Cap03FilterCounter Counter()
        {
            var instance = worker.GetValue(custom);
            return new Cap03FilterCounter { constructed = Cap03FilterUnknownWorker.Constructed, matches = Cap03FilterUnknownWorker.MatchesCalls,
                alwaysMatches = Cap03FilterUnknownWorker.AlwaysMatchesCalls, canEverMatch = Cap03FilterUnknownWorker.CanEverMatchCalls,
                workerExists = instance != null, actualWorkerType = instance?.GetType().FullName, workerIdentity = Id(instance) };
        }
        private static bool Complete(ProjectionFixtureStatus s) => s != null && s.usable && s.observation == "Complete" && s.capability == "Supported" && s.reason == "None";
        private static bool Same<T>(T a, T b) => Json.Stringify(a) == Json.Stringify(b);
        private void Require(string id, bool okay, string detail) { Record(id, "fixture", okay, detail); if (!okay) throw new InvalidOperationException(id + ": " + detail); }
        private bool Check(string id, bool okay, string detail) { Record(id, "behavior", okay, detail); if (!okay) failed = true; return okay; }
        private void Record(string id, string kind, bool okay, string detail)
        {
            var row = new ProjectionFixtureAssertion { sequence = result.assertions.Count + 1, id = id, kind = kind, passed = okay, detail = detail };
            result.assertions.Add(row); Capture("assertion", null, row);
        }
        private void Capture<T>(string kind, string scene, T data)
        {
            var row = new Cap03BudgetRecord { sequence = result.records.Count + 1, tick = Find.TickManager.TicksGame, kind = kind, trial = scene, data = Json.Stringify(data) };
            result.records.Add(row); HarnessSession.Event("cap03-a-" + kind, Json.Stringify(row));
        }
    }
}
