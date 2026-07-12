using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// #192 — make "Cook with the most-stocked ingredient first" (<c>cookMostStockFirst</c>, #137) work when
    /// Common Sense is installed. CS's <c>AddSort</c> transpiler rewrites the vanilla AllowMix cook-ingredient
    /// <c>SortBy</c> into a call to its own <c>DoSort(List&lt;Thing&gt;, Bill)</c> (a private static on the nested
    /// patch class <c>WorkGiver_DoBill_TryFindBestBillIngredientsInSet_AllowMix_CommonSensePatch</c> — declared at
    /// the CommonSense namespace root in avilmask's build, nested under <c>IngredientPriority</c> in
    /// catgirlfighter's fork; see <see cref="DoSortDeclaringTypeNames"/>), so HD CEDES its own competing transpiler
    /// to CS (two transpilers cannot rewrite the same call — see
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

        // The candidate DECLARING types for CS's cook-ingredient sort (DoSort), across the known Common Sense
        // forks. In both avilmask's original and catgirlfighter's fork, DoSort is a `private static void
        // DoSort(List<Thing>, Bill)` on the nested patch class WorkGiver_DoBill_TryFindBestBillIngredientsInSet_
        // AllowMix_CommonSensePatch — but avilmask's build declares that patch class at the CommonSense namespace
        // ROOT (Harmony's patch dump shows CommonSense.WorkGiver_DoBill_..._CommonSensePatch.AddSort), while
        // catgirlfighter NESTS it inside CommonSense.IngredientPriority (reflection name uses '+'). The first
        // resolve-only mistake (#192 follow-up) looked for DoSort on CommonSense.IngredientPriority ITSELF, where
        // it does not exist, so it never bound and warned every CS user. Try each candidate; the last entry is a
        // defensive direct-on-IngredientPriority guess in case a future fork promotes it there.
        private static readonly string[] DoSortDeclaringTypeNames =
        {
            "CommonSense.WorkGiver_DoBill_TryFindBestBillIngredientsInSet_AllowMix_CommonSensePatch",
            "CommonSense.IngredientPriority+WorkGiver_DoBill_TryFindBestBillIngredientsInSet_AllowMix_CommonSensePatch",
            "CommonSense.IngredientPriority",
        };

        /// <summary>Resolve CS's cook-ingredient sort once, at patch time, across the known CS forks (see
        /// <see cref="DoSortDeclaringTypeNames"/>). Returns false (skip the whole patch) when Common Sense is absent,
        /// or when <c>DoSort(List&lt;Thing&gt;, Bill)</c> did not resolve on any candidate — HD then relies on its
        /// own transpiler, which is active precisely because CS's transpiler isn't there to conflict, so most-stock
        /// still works without CS. A total miss (a genuinely unknown fork) is logged ONCE at debug level only, NOT a
        /// user-facing warning: this is an opt-in, default-off feature, so an unresolved layer must not nag every CS
        /// user (that nag was the #192-follow-up regression). It just falls back to HD's own batch-cook picker.</summary>
        static bool Prepare()
        {
            if (targetMethod != null)
                return true;
            if (!CommonSenseCompat.IsActive)
                return false;
            var paramTypes = new[] { typeof(List<Thing>), typeof(Bill) };
            foreach (var typeName in DoSortDeclaringTypeNames)
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null)
                    continue;
                // Typed lookup first; fall back to a name-only lookup so a fork that tweaks the parameter types
                // (but keeps the single DoSort) still binds.
                targetMethod = AccessTools.Method(t, "DoSort", paramTypes) ?? AccessTools.Method(t, "DoSort");
                if (targetMethod != null)
                    break;
            }
            if (targetMethod == null)
                HDLog.Dbg("Common Sense present but its cook-ingredient sort (DoSort) did not resolve on any known "
                          + "fork; 'cook with the most-stocked ingredient first' falls back to HD's own batch-cook "
                          + "picker under this CS build. Harmless — this is an opt-in feature.");
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
