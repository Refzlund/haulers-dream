using System.Reflection;
using HarmonyLib;
using RimWorld;
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

        public override void WriteSettings()
        {
            base.WriteSettings();
            // Re-sync work priorities when the settings window closes: toggling a work override
            // (allPawnsCanHaul/Clean/CutPlants) OFF otherwise leaves STALE priorities — the work scan runs off
            // priorities, so a pawn keeps doing the now-forbidden work while the work tab draws the box locked
            // (vanilla only re-syncs disabled work types on save load). Notifying unconditionally on every
            // settings close is cheap (it just re-reads GetDisabledWorkTypes and zeroes disabled priorities).
            // No try/catch: a re-sync failure is a real bug to surface as a red error, not a silent warning.
            if (Current.Game != null)
            {
                var pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (p?.Faction != null && p.Faction.IsPlayer)
                        p.Notify_DisabledWorkTypesChanged();
                }
            }
        }
    }

    /// <summary>Verbose logging gated behind the mod setting AND Dev Mode (parity with BLFT — debug spam never
    /// reaches a normal player even if the (now Dev-only) toggle was left on in an old config). See .docs/02.</summary>
    public static class HDLog
    {
        public static void Dbg(string message)
        {
            if (Prefs.DevMode && HaulersDreamMod.Settings != null && HaulersDreamMod.Settings.verboseLogging)
                Log.Message("[Hauler's Dream] " + message);
        }
    }
}
