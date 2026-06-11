using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Mod entry point: loads settings and applies all Harmony patches in this assembly.
    /// </summary>
    public class HaulersDreamMod : Mod
    {
        public const string HarmonyId = "giwaffed.HaulersDream";

        public static HaulersDreamMod Instance { get; private set; }
        public static HaulersDreamSettings Settings { get; private set; }

        public HaulersDreamMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<HaulersDreamSettings>();

            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[Hauler's Dream] initialised — carry limit defaults to each pawn's max carrying capacity.");
        }

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);

        public override string SettingsCategory() => "Hauler's Dream";
    }

    /// <summary>Verbose logging gated behind the mod setting (see .docs/02 on diagnostics).</summary>
    public static class HDLog
    {
        public static void Dbg(string message)
        {
            if (HaulersDreamMod.Settings != null && HaulersDreamMod.Settings.verboseLogging)
                Log.Message("[Hauler's Dream] " + message);
        }
    }
}
