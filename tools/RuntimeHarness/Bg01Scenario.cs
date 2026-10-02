using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    // A disposable scene and observations, not a replacement workgiver or recipe implementation.
    internal sealed class Bg01Scenario : IDisposable
    {
        private readonly Map map;
        private readonly string expectedBehavior;
        private readonly string caseId, recipeDefName;
        private readonly int ingredientUnitsPerDef, expectedMealUnits, maximumTicks;
        private readonly int startedTick;
        private readonly List<Filth> seededFilth = new List<Filth>();
        private readonly HashSet<int> executedNativeJobs = new HashSet<int>();
        private readonly HashSet<int> seenJobs = new HashSet<int>();
        private readonly HashSet<int> gatherJobs = new HashSet<int>();
        private readonly Dictionary<int, CleanEvent> filthRemovals = new Dictionary<int, CleanEvent>();
        private readonly HashSet<int> cleaningIncrements = new HashSet<int>();
        private readonly List<EndEvent> nativeEnds = new List<EndEvent>();
        private readonly HashSet<int> consumedIngredientIds = new HashSet<int>();
        private readonly List<IngredientConsumptionRecord> consumptionRecords = new List<IngredientConsumptionRecord>();
        private Bg01Observers observers;
        private Assembly commonSenseAssembly;
        private static readonly PropertyInfo CurrentToil = typeof(JobDriver).GetProperty("CurToil", BindingFlags.NonPublic | BindingFlags.Instance);
        private Pawn cook;
        private Building_WorkTable stove;
        private Bill_Production bill;
        private ThingDef riceDef, potatoDef, mealDef;
        private bool active, observing, fullInventorySweep, returnedWithIncompleteIngredients;
        private bool chosenByDoBillWorkgiver, cleaningDuringBill, forcedJobObserved;
        private bool allSeededFilthCleanedBeforeProduct;
        private List<IntVec3> roofCells;
        private bool roofStayedIntact = true;
        private bool leftBenchWithIngredients;
        private int firstProductTick = -1;
        private float cleanedAtStart;
        private string previousSnapshot;
        private int lastPositionEventTick;
        private int snapshotCount;
        private int maxInventoryRice, maxInventoryPotato;
        private int firstReturnTick = -1, stableSinceTick = -1, lastSettledTick = -1;
        private int settledPostProductTicks, settledBoundaries, maxSettledMeals;
        private int acquiredRice, acquiredPotato, producedUnits, producedCalls, eventSequence;
        private int consumedRice, consumedPotato;
        private string initialRiceId, initialPotatoId;
        private bool consumptionAttributionValid = true, acquisitionConserved = true;
        private bool secondGatherExcursion, storageDetour, batchJobObserved, observerHealthy = true;
        private bool cleaningAttributionValid = true, productContextValid = true, productCountsStayedStable = true;

        internal string CaseId => caseId;
        internal bool ObserveIngredientConsumption => caseId == "BG02";

        // Preserve the existing BG01 constructor and its five-plus-five/one-meal contract.
        internal Bg01Scenario(Map map, string expectedBehavior) : this(map, expectedBehavior, "BG01") { }

        internal Bg01Scenario(Map map, string expectedBehavior, string caseId)
        {
            if (caseId != "BG01" && caseId != "BG02")
                throw new ArgumentException("Supported ordinary-meal fixtures are BG01 and BG02.", nameof(caseId));
            this.map = map;
            this.expectedBehavior = expectedBehavior;
            this.caseId = caseId;
            recipeDefName = caseId == "BG02" ? "CookMealSimpleBulk" : "CookMealSimple";
            ingredientUnitsPerDef = caseId == "BG02" ? 20 : 5;
            expectedMealUnits = caseId == "BG02" ? 4 : 1;
            maximumTicks = caseId == "BG02" ? 12000 : 9000;
            startedTick = Find.TickManager.TicksGame;
            Setup();
            active = true;
            try
            {
                observers = new Bg01Observers(this, cook);
                cook.inventory.innerContainer.OnContentsChanged += InventoryChanged;
                cook.carryTracker.GetDirectlyHeldThings().OnContentsChanged += CarryChanged;
                Observe("fixture-start");
            }
            catch { Dispose(); throw; }
        }

        private static void Require(string id, bool passed, string observation)
        {
            HarnessSession.Check("fixture-" + id, passed, observation);
            if (!passed) throw new InvalidDataException("Fixture precondition failed: " + id + "; " + observation);
        }

        private void Setup()
        {
            Require("known-expectation", expectedBehavior == "baseline-gap" || expectedBehavior == "satisfied", expectedBehavior);
            var hdType = RequireType("HaulersDream.HaulersDreamMod");
            var hdSettings = hdType.GetProperty("Settings", BindingFlags.Static | BindingFlags.Public)?.GetValue(null, null);
            Require("hd-settings", hdSettings != null, "HaulersDreamMod.Settings");
            foreach (var name in new[] { "inventoryCraftDeliver", "shareForCrafting", "markForUnload" })
                SetBool(hdSettings.GetType(), hdSettings, name, true);
            SetBool(hdSettings.GetType(), hdSettings, "batchByDefault", false);
            var csSettings = RequireType("CommonSense.Settings");
            commonSenseAssembly = csSettings.Assembly;
            SetBool(csSettings, null, "adv_cleaning", true);
            SetBool(csSettings, null, "adv_haul_all_ings", false);
            // Isolate in-bill advanced cleaning from CS's independent before-work job insertion.
            SetBool(csSettings, null, "clean_before_work", false);
            SetBool(csSettings, null, "hauling_over_bills", false);
            var optimalField = csSettings.GetField("optimal_patching_in_use", BindingFlags.Public | BindingFlags.Static);
            Require("default-cs-patching-mode", optimalField != null && !(bool)optimalField.GetValue(null),
                "Actual startup patching mode must be false; the fixture does not rewrite the in-use marker.");
            RecordPatchOwners();

            // Retain no active starting pawns, incoming drop pods or animals that could alter the fixture.
            // These are objects in the newly generated disposable map only, never a user save.
            var removed = map.listerThings.AllThings.Where(x => x is Pawn || x is Skyfaller || x is ActiveTransporter).ToList();
            foreach (var thing in removed) if (thing.Spawned) thing.DeSpawn();
            HarnessSession.Event("fixture-isolation", "despawned initial actors/landing objects=" + removed.Count);

            var rect = new CellRect(map.Center.x - 20, map.Center.z - 6, 41, 13);
            Require("room-in-bounds", rect.Cells.All(x => x.InBounds(map)), rect.ToString());
            GenDebug.ClearArea(rect, map);
            foreach (var cell in rect.Cells)
            {
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                map.roofGrid.SetRoof(cell, null);
                map.areaManager.Home[cell] = true;
                map.fogGrid.Unfog(cell);
            }
            foreach (var cell in rect.EdgeCells)
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), cell, map);
            roofCells = rect.ContractedBy(1).Cells.ToList();
            Require("roof-support-distance", roofCells.All(c => Math.Min(Math.Min(c.x - rect.minX, rect.maxX - c.x),
                Math.Min(c.z - rect.minZ, rect.maxZ - c.z)) <= 6), "Every roof cell is within 6 cells of a supporting wall.");
            foreach (var cell in roofCells) map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);

            var stoveDef = DefDatabase<ThingDef>.GetNamed("FueledStove");
            stove = (Building_WorkTable)GenSpawn.Spawn(ThingMaker.MakeThing(stoveDef),
                new IntVec3(rect.minX + 5, 0, map.Center.z), map, Rot4.North);
            stove.SetFaction(Faction.OfPlayer);
            var fuel = stove.TryGetComp<CompRefuelable>();
            Require("fueled-stove-comp", fuel != null, stove.GetType().FullName);
            fuel.Refuel(50f);
            var gatherComp = stove.AllComps.SingleOrDefault(x => x.GetType().FullName == "HaulersDream.CompBenchGather");
            Require("bench-gather-comp", gatherComp != null, "CompBenchGather injected on FueledStove");
            SetBool(gatherComp.GetType(), gatherComp, "gatherIngredients", true);

            riceDef = DefDatabase<ThingDef>.GetNamed("RawRice");
            potatoDef = DefDatabase<ThingDef>.GetNamed("RawPotatoes");
            mealDef = DefDatabase<ThingDef>.GetNamed("MealSimple");
            var rice = SpawnStack(riceDef, ingredientUnitsPerDef, new IntVec3(rect.maxX - 6, 0, map.Center.z - 4));
            var potato = SpawnStack(potatoDef, ingredientUnitsPerDef, new IntVec3(rect.maxX - 6, 0, map.Center.z + 4));
            initialRiceId = rice.ThingID;
            initialPotatoId = potato.ThingID;
            Require("nutrition", Math.Abs(rice.GetStatValue(StatDefOf.Nutrition) - 0.05f) < 0.00001f
                && Math.Abs(potato.GetStatValue(StatDefOf.Nutrition) - 0.05f) < 0.00001f,
                "rice=" + rice.GetStatValue(StatDefOf.Nutrition) + "; potato=" + potato.GetStatValue(StatDefOf.Nutrition));

            var recipe = DefDatabase<RecipeDef>.GetNamed(recipeDefName);
            Require("ordinary-recipe", recipe.allowMixingIngredients && recipe.products.Count == 1
                && recipe.products[0].thingDef == mealDef && recipe.products[0].count == expectedMealUnits,
                "recipe=" + recipe.defName + "; allowMixing=" + recipe.allowMixingIngredients);
            if (ObserveIngredientConsumption)
            {
                float requiredNutrition = recipe.ingredients.Count == 1 ? recipe.ingredients[0].GetBaseCount() : float.NaN;
                Require("bg02-native-nutrition-recipe", recipe.ingredients.Count == 1
                    && Math.Abs(requiredNutrition - 2f) < 0.00001f
                    && recipe.IngredientValueGetter.GetType().Name == "IngredientValueGetter_Nutrition"
                    && recipe.Worker.GetType() == typeof(RecipeWorker) && recipe.unfinishedThingDef == null,
                    "recipe=" + recipe.defName + "; ingredientSlots=" + recipe.ingredients.Count
                    + "; requiredNutrition=" + requiredNutrition
                    + "; getter=" + recipe.IngredientValueGetter.GetType().FullName + "; worker=" + recipe.Worker.GetType().FullName);
            }
            bill = (Bill_Production)recipe.MakeNewBill();
            bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
            bill.repeatCount = 1;
            bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor);
            bill.ingredientFilter.SetDisallowAll();
            bill.ingredientFilter.SetAllow(riceDef, true);
            bill.ingredientFilter.SetAllow(potatoDef, true);
            stove.billStack.AddBill(bill);
            if (ObserveIngredientConsumption)
            {
                var componentType = RequireType("HaulersDream.HaulersDreamGameComponent");
                var component = componentType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                var isBatchBill = componentType.GetMethod("IsBatchBill", new[] { typeof(Bill) });
                Require("bg02-bill-batch-disabled", component != null && isBatchBill != null
                    && isBatchBill.ReturnType == typeof(bool) && !(bool)isBatchBill.Invoke(component, new object[] { bill }),
                    "batchByDefault=false; new bill is not marked as an HD batch; repeatCount=" + bill.repeatCount);
            }

            var cookingWork = DefDatabase<WorkTypeDef>.GetNamed("Cooking");
            Rand.PushState(25801);
            try
            {
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    var generated = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    if (generated.RaceProps.Humanlike && !generated.WorkTypeIsDisabled(cookingWork)
                        && !generated.WorkTypeIsDisabled(WorkTypeDefOf.Cleaning) && !generated.Downed)
                    { cook = generated; break; }
                }
            }
            finally { Rand.PopState(); }
            Require("capable-human", cook != null, "seed25801; normal colonist capable of Cooking and Cleaning");
            cook.inventory.innerContainer.ClearAndDestroyContents();
            cook.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            cook.workSettings.DisableAll();
            cook.workSettings.SetPriority(cookingWork, 1);
            cook.workSettings.SetPriority(WorkTypeDefOf.Cleaning, 4); // CS requires WorkIsActive(Cleaning).
            cook.skills.GetSkill(SkillDefOf.Cooking).Level = 10;
            cook.timetable.times = Enumerable.Repeat(TimeAssignmentDefOf.Work, 24).ToList();
            if (cook.needs.food != null) cook.needs.food.CurLevelPercentage = 1f;
            if (cook.needs.rest != null) cook.needs.rest.CurLevelPercentage = 1f;
            if (cook.needs.joy != null) cook.needs.joy.CurLevelPercentage = 1f;
            GenSpawn.Spawn(cook, stove.InteractionCell, map);
            if (ObserveIngredientConsumption)
            {
                // Installed GenRecipe emits one Thing per products entry and sets its
                // count before PostProcessProduct. ResolveReferences may supply a
                // work-table efficiency stat even when the XML does not name one.
                float efficiency = recipe.efficiencyStat == null ? 1f : cook.GetStatValue(recipe.efficiencyStat);
                if (recipe.workTableEfficiencyStat != null) efficiency *= stove.GetStatValue(recipe.workTableEfficiencyStat);
                Require("bg02-native-product-shape", Math.Abs(efficiency - 1f) < 0.00001f
                    && recipe.products.Count == 1 && recipe.products[0].count == expectedMealUnits
                    && (recipe.specialProducts == null || recipe.specialProducts.Count == 0)
                    && mealDef.stackLimit >= expectedMealUnits && !mealDef.Minifiable,
                    "one products entry; efficiency=" + efficiency + "; nativeProductThingCount=1; expectedStackCount="
                    + expectedMealUnits + "; mealStackLimit=" + mealDef.stackLimit + "; workTableEfficiencyStat=" + recipe.workTableEfficiencyStat?.defName);
            }
            cleanedAtStart = cook.records.GetValue(RecordDefOf.MessesCleaned);
            foreach (var offset in new[] { new IntVec3(1, 0, 0), new IntVec3(2, 0, 0), new IntVec3(1, 0, 1) })
            {
                bool made = FilthMaker.TryMakeFilth(stove.InteractionCell + offset, map, ThingDefOf.Filth_Dirt,
                    out var filth, 1, shouldPropagate: false);
                Require("seed-filth-" + seededFilth.Count, made && filth != null, "cell=" + (stove.InteractionCell + offset));
                seededFilth.Add(filth);
                Require("one-layer-filth-" + filth.thingIDNumber, filth.thickness == 1, "thickness=" + filth.thickness);
            }
            Require("only-actor", map.mapPawns.AllPawnsSpawned.Count == 1, "spawnedPawns=" + map.mapPawns.AllPawnsSpawned.Count);
            Require("exact-starting-food", CountAll(riceDef) == ingredientUnitsPerDef && CountAll(potatoDef) == ingredientUnitsPerDef && CountAll(mealDef) == 0,
                "rice=" + CountAll(riceDef) + "; potato=" + CountAll(potatoDef) + "; meals=" + CountAll(mealDef));
            Require("sources-away-from-bench", rice.Position.DistanceTo(stove.InteractionCell) > 15
                && potato.Position.DistanceTo(stove.InteractionCell) > 15 && rice.Position != potato.Position,
                "bench=" + stove.InteractionCell + "; rice=" + rice.Position + "; potato=" + potato.Position);
            HarnessSession.Event("fixture-ready", caseId + "; pawn=" + cook.ThingID + "; recipe=" + recipe.defName
                + "; bill=" + bill.GetUniqueLoadID() + "; stove=" + stove.ThingID
                + "; walls=41x13; supported roof; native bill automatic; only Cooking1/Cleaning4 active; no storage zones added; successful cleanup plus 300 stable post-product ticks required"
                + (ObserveIngredientConsumption ? "; initialRice=" + initialRiceId + "/20; initialPotatoes=" + initialPotatoId
                    + "/20; expectedProduct=MealSimple/4; maximumTicks=" + maximumTicks : ""));
        }

        private Thing SpawnStack(ThingDef def, int count, IntVec3 cell)
        {
            var thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            return GenSpawn.Spawn(thing, cell, map);
        }

        private static Type RequireType(string name)
        {
            var types = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType(name, false)).Where(x => x != null).ToList();
            Require("type-" + name, types.Count == 1, name + "; loadedDefinitions=" + types.Count);
            return types[0];
        }

        private static void SetBool(Type type, object instance, string name, bool value)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            Require("setting-exists-" + name, field != null && field.FieldType == typeof(bool), type.FullName + "." + name);
            field.SetValue(instance, value);
            Require("setting-value-" + name, (bool)field.GetValue(instance) == value, name + "=" + value);
        }

        private static void RecordPatchOwners()
        {
            var csPatch = RequireType("CommonSense.JobDriver_DoBill_MakeNewToils_CommonSensePatch");
            var hdPatch = RequireType("HaulersDream.Patch_WorkGiver_DoBill_InventoryRoute");
            Require("cs-patch-settings-assembly", csPatch.Assembly == RequireType("CommonSense.Settings").Assembly, csPatch.Assembly.FullName);
            Require("hd-route-mod-assembly", hdPatch.Assembly == RequireType("HaulersDream.HaulersDreamMod").Assembly, hdPatch.Assembly.FullName);
            Bg01Observers.RequireExactPatch(typeof(JobDriver_DoBill).GetMethod("MakeNewToils", BindingFlags.NonPublic | BindingFlags.Instance),
                "prefix", "net.avilmask.rimworld.mod.CommonSense", csPatch, "Prefix");
            Bg01Observers.RequireExactPatch(typeof(WorkGiver_DoBill).GetMethod("JobOnThing"),
                "postfix", "giwaffed.HaulersDream", hdPatch, "Postfix");
        }

        private int CountInventory(ThingDef def) => cook.inventory.innerContainer.Where(x => x.def == def).Sum(x => x.stackCount);
        private int CountCarry(ThingDef def) => cook.carryTracker.CarriedThing?.def == def ? cook.carryTracker.CarriedThing.stackCount : 0;
        private int CountFloor(ThingDef def) => map.listerThings.ThingsOfDef(def).Where(x => x.Spawned).Sum(x => x.stackCount);
        private int CountAll(ThingDef def) => CountFloor(def) + CountInventory(def) + CountCarry(def);
        private void InventoryChanged() => Observe("inventory-transfer");
        private void CarryChanged() => Observe("carry-transfer");

        internal sealed class CleanEvent
        {
            internal Filth filth;
            internal int jobId, tick, sequence;
            internal bool native, csToil, destroyed;
            internal float recordValue;
        }

        internal sealed class EndEvent
        {
            internal Job job;
            internal JobDriver driver;
            internal int jobId;
            internal JobCondition condition;
            internal bool completedCleanup;
        }

        internal sealed class IngredientConsumeEvent
        {
            internal Thing ingredient;
            internal RecipeWorker worker;
            internal ThingDef def;
            internal int thingId, beforeCount, jobId, sequence;
            internal bool native, alreadyDestroyed, mapMatched;
        }

        internal IngredientConsumeEvent CaptureIngredientConsumption(RecipeWorker worker, Thing ingredient, RecipeDef recipe, Map ingredientMap)
        {
            if (!active || !ObserveIngredientConsumption || recipe != bill.recipe) return null;
            ObserveActualJob();
            return new IngredientConsumeEvent
            {
                ingredient = ingredient, worker = worker, def = ingredient?.def,
                thingId = ingredient?.thingIDNumber ?? -1, beforeCount = ingredient?.stackCount ?? 0,
                jobId = cook.CurJob?.loadID ?? -1, sequence = ++eventSequence,
                native = IsNativeBill, alreadyDestroyed = ingredient == null || ingredient.Destroyed,
                mapMatched = ingredientMap == map
            };
        }

        internal void IngredientConsumed(IngredientConsumeEvent consumption)
        {
            if (!active || consumption == null) return;
            var ingredient = consumption.ingredient;
            bool destroyed = ingredient != null && ingredient.Destroyed;
            int remaining = destroyed ? 0 : ingredient?.stackCount ?? 0;
            int units = Math.Max(0, consumption.beforeCount - remaining);
            bool attributed = !consumption.alreadyDestroyed && consumption.beforeCount > 0 && destroyed
                && consumption.mapMatched && consumption.native && IsNativeBill && cook.CurJob.loadID == consumption.jobId
                && ReferenceEquals(consumption.worker, bill.recipe.Worker) && consumption.worker.GetType() == typeof(RecipeWorker)
                && ingredient.thingIDNumber == consumption.thingId && ingredient.def == consumption.def
                && (consumption.def == riceDef || consumption.def == potatoDef)
                && firstProductTick >= 0 && producedCalls == 1 && producedUnits == expectedMealUnits
                && units == consumption.beforeCount && consumedIngredientIds.Add(consumption.thingId);
            if (!attributed) consumptionAttributionValid = false;
            if (attributed)
            {
                if (consumption.def == riceDef) consumedRice += units;
                else consumedPotato += units;
            }
            var record = new IngredientConsumptionRecord
            {
                thingId = ingredient?.ThingID, defName = consumption.def?.defName,
                beforeCount = consumption.beforeCount, remainingCount = remaining, unitsConsumed = units,
                destroyed = destroyed, attributed = attributed, jobId = consumption.jobId,
                tick = Find.TickManager.TicksGame, sequence = consumption.sequence
            };
            consumptionRecords.Add(record);
            HarnessSession.Event("native-ingredient-consumed", "case=" + caseId + "; recipe=" + recipeDefName
                + "; thing=" + record.thingId + "; def=" + record.defName + "; before=" + record.beforeCount
                + "; remaining=" + record.remainingCount + "; consumed=" + units + "; destroyed=" + destroyed
                + "; job=" + record.jobId + "; sequence=" + record.sequence + "; attributed=" + attributed
                + "; totalConsumed=" + consumedRice + "," + consumedPotato);
        }

        private bool IsNativeBill => cook.CurJob?.bill == bill && cook.CurJob.def == JobDefOf.DoBill
            && cook.jobs.curDriver?.GetType() == typeof(JobDriver_DoBill);

        private bool IsCommonSenseCleaningToil()
        {
            var toil = CurrentToil?.GetValue(cook.jobs.curDriver, null) as Toil;
            var method = toil?.tickAction?.Method;
            return method != null && method.Module.Assembly == commonSenseAssembly
                && method.DeclaringType.FullName.StartsWith("CommonSense.Utility", StringComparison.Ordinal);
        }

        internal CleanEvent CaptureFilth(Filth filth)
        {
            if (!active || !seededFilth.Contains(filth)) return null;
            ObserveActualJob();
            return new CleanEvent
            {
                filth = filth, jobId = cook.CurJob?.loadID ?? -1, tick = Find.TickManager.TicksGame,
                sequence = ++eventSequence, native = IsNativeBill, csToil = IsCommonSenseCleaningToil(),
                recordValue = cook.records.GetValue(RecordDefOf.MessesCleaned)
            };
        }

        internal void FilthThinned(Filth filth, CleanEvent removal)
        {
            if (!active || removal == null) return;
            removal.destroyed = filth.Destroyed;
            bool attributable = removal.destroyed && removal.native && removal.csToil && firstProductTick < 0
                && IsNativeBill && cook.CurJob.loadID == removal.jobId && !filthRemovals.ContainsKey(filth.thingIDNumber);
            if (!attributable) cleaningAttributionValid = false;
            filthRemovals[filth.thingIDNumber] = removal;
            HarnessSession.Event("seeded-filth-removal", "filth=" + filth.ThingID + "; job=" + removal.jobId
                + "; sequence=" + removal.sequence + "; destroyed=" + removal.destroyed
                + "; native=" + removal.native + "; csToil=" + removal.csToil + "; beforeProduct=" + (firstProductTick < 0));
        }

        internal CleanEvent CaptureCleaningIncrement() =>
            CaptureFilth(cook.CurJob?.targetA.Thing as Filth);

        internal void CleaningIncremented(CleanEvent increment)
        {
            if (!active) return;
            int id = increment.filth.thingIDNumber;
            float delta = cook.records.GetValue(RecordDefOf.MessesCleaned) - increment.recordValue;
            bool attributable = increment.native && increment.csToil && firstProductTick < 0 && IsNativeBill
                && cook.CurJob.loadID == increment.jobId && delta == 1f
                && filthRemovals.TryGetValue(id, out var removal) && removal.destroyed && removal.native && removal.csToil
                && removal.jobId == increment.jobId && removal.tick == increment.tick && removal.sequence < increment.sequence
                && cleaningIncrements.Add(id);
            if (!attributable) cleaningAttributionValid = false;
            if (attributable) cleaningDuringBill = true;
            HarnessSession.Event("seeded-cleaning-increment", "filth=" + increment.filth.ThingID + "; job=" + increment.jobId
                + "; sequence=" + increment.sequence + "; delta=" + delta + "; attributed=" + attributable);
        }

        internal void ProductCreated(Thing product, RecipeDef recipe)
        {
            if (!active || recipe != bill.recipe) return;
            ObserveActualJob();
            producedCalls++;
            producedUnits += product?.stackCount ?? 0;
            if (!IsNativeBill || product?.def != mealDef) productContextValid = false;
            if (ObserveIngredientConsumption && (product == null || product.Destroyed || product.stackCount != expectedMealUnits))
                productContextValid = false;
            int sequence = ++eventSequence;
            if (firstProductTick < 0)
            {
                firstProductTick = Find.TickManager.TicksGame;
                allSeededFilthCleanedBeforeProduct = cleaningAttributionValid && cleaningIncrements.Count == seededFilth.Count
                    && seededFilth.All(f => f.Destroyed && filthRemovals.TryGetValue(f.thingIDNumber, out var removal)
                        && removal.sequence < sequence && removal.jobId == cook.CurJob?.loadID);
            }
            HarnessSession.Event("native-product-created", "thing=" + product?.ThingID + "; count=" + product?.stackCount
                + "; job=" + cook.CurJob?.loadID + "; calls=" + producedCalls + "; totalUnits=" + producedUnits
                + "; sequence=" + sequence + "; seededCleaningComplete=" + allSeededFilthCleanedBeforeProduct
                + (ObserveIngredientConsumption ? "; case=" + caseId + "; recipe=" + recipeDefName + "; expectedStackCount=" + expectedMealUnits : ""));
        }

        internal EndEvent CaptureCleanup(JobCondition condition)
        {
            if (!active) return null;
            ObserveActualJob();
            if (!IsNativeBill) return null;
            return new EndEvent { job = cook.CurJob, driver = cook.jobs.curDriver, jobId = cook.CurJob.loadID, condition = condition };
        }

        internal void CleanupCompleted(EndEvent ending)
        {
            if (!active) return;
            // Capture IDs and condition before pooling; inspect actual driver cleanup
            // and tracker release after CleanupCurrentJob returns.
            ending.completedCleanup = ending.driver.ended && !ReferenceEquals(cook.CurJob, ending.job)
                && !ReferenceEquals(cook.jobs.curDriver, ending.driver);
            nativeEnds.Add(ending);
            HarnessSession.Event("native-job-cleanup", "job=" + ending.jobId + "; condition=" + ending.condition
                + "; completedCleanup=" + ending.completedCleanup + "; observations=" + nativeEnds.Count);
        }

        internal void ObserveActualJob()
        {
            if (!active) return;
            var job = cook.CurJob;
            if (job == null) return;
            if (IsNativeBill) executedNativeJobs.Add(job.loadID);
            bool relevant = job.bill == bill || job.targetA.Thing == stove;
            if (relevant && job.playerForced) forcedJobObserved = true;
            if (relevant && job.workGiverDef != null && typeof(WorkGiver_DoBill).IsAssignableFrom(job.workGiverDef.giverClass))
                chosenByDoBillWorkgiver = true;
            if (relevant && (job.def.defName == "HaulersDream_BillPrepGather"
                || job.def.defName == "HaulersDream_GatherBillIngredients")) gatherJobs.Add(job.loadID);
            if (relevant && job.def.defName.IndexOf("Batch", StringComparison.OrdinalIgnoreCase) >= 0) batchJobObserved = true;
            if (seenJobs.Add(job.loadID)) HarnessSession.Event("executed-job", "id=" + job.loadID + "; def=" + job.def.defName
                + "; driver=" + cook.jobs.curDriver?.GetType().FullName + "; workgiver=" + job.workGiverDef?.defName
                + "; forced=" + job.playerForced + "; recipe=" + job.bill?.recipe?.defName);
        }

        internal void HeldCounts(out int rice, out int potato, out int floorRice, out int floorPotato)
        {
            rice = CountInventory(riceDef) + CountCarry(riceDef);
            potato = CountInventory(potatoDef) + CountCarry(potatoDef);
            floorRice = CountFloor(riceDef);
            floorPotato = CountFloor(potatoDef);
        }

        internal void HeldOperationCompleted(int beforeRice, int beforePotato, int beforeFloorRice, int beforeFloorPotato)
        {
            if (!active) return;
            HeldCounts(out int rice, out int potato, out int floorRice, out int floorPotato);
            acquiredRice += Math.Max(0, rice - beforeRice);
            acquiredPotato += Math.Max(0, potato - beforePotato);
            // SplitOff can remove a floor stack before TryAdd starts. Its floor
            // delta therefore cannot be checked against this narrower boundary.
            // BG02 verifies complete material counts at the enclosing settled
            // driver boundary; these net held deltas still count actual pickup.
            bool droppedIngredients = (rice < beforeRice && floorRice > beforeFloorRice)
                || (potato < beforePotato && floorPotato > beforeFloorPotato);
            if (droppedIngredients && cook.Position.DistanceTo(stove.InteractionCell) > 3f) storageDetour = true;
            ObserveActualJob();
            ObserveGatherPosition();
            HarnessSession.Event("settled-held-transfer", "job=" + cook.CurJob?.loadID + "; before=" + beforeRice + "," + beforePotato
                + "; after=" + rice + "," + potato + "; cumulativeAcquired=" + acquiredRice + "," + acquiredPotato
                + "; ingredientDrop=" + droppedIngredients + "; storageDetour=" + storageDetour
                + (ObserveIngredientConsumption ? "; floorBefore=" + beforeFloorRice + "," + beforeFloorPotato
                    + "; floorAfter=" + floorRice + "," + floorPotato + "; acquisitionConserved=" + acquisitionConserved
                    + "; identities=" + IngredientIdentities() : ""));
        }

        private string IngredientIdentities()
        {
            string Describe(Thing thing, string location) => thing.ThingID + "/" + thing.def.defName + "/" + thing.stackCount + "@" + location;
            var inventory = cook.inventory.innerContainer.Where(x => x.def == riceDef || x.def == potatoDef).Select(x => Describe(x, "inventory"));
            var carried = cook.carryTracker.CarriedThing;
            var hands = carried != null && (carried.def == riceDef || carried.def == potatoDef)
                ? new[] { Describe(carried, "hands") } : Array.Empty<string>();
            var floor = map.listerThings.ThingsOfDef(riceDef).Concat(map.listerThings.ThingsOfDef(potatoDef))
                .Where(x => x.Spawned).Select(x => Describe(x, x.Position.ToString()));
            return string.Join("|", inventory.Concat(hands).Concat(floor));
        }

        private void ObserveGatherPosition()
        {
            int invRice = CountInventory(riceDef), invPotato = CountInventory(potatoDef);
            maxInventoryRice = Math.Max(maxInventoryRice, invRice);
            maxInventoryPotato = Math.Max(maxInventoryPotato, invPotato);
            if (firstProductTick >= 0) return;
            bool hasIngredients = invRice + invPotato + CountCarry(riceDef) + CountCarry(potatoDef) > 0;
            bool nearBench = cook.Position.DistanceTo(stove.InteractionCell) <= 3f;
            if (hasIngredients && !nearBench) leftBenchWithIngredients = true;
            if (firstReturnTick < 0 && invRice == ingredientUnitsPerDef && invPotato == ingredientUnitsPerDef && !nearBench)
                fullInventorySweep = true;
            if (firstReturnTick < 0 && leftBenchWithIngredients && nearBench && hasIngredients)
            {
                firstReturnTick = Find.TickManager.TicksGame;
                returnedWithIncompleteIngredients = !fullInventorySweep;
                HarnessSession.Event("first-ingredient-return", "tick=" + firstReturnTick + "; fullInventorySweep=" + fullInventorySweep);
            }
            if (firstReturnTick >= 0 && !nearBench && (CountAll(riceDef) > 0 || CountAll(potatoDef) > 0))
                secondGatherExcursion = true;
        }

        internal void ObserveSettled(string reason)
        {
            if (!active || observing) return;
            ObserveActualJob();
            ObserveGatherPosition();
            int tick = Find.TickManager.TicksGame;
            int rice = CountAll(riceDef), potato = CountAll(potatoDef), meals = CountAll(mealDef);
            if (ObserveIngredientConsumption && firstProductTick < 0
                && (rice != ingredientUnitsPerDef || potato != ingredientUnitsPerDef)) acquisitionConserved = false;
            settledBoundaries++;
            maxSettledMeals = Math.Max(maxSettledMeals, meals);
            if (meals > expectedMealUnits) productCountsStayedStable = false;
            if (firstProductTick >= 0)
            {
                bool exact = rice == 0 && potato == 0 && meals == expectedMealUnits && bill.repeatCount == 0;
                if (!exact) productCountsStayedStable = false;
                bool cleanedUp = nativeEnds.Count == 1 && nativeEnds[0].condition == JobCondition.Succeeded && nativeEnds[0].completedCleanup;
                if (exact && cleanedUp)
                {
                    if (stableSinceTick < 0) stableSinceTick = tick;
                    if (lastSettledTick != tick) settledPostProductTicks++;
                }
                else { stableSinceTick = -1; settledPostProductTicks = 0; }
            }
            lastSettledTick = tick;
            Observe(reason + "-settled");
        }

        internal void ObserverFault(Exception error)
        {
            observerHealthy = false;
            HarnessSession.Event(caseId.ToLowerInvariant() + "-observer-fault", error.ToString());
        }

        internal void Observe(string reason)
        {
            if (reason == "tick") { ObserveSettled("game-tick"); return; }
            if (!active || observing) return;
            observing = true;
            try
            {
                int tick = Find.TickManager.TicksGame;
                if (roofCells.Any(c => map.roofGrid.RoofAt(c) != RoofDefOf.RoofConstructed)) roofStayedIntact = false;
                int invRice = CountInventory(riceDef), invPotato = CountInventory(potatoDef);
                var job = cook.CurJob;
                int meals = CountAll(mealDef);
                string state = "job=" + job?.loadID + "/" + job?.def.defName + "; inv=" + invRice + "," + invPotato
                    + "; carry=" + CountCarry(riceDef) + "," + CountCarry(potatoDef)
                    + "; floor=" + CountFloor(riceDef) + "," + CountFloor(potatoDef)
                    + "; products=" + meals + "; seededFilthRemaining=" + seededFilth.Count(x => !x.Destroyed)
                    + "; cleaned=" + (cook.records.GetValue(RecordDefOf.MessesCleaned) - cleanedAtStart)
                    + "; billRepeatRemaining=" + bill.repeatCount + "; fullInventorySweep=" + fullInventorySweep
                    + "; secondExcursion=" + secondGatherExcursion + "; acquired=" + acquiredRice + "," + acquiredPotato
                    + "; productCalls=" + producedCalls + "; stableSince=" + stableSinceTick;
                if (state != previousSnapshot || reason != "tick" || tick - lastPositionEventTick >= 60)
                {
                    snapshotCount++;
                    HarnessSession.Event("execution-state", "reason=" + reason + "; position=" + cook.Position + "; " + state);
                    previousSnapshot = state;
                    lastPositionEventTick = tick;
                }
            }
            finally { observing = false; }
        }

        internal ScenarioResult TryFinish()
        {
            if (!active) return null;
            Observe("tick");
            int tick = Find.TickManager.TicksGame;
            bool timeout = tick - startedTick >= maximumTicks;
            bool stableWindow = stableSinceTick >= 0 && tick - stableSinceTick >= 300 && settledPostProductTicks >= 300;
            if (!timeout && !stableWindow) return null;
            Dispose();
            bool successfulCleanup = nativeEnds.Count == 1 && nativeEnds[0].condition == JobCondition.Succeeded
                && nativeEnds[0].completedCleanup && executedNativeJobs.Contains(nativeEnds[0].jobId);
            bool productAccounting = producedCalls == 1 && producedUnits == expectedMealUnits && productContextValid
                && maxSettledMeals == expectedMealUnits && productCountsStayedStable;
            bool exactConsumption = consumptionAttributionValid && consumptionRecords.Count > 0
                && consumedRice == ingredientUnitsPerDef && consumedPotato == ingredientUnitsPerDef
                && consumptionRecords.All(x => x.attributed && x.destroyed && nativeEnds.Count == 1 && x.jobId == nativeEnds[0].jobId);
            bool stableCounts = stableWindow && productAccounting && CountAll(riceDef) == 0 && CountAll(potatoDef) == 0
                && CountAll(mealDef) == expectedMealUnits && bill.repeatCount == 0;
            bool nativeComplete = !timeout && successfulCleanup && stableCounts;
            bool cleaningComplete = allSeededFilthCleanedBeforeProduct && cleaningAttributionValid
                && cleaningIncrements.Count == seededFilth.Count && filthRemovals.Count == seededFilth.Count;
            bool ordinaryExecution = chosenByDoBillWorkgiver && executedNativeJobs.Count == 1 && !forcedJobObserved && !batchJobObserved;
            bool uniqueAcquisition = acquiredRice == ingredientUnitsPerDef && acquiredPotato == ingredientUnitsPerDef && gatherJobs.Count <= 1
                && (!ObserveIngredientConsumption || acquisitionConserved);
            bool oneSweep = fullInventorySweep && firstReturnTick >= 0 && !returnedWithIncompleteIngredients;
            bool noRegather = uniqueAcquisition && !secondGatherExcursion;
            HarnessSession.Check("execution-native-cooking-completed", nativeComplete,
                "remainingRice=" + CountAll(riceDef) + "; remainingPotato=" + CountAll(potatoDef) + "; meals=" + CountAll(mealDef)
                + "; billRepeat=" + bill.repeatCount + "; cleanupSucceeded=" + successfulCleanup + "; stableCounts=" + stableCounts + "; timeout=" + timeout);
            HarnessSession.Check("execution-ordinary-workgiver", ordinaryExecution,
                "selectedByDoBill=" + chosenByDoBillWorkgiver + "; nativeJobs=" + executedNativeJobs.Count + "; forced=" + forcedJobObserved + "; batch=" + batchJobObserved);
            HarnessSession.Check("execution-cs-cleaned-during-bill", cleaningComplete,
                "allSeededFilthCleanedBeforeProduct=" + allSeededFilthCleanedBeforeProduct
                + "; duringDoBill=" + cleaningDuringBill + "; removals=" + filthRemovals.Count + "; increments=" + cleaningIncrements.Count
                + "; attributed=" + cleaningAttributionValid + "; seededFilthRemainingNow=" + seededFilth.Count(x => !x.Destroyed));
            HarnessSession.Check("execution-native-job-cleanup-succeeded", successfulCleanup,
                "ends=" + string.Join(",", nativeEnds.Select(e => e.jobId + "/" + e.condition + "/released=" + e.completedCleanup)));
            HarnessSession.Check("execution-exact-native-product-events", productAccounting,
                "productCalls=" + producedCalls + "; units=" + producedUnits + "; nativeContext=" + productContextValid
                + "; maxSettledMeals=" + maxSettledMeals + "; countsNeverChangedAfterProduct=" + productCountsStayedStable);
            HarnessSession.Check("execution-post-product-counts-stable", stableCounts,
                "stableSinceTick=" + stableSinceTick + "; stableWindow=" + stableWindow + "; distinctPostProductTicks=" + settledPostProductTicks
                + "; totalSettledBoundaries=" + settledBoundaries);
            HarnessSession.Check("execution-ingredients-acquired-once", uniqueAcquisition,
                "acquiredRice=" + acquiredRice + "; acquiredPotato=" + acquiredPotato + "; executedGatherJobs=" + gatherJobs.Count);
            if (ObserveIngredientConsumption)
            {
                HarnessSession.Check("execution-bg02-native-ingredients-consumed-once", exactConsumption,
                    "consumedRice=" + consumedRice + "; consumedPotatoes=" + consumedPotato
                    + "; uniqueConsumedThings=" + consumedIngredientIds.Count + "; events=" + consumptionRecords.Count
                    + "; attributionValid=" + consumptionAttributionValid + "; requiredPerDef=" + ingredientUnitsPerDef);
                HarnessSession.Check("execution-bg02-acquisition-conserved", acquisitionConserved,
                    "Every settled pre-product driver/tick boundary preserved 20 rice and 20 potatoes across floor/inventory/hands; initialThings="
                    + initialRiceId + "," + initialPotatoId);
            }
            HarnessSession.Check("execution-no-ingredient-storage-detour", !storageDetour,
                "outsideBenchIngredientDrop=" + storageDetour);
            HarnessSession.Check("execution-" + caseId.ToLowerInvariant() + "-observer-healthy", observerHealthy, "No observation callback or observed driver/transfer exception.");
            HarnessSession.Check("execution-roof-stayed-intact", roofStayedIntact, "all constructed roof cells preserved throughout observed ticks");
            HarnessSession.Check("behavior-complete-inventory-sweep-before-return", oneSweep,
                "observed=" + fullInventorySweep + "; firstReturnTick=" + firstReturnTick + "; partialReturn=" + returnedWithIncompleteIngredients
                + "; maxInventoryRice=" + maxInventoryRice + "; maxInventoryPotato=" + maxInventoryPotato);
            HarnessSession.Check("behavior-no-second-gather-or-regather", noRegather,
                "secondExcursion=" + secondGatherExcursion + "; uniqueIngredientAcquisition=" + uniqueAcquisition);
            bool controlPassed = nativeComplete && cleaningComplete && ordinaryExecution && roofStayedIntact
                && observerHealthy && uniqueAcquisition && !storageDetour && (!ObserveIngredientConsumption || exactConsumption);
            bool satisfied = controlPassed && oneSweep && noRegather;
            // A baseline witness needs a real incomplete return followed by a
            // successful native recipe; an arbitrary failure cannot count as a gap.
            bool expected = expectedBehavior == "satisfied" ? satisfied
                : controlPassed && !fullInventorySweep && returnedWithIncompleteIngredients;
            return new ScenarioResult
            {
                caseId = caseId, recipeDefName = recipeDefName, expectedRice = ingredientUnitsPerDef,
                expectedPotatoes = ingredientUnitsPerDef, expectedProductUnits = expectedMealUnits,
                maximumTicks = maximumTicks, requiredStablePostProductTicks = 300,
                fixtureValid = true, requestedBehaviorSatisfied = satisfied, expectationMatched = expected,
                expectedBehavior = expectedBehavior, timedOut = timeout, completedNativeRecipe = nativeComplete,
                cleaningDuringNativeBill = cleaningComplete, completeInventorySweep = oneSweep,
                nativeDoBillJobs = executedNativeJobs.Count, finalRice = CountAll(riceDef), finalPotatoes = CountAll(potatoDef),
                finalMeals = CountAll(mealDef), elapsedTicks = tick - startedTick, executionSnapshots = snapshotCount,
                nativeCleanupSucceeded = successfulCleanup, noSecondGatherOrRegather = noRegather, noIngredientStorageDetour = !storageDetour,
                productCountsStable = stableCounts, productCreationCalls = producedCalls, productUnitsCreated = producedUnits,
                maxSettledMeals = maxSettledMeals, stablePostProductTicks = settledPostProductTicks,
                riceAcquired = acquiredRice, potatoesAcquired = acquiredPotato, seededCleaningIncrements = cleaningIncrements.Count,
                riceConsumed = ObserveIngredientConsumption ? (int?)consumedRice : null,
                potatoesConsumed = ObserveIngredientConsumption ? (int?)consumedPotato : null,
                nativeIngredientConsumptionExact = ObserveIngredientConsumption ? (bool?)exactConsumption : null,
                ingredientAcquisitionConserved = ObserveIngredientConsumption ? (bool?)acquisitionConserved : null,
                ingredientConsumptionEvents = ObserveIngredientConsumption ? consumptionRecords.ToArray() : null,
                initialRiceThingId = initialRiceId, initialPotatoThingId = initialPotatoId,
                status = timeout ? "inconclusive" : (!expected ? "failed" : (satisfied ? "passed" : "behavior-gap-observed"))
            };
        }

        public void Dispose()
        {
            active = false;
            if (cook != null)
            {
                cook.inventory.innerContainer.OnContentsChanged -= InventoryChanged;
                cook.carryTracker.GetDirectlyHeldThings().OnContentsChanged -= CarryChanged;
            }
            observers?.Dispose();
            observers = null;
        }
    }

    [DataContract]
    internal sealed class ScenarioResult
    {
        [DataMember] public string caseId;
        [DataMember] public string recipeDefName;
        [DataMember] public int expectedRice;
        [DataMember] public int expectedPotatoes;
        [DataMember] public int expectedProductUnits;
        [DataMember] public int maximumTicks;
        [DataMember] public int requiredStablePostProductTicks;
        [DataMember] public bool fixtureValid;
        [DataMember] public bool requestedBehaviorSatisfied;
        [DataMember] public bool expectationMatched;
        [DataMember] public string expectedBehavior;
        [DataMember] public string status;
        [DataMember] public bool timedOut;
        [DataMember] public bool completedNativeRecipe;
        [DataMember] public bool cleaningDuringNativeBill;
        [DataMember] public bool completeInventorySweep;
        [DataMember] public int nativeDoBillJobs;
        [DataMember] public int finalRice;
        [DataMember] public int finalPotatoes;
        [DataMember] public int finalMeals;
        [DataMember] public int elapsedTicks;
        [DataMember] public int executionSnapshots;
        [DataMember] public bool nativeCleanupSucceeded;
        [DataMember] public bool noSecondGatherOrRegather;
        [DataMember] public bool noIngredientStorageDetour;
        [DataMember] public bool productCountsStable;
        [DataMember] public int productCreationCalls;
        [DataMember] public int productUnitsCreated;
        [DataMember] public int maxSettledMeals;
        [DataMember] public int stablePostProductTicks;
        [DataMember] public int riceAcquired;
        [DataMember] public int potatoesAcquired;
        [DataMember] public int seededCleaningIncrements;
        [DataMember] public int? riceConsumed;
        [DataMember] public int? potatoesConsumed;
        [DataMember] public bool? nativeIngredientConsumptionExact;
        [DataMember] public bool? ingredientAcquisitionConserved;
        [DataMember] public IngredientConsumptionRecord[] ingredientConsumptionEvents;
        [DataMember] public string initialRiceThingId;
        [DataMember] public string initialPotatoThingId;
    }

    [DataContract]
    internal sealed class IngredientConsumptionRecord
    {
        [DataMember] public string thingId { get; set; }
        [DataMember] public string defName { get; set; }
        [DataMember] public int beforeCount { get; set; }
        [DataMember] public int remainingCount { get; set; }
        [DataMember] public int unitsConsumed { get; set; }
        [DataMember] public bool destroyed { get; set; }
        [DataMember] public bool attributed { get; set; }
        [DataMember] public int jobId { get; set; }
        [DataMember] public int tick { get; set; }
        [DataMember] public int sequence { get; set; }
    }
}
