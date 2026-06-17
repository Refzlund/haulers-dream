using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Dev stress-scenario generator (Dev Mode → Debug actions → "Hauler's Dream" → "Make colony"). A faithful
    /// port of While You're Up's <c>Testing.MakeColonyWyu</c>: clears a patch at map centre, fills it with every
    /// work table (every recipe billed), scatters one of every debug-spawnable ITEM, drops in 30 all-work
    /// colonists with Hauling DISABLED (so hauling happens only via opportunistic / HD detours — the whole point
    /// of the stress test), lays out seven Normal-priority resource stockpiles, and home-areas the patch. Use it
    /// to watch en-route pickup / storage routing / bulk hauling under heavy load.
    ///
    /// <para>Dev-only (it's a <c>[DebugAction]</c>, which Dev Mode gates). It uses RimWorld's own
    /// <see cref="Autotests_ColonyMaker"/> private layout helpers (accessible because HD publicizes
    /// Assembly-CSharp) exactly as WYU did — verified present in the live assembly. It does NOT auto-haul or
    /// touch HD settings; it only builds the scenario.</para>
    /// </summary>
    public static class HaulersDreamDebugActions_MakeColony
    {
        [DebugAction("Hauler's Dream", "Make colony (stress test)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MakeColony()
        {
            var godMode = DebugSettings.godMode;
            DebugSettings.godMode = true;
            DebugViewSettings.drawOpportunisticJobs = true;

            Thing.allowDestroyNonDestroyable = true;
            if (Autotests_ColonyMaker.usedCells == null)
                Autotests_ColonyMaker.usedCells = new BoolGrid(Autotests_ColonyMaker.Map);
            else
                Autotests_ColonyMaker.usedCells.ClearAndResizeTo(Autotests_ColonyMaker.Map);
            Autotests_ColonyMaker.overRect = new CellRect(
                Autotests_ColonyMaker.Map.Center.x - 50, Autotests_ColonyMaker.Map.Center.z - 50, 100, 50);
            Autotests_ColonyMaker.DeleteAllSpawnedPawns();
            GenDebug.ClearArea(Autotests_ColonyMaker.overRect, Find.CurrentMap);

            Autotests_ColonyMaker.Map.wealthWatcher.ForceRecount();

            // Every work table, every recipe billed.
            Autotests_ColonyMaker.TryGetFreeRect(90, 30, out var pawnAndThingRect);
            foreach (var thingDef in from def in DefDatabase<ThingDef>.AllDefs
                     where typeof(Building_WorkTable).IsAssignableFrom(def.thingClass)
                     select def)
            {
                if (Autotests_ColonyMaker.TryMakeBuilding(thingDef) is Building_WorkTable workTable)
                {
                    foreach (var recipe in workTable.def.AllRecipes)
                        workTable.billStack.AddBill(recipe.MakeNewBill());
                }
            }

            // One of every debug-spawnable item, scattered in the patch.
            pawnAndThingRect = pawnAndThingRect.ContractedBy(1);
            var itemDefs = (from def in DefDatabase<ThingDef>.AllDefs
                where DebugThingPlaceHelper.IsDebugSpawnable(def) && def.category == ThingCategory.Item
                select def).ToList();
            foreach (var itemDef in itemDefs)
                DebugThingPlaceHelper.DebugSpawn(itemDef, pawnAndThingRect.RandomCell, -1, true);

            // 30 colonists, all work enabled at priority 3 EXCEPT hauling (the stress test: hauling must happen
            // only via opportunistic / HD detours).
            var pawnCount = 30;
            var allWork = Enumerable.Repeat(TimeAssignmentDefOf.Work, 24).ToList();
            for (var i = 0; i < pawnCount; i++)
            {
                var pawn = PawnGenerator.GeneratePawn(Faction.OfPlayer.def.basicMemberKind, Faction.OfPlayer);
                pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.NewColonyOptimism);
                pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.NewColonyHope);
                pawn.timetable.times = allWork;

                foreach (var w in DefDatabase<WorkTypeDef>.AllDefs)
                {
                    if (!pawn.WorkTypeIsDisabled(w))
                        pawn.workSettings.SetPriority(w, 3);
                }
                pawn.workSettings.Disable(WorkTypeDefOf.Hauling);
                GenSpawn.Spawn(pawn, pawnAndThingRect.RandomCell, Autotests_ColonyMaker.Map);
            }

            // Seven Normal-priority resource stockpiles.
            var designated = new Designator_ZoneAddStockpile_Resources();
            for (var _ = 0; _ < 7; _++)
            {
                Autotests_ColonyMaker.TryGetFreeRect(8, 8, out var stockpileRect);
                stockpileRect = stockpileRect.ContractedBy(1);
                designated.DesignateMultiCell(stockpileRect.Cells);
                ((Zone_Stockpile)Autotests_ColonyMaker.Map.zoneManager.ZoneAt(stockpileRect.CenterCell))
                    .settings.Priority = StoragePriority.Normal;
            }

            Autotests_ColonyMaker.ClearAllHomeArea();
            Autotests_ColonyMaker.FillWithHomeArea(Autotests_ColonyMaker.overRect);
            DebugSettings.godMode = godMode;
            Thing.allowDestroyNonDestroyable = false;
        }
    }
}
