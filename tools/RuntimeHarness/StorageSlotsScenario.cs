using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    // CAP01 with CAP02/CAP03 controls. Real adapter inputs, not executed hauling jobs.
    // Bindings intentionally describe the published scalar interface. A replacement allocator needs
    // a reviewed fixture adapter; missing methods must never be interpreted as corrected behavior.
    internal sealed class StorageSlotsScenario
    {
        private readonly Map map;
        private readonly StorageSlotsResult result;
        private readonly List<Pawn> actors = new List<Pawn>();
        private readonly List<ClaimRecord> claims = new List<ClaimRecord>();
        private readonly List<ShelfScene> scenes = new List<ShelfScene>();
        private Type budgetType, dictionaryType;
        private MethodInfo measure, free, resolve, price, available, consume, commit, retire, moving, others;
        private int legacyOccupiedMeasurements;

        private StorageSlotsScenario(Map map, string expectation)
        {
            this.map = map;
            result = new StorageSlotsResult
            {
                caseId = "CAP01", expectedBehavior = expectation,
                adapterContract = "published-scalar-budget-v1",
                scope = "actual vanilla ShelfSmall, production plan budget and live claim gates; no driver execution or reporter-causation claim",
                startedTick = Find.TickManager.TicksGame, finishedTick = -1, status = "inconclusive",
                assertions = new List<StorageSlotsAssertion>(), observations = new List<StorageSlotsObservation>(),
                measurements = new List<StorageSlotsMeasurement>(), shelves = new List<StorageSlotsPhysical>()
            };
        }

        internal static StorageSlotsResult Run(Map map, string expectedBehavior)
        {
            var test = new StorageSlotsScenario(map, expectedBehavior);
            try
            {
                test.Setup();
                test.OccupiedPlan(ThingDefOf.Silver, ThingDefOf.Cloth, "silver-then-cloth");
                test.OccupiedPlan(ThingDefOf.Cloth, ThingDefOf.Silver, "cloth-then-silver");
                test.OccupiedClaims(ThingDefOf.Silver, ThingDefOf.Cloth, "silver-claim-then-cloth");
                test.OccupiedClaims(ThingDefOf.Cloth, ThingDefOf.Silver, "cloth-claim-then-silver");
                test.PartialControl();
                test.FullControl();
                test.EmptyControl();
                foreach (var scene in test.scenes)
                {
                    var physical = test.Physical(scene);
                    test.Require(scene.id + "-physical-unchanged", SameItems(scene.initial, physical),
                        "Budget/claim calls did not deposit cargo or change shelf items: " + Json.Stringify(physical));
                }
                test.Require("same-tick", Find.TickManager.TicksGame == test.result.startedTick, "Adapter fixture advances no simulation ticks.");
                test.Require("only-fixture-actors", map.mapPawns.AllPawnsSpawned.Count == test.actors.Count,
                    "spawned=" + map.mapPawns.AllPawnsSpawned.Count + "; fixture=" + test.actors.Count);
                test.result.fixtureValid = true;
                test.result.requestedBehaviorSatisfied = test.result.observations.All(x => x.correctedMatched);
                test.result.publishedOccupiedSplitObserved = test.legacyOccupiedMeasurements >= 8;
                test.result.expectationMatched = expectedBehavior == "satisfied" ? test.result.requestedBehaviorSatisfied
                    : test.result.observations.All(x => x.baselineMatched)
                        && test.result.publishedOccupiedSplitObserved && !test.result.requestedBehaviorSatisfied;
                test.result.status = !test.result.expectationMatched ? "failed"
                    : test.result.requestedBehaviorSatisfied ? "passed" : "behavior-gap-observed";
            }
            catch (Exception error)
            {
                var cause = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
                test.result.error = cause.ToString();
                test.Record("fixture", "exception", false, cause.ToString());
                test.Inconclusive();
            }
            finally
            {
                foreach (var claim in test.claims)
                {
                    try { test.retire.Invoke(null, new object[] { claim.pawn, claim.group, claim.def, 0, "runtime-slot-fixture-retire" }); }
                    catch (Exception error)
                    {
                        test.Record("fixture", "claim-retirement", false, error.ToString());
                        test.Inconclusive();
                    }
                }
                test.result.finishedTick = Find.TickManager.TicksGame;
                HarnessSession.Event("storage-slots-result", Json.Stringify(test.result));
            }
            return test.result;
        }

        private void Inconclusive()
        {
            result.fixtureValid = false;
            result.requestedBehaviorSatisfied = false;
            result.expectationMatched = false;
            result.status = "inconclusive";
        }

        private void Setup()
        {
            Require("expectation", result.expectedBehavior == "baseline-gap" || result.expectedBehavior == "satisfied", result.expectedBehavior);
            Require("map", map != null && map.IsPlayerHome, "Actual isolated player-home map required.");
            var adapter = FindType("HaulersDream.StorageCommitments");
            var bulk = FindType("HaulersDream.BulkHaul");
            budgetType = FindType("HaulersDream.Core.StorageGroupBudget");
            dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(ISlotGroup), budgetType);
            measure = Method(adapter, "MeasureGroup", typeof(Pawn), typeof(Thing), typeof(ISlotGroup), typeof(Map));
            free = Method(adapter, "FreeUnitsFor", typeof(Pawn), typeof(ISlotGroup), typeof(ThingDef), typeof(Thing), typeof(bool).MakeByRefType());
            resolve = Method(bulk, "ResolveGroupBudget", typeof(Pawn), typeof(Thing), typeof(IntVec3), typeof(Map), dictionaryType, typeof(bool).MakeByRefType());
            price = Method(bulk, "PriceDefInto", budgetType, typeof(Pawn), typeof(Thing), typeof(ISlotGroup), typeof(Map));
            available = budgetType.GetMethod("AvailableFor", new[] { typeof(object) });
            consume = budgetType.GetMethod("Consume", new[] { typeof(object), typeof(int) });
            Require("budget-methods", available != null && consume != null, "Actual StorageGroupBudget.AvailableFor and Consume");
            commit = Method(adapter, "TryCommit", typeof(Pawn), typeof(ISlotGroup), typeof(ThingDef), typeof(int), typeof(string));
            retire = Method(adapter, "Commit", typeof(Pawn), typeof(ISlotGroup), typeof(ThingDef), typeof(int), typeof(string));
            moving = Method(adapter, "UnitsMovingOf", typeof(Pawn), typeof(ThingDef));
            others = Method(adapter, "ClaimedByOthersFor", typeof(Pawn), typeof(ISlotGroup), typeof(ThingDef));
            result.productionAssembly = adapter.Assembly.FullName;
            result.productionModuleId = adapter.Module.ModuleVersionId.ToString();
            result.coreAssembly = budgetType.Assembly.FullName;
            result.coreModuleId = budgetType.Module.ModuleVersionId.ToString();
            HarnessSession.Event("storage-slots-bind", "production=" + result.productionAssembly + "; MVID=" + result.productionModuleId
                + "; Core=" + result.coreAssembly + "; MVID=" + result.coreModuleId
                + "; Resolve token=" + resolve.MetadataToken + "; Price token=" + price.MetadataToken);
            var hd = FindType("HaulersDream.HaulersDreamMod");
            Require("production-bindings-same-assembly", adapter.Assembly == bulk.Assembly && bulk.Assembly == hd.Assembly
                && adapter.Assembly.GetName().Name == "HaulersDream" && budgetType.Assembly.GetName().Name == "HaulersDream.Core",
                "StorageCommitments, BulkHaul and HD settings share the exact loaded HD assembly; budget is the exact loaded Core type.");
            RequirePatch(typeof(StoreUtility).GetMethod("IsGoodStoreCell"), "HaulersDream.Patch_IsGoodStoreCell_HonourCommitments", false, "storage-gate", hd.Assembly);
            RequirePatch(typeof(HaulAIUtility).GetMethod("HaulToCellStorageJob"), "HaulersDream.Patch_HaulToCellStorageJob_ClampToCommitments", false, "storage-counter", hd.Assembly);
            RequirePatch(typeof(JobDriver_HaulToCell).GetMethod("TryMakePreToilReservations"), "HaulersDream.Patch_JobDriver_HaulToCell_NoCellReservation", true, "storage-reservation", hd.Assembly);
            var settings = hd.GetProperty("Settings", BindingFlags.Static | BindingFlags.Public)?.GetValue(null, null);
            Require("settings", settings != null, "Actual fresh isolated HD settings; no reporter configuration inferred.");
            foreach (var name in new[] { "masterEnabled", "haulToStack" })
            {
                var field = settings.GetType().GetField(name);
                Require("setting-" + name, field != null && field.FieldType == typeof(bool), name);
                field.SetValue(settings, true);
            }
            Require("gates-active", (bool)Method(adapter, "GatesVanillaStorage", typeof(Map)).Invoke(null, new object[] { map }),
                "Home-map gate and actual storage seam active.");
            foreach (var def in new[] { ThingDefOf.Silver, ThingDefOf.Cloth, ThingDefOf.Steel, ThingDefOf.WoodLog })
                Require("stack-limit-" + def.defName, def.category == ThingCategory.Item && def.stackLimit > 7,
                    def.defName + " stackLimit=" + def.stackLimit);
            var initial = map.listerThings.AllThings.Where(x => x is Pawn || x is Skyfaller || x is ActiveTransporter).ToList();
            foreach (var thing in initial) if (thing.Spawned) thing.DeSpawn();
            var rect = new CellRect(map.Center.x - 22, map.Center.z - 12, 45, 25);
            Require("scene-bounds", rect.Cells.All(x => x.InBounds(map)), rect.ToString());
            Require("no-existing-zones", rect.Cells.All(x => map.zoneManager.ZoneAt(x) == null), "No existing zones are overwritten.");
            GenDebug.ClearArea(rect, map);
            foreach (var cell in rect.Cells)
            {
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                map.roofGrid.SetRoof(cell, null);
                map.areaManager.Home[cell] = true;
                map.fogGrid.Unfog(cell);
            }
            HarnessSession.Event("storage-slots-isolation", "Despawned generated actors/landing objects=" + initial.Count
                + "; clear outdoor rectangle; fresh unlinked shelves, actors and budgets per control; no executed jobs.");
        }

        private void OccupiedPlan(ThingDef firstDef, ThingDef secondDef, string id)
        {
            var scene = Scene("plan-" + id, 2);
            var first = Floor(scene, firstDef, firstDef.stackLimit, 0);
            var second = Floor(scene, secondDef, secondDef.stackLimit, 1);
            Measure(scene, first, true);
            Measure(scene, second, true);
            var budgets = Activator.CreateInstance(dictionaryType);
            var firstBudget = Budget(scene, first, budgets);
            Observe(scene.id + "-first-allowance", Available(firstBudget, firstDef), firstDef.stackLimit, firstDef.stackLimit,
                "One physical vacant slot; first incoming " + firstDef.defName);
            Spend(scene, firstBudget, firstDef, firstDef.stackLimit);
            Observe(scene.id + "-spent-first-tail", Available(firstBudget, firstDef), 0, 0, "Consumed one full first-def stack in the real plan budget.");
            var repricedFirst = Budget(scene, first, budgets);
            Require(scene.id + "-spent-budget-survives-reprice", ReferenceEquals(firstBudget, repricedFirst)
                && Available(repricedFirst, firstDef) == 0,
                "Re-resolve and explicitly re-price the already consumed definition; its spent allowance must stay zero.");
            var secondBudget = Budget(scene, second, budgets);
            Require(scene.id + "-same-budget", ReferenceEquals(firstBudget, secondBudget), "Both definitions resolve to the same actual group budget instance.");
            Observe(scene.id + "-second-after-spend", Available(secondBudget, secondDef), secondDef.stackLimit, 0,
                "Physical vacancy stayed one; the first virtual allocation has spent it. No physical deposit occurred.");
        }

        private void OccupiedClaims(ThingDef firstDef, ThingDef secondDef, string id)
        {
            var scene = Scene("claim-" + id, 2);
            var carrier = PawnAt(scene.cell + new IntVec3(2, 0, 0));
            var first = Held(carrier, firstDef, firstDef.stackLimit);
            var second = Floor(scene, secondDef, secondDef.stackLimit, 0);
            Measure(scene, first, true);
            Measure(scene, second, true);
            Observe(scene.id + "-before-claim", Free(scene, second), secondDef.stackLimit, secondDef.stackLimit,
                "No live claim yet; second pawn's candidate is spawned floor cargo.");
            Claim(scene, carrier, firstDef, firstDef.stackLimit);
            int liveOthers = (int)others.Invoke(null, new object[] { scene.asker, scene.group, firstDef });
            Require(scene.id + "-foreign-claim-visible", liveOthers == firstDef.stackLimit,
                "ClaimedByOthersFor(" + firstDef.defName + ")=" + liveOthers + "; asker=" + scene.asker.ThingID + "; carrier=" + carrier.ThingID);
            Observe(scene.id + "-after-foreign-claim", Free(scene, second), secondDef.stackLimit, 0,
                "The foreign live cargo owns the only vacant stack slot, even though its def differs from this candidate.");
            Observe(scene.id + "-patched-store-gate", Gate(scene, second) ? 1 : 0, 1, 0,
                "Actual StoreUtility.IsGoodStoreCell after the live claim; not a manually supplied Core delivery flag.");
        }

        private void PartialControl()
        {
            var scene = Scene("genuine-silver-top-up", 3, silverDeficit: 7);
            var silver = Floor(scene, ThingDefOf.Silver, 7, 0);
            var cloth = Floor(scene, ThingDefOf.Cloth, ThingDefOf.Cloth.stackLimit, 1);
            var silverMeasure = Measure(scene, silver, false);
            Require(scene.id + "-observed-deficit", silverMeasure.partialSpace == 7 && silverMeasure.emptyCells == 0,
                "A full cell has an actual compatible silver deficit of seven.");
            var budgets = Activator.CreateInstance(dictionaryType);
            var budget = Budget(scene, silver, budgets);
            Observe(scene.id + "-plan-before", Available(budget, silver.def), 7, 7, "Preserve real existing-stack top-up room.");
            Spend(scene, budget, silver.def, 7);
            Observe(scene.id + "-plan-after", Available(budget, silver.def), 0, 0, "Seven virtual units spent, not deposited.");
            var repricedSilver = Budget(scene, silver, budgets);
            Require(scene.id + "-spent-budget-survives-reprice", ReferenceEquals(budget, repricedSilver)
                && Available(repricedSilver, silver.def) == 0,
                "Re-resolve and explicitly re-price after spending the genuine seven-unit top-up; no reset is allowed.");
            Observe(scene.id + "-other-def-plan", Available(Budget(scene, cloth, budgets), cloth.def), 0, 0, "No new slot exists for cloth.");
            Observe(scene.id + "-free-before-claim", Free(scene, silver), 7, 7, "Real adapter preserves the genuine deficit.");
            Observe(scene.id + "-silver-gate", Gate(scene, silver) ? 1 : 0, 1, 1, "Compatible partial succeeds physically.");
            Observe(scene.id + "-cloth-gate", Gate(scene, cloth) ? 1 : 0, 0, 0, "Incompatible full-cell input fails physically.");
            var carrier = PawnAt(scene.cell + new IntVec3(2, 0, 0));
            Held(carrier, ThingDefOf.Silver, 7);
            Claim(scene, carrier, ThingDefOf.Silver, 7);
            Observe(scene.id + "-free-after-claim", Free(scene, silver), 0, 0, "Another pawn's live silver claim spends the seven top-up units.");
            Observe(scene.id + "-gate-after-claim", Gate(scene, silver) ? 1 : 0, 0, 0, "Real gate honors the competing same-def claim.");
        }

        private void FullControl()
        {
            var scene = Scene("full-incompatible", 3);
            var budgets = Activator.CreateInstance(dictionaryType);
            foreach (var def in new[] { ThingDefOf.Silver, ThingDefOf.Cloth })
            {
                var subject = Floor(scene, def, def.stackLimit, def == ThingDefOf.Silver ? 0 : 1);
                Measure(scene, subject, false);
                Observe(scene.id + "-plan-" + def.defName, Available(Budget(scene, subject, budgets), def), 0, 0, "All three shelf slots contain full incompatible fillers.");
                Observe(scene.id + "-free-" + def.defName, Free(scene, subject), 0, 0, "Full incompatible cell has no certified room.");
                Observe(scene.id + "-gate-" + def.defName, Gate(scene, subject) ? 1 : 0, 0, 0, "Actual full-cell StoreUtility control.");
            }
        }

        private void EmptyControl()
        {
            var scene = Scene("empty-three-slot-mixed-plan", 0);
            var budgets = Activator.CreateInstance(dictionaryType);
            var defs = new[] { ThingDefOf.Silver, ThingDefOf.Cloth, ThingDefOf.WoodLog, ThingDefOf.Steel };
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                var subject = Floor(scene, def, def.stackLimit, i);
                Measure(scene, subject, false);
                var budget = Budget(scene, subject, budgets);
                int actual = Available(budget, def);
                int corrected = Math.Max(0, 3 - i) * def.stackLimit;
                int baseline = i == 0 ? 3 * def.stackLimit : 0;
                Observe(scene.id + "-allowance-" + i + "-" + def.defName, actual, baseline, corrected,
                    "Request one complete stack; new incompatible definitions should consume one slot each, not the entire cell.");
                // Follow the actual plan's admission answer. Never spend a rejected request to fabricate progress.
                if (actual >= def.stackLimit && i < 3) Spend(scene, budget, def, def.stackLimit);
                else HarnessSession.Event("storage-slots-plan-rejected", scene.id + "; def=" + def.defName + "; actualAvailable=" + actual + "; no Consume call");
            }
        }

        private ShelfScene Scene(string id, int occupied, int silverDeficit = 0)
        {
            int index = scenes.Count;
            var cell = map.Center + new IntVec3(-18 + index % 4 * 10, 0, -8 + index / 4 * 10);
            var def = DefDatabase<ThingDef>.GetNamed("ShelfSmall");
            var shelf = (Building_Storage)GenSpawn.Spawn(ThingMaker.MakeThing(def, ThingDefOf.WoodLog), cell, map);
            shelf.SetFaction(Faction.OfPlayer);
            shelf.GetStoreSettings().filter.SetDisallowAll();
            foreach (var accepted in new[] { ThingDefOf.Silver, ThingDefOf.Cloth, ThingDefOf.Steel,
                ThingDefOf.WoodLog, DefDatabase<ThingDef>.GetNamed("Uranium") })
                shelf.GetStoreSettings().filter.SetAllow(accepted, true);
            shelf.GetStoreSettings().Priority = StoragePriority.Critical;
            var group = shelf.GetSlotGroup();
            Require(id + "-vanilla-single-cell", shelf.GetType() == typeof(Building_Storage)
                && group.CellsList.Count == 1 && group.CellsList[0] == cell && group.StorageGroup == null
                && ReferenceEquals(map.haulDestinationManager.SlotGroupAt(cell), group), "Actual vanilla unlinked ShelfSmall; type=" + shelf.GetType().FullName);
            Require(id + "-three-slots", cell.GetMaxItemsAllowedInCell(map) == 3, "Patched actual GetMaxItemsAllowedInCell=" + cell.GetMaxItemsAllowedInCell(map));
            if (occupied > 0) Spawn(ThingDefOf.Steel, ThingDefOf.Steel.stackLimit, cell);
            if (occupied > 1) Spawn(ThingDefOf.WoodLog, ThingDefOf.WoodLog.stackLimit, cell);
            if (occupied > 2)
            {
                var third = silverDeficit > 0 ? ThingDefOf.Silver : DefDatabase<ThingDef>.GetNamed("Uranium");
                Spawn(third, third.stackLimit - silverDeficit, cell);
            }
            var scene = new ShelfScene { id = id, cell = cell, shelf = shelf, group = group, asker = PawnAt(cell + new IntVec3(-2, 0, 0)) };
            scene.initial = Physical(scene);
            Require(id + "-occupancy", scene.initial.itemCount == occupied && scene.initial.vacantSlots == 3 - occupied,
                Json.Stringify(scene.initial));
            scenes.Add(scene);
            result.shelves.Add(scene.initial);
            HarnessSession.Event("storage-slots-physical", Json.Stringify(scene.initial));
            return scene;
        }

        private Thing Spawn(ThingDef def, int count, IntVec3 cell)
        {
            var thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            return GenSpawn.Spawn(thing, cell, map);
        }

        private Thing Floor(ShelfScene scene, ThingDef def, int count, int offset)
        {
            var t = Spawn(def, count, scene.cell + new IntVec3(-3 + offset, 0, 3));
            Require(scene.id + "-floor-" + t.ThingID, t.Spawned && t.Position != scene.cell
                && t.Map == map && ReferenceEquals(t.ParentHolder, map)
                && t.stackCount == count && scene.shelf.GetStoreSettings().AllowedToAccept(t),
                t.ThingID + "; def=" + def.defName + "; count=" + t.stackCount + "; cell=" + t.Position);
            return t;
        }

        private Pawn PawnAt(IntVec3 cell)
        {
            Pawn pawn;
            Rand.PushState(10101 + actors.Count);
            try { pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); }
            finally { Rand.PopState(); }
            Require("actor-" + actors.Count, pawn != null && pawn.RaceProps.Humanlike && !pawn.Downed
                && pawn.inventory != null && pawn.carryTracker != null, "Generated ordinary disposable colonist.");
            pawn.inventory.innerContainer.ClearAndDestroyContents();
            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            pawn.workSettings.DisableAll();
            GenSpawn.Spawn(pawn, cell, map);
            actors.Add(pawn);
            return pawn;
        }

        private Thing Held(Pawn pawn, ThingDef def, int count)
        {
            var thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            Require("held-transfer-" + thing.ThingID, pawn.inventory.innerContainer.TryAdd(thing, false)
                && ReferenceEquals(thing.holdingOwner, pawn.inventory.innerContainer)
                && ReferenceEquals(thing.ParentHolder, pawn.inventory) && thing.stackCount == count,
                "Actual inventory ThingOwner; pawn=" + pawn.ThingID + "; thing=" + thing.ThingID + "; count=" + count);
            var comp = pawn.AllComps.SingleOrDefault(x => x.GetType().FullName == "HaulersDream.CompHauledToInventory");
            Require("tag-comp-" + pawn.ThingID, comp != null, "Actual injected HD tracking component.");
            var register = comp.GetType().GetMethod("RegisterHauledItem", new[] { typeof(Thing), typeof(int) });
            Require("tag-method", register != null, "RegisterHauledItem(Thing,int)");
            register.Invoke(comp, new object[] { thing, 0 });
            HarnessSession.Event("storage-slots-setup-cargo", "pawn=" + pawn.ThingID + "; thing=" + thing.ThingID
                + "; def=" + def.defName + "; count=" + count + "; ParentHolder=" + thing.ParentHolder.GetType().FullName + "; real tagged inventory; no executed pickup");
            return thing;
        }

        private void Claim(ShelfScene scene, Pawn carrier, ThingDef def, int count)
        {
            bool accepted = (bool)commit.Invoke(null, new object[] { carrier, scene.group, def, count, "runtime-slot-fixture" });
            if (accepted) claims.Add(new ClaimRecord { pawn = carrier, group = scene.group, def = def });
            Require(scene.id + "-claim", accepted, "Actual TryCommit recorded " + count + " " + def.defName + "; no claim inferred from a candidate.");
            int evidence = (int)moving.Invoke(null, new object[] { carrier, def });
            Require(scene.id + "-live-evidence", evidence == count, "Production UnitsMovingOf=" + evidence + "; committed=" + count);
            HarnessSession.Event("storage-slots-live-claim", "scene=" + scene.id + "; carrier=" + carrier.ThingID + "; def=" + def.defName + "; units=" + count + "; evidence=" + evidence);
        }

        private StorageSlotsMeasurement Measure(ShelfScene scene, Thing subject, bool occupiedGap)
        {
            var measured = measure.Invoke(null, new object[] { scene.asker, subject, scene.group, map });
            var row = new StorageSlotsMeasurement
            {
                scene = scene.id, subject = subject.ThingID, def = subject.def.defName,
                scalarCellSpace = scene.cell.GetItemStackSpaceLeftFor(map, subject.def),
                emptyCells = Field<int>(measured, "EmptyCells"), partialSpace = Field<int>(measured, "PartialSpace"),
                perCellCapacity = Field<int>(measured, "PerCellCapacity"), unbounded = Field<bool>(measured, "Unbounded"),
                truncated = Field<bool>(measured, "Truncated"), observedFloor = Field<int>(measured, "ObservedFloor")
            };
            Require(scene.id + "-complete-measure-" + subject.ThingID, !row.unbounded && !row.truncated,
                "Single real shelf measurement must be bounded and complete.");
            if (occupiedGap && row.emptyCells == 0 && row.partialSpace == subject.def.stackLimit
                && scene.initial.vacantSlots == 1 && scene.initial.items.All(x => x.def != subject.def.defName))
                legacyOccupiedMeasurements++;
            result.measurements.Add(row);
            HarnessSession.Event("storage-slots-measurement", Json.Stringify(row));
            return row;
        }

        private object Budget(ShelfScene scene, Thing subject, object budgets)
        {
            object[] args = { scene.asker, subject, scene.cell, map, budgets, false };
            var budget = resolve.Invoke(null, args);
            Require(scene.id + "-resolved-" + subject.ThingID, budget != null && !(bool)args[5],
                "Actual BulkHaul.ResolveGroupBudget returned a supported, allowed budget.");
            int before = Available(budget, subject.def);
            // Resolve invokes PriceDefInto internally. Invoke it explicitly a second time to verify that
            // production lazy pricing retains the same shared plan object and does not reset prior spends.
            price.Invoke(null, new object[] { budget, scene.asker, subject, scene.group, map });
            Require(scene.id + "-idempotent-price-" + subject.ThingID, Available(budget, subject.def) == before,
                "Repeated actual PriceDefInto leaves AvailableFor=" + before);
            HarnessSession.Event("storage-slots-plan-price", "scene=" + scene.id + "; subject=" + subject.ThingID
                + "; def=" + subject.def.defName + "; available=" + before + "; budget=" + BudgetState(budget));
            return budget;
        }

        private int Available(object budget, ThingDef def) => (int)available.Invoke(budget, new object[] { def });

        private void Spend(ShelfScene scene, object budget, ThingDef def, int count)
        {
            Require(scene.id + "-spend-authorized-" + def.defName, Available(budget, def) >= count && count > 0,
                "Only consume a positive quantity permitted by the actual plan budget.");
            consume.Invoke(budget, new object[] { def, count });
            HarnessSession.Event("storage-slots-plan-consume", "scene=" + scene.id + "; def=" + def.defName
                + "; virtualAllocatedUnits=" + count + "; remainingForDef=" + Available(budget, def) + "; budget=" + BudgetState(budget));
        }

        private int Free(ShelfScene scene, Thing subject)
        {
            object[] args = { scene.asker, scene.group, subject.def, subject, false };
            int answer = (int)free.Invoke(null, args);
            Require(scene.id + "-free-not-truncated-" + subject.ThingID, !(bool)args[4] && answer != int.MaxValue,
                "Actual FreeUnitsFor=" + answer + "; single cell must be measured.");
            return answer;
        }

        private bool Gate(ShelfScene scene, Thing subject) => StoreUtility.IsGoodStoreCell(scene.cell, map, subject, scene.asker, scene.asker.Faction);

        private string BudgetState(object budget)
        {
            var partial = Field<IDictionary>(budget, "partialByDef");
            var perCell = Field<IDictionary>(budget, "perCellByDef");
            var entries = new List<string>();
            foreach (DictionaryEntry pair in partial)
                entries.Add(((ThingDef)pair.Key).defName + ":partial=" + pair.Value + ",perCell=" + perCell[pair.Key]);
            entries.Sort(StringComparer.Ordinal);
            return "emptyCells=" + Field<int>(budget, "emptyCells") + "; " + string.Join("; ", entries);
        }

        private StorageSlotsPhysical Physical(ShelfScene scene) => new StorageSlotsPhysical
        {
            scene = scene.id, shelf = scene.shelf.ThingID, cell = scene.cell.ToString(),
            maxSlots = scene.cell.GetMaxItemsAllowedInCell(map), itemCount = scene.cell.GetItemCount(map),
            vacantSlots = Math.Max(0, scene.cell.GetMaxItemsAllowedInCell(map) - scene.cell.GetItemCount(map)),
            items = scene.cell.GetThingList(map).Where(x => x.def.category == ThingCategory.Item).OrderBy(x => x.thingIDNumber)
                .Select(x => new StorageSlotsItem { id = x.ThingID, def = x.def.defName, count = x.stackCount, stackLimit = x.def.stackLimit }).ToList()
        };

        private static bool SameItems(StorageSlotsPhysical before, StorageSlotsPhysical after) => before.maxSlots == after.maxSlots
            && before.itemCount == after.itemCount && before.items.Count == after.items.Count
            && before.items.Zip(after.items, (a, b) => a.id == b.id && a.def == b.def && a.count == b.count && a.stackLimit == b.stackLimit).All(x => x);

        private void Observe(string id, int actual, int baseline, int corrected, string detail)
        {
            var row = new StorageSlotsObservation { id = id, actual = actual, baseline = baseline, corrected = corrected,
                baselineMatched = actual == baseline, correctedMatched = actual == corrected, detail = detail };
            result.observations.Add(row);
            Record("behavior", id, row.correctedMatched, Json.Stringify(row));
            HarnessSession.Event("storage-slots-observation", Json.Stringify(row));
        }

        private T Field<T>(object instance, string name)
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Require("field-" + instance.GetType().Name + "-" + name, field != null, "Published interface field; replacement requires reviewed fixture binding.");
            return (T)field.GetValue(instance);
        }

        private Type FindType(string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType(name, false)).Where(x => x != null).ToList();
            Require("type-" + name, matches.Count == 1, name + "; actual loaded definitions=" + matches.Count);
            return matches[0];
        }

        private void RequirePatch(MethodBase target, string typeName, bool prefix, string id, Assembly production)
        {
            var type = FindType(typeName);
            var info = target == null ? null : Harmony.GetPatchInfo(target);
            var patches = prefix ? info?.Prefixes : info?.Postfixes;
            bool exact = type.Assembly == production && patches?.Count(p => p.owner == "giwaffed.HaulersDream"
                && p.PatchMethod.DeclaringType == type && p.PatchMethod.Name == (prefix ? "Prefix" : "Postfix")
                && p.PatchMethod.Module.Assembly == production) == 1;
            Require("patch-" + id, exact, "target=" + target + "; patch=" + typeName + "; assembly=" + production.FullName
                + "; path=" + production.Location + "; MVID=" + production.ManifestModule.ModuleVersionId);
        }

        private MethodInfo Method(Type type, string name, params Type[] signature)
        {
            var found = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, signature, null);
            Require("method-" + name, found != null, "Actual production " + type.FullName + "." + name);
            return found;
        }

        private void Require(string id, bool passed, string detail)
        {
            Record("fixture", id, passed, detail);
            if (!passed) throw new InvalidDataException("Storage slots fixture precondition failed: " + id + "; " + detail);
        }

        private void Record(string kind, string id, bool passed, string detail)
        {
            result.assertions.Add(new StorageSlotsAssertion { kind = kind, id = id, passed = passed, detail = detail });
            HarnessSession.Check("storage-slots-" + kind + "-" + id, passed, detail);
        }

        private sealed class ShelfScene
        {
            internal string id { get; set; }
            internal IntVec3 cell { get; set; }
            internal Building_Storage shelf { get; set; }
            internal SlotGroup group { get; set; }
            internal Pawn asker { get; set; }
            internal StorageSlotsPhysical initial { get; set; }
        }

        private sealed class ClaimRecord
        {
            internal Pawn pawn { get; set; }
            internal ISlotGroup group { get; set; }
            internal ThingDef def { get; set; }
        }
    }

    [DataContract]
    internal sealed class StorageSlotsResult
    {
        [DataMember] public string caseId { get; set; }
        [DataMember] public string expectedBehavior { get; set; }
        [DataMember] public string adapterContract { get; set; }
        [DataMember] public string scope { get; set; }
        [DataMember] public int startedTick { get; set; }
        [DataMember] public int finishedTick { get; set; }
        [DataMember] public string productionAssembly { get; set; }
        [DataMember] public string productionModuleId { get; set; }
        [DataMember] public string coreAssembly { get; set; }
        [DataMember] public string coreModuleId { get; set; }
        [DataMember] public bool fixtureValid { get; set; }
        [DataMember] public bool requestedBehaviorSatisfied { get; set; }
        [DataMember] public bool expectationMatched { get; set; }
        [DataMember] public bool publishedOccupiedSplitObserved { get; set; }
        [DataMember] public string status { get; set; }
        [DataMember] public string error { get; set; }
        [DataMember] public List<StorageSlotsAssertion> assertions { get; set; }
        [DataMember] public List<StorageSlotsObservation> observations { get; set; }
        [DataMember] public List<StorageSlotsMeasurement> measurements { get; set; }
        [DataMember] public List<StorageSlotsPhysical> shelves { get; set; }
    }

    [DataContract]
    internal sealed class StorageSlotsAssertion
    {
        [DataMember] public string kind { get; set; }
        [DataMember] public string id { get; set; }
        [DataMember] public bool passed { get; set; }
        [DataMember] public string detail { get; set; }
    }

    [DataContract]
    internal sealed class StorageSlotsObservation
    {
        [DataMember] public string id { get; set; }
        [DataMember] public int actual { get; set; }
        [DataMember] public int baseline { get; set; }
        [DataMember] public int corrected { get; set; }
        [DataMember] public bool baselineMatched { get; set; }
        [DataMember] public bool correctedMatched { get; set; }
        [DataMember] public string detail { get; set; }
    }

    [DataContract]
    internal sealed class StorageSlotsMeasurement
    {
        [DataMember] public string scene { get; set; }
        [DataMember] public string subject { get; set; }
        [DataMember] public string def { get; set; }
        [DataMember] public int scalarCellSpace { get; set; }
        [DataMember] public int emptyCells { get; set; }
        [DataMember] public int partialSpace { get; set; }
        [DataMember] public int perCellCapacity { get; set; }
        [DataMember] public bool unbounded { get; set; }
        [DataMember] public bool truncated { get; set; }
        [DataMember] public int observedFloor { get; set; }
    }

    [DataContract]
    internal sealed class StorageSlotsPhysical
    {
        [DataMember] public string scene { get; set; }
        [DataMember] public string shelf { get; set; }
        [DataMember] public string cell { get; set; }
        [DataMember] public int maxSlots { get; set; }
        [DataMember] public int itemCount { get; set; }
        [DataMember] public int vacantSlots { get; set; }
        [DataMember] public List<StorageSlotsItem> items { get; set; }
    }

    [DataContract]
    internal sealed class StorageSlotsItem
    {
        [DataMember] public string id { get; set; }
        [DataMember] public string def { get; set; }
        [DataMember] public int count { get; set; }
        [DataMember] public int stackLimit { get; set; }
    }
}
