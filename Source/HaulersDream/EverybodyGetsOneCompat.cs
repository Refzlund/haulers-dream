using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// "Everybody Gets One" (alextd / Uuugggg — Steam "Everybody Gets One - Continued") compatibility bridge —
    /// REFLECTION ONLY, no hard assembly reference.
    ///
    /// EGO adds three custom <see cref="BillRepeatModeDef"/>s (TD_PersonCount "one per person", TD_XPerPerson
    /// "X per person", TD_WithSurplusIng "with surplus") and injects their entries into the repeat-mode dropdown
    /// with a TRANSPILER on <c>BillRepeatModeUtility.MakeConfigFloatMenu</c>: it hooks the
    /// <c>new List&lt;FloatMenuOption&gt;()</c> and calls its own <c>InsertMode(list, bill)</c> to add the options.
    ///
    /// HD's batch feature rebuilds that same menu from a Prefix that returns <c>false</c> — which SKIPS the
    /// original method body, and with it EGO's transpiler, so EGO's modes silently vanish from the dropdown. That
    /// is the reported "the bill doesn't show up when trying to craft clothing / doesn't work at all with this
    /// enabled" bug: a player can no longer pick EGO's "one per person" mode.
    ///
    /// This bridge lets HD's rebuilt menu invoke EGO's OWN <c>InsertMode</c> — so every EGO mode reappears with
    /// EGO's exact labels and per-mode guards (CanCountProducts / single-ingredient checks) — instead of HD trying
    /// to reproduce them. Fail-open when EGO is absent (nothing inserted; HD's menu is unchanged). Mirrors the
    /// reflection-soft-dep style of <see cref="CommonSenseCompat"/> / CECompat.
    /// </summary>
    public static class EverybodyGetsOneCompat
    {
        private static bool initialized;
        // Everybody_Gets_One.MakeConfigFloatMenu_Patch.InsertMode(List<FloatMenuOption>, Bill_Production)
        // -> mutates the passed list in place (and returns it). Cached; the bool VALUE (active) is derived from it.
        private static MethodInfo insertModeMethod;

        /// <summary>Whether Everybody Gets One is loaded and its menu-insertion method resolved. Cached.</summary>
        public static bool IsActive
        {
            get { if (!initialized) Init(); return insertModeMethod != null; }
        }

        /// <summary>
        /// Append EGO's repeat-mode menu entries to <paramref name="options"/> by invoking EGO's own
        /// <c>InsertMode</c> (so its labels + per-mode guards stay authoritative). No-op when EGO is absent or its
        /// method didn't resolve, so HD's repeat-mode menu is unchanged without EGO.
        /// </summary>
        public static void TryInsertModes(List<FloatMenuOption> options, Bill_Production bill)
        {
            if (!initialized)
                Init();
            if (insertModeMethod == null || options == null || bill == null)
                return;
            // EGO's InsertMode adds its FloatMenuOptions to the list in place; the returned list is ignored.
            insertModeMethod.Invoke(null, new object[] { options, bill });
        }

        private static void Init()
        {
            initialized = true;
            // TypeByName returns null (never throws) when EGO isn't loaded — that is the real precondition.
            var patchType = AccessTools.TypeByName("Everybody_Gets_One.MakeConfigFloatMenu_Patch");
            if (patchType == null)
                return; // EGO not loaded — HD's menu shows only vanilla + HD-batch entries, exactly as before.
            insertModeMethod = AccessTools.Method(patchType, "InsertMode",
                new[] { typeof(List<FloatMenuOption>), typeof(Bill_Production) });
            if (insertModeMethod != null)
                Log.Message("[Hauler's Dream] Everybody Gets One detected — its bill repeat modes are surfaced in "
                            + "the batch-aware repeat-mode menu.");
            else
                HDLog.Warn("Everybody Gets One present but MakeConfigFloatMenu_Patch.InsertMode did not resolve "
                           + "(a version/rename?); its repeat modes will not appear in HD's repeat-mode menu.");
        }
    }
}
