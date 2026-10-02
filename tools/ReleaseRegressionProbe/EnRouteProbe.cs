using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ReleaseRegressionProbe
{
    internal static class EnRouteProbe
    {
        internal static void Run()
        {
            var map=Find.CurrentMap;
            foreach(var p in map.mapPawns.FreeColonistsSpawned.ToList()) { p.jobs.EndCurrentJob(JobCondition.InterruptForced); p.drafter.Drafted=true; }
            var pawn=map.mapPawns.FreeColonistsSpawned.First();pawn.drafter.Drafted=false;
            foreach(var z in map.zoneManager.AllZones.ToList())map.zoneManager.DeregisterZone(z);
            var zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,map.zoneManager);
            map.zoneManager.RegisterZone(zone);zone.settings.filter.SetDisallowAll();zone.settings.filter.SetAllow(ThingDefOf.Steel,true);zone.settings.Priority=StoragePriority.Critical;
            zone.AddCell(new IntVec3(90,0,90));
            var thing=ThingMaker.MakeThing(ThingDefOf.Steel);thing.stackCount=10;GenSpawn.Spawn(thing,new IntVec3(62,0,62),map);
            var type=AccessTools.TypeByName("HaulersDream.Patch_Pawn_JobTracker_EnRoutePickup");
            var settings=AccessTools.Property(AccessTools.TypeByName("HaulersDream.HaulersDreamMod"),"Settings").GetValue(null);
            // Isolate the actual producer seam. This does not claim route-selection coverage.
            var job=(Job)AccessTools.Method(type,"BuildEnRouteJob").Invoke(null,new object[]{pawn,thing,new IntVec3(120,0,120),settings});
            string result="Producer="+(job?.def.defName??"null")+Environment.NewLine;
            if(job!=null)
            {
                var expected=job.def;
                pawn.jobs.StartJob(job,JobCondition.InterruptForced);
                result+="AfterStart="+pawn.CurJob?.def.defName+Environment.NewLine;
                result+=pawn.CurJob?.def==expected ? "PASS" : "FAIL";
            }
            else result+=Probe.Mode.Contains("foreign") ? "PASS: inadmissible optional pickup declined" : "FAIL: supported pickup unexpectedly declined";
            File.WriteAllText(Path.Combine(Probe.Output,"result.txt"),result);
        }
    }
}
