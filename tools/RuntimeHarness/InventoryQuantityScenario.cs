using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    // Command-body fixture only. UI submission and actual network dispatch have separate acceptance.
    internal sealed class InventoryQuantityScenario
    {
        private const string ObserverId = "HaulersDream.RuntimeHarness.InventoryQuantity";
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private readonly Map map;
        private readonly Assembly hd;
        private readonly MethodInfo command, nativeDrop;
        private readonly Type compType;
        private readonly CellRect area;
        private Pawn actor, other;
        private Thing source;
        private object comp;
        private object settings, savedRules;
        private List<InventoryQuantityRule> originalRules;
        private Type ruleType, ruleModeType;
        private readonly List<Thing> owned = new List<Thing>();
        private static InventoryQuantityScene active;
        private static Thing observedSource;
        private static InventoryQuantityScenario observingFixture;
        private bool failed;
        private int completedScenes;

        private InventoryQuantityScenario(Map map)
        {
            this.map = map;
            hd = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "HaulersDream");
            compType = hd.GetType("HaulersDream.CompHauledToInventory", true);
            command = hd.GetType("HaulersDream.InventoryDropCommand", true).GetMethod("DropInventoryCountSynced", Flags,
                null, new[] { typeof(Pawn), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) }, null);
            nativeDrop = typeof(ThingOwner).GetMethod("TryDrop", new[] { typeof(Thing), typeof(IntVec3), typeof(Map),
                typeof(ThingPlaceMode), typeof(int), typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>), typeof(Predicate<IntVec3>) });
            if (command == null || command.ReturnType != typeof(void) || !command.IsStatic || nativeDrop == null)
                throw new MissingMethodException("Exact inventory command/native quantity overload unavailable.");
            area = new CellRect(map.Center.x - 15, map.Center.z - 15, 31, 31);
        }

        internal static bool Run(Map map)
        {
            InventoryQuantityScenario fixture = null;
            Harmony observer = null;
            try
            {
                fixture = new InventoryQuantityScenario(map);
                fixture.Setup();
                observer = new Harmony(ObserverId);
                observer.Patch(fixture.nativeDrop,
                    new HarmonyMethod(typeof(InventoryQuantityScenario), nameof(BeforeNative)),
                    new HarmonyMethod(typeof(InventoryQuantityScenario), nameof(AfterNative)));
                HarnessSession.Event("quantity-bindings", "command=" + fixture.command.Module.ModuleVersionId + ":" + fixture.command.MetadataToken
                    + "; native=" + fixture.nativeDrop.Module.ModuleVersionId + ":" + fixture.nativeDrop.MetadataToken + "; observer=" + ObserverId);
                fixture.Scene("one-unit", 1, 1);
                fixture.Scene("partial-kept", 4, 4, keep: 7);
                fixture.Scene("partial-explicit-rule", 4, 4, ruled: true);
                fixture.Scene("full-last-stack", 10, 10, keep: 7);
                fixture.Scene("full-other-stack", 10, 10, keep: 7, peerCount: 3);
                fixture.Scene("untagged", 4, 4, tagged: false);
                fixture.Scene("ordinary-floor-merge", 4, 4, floorMerge: true);
                fixture.Scene("blocked", 4, 0, blocked: true);
                fixture.Scene("partial-native-false", 4, 1, blocked: true, floorMerge: true);
                fixture.Scene("zero-request", 0, 0, rejected: true);
                fixture.Scene("negative-request", -1, 0, rejected: true);
                fixture.Scene("excess-request", 11, 0, rejected: true);
                fixture.Scene("changed-opening-count", 4, 0, rejected: true, mutation: "count");
                fixture.Scene("wrong-id", 4, 0, rejected: true, mutation: "id");
                fixture.Scene("wrong-map", 4, 0, rejected: true, mutation: "map");
                fixture.Scene("wrong-policy", 4, 0, rejected: true, mutation: "policy");
                fixture.Scene("current-custody-other", 4, 0, rejected: true, mutation: "other");
                fixture.Scene("restored-custody", 4, 4, mutation: "restored");
                fixture.Scene("equivalent-new-owner", 4, 4, mutation: "owner");
                fixture.Check("all-command-scenes", fixture.completedScenes == 19, "Completed separate scenes=" + fixture.completedScenes);
            }
            catch (Exception error)
            {
                HarnessSession.Check("quantity-fixture-exception", false, Unwrap(error).ToString());
                if (fixture != null) fixture.failed = true;
            }
            finally
            {
                active = null; observedSource = null; observingFixture = null;
                try
                {
                    if (observer != null && fixture != null)
                    {
                        observer.Unpatch(fixture.nativeDrop, HarmonyPatchType.All, ObserverId);
                        fixture.Check("observer-removed", !(Harmony.GetPatchInfo(fixture.nativeDrop)?.Owners.Contains(ObserverId) ?? false), "Native observer removed.");
                    }
                }
                catch (Exception error)
                {
                    HarnessSession.Check("quantity-unpatch-exception", false, Unwrap(error).ToString());
                    if (fixture != null) fixture.failed = true;
                }
                try { fixture?.Cleanup(); }
                catch (Exception error)
                {
                    HarnessSession.Check("quantity-cleanup-exception", false, Unwrap(error).ToString());
                    if (fixture != null) fixture.failed = true;
                }
            }
            return fixture != null && !fixture.failed;
        }

        private void Setup()
        {
            Require("initialized-private-map", map != null && map.IsPlayerHome && Current.Game != null && UnityData.IsInMainThread,
                "Controller-authorized disposable home map after native initialization.");
            Require("clear-bounds", area.Cells.All(c => c.InBounds(map) && map.zoneManager.ZoneAt(c) == null), "No existing zone in the disposable scene.");
            foreach (var t in map.listerThings.AllThings.Where(t => t is Pawn || t is Skyfaller || t is ActiveTransporter).ToList())
                if (t.Spawned) t.DeSpawn();
            GenDebug.ClearArea(area, map);
            foreach (var c in area.Cells)
            {
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete); map.roofGrid.SetRoof(c, null);
                map.areaManager.Home[c] = true; map.fogGrid.Unfog(c);
            }
            actor = MakePawn(map.Center, 42502); other = MakePawn(map.Center + new IntVec3(14, 0, 14), 42503);
            comp = actor.AllComps.Single(c => compType.IsInstanceOfType(c));
            Require("normal-live-actors", !actor.Downed && !other.Downed && !actor.Dead && !other.Dead
                && actor.Faction == Faction.OfPlayer && other.Faction == Faction.OfPlayer, "Two normally generated live colonists.");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            settings = hd.GetType("HaulersDream.HaulersDreamMod", true).GetProperty("Settings", Flags).GetValue(null, null);
            savedRules = settings.GetType().GetMethod("GetItemRulesCopy", Flags).Invoke(settings, null);
            ruleType = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "HaulersDream.Core").GetType("HaulersDream.Core.ItemUnloadRule", true);
            ruleModeType = ruleType.Assembly.GetType("HaulersDream.Core.ItemUnloadMode", true);
            originalRules = Rules();
            HarnessSession.Event("quantity-rules-original", Json.Stringify(originalRules));
        }

        private Pawn MakePawn(IntVec3 cell, int seed)
        {
            Pawn p;
            Rand.PushState(seed); try { p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); } finally { Rand.PopState(); }
            owned.Add(p);
            p.inventory.innerContainer.ClearAndDestroyContents();
            p.workSettings.EnableAndInitializeIfNotAlreadyInitialized(); p.workSettings.DisableAll();
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private void Scene(string id, int requested, int expectedMoved, int keep = 0, int peerCount = 0,
            bool tagged = true, bool blocked = false, bool floorMerge = false, bool rejected = false, string mutation = null, bool ruled = false)
        {
            ClearCargo();
            source = Held(actor, 10);
            if (peerCount > 0) Held(actor, peerCount);
            var sentinel = ThingMaker.MakeThing(ThingDefOf.WoodLog); sentinel.stackCount = 5; owned.Add(sentinel);
            Require(id + "-unrelated-stock", actor.inventory.innerContainer.TryAdd(sentinel, false), "Unrelated tagged Wood5/pin5 must survive every command.");
            CompCall("RegisterHauledItem", sentinel, 0); CompCall("SetKeptCount", ThingDefOf.WoodLog, 5);
            if (tagged) CompCall("RegisterHauledItem", source, 0);
            CompCall("SetKeptCount", ThingDefOf.Steel, keep);
            CompCall("GetHashSet"); // Complete normal setup healing before measuring settlement.
            var tagAges = (Dictionary<Thing, int>)compType.GetField("taggedTick", Flags).GetValue(comp);
            tagAges[sentinel] = Find.TickManager.TicksGame - 5;
            foreach (var peer in actor.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel && !ReferenceEquals(t, source)))
                if (tagAges.ContainsKey(peer)) tagAges[peer] = Find.TickManager.TicksGame - 4;
            if (tagged)
            {
                tagAges[source] = Find.TickManager.TicksGame - 3;
            }
            if (ruled)
            {
                var rules = (IDictionary)Activator.CreateInstance(savedRules.GetType());
                rules.Add("Steel", Activator.CreateInstance(ruleType, Enum.Parse(ruleModeType, "KeepAtMost"), 7));
                settings.GetType().GetMethod("SetItemRules", Flags).Invoke(settings, new object[] { rules });
            }
            var row = new InventoryQuantityScene { id = id, requested = requested, expectedMoved = expectedMoved, rejected = rejected,
                mapId = map.uniqueID, pawnId = actor.thingIDNumber, sourceId = source.thingIDNumber,
                expectedCount = 10, commandMap = map.uniqueID, policy = 0, agedTagSeeded = tagged, tickBefore = Find.TickManager.TicksGame,
                rulesBefore = Rules() };
            var declaredTags = new List<int> { sentinel.thingIDNumber };
            if (tagged) declaredTags.AddRange(actor.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).Select(t => t.thingIDNumber));
            declaredTags.Sort();
            var declaredAges = declaredTags.Select(n => new InventoryQuantityAge { id = n,
                tick = row.tickBefore - (n == sentinel.thingIDNumber ? 5 : n == source.thingIDNumber ? 3 : 4) }).ToList();
            var declaredKeeps = new List<InventoryQuantityKeep> { new InventoryQuantityKeep { def = "WoodLog", count = 5 } };
            if (keep > 0) declaredKeeps.Add(new InventoryQuantityKeep { def = "Steel", count = keep });
            row.prepared = Snapshot();
            Require(id + "-declared-physical-stock", row.prepared.sourceHeldByActor && !row.prepared.sourceSpawned && !row.prepared.sourceDestroyed
                && row.prepared.sourceCount == 10 && row.prepared.actorSteel == 10 + peerCount && row.prepared.otherSteel == 0
                && row.prepared.actorInventory.Count == (peerCount > 0 ? 3 : 2) && row.prepared.otherInventory.Count == 0
                && row.prepared.actorInventory.Any(t => t.id == sentinel.thingIDNumber && t.def == "WoodLog" && t.count == 5)
                && row.prepared.actorInventory.Any(t => t.id == source.thingIDNumber && t.def == "Steel" && t.count == 10)
                && (peerCount == 0 || row.prepared.actorInventory.Any(t => t.id != source.thingIDNumber && t.def == "Steel" && t.count == peerCount)),
                "Original Steel10, optional exact peer and unrelated Wood5 occupy the declared direct owner before any scenario mutation.");
            Require(id + "-declared-bookkeeping", declaredTags.SequenceEqual(row.prepared.taggedIds)
                && SameAges(declaredAges, row.prepared.tagAges) && SameKeeps(declaredKeeps, row.prepared.keeps)
                && row.prepared.otherTaggedIds.Count == 0 && row.prepared.otherTagAges.Count == 0 && row.prepared.otherKeeps.Count == 0,
                "Exact intended tagged/untagged source, distinct sentinel/peer ages and full pin dictionaries exist before mutation/warming.");
            Require(id + "-known-rule-policy", ruled ? row.rulesBefore.Count == 1 && row.rulesBefore[0].def == "Steel"
                && row.rulesBefore[0].mode == "KeepAtMost" && row.rulesBefore[0].amount == 7 : row.rulesBefore.Count == 0, "Exact isolated explicit-rule setup recorded.");
            int thingId = source.thingIDNumber;
            if (mutation == "count") row.expectedCount = 9;
            if (mutation == "id") thingId = other.thingIDNumber;
            if (mutation == "map") row.commandMap = -123;
            if (mutation == "policy") row.policy = 99;
            row.commandThingId = thingId;
            if (mutation == "other" || mutation == "restored")
            {
                Require(id + "-transfer-out", actor.inventory.innerContainer.Remove(source) && other.inventory.innerContainer.TryAdd(source, false), "Normal direct native owner transfer to B.");
                if (mutation == "restored") Require(id + "-transfer-back", other.inventory.innerContainer.Remove(source)
                    && actor.inventory.innerContainer.TryAdd(source, false), "Same exact Thing restored to A before command.");
                else
                {
                    CompCall("Deregister", source); // Explicit fixture transfer bookkeeping, before the rejection measurement.
                    declaredTags.Remove(source.thingIDNumber);
                    declaredAges.RemoveAll(a => a.id == source.thingIDNumber);
                }
            }
            if (mutation == "owner")
            {
                var old = actor.inventory;
                var replacement = new Pawn_InventoryTracker(actor);
                foreach (var held in old.innerContainer.ToList())
                    Require(id + "-replace-owner-" + held.thingIDNumber, old.innerContainer.Remove(held) && replacement.innerContainer.TryAdd(held, false), "Equivalent native tracker owns each original Thing.");
                actor.inventory = replacement;
                Require(id + "-old-owner-empty", old.innerContainer.Count == 0 && !ReferenceEquals(old.innerContainer, actor.inventory.innerContainer), "Old tracker empty, new direct custody active.");
            }
            var blockedCells = blocked ? GenRadial.RadialCellsAround(actor.Position, 13f, true).ToList() : new List<IntVec3>();
            if (blocked)
            {
                Require(id + "-candidate-area", blockedCells.All(cell => cell.InBounds(map) && area.Contains(cell)), "Entire native 12.9 radius covered by the disposable floor.");
                foreach (var cell in blockedCells)
                {
                    SpawnStack(ThingDefOf.WoodLog, ThingDefOf.WoodLog.stackLimit, cell);
                }
            }
            if (floorMerge)
            {
                foreach (var t in map.thingGrid.ThingsListAt(actor.Position).Where(t => t.def.category == ThingCategory.Item).ToList()) t.Destroy(DestroyMode.Vanish);
                SpawnStack(ThingDefOf.Steel, ThingDefOf.Steel.stackLimit - 1, actor.Position);
            }
            row.blockedCells = blockedCells.Count;
            row.blockers = CaptureBlockers(blockedCells);
            Require(id + "-native-floor-occupancy", row.blockers.All(b => b.maximum == 1 && b.items.Count == 1
                && (b.cell == actor.Position.ToString() && floorMerge ? b.items[0].def == "Steel" && b.items[0].count == b.items[0].limit - 1
                    : b.items[0].def == "WoodLog" && b.items[0].count == b.items[0].limit)), "Recorded actual candidate capacity/contents admit only the named one-unit merge, if present.");
            row.beforeWarm = Snapshot();
            Require(id + "-declared-command-custody", !row.beforeWarm.sourceDestroyed && !row.beforeWarm.sourceSpawned && row.beforeWarm.sourceCount == 10
                && row.beforeWarm.sourceHeldByActor == (mutation != "other") && row.beforeWarm.sourceHeldByOther == (mutation == "other")
                && row.beforeWarm.actorSteel == (mutation == "other" ? peerCount : 10 + peerCount) && row.beforeWarm.otherSteel == (mutation == "other" ? 10 : 0),
                "Current exact source custody and counts match the declared command scenario.");
            Require(id + "-prewarm-declared-tags", declaredTags.SequenceEqual(row.beforeWarm.taggedIds)
                && SameKeeps(declaredKeeps, row.beforeWarm.keeps)
                && declaredAges.All(a => row.beforeWarm.tagAges.Any(b => a.id == b.id && a.tick == b.tick)),
                "Intended current-custody tag set and surviving ages/pins are present before readers; transfer may leave only its departed age awaiting heal.");
            row.cachesBefore = ReadCaches();
            // Readers may heal; capture the actual state the command receives after warming them.
            row.before = Snapshot();
            Require(id + "-warm-readers-preserve-stock", SameStacks(row.beforeWarm.actorInventory, row.before.actorInventory)
                && SameStacks(row.beforeWarm.otherInventory, row.before.otherInventory) && SameStacks(row.beforeWarm.floor, row.before.floor),
                "Warm readers preserve every initial inventory/floor identity and count before command execution.");
            Require(id + "-command-entry-declared-bookkeeping", declaredTags.SequenceEqual(row.before.taggedIds)
                && SameAges(declaredAges, row.before.tagAges) && SameKeeps(declaredKeeps, row.before.keeps),
                "All intended source/sentinel/peer tags, ages and pins remain exact at actual command entry.");
            Check(id + "-warm-baselines", Close(row.cachesBefore.mass, MassUtility.GearAndInventoryMass(actor))
                && Close(row.cachesBefore.trackedMass, DirectTrackedMass())
                && row.cachesBefore.shared == DirectTaggedCount(actor) + DirectTaggedCount(other)
                && row.cachesBefore.surplus == (bool)HdCall("InventorySurplus", "HasAnySurplus", actor)
                && row.cachesBefore.ruledSurplus == (bool)HdCall("InventorySurplus", "HasAnyRuledSurplus", actor), "Each relevant warm value agrees with the actual pre-command state/policy.");
            if (mutation != "other") Check(id + "-organic-warm-baseline", row.cachesBefore.organic == row.before.actorSteel, "Warm own-stock count matches actual initial quantity.");
            HarnessSession.Event("quantity-before", Json.Stringify(row));
            active = row; observedSource = source; observingFixture = this;
            try { command.Invoke(null, new object[] { actor, thingId, requested, row.expectedCount, row.commandMap, row.policy }); }
            finally { active = null; observedSource = null; observingFixture = null; }
            row.afterSettlement = Snapshot();
            row.blockersAfter = CaptureBlockers(blockedCells);
            row.cachesAfter = ReadCaches();
            row.afterReaders = Snapshot(); row.tickAfter = Find.TickManager.TicksGame;
            row.rulesAfter = Rules();
            Check(id + "-same-tick", row.tickBefore == row.tickAfter, "Paused command and both cache reads use one actual tick.");
            Check(id + "-one-native-call", row.nativeCalls == (rejected ? 0 : 1), "calls=" + row.nativeCalls);
            if (!rejected) Check(id + "-native-arguments", row.nativeCount == requested && row.nativeMode == "Near"
                && row.nativeCell == actor.Position.ToString() && row.nativeMap == map.uniqueID && row.nativeOwnerMatches && row.nativeCurrentInventory,
                "Observed exact native count, location, map, placement mode and direct owner.");
            if (!rejected) Check(id + "-callback-forwarded", row.originalCallbackPresent && row.callbackForwarded == row.placements.Count,
                "Actual nonnull command callback forwarded and returned exactly once per receipt.");
            Check(id + "-physical-conservation", row.before.totalSteel == row.afterSettlement.totalSteel,
                "before=" + row.before.totalSteel + "; after=" + row.afterSettlement.totalSteel);
            Check(id + "-exact-transfer", row.before.inventoryTotal - row.afterSettlement.inventoryTotal == expectedMoved
                && row.afterSettlement.floorTotal - row.before.floorTotal == expectedMoved, "Expected source loss equals total destination growth: " + expectedMoved);
            var expectedActorInventory = row.before.actorInventory.Select(t => new InventoryQuantityStack { id = t.id, count = t.count,
                cell = t.cell, def = t.def, limit = t.limit }).ToList();
            var expectedSource = expectedActorInventory.SingleOrDefault(t => t.id == row.sourceId);
            if (expectedSource != null)
            {
                expectedSource.count -= expectedMoved;
                if (expectedSource.count == 0) expectedActorInventory.Remove(expectedSource);
            }
            Check(id + "-exact-inventory-identities", SameStacks(expectedActorInventory, row.afterSettlement.actorInventory)
                && SameStacks(row.before.otherInventory, row.afterSettlement.otherInventory), "Only original A source loses the requested observed units; every peer/B reference, def and count is preserved.");
            if (row.before.sourceHeldByActor)
                Check(id + "-original-source-custody", row.afterSettlement.sourceHeldByActor == (10 - expectedMoved > 0)
                    && (10 - expectedMoved == 0 || (!row.afterSettlement.sourceDestroyed && !row.afterSettlement.sourceSpawned
                        && row.afterSettlement.sourceCount == 10 - expectedMoved)), "Original held remainder keeps exact identity/custody/count.");
            Check(id + "-placement-deltas", row.placements.Sum(p => p.delta) == expectedMoved
                && row.placements.All(p => p.delta > 0 && p.spawned && !p.destroyed && p.mapId == map.uniqueID && p.def == "Steel"), "Actual callback deltas match independently counted transfer.");
            var expectedFloor = row.before.floor.ToDictionary(t => t.id, t => t.count);
            bool callbackCountsAgree = true;
            foreach (var placement in row.placements)
            {
                expectedFloor.TryGetValue(placement.id, out int prior);
                expectedFloor[placement.id] = prior + placement.delta;
                callbackCountsAgree &= placement.resultingCount == prior + placement.delta;
                callbackCountsAgree &= row.afterSettlement.floor.Any(t => t.id == placement.id && t.cell == placement.cell && t.def == placement.def);
            }
            Check(id + "-each-destination-growth", callbackCountsAgree && expectedFloor.Count == row.afterSettlement.floor.Count
                && row.afterSettlement.floor.All(t => expectedFloor.TryGetValue(t.id, out int expected) && expected == t.count)
                && row.before.floor.All(t => row.afterSettlement.floor.Any(a => a.id == t.id && a.cell == t.cell && a.def == t.def && a.limit == t.limit)),
                "Every callback destination has its own actual growth; no unrelated floor identity changes.");
            Check(id + "-blocking-neighbors-preserved", row.blockers.Count == row.blockersAfter.Count
                && row.blockers.Zip(row.blockersAfter, (a, b) => a.cell == b.cell && a.maximum == b.maximum
                    && (a.items[0].def == "Steel" ? SameStacks(a.items.Where(t => t.def != "Steel").ToList(), b.items.Where(t => t.def != "Steel").ToList())
                        : SameStacks(a.items, b.items))).All(x => x), "All unrelated native blocking stacks and cell capacities remain unchanged.");
            if (!rejected) Check(id + "-native-return", row.nativeReturned && row.nativeResult == (!blocked), "Native return observed=" + row.nativeResult);
            bool retained = row.afterSettlement.sourceHeldByActor;
            Check(id + "-tag-settlement", row.afterSettlement.taggedSource == (row.before.taggedSource && retained)
                || (expectedMoved == 0 && row.afterSettlement.taggedSource == row.before.taggedSource), "Direct tag state before any post-command heal.");
            if (retained && tagged) Check(id + "-age-preserved", row.before.tagAge == row.afterSettlement.tagAge, "Seeded earlier tag age is unchanged by settlement.");
            var expectedTags = row.before.taggedIds.Where(n => n != row.sourceId || retained || expectedMoved == 0).ToList();
            Check(id + "-all-tags-preserved", expectedTags.SequenceEqual(row.afterSettlement.taggedIds)
                && expectedTags.SequenceEqual(row.afterReaders.taggedIds), "Exact source/peer tag identities before and after reader healing; no floor or untagged remainder is newly tagged.");
            var retainedAges = row.before.tagAges.Where(a => expectedTags.Contains(a.id)).ToList();
            Check(id + "-all-retained-ages", retainedAges.All(a => row.afterSettlement.tagAges.Any(b => b.id == a.id && b.tick == a.tick))
                && retainedAges.All(a => row.afterReaders.tagAges.Any(b => b.id == a.id && b.tick == a.tick))
                && row.afterSettlement.tagAges.All(a => row.before.tagAges.Any(b => b.id == a.id && b.tick == a.tick))
                && SameAges(retainedAges, row.afterReaders.tagAges), "Only departed-source age pruning may occur; every retained/peer age survives unchanged.");
            Check(id + "-keep-intent", row.afterSettlement.kept == (expectedMoved > 0 && row.afterSettlement.actorSteel == 0 ? 0 : row.before.kept), "Pin preserved while any same-def inventory remains; last departure prunes it.");
            var expectedKeeps = row.before.keeps.Where(k => !(expectedMoved > 0 && row.afterSettlement.actorSteel == 0 && k.def == "Steel")).ToList();
            Check(id + "-all-keep-preferences", SameKeeps(expectedKeeps, row.afterSettlement.keeps) && SameKeeps(expectedKeeps, row.afterReaders.keeps)
                && SameKeeps(row.before.otherKeeps, row.afterSettlement.otherKeeps) && SameKeeps(row.before.otherKeeps, row.afterReaders.otherKeeps), "All keep preferences retain exact definition/count, except the explicit last-stack prune.");
            Check(id + "-explicit-rules-unchanged", Json.Stringify(row.rulesBefore) == Json.Stringify(row.rulesAfter), "Quantity drop preserves all actual explicit rule mode/amount values.");
            Check(id + "-readers-preserve-custody", SameStacks(row.afterSettlement.actorInventory, row.afterReaders.actorInventory)
                && SameStacks(row.afterSettlement.otherInventory, row.afterReaders.otherInventory) && SameStacks(row.afterSettlement.floor, row.afterReaders.floor)
                && row.before.otherTaggedIds.SequenceEqual(row.afterSettlement.otherTaggedIds) && SameAges(row.before.otherTagAges, row.afterSettlement.otherTagAges)
                && row.before.otherTaggedIds.SequenceEqual(row.afterReaders.otherTaggedIds) && SameAges(row.before.otherTagAges, row.afterReaders.otherTagAges), "Raw settlement and post-command cache reads preserve the other pawn's bookkeeping and physical cargo.");
            Check(id + "-mass-cache", Close(row.cachesAfter.mass, MassUtility.GearAndInventoryMass(actor)), "Warm memo agrees with native live inventory mass after command.");
            Check(id + "-tracked-cache", Close(row.cachesAfter.trackedMass, DirectTrackedMass()), "Warm tracked memo agrees with direct current tags/counts.");
            Check(id + "-surplus-caches", row.cachesAfter.surplus == (bool)HdCall("InventorySurplus", "HasAnySurplus", actor)
                && row.cachesAfter.ruledSurplus == (bool)HdCall("InventorySurplus", "HasAnyRuledSurplus", actor), "Both warm surplus memos agree with uncached post-command policy readers.");
            if (keep == 7 && expectedMoved > 0)
                Check(id + "-surplus-crosses-to-none", row.cachesBefore.surplus && !row.cachesAfter.surplus, "Actual10/13 above keep7 becomes6/3/0: a stale true memo would fail.");
            if (ruled) Check(id + "-ruled-surplus-crosses-to-none", row.cachesBefore.ruledSurplus && !row.cachesAfter.ruledSurplus
                && row.cachesBefore.surplus && !row.cachesAfter.surplus, "Actual KeepAtMost7 rule: both independently warmed memos change true to false after10 becomes6.");
            if (mutation != "other")
            {
                Check(id + "-shared-cache", row.cachesAfter.shared == DirectTaggedCount(actor) + DirectTaggedCount(other), "Warm shared count matches currently held tagged references.");
                Check(id + "-organic-cache", row.cachesAfter.organic == row.afterReaders.actorSteel, "Other actor inventory empty; current own stock matches warm organic count.");
            }
            row.complete = true;
            completedScenes++;
            HarnessSession.Event("quantity-scene", Json.Stringify(row));
        }

        private Thing Held(Pawn p, int count)
        {
            var t = ThingMaker.MakeThing(ThingDefOf.Steel); t.stackCount = count;
            owned.Add(t);
            if (!p.inventory.innerContainer.TryAdd(t, false)) throw new InvalidOperationException("Native inventory insertion failed.");
            return t;
        }
        private void SpawnStack(ThingDef def, int count, IntVec3 cell)
        { var t = ThingMaker.MakeThing(def); t.stackCount = count; owned.Add(t); GenSpawn.Spawn(t, cell, map); }
        private object CompCall(string name, params object[] args) => compType.GetMethod(name, Flags).Invoke(comp, args);
        private object HdCall(string type, string name, params object[] args) => hd.GetType("HaulersDream." + type, true).GetMethod(name, Flags).Invoke(null, args);
        private HashSet<Thing> Tags(Pawn p) => (HashSet<Thing>)compType.GetMethod("PeekHashSet", Flags).Invoke(p.AllComps.Single(c => compType.IsInstanceOfType(c)), null);
        private int DirectTaggedCount(Pawn p) => Tags(p).Where(t => !t.Destroyed && t.def == ThingDefOf.Steel && ReferenceEquals(t.holdingOwner, p.inventory.innerContainer)).Sum(t => t.stackCount);
        private float DirectTrackedMass() => Tags(actor).Where(t => !t.Destroyed).Sum(t => t.stackCount * t.GetStatValue(StatDefOf.Mass));
        private InventoryQuantityCaches ReadCaches() => new InventoryQuantityCaches {
            mass = (float)HdCall("PawnMassCache", "CurrentMass", actor), trackedMass = (float)HdCall("TrackedMassCache", "TrackedMass", actor, comp),
            shared = (int)HdCall("InventoryShare", "CountSharable", map, actor, ThingDefOf.Steel),
            organic = (int)HdCall("OrganicInventoryShare", "CountOrganic", map, actor, ThingDefOf.Steel),
            surplus = (bool)HdCall("SurplusCache", "HasAnySurplus", actor), ruledSurplus = (bool)HdCall("SurplusCache", "HasAnyRuledSurplus", actor) };
        private InventoryQuantityState Snapshot()
        {
            var floors = area.Cells.SelectMany(c => map.thingGrid.ThingsListAt(c)).Where(t => t.def == ThingDefOf.Steel).Distinct().OrderBy(t => t.thingIDNumber)
                .Select(t => Stack(t)).ToList();
            int a = actor.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).Sum(t => t.stackCount);
            int b = other.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).Sum(t => t.stackCount);
            return new InventoryQuantityState { actorSteel = a, otherSteel = b, inventoryTotal = a + b,
                floorTotal = floors.Sum(t => t.count), totalSteel = a + b + floors.Sum(t => t.count), floor = floors,
                sourceCount = source.stackCount, sourceDestroyed = source.Destroyed, sourceSpawned = source.Spawned,
                sourceHeldByActor = !source.Destroyed && !source.Spawned && ReferenceEquals(source.holdingOwner, actor.inventory.innerContainer),
                sourceHeldByOther = !source.Destroyed && !source.Spawned && ReferenceEquals(source.holdingOwner, other.inventory.innerContainer),
                taggedSource = Tags(actor).Contains(source), taggedIds = Tags(actor).Select(t => t.thingIDNumber).OrderBy(x => x).ToList(),
                actorInventory = actor.inventory.innerContainer.Select(t => Stack(t)).ToList(), otherInventory = other.inventory.innerContainer.Select(t => Stack(t)).ToList(),
                tagAges = Ages(actor), otherTagAges = Ages(other), keeps = Keeps(actor), otherKeeps = Keeps(other),
                otherTaggedIds = Tags(other).Select(t => t.thingIDNumber).OrderBy(x => x).ToList(),
                tagAge = (int)CompCall("FirstTaggedTick", source), kept = (int)CompCall("KeptCountOf", ThingDefOf.Steel) };
        }
        private object CompOf(Pawn p) => p.AllComps.Single(c => compType.IsInstanceOfType(c));
        private List<InventoryQuantityRule> Rules()
        {
            var rules = (IDictionary)settings.GetType().GetMethod("GetItemRulesCopy", Flags).Invoke(settings, null);
            return rules.Keys.Cast<string>().OrderBy(k => k, StringComparer.Ordinal).Select(k => new InventoryQuantityRule {
                def = k, mode = ruleType.GetField("mode").GetValue(rules[k]).ToString(), amount = (int)ruleType.GetField("amount").GetValue(rules[k]) }).ToList();
        }
        private List<InventoryQuantityAge> Ages(Pawn p) => ((Dictionary<Thing, int>)compType.GetField("taggedTick", Flags).GetValue(CompOf(p))
            ?? new Dictionary<Thing, int>()).OrderBy(k => k.Key.thingIDNumber).Select(k => new InventoryQuantityAge { id = k.Key.thingIDNumber, tick = k.Value }).ToList();
        private List<InventoryQuantityKeep> Keeps(Pawn p) => ((Dictionary<ThingDef, int>)compType.GetMethod("PeekKeptCounts", Flags).Invoke(CompOf(p), null))
            .OrderBy(k => k.Key.defName, StringComparer.Ordinal).Select(k => new InventoryQuantityKeep { def = k.Key.defName, count = k.Value }).ToList();
        private List<InventoryQuantityBlocker> CaptureBlockers(List<IntVec3> cells) => cells.Select(cell => new InventoryQuantityBlocker {
            cell = cell.ToString(), maximum = cell.GetMaxItemsAllowedInCell(map), items = map.thingGrid.ThingsListAt(cell)
                .Where(t => t.def.category == ThingCategory.Item).Select(t => Stack(t)).ToList() }).ToList();
        private static bool SameStacks(List<InventoryQuantityStack> a, List<InventoryQuantityStack> b) => a.Count == b.Count
            && a.All(x => b.Any(y => x.id == y.id && x.count == y.count && x.def == y.def && x.limit == y.limit && x.cell == y.cell));
        private static bool SameAges(List<InventoryQuantityAge> a, List<InventoryQuantityAge> b) => a.Count == b.Count
            && a.All(x => b.Any(y => x.id == y.id && x.tick == y.tick));
        private static bool SameKeeps(List<InventoryQuantityKeep> a, List<InventoryQuantityKeep> b) => a.Count == b.Count
            && a.All(x => b.Any(y => x.def == y.def && x.count == y.count));
        private static InventoryQuantityStack Stack(Thing t) => new InventoryQuantityStack {
            id = t.thingIDNumber, count = t.stackCount, cell = t.Position.ToString(), def = t.def.defName, limit = t.def.stackLimit };
        private void ClearCargo()
        {
            foreach (var p in new[] { actor, other }.Where(p => p != null))
                foreach (var tagged in Tags(p).ToList()) compType.GetMethod("Deregister", Flags).Invoke(CompOf(p), new object[] { tagged });
            actor?.inventory.innerContainer.ClearAndDestroyContents(); other?.inventory.innerContainer.ClearAndDestroyContents();
            foreach (var t in area.Cells.SelectMany(c => map.thingGrid.ThingsListAt(c)).Where(t => t.def.category == ThingCategory.Item).Distinct().ToList()) t.Destroy(DestroyMode.Vanish);
            foreach (var t in owned.Where(t => !(t is Pawn) && !t.Destroyed).ToList()) t.Destroy(DestroyMode.Vanish);
            foreach (var p in new[] { actor, other }.Where(p => p != null))
            {
                compType.GetMethod("GetHashSet", Flags).Invoke(CompOf(p), null);
                compType.GetMethod("SetKeptCount", Flags).Invoke(CompOf(p), new object[] { ThingDefOf.Steel, 0 });
                Require("empty-scene-" + p.thingIDNumber, p.inventory.innerContainer.Count == 0 && Tags(p).Count == 0 && Ages(p).Count == 0 && Keeps(p).Count == 0,
                    "Old tags deregistered before empty-owner heal; no destroyed-tag carry-over enters the next scene.");
            }
            if (settings != null && savedRules != null) settings.GetType().GetMethod("SetItemRules", Flags).Invoke(settings, new[] { Activator.CreateInstance(savedRules.GetType()) });
            foreach (string type in new[] { "PawnMassCache", "TrackedMassCache", "InventoryShare", "OrganicInventoryShare", "CarriedHaulShare", "SurplusCache" }) HdCall(type, "Clear");
        }
        private void Cleanup()
        {
            try
            {
                try { ClearCargo(); }
                catch (Exception error) { Check("clear-cargo-cleanup", false, Unwrap(error).ToString()); }
                foreach (var thing in owned.Where(t => !t.Destroyed).ToList())
                    try { thing.Destroy(DestroyMode.Vanish); }
                    catch (Exception error) { Check("owned-cleanup-" + thing.thingIDNumber, false, Unwrap(error).ToString()); }
                Check("all-owned-objects-retired", owned.All(t => t.Destroyed), "Every registered pawn/source/sentinel/blocker/native destination retired, including displaced objects; count=" + owned.Count);
                Check("test-cargo-retired", area.Cells.SelectMany(c => map.thingGrid.ThingsListAt(c)).All(t => t.def.category != ThingCategory.Item), "No fixture item survives cleanup.");
            }
            finally
            {
                if (settings != null && savedRules != null)
                {
                    settings.GetType().GetMethod("SetItemRules", Flags).Invoke(settings, new[] { savedRules });
                    HarnessSession.Event("quantity-rules-restored", Json.Stringify(Rules()));
                    Check("original-rules-restored", Json.Stringify(originalRules) == Json.Stringify(Rules()), "Original rule mode/amount set restored through native settings API; no WriteSettings call.");
                }
            }
        }
        private void Check(string id, bool passed, string detail) { if (!passed) failed = true; HarnessSession.Check("quantity-" + id, passed, detail); }
        private void Require(string id, bool passed, string detail) { Check(id, passed, detail); if (!passed) throw new InvalidOperationException(id + ": " + detail); }
        private static bool Close(float a, float b) => Math.Abs(a - b) < 0.001f;
        private static Exception Unwrap(Exception e) => e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
        private static void BeforeNative(ThingOwner __instance, Thing thing, IntVec3 dropLoc, Map map, ThingPlaceMode mode, int count, ref Action<Thing, int> placedAction)
        {
            if (active == null || !ReferenceEquals(thing, observedSource)) return;
            var row = active; row.nativeCalls++; row.nativeCount = count; row.nativeMode = mode.ToString(); row.nativeCell = dropLoc.ToString();
            row.nativeMap = map.uniqueID; row.nativeOwnerMatches = ReferenceEquals(thing.holdingOwner, __instance);
            var fixture = observingFixture;
            row.nativeCurrentInventory = fixture != null && ReferenceEquals(__instance, fixture.actor.inventory.innerContainer);
            var original = placedAction;
            row.originalCallbackPresent = original != null;
            placedAction = (destination, delta) => {
                row.placements.Add(new InventoryQuantityPlacement { id = destination?.thingIDNumber ?? -1, delta = delta,
                    resultingCount = destination?.stackCount ?? -1, spawned = destination?.Spawned ?? false, destroyed = destination?.Destroyed ?? true,
                    mapId = destination?.Map?.uniqueID ?? -1, def = destination?.def?.defName, cell = destination?.Position.ToString() });
                if (destination != null && fixture != null && !fixture.owned.Any(t => ReferenceEquals(t, destination))) fixture.owned.Add(destination);
                original?.Invoke(destination, delta);
                if (original != null) row.callbackForwarded++;
            };
        }
        private static void AfterNative(Thing thing, bool __result)
        {
            if (active == null || !ReferenceEquals(thing, observedSource)) return;
            active.nativeReturned = true; active.nativeResult = __result;
        }
    }

    [DataContract] internal sealed class InventoryQuantityScene
    {
        [DataMember] public string id, nativeMode, nativeCell;
        [DataMember] public int requested, expectedMoved, expectedCount, commandThingId, commandMap, policy, mapId, pawnId, sourceId, tickBefore, tickAfter, blockedCells, nativeCalls, nativeCount, nativeMap, callbackForwarded;
        [DataMember] public bool rejected, agedTagSeeded, nativeReturned, nativeResult, complete, nativeOwnerMatches, nativeCurrentInventory, originalCallbackPresent;
        [DataMember] public InventoryQuantityState prepared, beforeWarm, before, afterSettlement, afterReaders;
        [DataMember] public InventoryQuantityCaches cachesBefore, cachesAfter;
        [DataMember] public List<InventoryQuantityPlacement> placements = new List<InventoryQuantityPlacement>();
        [DataMember] public List<InventoryQuantityBlocker> blockers = new List<InventoryQuantityBlocker>();
        [DataMember] public List<InventoryQuantityBlocker> blockersAfter = new List<InventoryQuantityBlocker>();
        [DataMember] public List<InventoryQuantityRule> rulesBefore, rulesAfter;
    }
    [DataContract] internal sealed class InventoryQuantityState
    {
        [DataMember] public int actorSteel, otherSteel, inventoryTotal, floorTotal, totalSteel, sourceCount, kept, tagAge;
        [DataMember] public bool sourceDestroyed, sourceSpawned, sourceHeldByActor, sourceHeldByOther, taggedSource;
        [DataMember] public List<InventoryQuantityStack> floor, actorInventory, otherInventory;
        [DataMember] public List<int> taggedIds, otherTaggedIds;
        [DataMember] public List<InventoryQuantityAge> tagAges, otherTagAges;
        [DataMember] public List<InventoryQuantityKeep> keeps, otherKeeps;
    }
    [DataContract] internal sealed class InventoryQuantityStack { [DataMember] public int id, count, limit; [DataMember] public string cell, def; }
    [DataContract] internal sealed class InventoryQuantityBlocker { [DataMember] public string cell; [DataMember] public int maximum; [DataMember] public List<InventoryQuantityStack> items; }
    [DataContract] internal sealed class InventoryQuantityCaches { [DataMember] public float mass, trackedMass; [DataMember] public int shared, organic; [DataMember] public bool surplus, ruledSurplus; }
    [DataContract] internal sealed class InventoryQuantityAge { [DataMember] public int id, tick; }
    [DataContract] internal sealed class InventoryQuantityKeep { [DataMember] public string def; [DataMember] public int count; }
    [DataContract] internal sealed class InventoryQuantityRule { [DataMember] public string def, mode; [DataMember] public int amount; }
    [DataContract] internal sealed class InventoryQuantityPlacement
    {
        [DataMember] public int id, delta, resultingCount, mapId; [DataMember] public bool spawned, destroyed; [DataMember] public string def, cell;
    }
}
