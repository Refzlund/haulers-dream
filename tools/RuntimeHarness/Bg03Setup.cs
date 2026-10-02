using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream.RuntimeHarness
{
    internal sealed partial class Bg03Scenario
    {
        private static void Require(string id, bool passed, string observation)
        {
            HarnessSession.Check("fixture-" + id, passed, observation);
            if (!passed) throw new InvalidDataException("Fixture precondition failed: " + id + "; " + observation);
        }

        private void Setup()
        {
            Require("bg03p1-real-map", map != null && map.IsPlayerHome && Current.Game != null && UnityData.IsInMainThread,
                "Actual initialized isolated player-home map, Unity main thread.");
            Require("known-expectation", expectedBehavior == "baseline-gap" || expectedBehavior == "satisfied", expectedBehavior);
            var hdType = RequireType("HaulersDream.HaulersDreamMod");
            var hdSettings = hdType.GetProperty("Settings", BindingFlags.Static | BindingFlags.Public)?.GetValue(null, null);
            Require("hd-settings", hdSettings != null, "HaulersDreamMod.Settings");
            foreach (var name in new[] { "masterEnabled", "inventoryCraftDeliver", "shareForCrafting", "markForUnload" })
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
            Require("bg03p1-no-existing-room-zones", rect.Cells.All(x => map.zoneManager.ZoneAt(x) == null), "No preexisting storage zone may alter initial carried-milk behavior.");
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
            milkDef = DefDatabase<ThingDef>.GetNamed("Milk");
            mealDef = DefDatabase<ThingDef>.GetNamed("MealSimple");
            expected[milkDef] = 12; expected[riceDef] = 14; expected[potatoDef] = 14;
            var rice = SpawnStack(riceDef, ingredientUnitsPerDef, new IntVec3(rect.maxX - 6, 0, map.Center.z - 4));
            var potato = SpawnStack(potatoDef, ingredientUnitsPerDef, new IntVec3(rect.maxX - 6, 0, map.Center.z + 4));
            initialRiceId = rice.ThingID;
            initialPotatoId = potato.ThingID;
            accounting.Seed(rice, false); accounting.Seed(potato, false);
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
                Require("bg03p1-native-nutrition-recipe", recipe.ingredients.Count == 1
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
            bill.ingredientFilter.SetAllow(milkDef, true);
            stove.billStack.AddBill(bill);
            if (ObserveIngredientConsumption)
            {
                var componentType = RequireType("HaulersDream.HaulersDreamGameComponent");
                var component = componentType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                var isBatchBill = componentType.GetMethod("IsBatchBill", new[] { typeof(Bill) });
                Require("bg03p1-bill-batch-disabled", component != null && isBatchBill != null
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
            initialMilk = ThingMaker.MakeThing(milkDef); initialMilk.stackCount = 12;
            Require("bg03p1-initial-milk-owned", cook.inventory.innerContainer.TryAdd(initialMilk, false)
                && initialMilk.stackCount == 12 && !initialMilk.Spawned
                && ReferenceEquals(initialMilk.holdingOwner, cook.inventory.innerContainer)
                && ReferenceEquals(initialMilk.ParentHolder, cook.inventory), "Explicit fixture setup: actual twelve-unit inventory milk Thing.");
            tagComp = cook.AllComps.SingleOrDefault(x => x.GetType().FullName == "HaulersDream.CompHauledToInventory");
            Require("bg03p1-tag-component", tagComp != null && tagComp.GetType().Assembly == hdType.Assembly, "Actual loaded HD tag component.");
            var register = tagComp.GetType().GetMethod("RegisterHauledItem", new[] { typeof(Thing), typeof(int) });
            tagPeek = tagComp.GetType().GetMethod("PeekHashSet", Type.EmptyTypes);
            Require("bg03p1-tag-bindings", register != null && register.ReturnType == typeof(void)
                && tagPeek != null && tagPeek.ReturnType == typeof(HashSet<Thing>), "RegisterHauledItem(Thing,int) and side-effect-free PeekHashSet().");
            register.Invoke(tagComp, new object[] { initialMilk, 0 }); // mergedCount=0; the actual fresh Thing has twelve units.
            InitializeFreshCargo(hdSettings);
            var initialTags = (HashSet<Thing>)tagPeek.Invoke(tagComp, null);
            Require("bg03p1-initial-tag", initialTags != null && initialTags.Count == 1 && initialTags.Contains(initialMilk)
                && cook.carryTracker.CarriedThing == null, "Read-only membership observation after exactly one fixture registration.");
            accounting.Seed(initialMilk, true);
            Require("bg03p1-three-native-nutrition-defs", expected.Keys.All(d => Math.Abs((d == milkDef ? initialMilk : d == riceDef ? rice : potato)
                    .GetStatValue(StatDefOf.Nutrition) - 0.05f) < 0.00001f)
                && new[] { initialMilk, rice, potato }.All(t => bill.ingredientFilter.Allows(t)
                    && recipe.ingredients[0].filter.Allows(t)), "Milk12 + rice14 + potatoes14 are actual accepted 0.05 nutrition units.");
            Require("bg03p1-native-identity-operations", new[] { initialMilk, rice, potato }.All(t =>
            {
                var splitOwner = t.GetType().GetMethod("SplitOff", new[] { typeof(int) }).DeclaringType;
                var mergeOwner = t.GetType().GetMethod("TryAbsorbStack", new[] { typeof(Thing), typeof(bool) }).DeclaringType;
                return (splitOwner == typeof(Thing) || splitOwner == typeof(ThingWithComps))
                    && (mergeOwner == typeof(Thing) || mergeOwner == typeof(ThingWithComps));
            }), "Relevant actual ingredients use the inspected native/base-comps identity methods observed by the harness.");
            HarnessSession.Event("bg03-initial-tag-setup", "thing=" + initialMilk.ThingID + "; units=12; inventory owner=" + initialMilk.ParentHolder.GetType().FullName
                + "; RegisterHauledItem mergedCount argument=0; PeekHashSet contains=true; synthetic initial cargo, not an observed previous haul.");
            if (ObserveIngredientConsumption)
            {
                // Installed GenRecipe emits one Thing per products entry and sets its
                // count before PostProcessProduct. ResolveReferences may supply a
                // work-table efficiency stat even when the XML does not name one.
                float efficiency = recipe.efficiencyStat == null ? 1f : cook.GetStatValue(recipe.efficiencyStat);
                if (recipe.workTableEfficiencyStat != null) efficiency *= stove.GetStatValue(recipe.workTableEfficiencyStat);
                Require("bg03p1-native-product-shape", Math.Abs(efficiency - 1f) < 0.00001f
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
            Require("exact-starting-food", CountAll(riceDef) == ingredientUnitsPerDef && CountAll(potatoDef) == ingredientUnitsPerDef && CountAll(milkDef) == 12 && CountAll(mealDef) == 0,
                "rice=" + CountAll(riceDef) + "; potato=" + CountAll(potatoDef) + "; milk=" + CountAll(milkDef) + "; meals=" + CountAll(mealDef));
            Require("sources-away-from-bench", rice.Position.DistanceTo(stove.InteractionCell) > 15
                && potato.Position.DistanceTo(stove.InteractionCell) > 15 && rice.Position != potato.Position,
                "bench=" + stove.InteractionCell + "; rice=" + rice.Position + "; potato=" + potato.Position);
            HarnessSession.Event("fixture-ready", caseId + "; pawn=" + cook.ThingID + "; recipe=" + recipe.defName
                + "; bill=" + bill.GetUniqueLoadID() + "; stove=" + stove.ThingID
                + "; walls=41x13; supported roof; native bill automatic; only Cooking1/Cleaning4 active; no storage zones added; successful cleanup plus 300 stable post-product ticks required"
                + (ObserveIngredientConsumption ? "; initialRice=" + initialRiceId + "/14; initialPotatoes=" + initialPotatoId
                    + "/14; expectedProduct=MealSimple/4; maximumTicks=" + maximumTicks : ""));
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
            Bg03Observers.RequireExactPatch(typeof(JobDriver_DoBill).GetMethod("MakeNewToils", BindingFlags.NonPublic | BindingFlags.Instance),
                "prefix", "net.avilmask.rimworld.mod.CommonSense", csPatch, "Prefix");
            Bg03Observers.RequireExactPatch(typeof(WorkGiver_DoBill).GetMethod("JobOnThing"),
                "postfix", "giwaffed.HaulersDream", hdPatch, "Postfix");
        }


    }
}
