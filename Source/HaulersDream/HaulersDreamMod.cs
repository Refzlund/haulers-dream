using System;
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
            ApplyPatchesResilient(harmony, Assembly.GetExecutingAssembly());
            Log.Message("[Hauler's Dream] initialised — carry limit defaults to each pawn's max carrying capacity.");
        }

        /// <summary>
        /// Like <c>harmony.PatchAll(assembly)</c>, but applies each annotated patch class in its OWN try/catch so a
        /// single unresolvable target — e.g. a private vanilla method renamed in a future RimWorld point-release —
        /// degrades that ONE feature with a logged warning instead of throwing inside <c>PatchAll</c> and taking
        /// down ALL of the mod's patches (the catastrophic-failure mode: one rename = total mod death in a large
        /// load order). <c>[HarmonyPriority]</c> still governs per-target injection order, so behavior is unchanged
        /// when every target resolves.
        ///
        /// We ONLY process types that are genuine patch containers — i.e. carry a DIRECT (non-inherited) Harmony
        /// attribute on the class or on a method. Harmony's own <c>PatchAll</c> calls <c>CreateClassProcessor(t).Patch()</c>
        /// on EVERY type and relies on a null container-attribute set to skip non-patches; but its attribute lookup
        /// uses <c>GetCustomAttributes(inherit: true)</c>, which mis-classifies this mod's OWN <c>JobDriver</c>
        /// subclasses (they inherit attributes through the vanilla <c>JobDriver</c> chain) and makes Harmony try to
        /// patch <c>JobDriver.Cleanup</c> for each — exactly the spurious failures the inherit:false filter below
        /// excludes. The filter captures every real HD patch (all use a direct class- or method-level attribute).
        /// </summary>
        private static void ApplyPatchesResilient(Harmony harmony, Assembly assembly)
        {
            int applied = 0, failed = 0;
            // GetTypesFromAssembly tolerates a ReflectionTypeLoadException (returns the loadable types).
            foreach (var type in AccessTools.GetTypesFromAssembly(assembly))
            {
                if (!IsHarmonyPatchContainer(type))
                    continue; // the mod's own JobDrivers/Comps/etc. — not patches
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    applied++;
                }
                catch (System.Exception e)
                {
                    failed++;
                    HDLog.Err($"patch class '{type.Name}' could not be applied on this RimWorld build "
                        + "(a hooked vanilla target is likely missing or renamed) — that feature is disabled; the "
                        + $"rest of the mod continues. {e.GetType().Name}: {e.Message}");
                }
            }
            if (failed > 0)
                HDLog.Warn($"{applied} patch class(es) applied, {failed} skipped due to missing targets (see errors above).");
        }

        // A genuine patch container has a DIRECT (inherit:false) Harmony attribute on the class, or a method carrying
        // a Harmony injection/patch attribute. Deliberately inherit:false so the mod's own JobDriver subclasses
        // (which inherit attributes via the vanilla JobDriver chain) are NOT treated as patches.
        private static bool IsHarmonyPatchContainer(Type type)
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0)
                return true;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var m in type.GetMethods(all))
            {
                if (m.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0
                    || m.GetCustomAttributes(typeof(HarmonyPrefix), inherit: false).Length > 0
                    || m.GetCustomAttributes(typeof(HarmonyPostfix), inherit: false).Length > 0
                    || m.GetCustomAttributes(typeof(HarmonyTranspiler), inherit: false).Length > 0
                    || m.GetCustomAttributes(typeof(HarmonyFinalizer), inherit: false).Length > 0
                    || m.GetCustomAttributes(typeof(HarmonyReversePatch), inherit: false).Length > 0)
                    return true;
            }
            return false;
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

        /// <summary>An ALWAYS-emitted warning (not Dev/verbose-gated) carrying the standard prefix — for genuine
        /// degrade-but-keep-going conditions (e.g. an optional mod is present but a load-bearing reflected member
        /// did not bind, so a feature is silently disabled). No dedup: each call logs (callers self-gate with a
        /// `warned` latch where one-shot is wanted).</summary>
        public static void Warn(string message)
        {
            Log.Warning("[Hauler's Dream] " + message);
        }

        /// <summary>An ALWAYS-emitted error (not Dev/verbose-gated) carrying the standard prefix — for fail-loud
        /// faults (a transpiler IL match broke, a foreign WorkGiver threw). No dedup: callers that need
        /// once-per-key dedup must keep <c>Log.ErrorOnce</c> instead.</summary>
        public static void Err(string message)
        {
            Log.Error("[Hauler's Dream] " + message);
        }
    }
}
