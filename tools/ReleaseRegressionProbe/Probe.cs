using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ReleaseRegressionProbe
{
    public class ProbeMod : Mod
    {
        public ProbeMod(ModContentPack pack) : base(pack)
        {
            GenCommandLine.TryGetCommandLineArg("regression-output", out Probe.Output);
            GenCommandLine.TryGetCommandLineArg("regression-mode", out Probe.Mode);
            if (Probe.Output == null) throw new InvalidOperationException("Private probe arguments required");
            if (!Path.GetFullPath(GenFilePaths.SaveDataFolderPath).StartsWith("C:\\HDQA\\release-regressions-20261002\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Private savedata required");
            Directory.CreateDirectory(Probe.Output);
            Application.runInBackground = true;
            StartupTrace.Install();
            if (Probe.Mode.Contains("foreign")) HaulProbe.InstallForeignPredicate();
            Probe.Monitor = new System.Threading.Timer(_ =>
            {
                try
                {
                    var ev = AccessTools.Field(typeof(LongEventHandler), "currentEvent").GetValue(null);
                    string status = DateTime.UtcNow.ToString("O") + " event=" + (ev == null ? "none" : AccessTools.Field(ev.GetType(), "eventText").GetValue(ev));
                    var profilers = AccessTools.Field(typeof(DeepProfiler), "deepProfilers").GetValue(null) as System.Collections.IDictionary;
                    lock (AccessTools.Field(typeof(DeepProfiler), "DeepProfilersLock").GetValue(null))
                    foreach (System.Collections.DictionaryEntry item in profilers)
                    {
                        var stack = AccessTools.Field(item.Value.GetType(), "watchers").GetValue(item.Value) as System.Collections.IEnumerable;
                        status += " thread=" + item.Key;
                        foreach (var watcher in stack) status += " / " + AccessTools.Field(watcher.GetType(), "label").GetValue(watcher);
                    }
                    File.AppendAllText(Path.Combine(Probe.Output, "loading.txt"), status + Environment.NewLine);
                }
                catch { }
            }, null, 5000, 10000);
        }
    }
    [StaticConstructorOnStartup] public static class Loader
    {
        static Loader() { LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame(Probe.Mode != "verify" ? "Benchmark20" : "RegressionBeforeUpdate")); }
    }
    public sealed class ProbeGame : GameComponent
    {
        public ProbeGame(Game game) { }
        public override void LoadedGame() { Probe.Loaded = true; }
        public override void GameComponentUpdate() { if (Probe.Mode.StartsWith("haul")) HaulProbe.Update(); else Probe.Update(); }
        public override void GameComponentTick() { if (Probe.Mode.StartsWith("haul")) HaulProbe.Tick(); }
    }
    internal static class Probe
    {
        internal static string Output, Mode;
        internal static System.Threading.Timer Monitor;
        internal static bool Loaded;
        private static bool done;
        internal static void Update()
        {
            if (!Loaded || done || LongEventHandler.ShouldWaitForEvent || Current.ProgramState != ProgramState.Playing) return;
            done = true;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (Mode == "refuel") { RefuelProbe.Run(); Application.Quit(); return; }
                if (Mode.StartsWith("enroute")) { EnRouteProbe.Run(); Application.Quit(); return; }
                var map = Find.CurrentMap;
                var report = new List<string>();
                var hd = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "HaulersDream");
                var witness = hd?.GetType("HaulersDream.RefuelNativeWitness");
                report.Add("HDReady=" + (witness == null ? "absent" : AccessTools.Property(witness, "Ready").GetValue(null).ToString()));
                report.Add("Mods=" + string.Join(",", LoadedModManager.RunningModsListForReading.Select(m => m.PackageId)));
                if (Mode == "produce")
                {
                    var created = new List<Thing>();
                    var defs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.category == ThingCategory.Building && d.comps != null && d.comps.Any(c => c.compClass != null && c.compClass.Name.Contains("Refuelable"))).OrderBy(d => d.defName).Take(30).ToList();
                    foreach (var def in defs)
                    {
                        var thing = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
                        thing.SetFaction(Faction.OfPlayer);
                        int i = created.Count;
                        GenSpawn.Spawn(thing, new IntVec3(35 + i % 6 * 12, 0, 35 + i / 6 * 12), map, Rot4.North);
                        var fuel = thing.TryGetComp<CompRefuelable>();
                        if (fuel != null) fuel.Refuel(5f);
                        created.Add(thing);
                    }
                    File.WriteAllLines(Path.Combine(Output, "expected.txt"), created.Select(t => t.ThingID + "|" + t.def.defName));
                    report.Add("Created=" + created.Count);
                    if (created.Count < 10) throw new InvalidOperationException("Insufficient native refuelable coverage");
                    GameDataSaveLoader.SaveGame("RegressionBeforeUpdate");
                }
                else
                {
                    var expected = File.ReadAllLines(Path.Combine(Output, "expected.txt"));
                    var actual = new HashSet<string>(map.listerThings.AllThings.Select(t => t.ThingID + "|" + t.def.defName));
                    var missing = expected.Where(e => !actual.Contains(e)).ToArray();
                    report.Add("Expected=" + expected.Length + "; Retained=" + (expected.Length - missing.Length));
                    report.Add("Missing=" + string.Join(",", missing));
                    // Exercise the composed ordinary refuel method, not only patch metadata.
                    int consumers = 0;
                    foreach (var t in map.listerThings.AllThings.ToList())
                    {
                        if (!expected.Contains(t.ThingID + "|" + t.def.defName)) continue;
                        var fuel = t.TryGetComp<CompRefuelable>();
                        if (fuel == null || fuel.IsFull) continue;
                        var fuelDef = fuel.Props.fuelFilter.AllowedThingDefs.FirstOrDefault(d => d.category == ThingCategory.Item);
                        if (fuelDef == null) continue;
                        var item = ThingMaker.MakeThing(fuelDef); item.stackCount = 1;
                        float before = fuel.Fuel;
                        fuel.Refuel(new List<Thing> { item });
                        if (fuel.Fuel <= before || !item.Destroyed) throw new InvalidOperationException("Native provider refuel did not consume and pay: " + t.def.defName);
                        consumers++;
                    }
                    report.Add("NativeFuelConsumers=" + consumers);
                    File.WriteAllLines(Path.Combine(Output, "observations.txt"), report);
                    if (consumers < 1 || missing.Length != 0) throw new InvalidOperationException("Save-load/refuel regression");
                    GameDataSaveLoader.SaveGame("RegressionAfterUpdate");
                }
                report.Add("PASS");
                File.WriteAllLines(Path.Combine(Output, "result.txt"), report);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(Output, "failure.txt"), e.ToString()); Log.Error(e.ToString()); }
            Application.Quit();
        }
    }
}
