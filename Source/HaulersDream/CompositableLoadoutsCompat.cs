using System;
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

        // --- #200 inventory-KEEP API (a separate CL surface from the bill menu above). CL assigns each pawn a
        // loadout of items with desired counts and a think-node re-fetches any shortfall; HD's "adopt all surplus"
        // would ship those items to storage and CL re-fetches them (the unload<->pickup loop). KeepCount below sums
        // the loadout's desired count per def into HD's keep so a loadout item is never counted as surplus. Member
        // names are from CL's OPEN SOURCE (simplyWiri/Loadout-Compositing, namespace Inventory) but are NOT
        // decompile-verified (CL isn't installed here), so they are resolved reflectively + guarded: a rename
        // degrades to "keep nothing extra" with a logged warning, never a crash. Resolved lazily + independently of
        // the bill-menu method (a CL build could expose one and not the other).
        private static bool keepApiInitialized;
        private static bool keepApiOk;
        private static Type loadoutComponentType;   // Inventory.LoadoutComponent (a ThingComp on the pawn)
        private static MethodInfo loadoutGetter;     // LoadoutComponent.Loadout getter -> Inventory.Loadout
        private static MethodInfo itemsGetter;       // Loadout.Items getter -> IEnumerable<Inventory.Item>
        private static MethodInfo itemDefGetter;     // Item.Def getter -> ThingDef
        private static MethodInfo itemQtyGetter;     // Item.Quantity getter -> int

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

        /// <summary>
        /// How many of <paramref name="def"/> the pawn's Compositable Loadouts loadout wants kept in inventory — summed
        /// into HD's keep-count (see <see cref="InventorySurplus.KeepCountOf"/>) so "adopt all surplus" never ships a
        /// loadout item to storage that CL would immediately re-fetch (the #200 unload↔pickup loop). Returns 0 when CL
        /// is absent, the pawn has no loadout, the item isn't in it, or the keep API didn't resolve.
        ///
        /// Defensively try/caught — UNLIKE the compile-verified <see cref="CECompat.LoadoutKeepCount"/> — precisely
        /// because CL's members are resolved reflectively from its source names and were NOT decompile-verified against
        /// a running CL here (CL wasn't installed): a wrong/renamed member must degrade to "keep nothing" + one report,
        /// never crash HD's unload path.
        /// </summary>
        public static int KeepCount(Pawn pawn, ThingDef def)
        {
            if (!keepApiInitialized)
                InitKeepApi();
            if (!keepApiOk || pawn == null || def == null)
                return 0;
            var comp = FindLoadoutComp(pawn);
            if (comp == null)
                return 0; // this pawn has no CL loadout component -> nothing to keep
            try
            {
                var loadout = loadoutGetter.Invoke(comp, null);
                if (loadout == null)
                    return 0;
                if (!(itemsGetter.Invoke(loadout, null) is System.Collections.IEnumerable items))
                    return 0;
                int keep = 0;
                foreach (var item in items)
                {
                    if (item == null || (itemDefGetter.Invoke(item, null) as ThingDef) != def)
                        continue; // def-matched entries only; a generic filter entry (null Def) isn't shielded here
                    // Clamp per item at >= 0 (mirrors ItemPolicyCompat.KeepCount): a stray negative desired-count
                    // must never SUBTRACT from another loadout entry's keep and let surplus leak into the unload.
                    keep += Math.Max(0, (int)itemQtyGetter.Invoke(item, null));
                }
                return keep;
            }
            catch (Exception e)
            {
                // Stop probing a broken/renamed API for the rest of the session and report once (the loop may recur,
                // but HD's unload never crashes on CL). This is RECOVER + REPORT, not a silent swallow.
                keepApiOk = false;
                HDLog.ErrOnce("Compositable Loadouts keep-count read threw for " + (pawn.def?.defName ?? "a pawn")
                    + "; HD is standing down its CL loadout shield (an unload↔re-fetch loop may recur). Please report "
                    + "it (issue #200).\n" + e, 0x20C10AD7);
                return 0;
            }
        }

        // Resolve CL's loadout keep API lazily + independently of the bill-menu Init (a CL build could expose one and
        // not the other). Every member is guarded; keepApiOk is set only when the full read path resolved, and a
        // partial resolve is surfaced once so a silent CL rename doesn't just re-open the unload loop unnoticed.
        private static void InitKeepApi()
        {
            keepApiInitialized = true;
            loadoutComponentType = AccessTools.TypeByName("Inventory.LoadoutComponent");
            if (loadoutComponentType == null)
                return; // CL not loaded (or the component was renamed) -> keep nothing extra, no warning (absent is normal).
            loadoutGetter = AccessTools.PropertyGetter(loadoutComponentType, "Loadout");
            var loadoutType = AccessTools.TypeByName("Inventory.Loadout");
            itemsGetter = loadoutType != null ? AccessTools.PropertyGetter(loadoutType, "Items") : null;
            var itemType = AccessTools.TypeByName("Inventory.Item");
            itemDefGetter = itemType != null ? AccessTools.PropertyGetter(itemType, "Def") : null;
            itemQtyGetter = itemType != null ? AccessTools.PropertyGetter(itemType, "Quantity") : null;
            keepApiOk = loadoutGetter != null && itemsGetter != null && itemDefGetter != null && itemQtyGetter != null;
            if (!keepApiOk)
                HDLog.Warn("Compositable Loadouts present but its per-pawn loadout keep API (LoadoutComponent.Loadout "
                           + "/ Loadout.Items / Item.Def|Quantity) did not fully resolve — a CL version/rename likely. "
                           + "HD cannot shield CL-loadout items from 'adopt all surplus', so an unload↔re-fetch loop "
                           + "may recur. Please report it (issue #200). HD continues.");
        }

        // The pawn's Inventory.LoadoutComponent, matched on AllComps by the reflected type (TryGetComp<T> is generic,
        // and the concrete type is only known reflectively here).
        private static ThingComp FindLoadoutComp(Pawn pawn)
        {
            var comps = pawn.AllComps;
            if (comps == null)
                return null;
            for (int i = 0; i < comps.Count; i++)
                if (comps[i] != null && loadoutComponentType.IsInstanceOfType(comps[i]))
                    return comps[i];
            return null;
        }
    }
}
