using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ReleaseRegressionProbe
{
    internal static class HaulProbe
    {
        static bool started, done;
        static Pawn pawn;
        static int ticks, initial, frames;
        static Stopwatch time;
        static StreamWriter trace;
        internal static void InstallForeignPredicate()
        {
            // Real foreign Harmony registration, with unchanged native acceptance semantics.
            // Exercises HD's unknown-provider policy without inventing a particular storage mod's rules.
            new Harmony("local.regression.foreign-storage").Patch(
                AccessTools.Method(typeof(StoreUtility), "NoStorageBlockersIn", new[] { typeof(IntVec3), typeof(Map), typeof(Thing) }),
                postfix: new HarmonyMethod(typeof(HaulProbe), nameof(ForeignPredicate)));
        }
        static void ForeignPredicate() { }
        internal static void Update()
        {
            if (!Probe.Loaded || done || LongEventHandler.ShouldWaitForEvent) return;
            if (++frames < 120) return; // Allow mod MapLoaded/queued startup work to finish first.
            if (started && frames % 120 == 0) trace.WriteLine("frame="+frames+" ticks="+ticks+" paused="+Find.TickManager.Paused+" windows="+string.Join(",",Find.WindowStack.Windows.Select(w=>w.GetType().FullName)));
            if (!started)
            {
                started = true;
                try { Setup(); }
                catch (Exception e) { File.WriteAllText(Path.Combine(Probe.Output,"failure.txt"),e.ToString()); done=true; Application.Quit(); }
            }
        }
        static void Setup()
        {
            time = Stopwatch.StartNew();
            trace = new StreamWriter(Path.Combine(Probe.Output,"trace.txt")) { AutoFlush = true };
            var harmony = new Harmony("local.regression.trace");
            var type = AccessTools.TypeByName("HaulersDream.StorageCommitments");
            if (type != null) harmony.Patch(AccessTools.Method(type,"AdmitBulkParcel"),new HarmonyMethod(typeof(HaulProbe),nameof(BeforeAdmit)),new HarmonyMethod(typeof(HaulProbe),nameof(AfterAdmit)));
            var map=Find.CurrentMap;
            foreach (var p in map.mapPawns.FreeColonistsSpawned.ToList()) { p.jobs.EndCurrentJob(JobCondition.InterruptForced); p.drafter.Drafted=true; }
            pawn=map.mapPawns.FreeColonistsSpawned.First();pawn.drafter.Drafted=false;
            pawn.Position=new IntVec3(60,0,60);
            foreach (var z in map.zoneManager.AllZones.ToList()) map.zoneManager.DeregisterZone(z);
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,map.zoneManager);
            map.zoneManager.RegisterZone(zone);zone.settings.filter.SetDisallowAll();zone.settings.filter.SetAllow(ThingDefOf.Steel,true);zone.settings.Priority=StoragePriority.Critical;
            for(int x=90;x<100;x++)for(int z=90;z<100;z++)zone.AddCell(new IntVec3(x,0,z));
            var things = new List<Thing>();
            for(int i=0;i<12;i++) { var t=ThingMaker.MakeThing(ThingDefOf.Steel);t.stackCount=10;GenSpawn.Spawn(t,new IntVec3(62+i%4*2,0,62+i/4*2),map);things.Add(t); }
            initial=120;
            foreach(var d in DefDatabase<WorkTypeDef>.AllDefsListForReading) if(!pawn.WorkTypeIsDisabled(d))pawn.workSettings.SetPriority(d,d==WorkTypeDefOf.Hauling?1:0);
            // Genuine work-giver job and native StartJob; no direct driver/toil calls.
            WorkGiver_Scanner giver=new WorkGiver_HaulGeneral();
            string giverName="HaulGeneral";
            if(Probe.Mode.Contains("urgent"))
            {
                giverName="HaulUrgently";
                giver=(WorkGiver_Scanner)DefDatabase<WorkGiverDef>.GetNamed(giverName).Worker;
                var designation=DefDatabase<DesignationDef>.GetNamed("HaulUrgentlyDesignation");
                foreach(var t in things)map.designationManager.AddDesignation(new Designation(t,designation));
                trace.WriteLine("Provider="+giver.GetType().AssemblyQualifiedName);
            }
            var job=giver.JobOnThing(pawn,things[0],false);
            trace.WriteLine("Built="+job?.def?.defName+" counts="+(job?.countQueue==null?"none":string.Join(",",job.countQueue)));
            if(job==null)throw new InvalidOperationException("No native hauling job");
            var expectedDef=job.def;
            job.workGiverDef=DefDatabase<WorkGiverDef>.GetNamed(giverName);
            pawn.jobs.StartJob(job,JobCondition.InterruptForced);
            trace.WriteLine("AfterStart="+pawn.CurJob?.def?.defName);
            if(pawn.CurJob?.def!=expectedDef)throw new InvalidOperationException("The provider job was rejected by native StartJob");
            Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
        }
        static void BeforeAdmit(out long __state) { __state=Stopwatch.GetTimestamp(); }
        static void AfterAdmit(bool __result,long __state) { trace.WriteLine("Admit="+__result+" ms="+((Stopwatch.GetTimestamp()-__state)*1000d/Stopwatch.Frequency)); }
        internal static void Tick()
        {
            if(!started||done)return;
            ticks++;
            if(ticks%120==0)trace.WriteLine("tick="+ticks+" job="+pawn.CurJob+" pos="+pawn.Position+" steel="+pawn.inventory.innerContainer.Where(t=>t.def==ThingDefOf.Steel).Sum(t=>t.stackCount)+" seconds="+time.Elapsed.TotalSeconds);
            if(ticks<6000)return;
            done=true;
            var map=Find.CurrentMap;
            var stored=map.listerThings.ThingsOfDef(ThingDefOf.Steel).Where(t=>t.Position.x>=90&&t.Position.x<100&&t.Position.z>=90&&t.Position.z<100).Sum(t=>t.stackCount);
            File.WriteAllText(Path.Combine(Probe.Output,"result.txt"),"stored="+stored+"; initial="+initial+"; ticks="+ticks+"; seconds="+time.Elapsed.TotalSeconds+"; "+(stored==initial?"PASS":"FAIL"));
            trace.Dispose();Application.Quit();
        }
    }
}
