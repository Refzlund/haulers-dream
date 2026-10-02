using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ReleaseRegressionProbe
{
    internal static class RefuelProbe
    {
        static int searches;
        static void Search() { searches++; }
        internal static void Run()
        {
            var report=new List<string>();
            var map=Find.CurrentMap;
            var pawn=map.mapPawns.FreeColonistsSpawned.First();
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            pawn.Position=new IntVec3(60,0,60);pawn.inventory.innerContainer.ClearAndDestroyContents();
            var stove=(Building)ThingMaker.MakeThing(ThingDef.Named("FueledStove"));stove.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(stove,new IntVec3(66,0,60),map);
            var comp=stove.TryGetComp<CompRefuelable>();
            var ground=new List<Thing>();
            for(int i=0;i<2;i++){var wood=ThingMaker.MakeThing(ThingDefOf.WoodLog);wood.stackCount=5;GenSpawn.Spawn(wood,new IntVec3(61+i,0,62),map);ground.Add(wood);}
            new Harmony("local.regression.refuel").Patch(AccessTools.Method(typeof(RefuelWorkGiverUtility),"FindEnoughReservableThings"),new HarmonyMethod(typeof(RefuelProbe),nameof(Search)));
            var giver=new WorkGiver_Refuel();int jobs=0;
            for(int i=0;i<100;i++)if(giver.HasJobOnThing(pawn,stove))jobs++;
            report.Add("GroundEligibilityJobs="+jobs+"; GroundPlanSearches="+searches);
            int before=searches;var job=giver.JobOnThing(pawn,stove);
            report.Add("SelectedJob="+job?.def.defName+"; SelectedSearches="+(searches-before));
            foreach(var wood in ground)wood.Destroy();
            var held=ThingMaker.MakeThing(ThingDefOf.WoodLog);held.stackCount=10;pawn.inventory.innerContainer.TryAdd(held,false);
            before=searches;
            bool owned=giver.HasJobOnThing(pawn,stove);int heldSearches=searches-before;
            report.Add("HeldOnlyEligibility="+owned+"; HeldOnlyGroundSearches="+heldSearches);
            var heldJob=giver.JobOnThing(pawn,stove);report.Add("HeldOnlyJob="+heldJob?.def.defName);
            bool pass=jobs==100 && before==1 && job?.def.defName=="HaulersDream_BulkRefuel" && owned && heldSearches==0 && heldJob?.def.defName=="HaulersDream_BulkRefuel";
            report.Add(pass?"PASS":"FAIL");File.WriteAllLines(Path.Combine(Probe.Output,"result.txt"),report);
        }
    }
}
