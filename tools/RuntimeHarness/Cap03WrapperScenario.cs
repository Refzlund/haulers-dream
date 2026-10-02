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
    // Case-only native counter objects. No global patch, catalog exemption or HD reference.
    internal sealed partial class Cap03WrapperScenario
    {
        private readonly Map map;
        private readonly Cap03WrapperResult result;
        private readonly Cap03BudgetBridge api;
        private readonly FieldInfo specials;
        private readonly List<object> identities = new List<object>();
        private readonly List<Thing> owned = new List<Thing>();
        private readonly List<Zone_Stockpile> zones = new List<Zone_Stockpile>();
        private readonly Dictionary<Cap03WrapperInner, int> maximumHitPoints = new Dictionary<Cap03WrapperInner, int>();
        private ThingDef innerDef;
        private Pawn actor;
        private bool failed;
        private sealed class Scene
        {
            internal Cap03WrapperScene row;
            internal ISlotGroupParent parent, positiveParent;
            internal IntVec3 cell, positiveCell;
            internal Thing steel;
            internal readonly List<Thing> owned = new List<Thing>();
            internal readonly List<Cap03WrapperInner> inners = new List<Cap03WrapperInner>();
            internal readonly List<MinifiedThing> wrappers = new List<MinifiedThing>();
            internal readonly List<ISlotGroupParent> parents = new List<ISlotGroupParent>();
            internal readonly Dictionary<ISlotGroupParent, List<IntVec3>> footprints = new Dictionary<ISlotGroupParent, List<IntVec3>>();
            internal Zone_Stockpile zone;
        }

        private Cap03WrapperScenario(Map map, string expected)
        {
            this.map = map;
            result = new Cap03WrapperResult { expectedBehavior = expected, mapId = map?.uniqueID ?? -1,
                startedTick = Find.TickManager.TicksGame, mainThread = Thread.CurrentThread.ManagedThreadId };
            api = new Cap03BudgetBridge(); api.InitializeBudgetBindings();
            result.session = api.SessionId.ToString("N"); result.assemblies = api.Assemblies; result.bindings = api.Bindings;
            specials = typeof(ThingFilter).GetField("disallowedSpecialFilters", BindingFlags.NonPublic | BindingFlags.Instance);
            if (specials?.FieldType != typeof(List<SpecialThingFilterDef>)) throw new MissingFieldException("Native special-filter list binding differs.");
            Bind(specials);
            foreach (var member in new MemberInfo[] {
                typeof(MinifyUtility).GetMethod("MakeMinified", new[] { typeof(Thing), typeof(DestroyMode) }),
                typeof(MinifyUtility).GetMethod("GetInnerIfMinified", new[] { typeof(Thing) }),
                typeof(MinifiedThing).GetMethod("CanStackWith", new[] { typeof(Thing) }),
                typeof(MinifiedThing).GetMethod("GetDirectlyHeldThings", Type.EmptyTypes),
                typeof(MinifiedThing).GetProperty("InnerThing").GetGetMethod(),
                typeof(MinifiedThing).GetProperty("InnerThing").GetSetMethod(),
                typeof(Thing).GetProperty("HitPoints").GetGetMethod(),
                typeof(Thing).GetProperty("MaxHitPoints").GetGetMethod(),
                typeof(Cap03WrapperInner).GetProperty("HitPoints").GetGetMethod(),
                typeof(Cap03WrapperInner).GetProperty("HitPoints").GetSetMethod(),
                typeof(Cap03WrapperInner).GetProperty("RawHitPoints").GetGetMethod(),
                typeof(Cap03WrapperInner).GetProperty("StackCalls").GetGetMethod(),
                typeof(Cap03WrapperInner).GetProperty("HitPointReads").GetGetMethod(),
                typeof(Cap03WrapperInner).GetConstructor(Type.EmptyTypes),
                typeof(Cap03WrapperInner).GetMethod("CanStackWith", new[] { typeof(Thing) }),
                typeof(ThingFilter).GetMethod("Allows", new[] { typeof(Thing) }),
                typeof(StorageSettings).GetMethod("AllowedToAccept", new[] { typeof(Thing) }),
                typeof(StoreUtility).GetMethod("IsGoodStoreCell", new[] { typeof(IntVec3), typeof(Map), typeof(Thing), typeof(Pawn), typeof(Faction) }),
                typeof(StoreUtility).GetMethod("NoStorageBlockersIn", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IntVec3), typeof(Map), typeof(Thing) }, null),
                typeof(Zone_Stockpile).GetMethod("GetParentStoreSettings", Type.EmptyTypes),
                typeof(Zone).GetMethod("Delete", new[] { typeof(bool) }) }) Bind(member);
        }

        internal static Cap03WrapperResult Run(Map map, string expectedBehavior)
        {
            Cap03WrapperScenario test = null;
            try
            {
                test = new Cap03WrapperScenario(map, expectedBehavior); test.Setup();
                test.RunScene("incoming-native-stockpile", false, false, map.Center + new IntVec3(-14, 0, -8));
                test.RunScene("incoming-neat", false, true, map.Center + new IntVec3(10, 0, -8));
                test.RunScene("resident-native-shelf", true, false, map.Center + new IntVec3(-14, 0, 8));
                test.RunScene("resident-neat", true, true, map.Center + new IntVec3(10, 0, 8));
                test.Check("four-independent-scenes", test.result.scenes.Count == 4 && test.result.scenes.All(x => x.completed && x.restored), "Every separate incoming/resident native/ASF scene completes and retires.");
                test.Check("eight-native-wrappers", test.result.scenes.SelectMany(x => x.beforeProtected.wrappers).Select(x => x.outer.thingId).Distinct().Count() == 8
                    && test.result.scenes.SelectMany(x => x.beforeProtected.wrappers).Select(x => x.inner.thingId).Distinct().Count() == 8
                    && test.result.scenes.SelectMany(x => x.beforeProtected.wrappers).SelectMany(x => new[] { x.outer.thingId, x.inner.thingId }).Distinct().Count() == 16,
                    "Eight distinct native wrappers and eight distinct actual inner buildings, with no cross-scene ID reuse.");
                test.Check("eight-closed-scopes", test.result.scenes.Sum(x => x.trials.Count) == 8 && test.result.scenes.All(x => x.trials.All(t => t.completed && t.disposed)) && !test.api.HasOpenScope,
                    "One protected boundary scope and one ordinary positive scope per scene.");
                test.Check("eight-distinct-scopes", test.result.scenes.SelectMany(x => x.trials).Select(x => x.scopeIdentity).Distinct().Count() == 8,
                    "Each operation pair used a fresh actual projection scope, not a recycled accepted handle.");
                test.Require("same-tick", Find.TickManager.TicksGame == test.result.startedTick, "Synchronous main-thread fixture; no yielded frame/tick or executed hauling.");
                test.result.fixtureValid = true; test.result.requestedBehaviorSatisfied = !test.failed;
                test.result.expectationMatched = !test.failed && expectedBehavior == "satisfied";
                test.result.status = test.result.expectationMatched ? "passed" : "failed";
            }
            catch (Exception error)
            {
                var cause = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                if (test == null)
                {
                    var binding = new Cap03WrapperResult { expectedBehavior = expectedBehavior, error = cause.ToString(), finishedTick = Find.TickManager.TicksGame };
                    HarnessSession.Event("cap03-c-result", Json.Stringify(binding)); return binding;
                }
                test.Inconclusive(cause); test.Record("fixture-exception", "fixture", false, cause.ToString());
            }
            finally
            {
                if (test != null)
                {
                    foreach (var thing in test.owned.OfType<MinifiedThing>().Cast<Thing>().Reverse().Concat(test.owned.AsEnumerable().Reverse().Where(t => !(t is MinifiedThing))))
                        try { if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish); }
                        catch (Exception error) { test.Inconclusive(error); }
                    foreach (var zone in test.zones)
                        try { if (map.zoneManager.AllZones.Contains(zone)) zone.Delete(false); }
                        catch (Exception error) { test.Inconclusive(error); }
                    test.result.finishedTick = Find.TickManager.TicksGame;
                    HarnessSession.Event("cap03-c-result", Json.Stringify(test.result));
                }
            }
            return test.result;
        }

        private void Setup()
        {
            Require("expectation", result.expectedBehavior == "satisfied", "Only explicit satisfied containment expectation is accepted.");
            Require("main-thread-map", map != null && map.IsPlayerHome && Current.Game != null && UnityData.IsInMainThread, "Actual initialized disposable home map on simulation thread.");
            innerDef = DefDatabase<ThingDef>.GetNamed("HDHarness_CAP03C_Inner");
            result.definition = new Cap03WrapperDefinition { def = innerDef.defName, runtimeType = innerDef.thingClass.FullName, package = innerDef.modContentPack.PackageId,
                category = innerDef.category.ToString(), minifiedDef = innerDef.minifiedDef?.defName, outerType = innerDef.minifiedDef?.thingClass.FullName,
                minifiable = innerDef.Minifiable, everStorableWhenMinified = innerDef.EverStorable(true), everStorableAsBuilding = innerDef.EverStorable(false),
                useHitPoints = innerDef.useHitPoints, width = innerDef.size.x, depth = innerDef.size.z, outerStackLimit = innerDef.minifiedDef?.stackLimit ?? 0,
                withinBuildings = innerDef.IsWithinCategory(DefDatabase<ThingCategoryDef>.GetNamed("Buildings")),
                withinNeat = innerDef.IsWithinCategory(DefDatabase<ThingCategoryDef>.GetNamed("BuildingsNeatStorage")),
                categories = innerDef.thingCategories.Select(x => x.defName).ToList() };
            Capture("definition", null, result.definition);
            Require("case-only-inner-definition", innerDef.thingClass == typeof(Cap03WrapperInner) && innerDef.modContentPack.assemblies.loadedAssemblies.Contains(GetType().Assembly)
                && result.definition.category == "Building" && result.definition.minifiable && result.definition.everStorableWhenMinified && !result.definition.everStorableAsBuilding
                && result.definition.withinBuildings && !result.definition.withinNeat && result.definition.useHitPoints && result.definition.width == 1 && result.definition.depth == 1
                && innerDef.minifiedDef.thingClass == typeof(MinifiedThing) && innerDef.minifiedDef.stackLimit == 1, "Actual patched native minifiable shape/category, without changing shared definitions.");
            Capture("bindings", null, new ProjectionFixtureBinding { assemblies = result.assemblies, bindings = result.bindings });
            Require("actual-asf", result.assemblies.Single(x => x.name.StartsWith("AdaptiveStorageFramework,", StringComparison.Ordinal)).sha256 == "28DAA37ADE4144CAD2B7669EDFDA2201F9C0E8E99D4639853131E066026935B9", "Reviewed actual ASF1.2.4 binary.");
            var mods = LoadedModManager.RunningModsListForReading;
            Input(mods.Single(x => x.assemblies.loadedAssemblies.Contains(GetType().Assembly)), "Defs/Cap03WrapperDefs.xml", "C6039286A8D9070DB9FC18AD7F7A7CFC4CA5ED9D3E99AB81A9BAB1780D85C4CE");
            Input(mods.Single(x => x.assemblies.loadedAssemblies.Contains(api.AsfParentType.Assembly)), "Defs/ThingDefBase.xml", "DECE4A55D724F4D1EE23B6F21C531BB4F5EF627EC93E4A4D02C7565FE73A242B");
            Input(mods.Single(x => string.Equals(x.PackageId, "sbz.NeatStorage", StringComparison.OrdinalIgnoreCase)), "1.6/Defs/ThingDefs_Buildings/Buildings_CrateAndPallet.xml", "9B9F757E13A16327250B5F62BA15DC457F40F4B45DD7500CA9DEA0F56BC4D582");
            var core = mods.Single(x => string.Equals(x.PackageId, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase));
            Input(core, "Defs/ThingDefs_Buildings/Buildings_Furniture.xml", "CEA362CA9451F0762F8A104B2344BD540B5F6E8663DD9A4E75F3C39B46607C55");
            Input(core, "Defs/ThingDefs_Items/Items_Unfinished.xml", "E095396DAD8A83421FF04E47010701CF56DF67E4E00C0D25C1704A51FB8E3130");
            foreach (var thing in map.listerThings.AllThings.Where(x => x is Pawn || x is Skyfaller || x is ActiveTransporter).ToList()) if (thing.Spawned) thing.DeSpawn();
            var rect = new CellRect(map.Center.x - 20, map.Center.z - 22, 44, 39);
            Require("private-bounds", rect.Cells.All(c => c.InBounds(map) && map.zoneManager.ZoneAt(c) == null), "No existing zone or player save cell overwritten.");
            GenDebug.ClearArea(rect, map);
            foreach (var cell in rect.Cells) { map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.roofGrid.SetRoof(cell, null); map.areaManager.Home[cell] = true; map.fogGrid.Unfog(cell); }
            Rand.PushState(12033); try { actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); } finally { Rand.PopState(); }
            Require("normal-actor", actor != null && actor.RaceProps.Humanlike && !actor.Downed && actor.inventory != null && actor.carryTracker != null, "Actual normal human actor.");
            owned.Add(actor); actor.inventory.innerContainer.ClearAndDestroyContents(); actor.workSettings.EnableAndInitializeIfNotAlreadyInitialized(); actor.workSettings.DisableAll();
            GenSpawn.Spawn(actor, map.Center + new IntVec3(0, 0, -20), map);
            Require("idle-actor", map.mapPawns.AllPawnsSpawned.Count == 1 && ActorIdle(), "No executing job or held cargo.");
            result.thingDefs = DefDatabase<ThingDef>.AllDefsListForReading.Count; result.categoryDefs = DefDatabase<ThingCategoryDef>.AllDefsListForReading.Count;
            result.specialDefs = DefDatabase<SpecialThingFilterDef>.AllDefsListForReading.Count; result.gameMaps = Current.Game.Maps.Count;
            result.catalogStatus = api.CreateCatalog(); Capture("catalog", null, result.catalogStatus);
            Require("catalog-created", Complete(result.catalogStatus) && api.NativeReviewed, Json.Stringify(result.catalogStatus));
            result.nativeIdentity = api.NativeIdentity; result.asfIdentity = api.AsfIdentity; result.patchInventory = api.PatchInventory;
            Capture("patch-inventory", null, result.patchInventory);
        }

        private void Input(ModContentPack mod, string relative, string expected)
        {
            using (var stream = File.OpenRead(Path.Combine(mod.RootDir, relative.Replace('/', Path.DirectorySeparatorChar))))
            using (var sha = SHA256.Create())
            {
                var row = new Cap03BudgetFile { packageId = mod.PackageId, root = mod.RootDir, relativePath = relative, sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") };
                result.inputs.Add(row); Capture("input", null, row); Require("input-" + result.inputs.Count, row.sha256 == expected, Json.Stringify(row));
            }
        }
        private void Bind(MemberInfo member)
        { if (member == null) throw new MissingMemberException("CAP03-C native binding missing."); result.bindings.Add(member.DeclaringType.FullName + "." + member.Name + ";token=" + member.MetadataToken + ";mvid=" + member.Module.ModuleVersionId); }
        private string Id(object value)
        {
            if (value == null) return null;
            for (int i = 0; i < identities.Count; i++) if (ReferenceEquals(identities[i], value)) return "ref:" + (i + 1);
            if (identities.Count >= 512) throw new InvalidOperationException("Bounded fixture identity registry exhausted.");
            identities.Add(value); return "ref:" + identities.Count;
        }
        private static string Key(ISlotGroupParent parent) => parent is Thing thing ? "building:" + thing.thingIDNumber : "zone:" + ((Zone)parent).ID;
        private bool ActorIdle() => actor.Spawned && actor.CurJob == null && actor.inventory.innerContainer.Count == 0 && actor.carryTracker.CarriedThing == null;
        private static bool Complete(ProjectionFixtureStatus s) => s != null && s.usable && s.observation == "Complete" && s.capability == "Supported" && s.reason == "None";
        private static bool Same<T>(T a, T b) => Json.Stringify(a) == Json.Stringify(b);
        private void Inconclusive(Exception error) { result.error = (result.error ?? "") + "\n" + error; result.status = "inconclusive"; result.fixtureValid = result.requestedBehaviorSatisfied = result.expectationMatched = false; }
        private void Require(string id, bool okay, string detail) { Record(id, "fixture", okay, detail); if (!okay) throw new InvalidOperationException(id + ": " + detail); }
        private bool Check(string id, bool okay, string detail) { Record(id, "behavior", okay, detail); if (!okay) failed = true; return okay; }
        private void Record(string id, string kind, bool okay, string detail)
        { var row = new ProjectionFixtureAssertion { sequence = result.assertions.Count + 1, id = id, kind = kind, passed = okay, detail = detail }; result.assertions.Add(row); Capture("assertion", null, row); }
        private void Capture<T>(string kind, string scene, T value)
        { var row = new Cap03BudgetRecord { sequence = result.records.Count + 1, tick = Find.TickManager.TicksGame, kind = kind, trial = scene, data = Json.Stringify(value) }; result.records.Add(row); HarnessSession.Event("cap03-c-" + kind, Json.Stringify(row)); }
    }
}
