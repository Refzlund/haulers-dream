using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// #192 — make "Cook with the most-stocked ingredient first" (<c>cookMostStockFirst</c>, #137) work when
    /// Common Sense is installed. CS transpiles the vanilla AllowMix cook-ingredient <c>SortBy</c> into its own
    /// <c>CommonSense.IngredientPriority.DoSort(List&lt;Thing&gt;, Bill)</c>, so HD CEDES its own competing
    /// transpiler to CS (two transpilers cannot rewrite the same call — see
    /// <see cref="Patch_WorkGiver_DoBill_TryFindBestBillIngredientsInSet_AllowMix"/>). The casualty was HD's
    /// most-stocked-first key: CS has no equivalent, and with HD's transpiler ceded the key never reached the
    /// vanilla chooser, so it "did nothing" for CS users (its only surviving path was HD's own batch-cook picker).
    ///
    /// The fix layers most-stock on top of CS WITHOUT touching a transpiler: a POSTFIX on <c>DoSort</c> runs after
    /// CS has sorted the candidate list (spoiling/medical first) and STABLE-reorders it so the most-stocked def
    /// floats forward while CS's order is preserved within each def (see
    /// <see cref="SpoilingFirst.ApplyMostStockStable"/>). DoSort mutates the SAME list the vanilla greedy fill loop
    /// then walks, so reordering it here changes only WHICH valid ingredient is chosen, never recipe satisfaction.
    ///
    /// Reflection-only soft dep: <see cref="Prepare"/> skips the whole patch when CS (or DoSort) is absent, so a
    /// non-CS game is byte-identical and HD's own transpiler owns the cook sort there (no conflict, since CS's
    /// transpiler isn't present to compete). Live-gated on the toggle, so it is a no-op when
    /// <see cref="HaulersDreamSettings.cookMostStockFirst"/> is off (CS's order untouched).
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CommonSense_IngredientPriority_DoSort
    {
        private static MethodBase targetMethod;

        /// <summary>Resolve CS's cook-ingredient sort once, at patch time. Returns false (skip the whole patch)
        /// when Common Sense is absent, or when <c>DoSort</c> did not resolve (a CS fork/rename) — HD then relies on
        /// its own transpiler, which is active precisely because CS's transpiler isn't there to conflict, so
        /// most-stock still works without CS. Surfaces a rename once so the compat loss is not silent.</summary>
        static bool Prepare()
        {
            if (targetMethod != null)
                return true;
            if (!CommonSenseCompat.IsActive)
                return false;
            var t = AccessTools.TypeByName("CommonSense.IngredientPriority");
            targetMethod = t == null
                ? null
                : AccessTools.Method(t, "DoSort", new[] { typeof(List<Thing>), typeof(Bill) });
            if (targetMethod == null)
                HDLog.Warn("Common Sense present but CommonSense.IngredientPriority.DoSort(List<Thing>, Bill) did "
                           + "not resolve; 'cook with the most-stocked ingredient first' cannot layer onto CS's "
                           + "cook-ingredient order (a CS rename likely). Please report it. HD continues.");
            return targetMethod != null;
        }

        static MethodBase TargetMethod() => targetMethod;

        /// <summary>Reorder the SAME list CS's <c>DoSort</c> just sorted. Positional <c>__0</c>/<c>__1</c> bind by
        /// argument POSITION regardless of CS's parameter names, so a CS rename of the parameters (not the method)
        /// still binds. Live-gated: a no-op unless the most-stocked-first key is on and it is a cook-food bill.</summary>
        /// <param name="__0">CS's already-sorted candidate list (the <c>availableThings</c> arg), reordered in place.</param>
        /// <param name="__1">The bill being filled (the <c>bill</c> arg), used to gate on cook-food bills.</param>
        static void Postfix(List<Thing> __0, Bill __1)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.cookMostStockFirst)
                return;
            SpoilingFirst.ApplyMostStockStable(__0, __1, s);
        }
    }
}
