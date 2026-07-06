using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// "Ingredient Threshold" (rileydoggy / 0x5A4159, Steam id 3677669154) compatibility bridge. REFLECTION
    /// ONLY, no hard assembly reference.
    ///
    /// <para>Ingredient Threshold adds one custom <see cref="BillRepeatModeDef"/> (its static
    /// <c>IngredientThreshold.ThresholdRepeatModeDef.IngredientThreshold</c>, "do the bill only while you have
    /// more than X of an ingredient") and injects its dropdown entry with a FULL-REPLACE Prefix on
    /// <c>BillRepeatModeUtility.MakeConfigFloatMenu</c> that returns <c>false</c>. HD's batch feature rebuilds that
    /// same menu from a Prefix that also returns <c>false</c>. Harmony runs prefixes highest-priority first and a
    /// prefix that returns <c>false</c> skips every remaining prefix, so among competing full-replace prefixes only
    /// the highest-priority one runs and the other mod's modes never get added; depending on load order that left
    /// Ingredient Threshold's mode out of the menu (the reported "I can't select it as an option for a bill after
    /// installing your mod").</para>
    ///
    /// <para>This bridge lets HD's rebuilt menu re-add Ingredient Threshold's mode entry, exactly as that mod's own
    /// prefix does (pick the mode; its own <c>Dialog_BillConfig</c> patch then draws the threshold controls and its
    /// <c>Bill.ShouldDoNow</c> prefix runs the gating). HD's prefix is ordered <c>Priority.First</c> so it is the
    /// prefix that runs (rather than being skipped by the other mod's), and it re-adds every mode so its menu is the
    /// complete one. Fail-open when the mod is absent (nothing inserted; HD's menu is unchanged). Mirrors
    /// <see cref="EverybodyGetsOneCompat"/> / <see cref="CompositableLoadoutsCompat"/>; unlike EGO it has no reusable
    /// inserter to call, so HD reproduces the single entry directly.</para>
    /// </summary>
    public static class IngredientThresholdCompat
    {
        private static bool initialized;
        // IngredientThreshold.ThresholdRepeatModeDef.IngredientThreshold, a [DefOf] static BillRepeatModeDef field,
        // populated once defs load. Read lazily at menu-build time (always after load). Cached FieldInfo.
        private static FieldInfo thresholdModeField;

        /// <summary>Whether Ingredient Threshold is loaded and its repeat-mode def field resolved. Cached.</summary>
        public static bool IsActive
        {
            get { if (!initialized) Init(); return thresholdModeField != null; }
        }

        /// <summary>
        /// Append Ingredient Threshold's repeat-mode entry to <paramref name="options"/>, reproducing that mod's own
        /// entry (its label; picking it sets <paramref name="bill"/>'s repeat mode to the threshold def). No-op when
        /// the mod is absent, its field didn't resolve, or the def isn't populated yet, so HD's repeat-mode menu is
        /// unchanged without it.
        /// </summary>
        public static void TryInsertModes(List<FloatMenuOption> options, Bill_Production bill)
        {
            if (!initialized)
                Init();
            if (thresholdModeField == null || options == null || bill == null)
                return;
            if (!(thresholdModeField.GetValue(null) is BillRepeatModeDef mode))
                return; // def not populated (shouldn't happen in-game), skip rather than add a broken entry
            // bill.repeatMode is vanilla's own field, already synced by RimWorld's bill-config UI in multiplayer, so
            // a direct set here matches how HD's own vanilla-mode entries (and Ingredient Threshold's) set it.
            options.Add(new FloatMenuOption(mode.LabelCap, () => bill.repeatMode = mode));
        }

        private static void Init()
        {
            initialized = true;
            // TypeByName returns null (never throws) when the mod isn't loaded; that is the real precondition.
            var type = AccessTools.TypeByName("IngredientThreshold.ThresholdRepeatModeDef");
            if (type == null)
                return; // not loaded, HD's menu shows only vanilla + HD-batch (+ other compat) entries, as before.
            thresholdModeField = AccessTools.Field(type, "IngredientThreshold");
            if (thresholdModeField != null)
                Log.Message("[Hauler's Dream] Ingredient Threshold detected, its repeat mode is surfaced in the "
                            + "batch-aware repeat-mode menu.");
            else
                HDLog.Warn("Ingredient Threshold present but ThresholdRepeatModeDef.IngredientThreshold did not "
                           + "resolve (a version/rename?); its repeat mode will not appear in HD's repeat-mode menu.");
        }
    }
}
