using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// "Compositable Loadouts" (Wiri / simplyWiri — Steam id 2679126859, assembly <c>Inventory.dll</c>, namespace
    /// <c>Inventory</c>) compatibility bridge — REFLECTION ONLY, no hard assembly reference.
    ///
    /// CL adds a custom <see cref="BillRepeatModeDef"/> <c>W_PerTag</c> ("X per Tag" — make one product per colonist
    /// assigned a given loadout tag) and injects its dropdown entry with a TRANSPILER on
    /// <c>BillRepeatModeUtility.MakeConfigFloatMenu</c>: it splices a call to its own static
    /// <c>Inventory.MakeConfigFloatMenu_Patch.GetOptions(list, bill)</c> in just before the vanilla RepeatCount entry.
    ///
    /// HD's batch feature rebuilds that same menu from a Prefix that returns <c>false</c> — which SKIPS the original
    /// method body, and with it CL's transpiler, so CL's "X per Tag" mode silently vanishes from the dropdown (issue
    /// #92 — the same class of breakage as the Everybody Gets One bug, see <see cref="EverybodyGetsOneCompat"/>).
    ///
    /// This bridge lets HD's rebuilt menu invoke CL's OWN <c>GetOptions</c> — so its entry reappears with CL's exact
    /// label, guard, and per-bill setup (its option action sets <c>repeatMode</c>/<c>targetCount</c>/<c>repeatCount</c>/
    /// <c>includeEquipped</c>) — instead of HD reproducing it. <c>GetOptions</c> only adds a <c>FloatMenuOption</c> and,
    /// inside that option's action, sets fields on the passed bill; it touches none of CL's <c>LoadoutManager</c> state,
    /// so it is safe to call from the bill-config UI on any map. Fail-open when CL is absent (nothing inserted; HD's
    /// menu is unchanged). Mirrors the reflection-soft-dep style of <see cref="EverybodyGetsOneCompat"/> / CECompat.
    ///
    /// HD never tries to BATCH a <c>W_PerTag</c> bill: <see cref="CraftBatchPlanner.CanBatch"/> only accepts the three
    /// vanilla repeat modes, so a CL-mode bill routes as plain vanilla and HD's "Batch: …" variants are not offered —
    /// CL's per-tag counting/gating runs untouched.
    /// </summary>
    public static class CompositableLoadoutsCompat
    {
        private static bool initialized;
        // Inventory.MakeConfigFloatMenu_Patch.GetOptions(List<FloatMenuOption>, Bill_Production)
        // -> mutates the passed list in place (adds CL's "X per Tag" entry); returns void. Cached.
        private static MethodInfo getOptionsMethod;

        /// <summary>Whether Compositable Loadouts is loaded and its menu-insertion method resolved. Cached.</summary>
        public static bool IsActive
        {
            get { if (!initialized) Init(); return getOptionsMethod != null; }
        }

        /// <summary>
        /// Append Compositable Loadouts' repeat-mode menu entry to <paramref name="options"/> by invoking CL's own
        /// <c>GetOptions</c> (so its label + guard + bill setup stay authoritative). No-op when CL is absent or its
        /// method didn't resolve, so HD's repeat-mode menu is unchanged without CL.
        /// </summary>
        public static void TryInsertModes(List<FloatMenuOption> options, Bill_Production bill)
        {
            if (!initialized)
                Init();
            if (getOptionsMethod == null || options == null || bill == null)
                return;
            // CL's GetOptions adds its FloatMenuOption(s) to the list in place; it returns void.
            getOptionsMethod.Invoke(null, new object[] { options, bill });
        }

        private static void Init()
        {
            initialized = true;
            // TypeByName returns null (never throws) when CL isn't loaded — that is the real precondition.
            var patchType = AccessTools.TypeByName("Inventory.MakeConfigFloatMenu_Patch");
            if (patchType == null)
                return; // CL not loaded — HD's menu shows only vanilla + HD-batch entries, exactly as before.
            getOptionsMethod = AccessTools.Method(patchType, "GetOptions",
                new[] { typeof(List<FloatMenuOption>), typeof(Bill_Production) });
            if (getOptionsMethod != null)
                Log.Message("[Hauler's Dream] Compositable Loadouts detected — its 'X per Tag' bill repeat mode is "
                            + "surfaced in the batch-aware repeat-mode menu.");
            else
                HDLog.Warn("Compositable Loadouts present but MakeConfigFloatMenu_Patch.GetOptions did not resolve "
                           + "(a version/rename?); its repeat mode will not appear in HD's repeat-mode menu.");
        }
    }
}
