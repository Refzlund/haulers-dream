using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// F3c: make a colonist's carried (tagged) materials count as bill ingredients, so a crafter/cook/
    /// tailor can fulfil a recipe from another pawn's inventory. The vanilla DoBill driver already walks
    /// to the carrier (GotoThing canGotoSpawnedParent) and pulls from inventory (StartCarryThing
    /// canTakeFromInventory) once an inventory stack is chosen — so we only need to add those stacks to
    /// the candidate set the ingredient chooser sees.
    ///
    /// We stash the worker pawn during <c>TryFindBestBillIngredients</c> (which has it) and read it in the
    /// chooser <c>TryFindBestBillIngredientsInSet</c> (which doesn't), appending eligible inventory stacks
    /// once per search. NOTE the real semantics: vanilla's FIRST foundAllIngredientsAndChoose pass runs
    /// BEFORE any region traversal, so when the injected carried stock alone satisfies the bill it wins
    /// OUTRIGHT — nearby floor stock is never even considered. Floor stock participates only when the
    /// carried stock is insufficient (the chooser then ranks the mixed set by held-distance).
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
    public static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
    {
        [ThreadStatic] public static Pawn CurrentWorker;
        [ThreadStatic] public static bool AddedThisSearch;

        static void Prefix(Pawn pawn)
        {
            CurrentWorker = pawn;
            AddedThisSearch = false;
        }

        // Runs even on exception so the thread-static never leaks into an unrelated search.
        static void Finalizer()
        {
            CurrentWorker = null;
            AddedThisSearch = false;
        }
    }

    /// <summary>The ingredient chooser. We (1) add carried stock to its candidate list exactly once per
    /// search, then (2) — for the NoMix path — stable-reorder the list "spoiling-first" so the colonist
    /// reaches for the most-perishable already-valid candidate, and set <c>alreadySorted = true</c> so
    /// vanilla skips its own distance re-sort and walks our order (the selection loop below is order-
    /// driven, so this only changes WHICH valid stack wins — never recipe satisfaction).</summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredientsInSet")]
    public static class Patch_WorkGiver_DoBill_TryFindBestBillIngredientsInSet
    {
        // Param names bind by Harmony to the target's declared names (verified vs the 1.6 decompile:
        // availableThings, bill, rootCell, alreadySorted). rootCell is bound for completeness but the
        // reorder uses the candidates' own original (vanilla-distance) order as the stable tiebreak.
        static void Prefix(List<Thing> availableThings, Bill bill, IntVec3 rootCell, ref bool alreadySorted)
        {
            var s = HaulersDreamMod.Settings;

            // (1) Existing carried-stock injection — UNCHANGED, runs first so injected inventory stacks
            //     participate in the spoiling reorder below.
            if (!Patch_WorkGiver_DoBill_TryFindBestBillIngredients.AddedThisSearch
                && s != null && s.shareForCrafting && availableThings != null)
            {
                var worker = Patch_WorkGiver_DoBill_TryFindBestBillIngredients.CurrentWorker;
                if (worker != null && bill?.recipe != null)
                {
                    InventoryShare.AddSharableStacksForBill(worker, bill, availableThings);
                    Patch_WorkGiver_DoBill_TryFindBestBillIngredients.AddedThisSearch = true;
                }
            }

            // (2) Spoiling-First reorder. NoMix path only (AllowMix re-sorts internally and ignores
            //     alreadySorted — out of scope). Floats already-valid rottable candidates forward; the
            //     selection loop below then runs byte-identical on the reordered list.
            if (s == null || availableThings == null || bill?.recipe == null) return;
            if (bill.recipe.allowMixingIngredients) return; // out of scope (mix path uses value-per-unit sort)
            if (alreadySorted) return;                      // surgery/medicine: leave vanilla's pre-sort intact
            if (SpoilingFirst.ReorderInPlace(availableThings, s))
                alreadySorted = true;   // suppress vanilla's own distance re-sort in _NoMixHelper
        }
    }

    /// <summary>
    /// Item 3+4 (COOK / AllowMix path): make <c>cookSpoilingFirst</c> actually work for cooking.
    /// EVERY vanilla cooking recipe sets <c>allowMixingIngredients=true</c>, so it flows through
    /// <c>TryFindBestBillIngredientsInSet_AllowMix</c> — NOT the NoMix Prefix above. That method's
    /// FIRST statement re-sorts the candidate list with
    /// <c>availableThings.SortBy(valuePerUnit, squaredDistance)</c> and has no <c>alreadySorted</c>
    /// flag, so a pre-sort Prefix is a no-op (vanilla immediately discards it). We instead transpile
    /// that single <c>GenCollection.SortBy&lt;Thing,float,int&gt;</c> call to
    /// <see cref="SpoilingFirst.SortAllowMix"/>, forwarding the SAME receiver list + the SAME two key
    /// selectors and pushing <c>bill</c> + <c>Settings</c>. The vanilla greedy fill loop below it is
    /// left byte-for-byte intact; only the order it walks changes — and only for cook-food bills with
    /// the toggle on (else <see cref="SpoilingFirst.SortAllowMix"/> calls vanilla's SortBy verbatim,
    /// so chemfuel/patchleather/non-cook bills are byte-identical). The butcher NoMix path is
    /// untouched. Fail-loud: if the IL match ever breaks, Harmony logs and the feature reverts to
    /// vanilla (a no-op) — never silently wrong.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredientsInSet_AllowMix")]
    public static class Patch_WorkGiver_DoBill_TryFindBestBillIngredientsInSet_AllowMix
    {
        // The target: Verse.GenCollection.SortBy<T,TSortBy,TThenBy>(List<T>, Func<T,TSortBy>, Func<T,TThenBy>).
        private static readonly MethodInfo SortBy3 = AccessTools.GetDeclaredMethods(typeof(GenCollection))
            .Find(m => m.Name == "SortBy"
                       && m.IsGenericMethodDefinition
                       && m.GetGenericArguments().Length == 3
                       && m.GetParameters().Length == 3);

        // Our replacement, matching the concrete SortBy<Thing,float,int> stack shape plus (bill, settings):
        // SortAllowMix(List<Thing>, Func<Thing,float>, Func<Thing,int>, Bill, HaulersDreamSettings).
        private static readonly MethodInfo SortAllowMix =
            AccessTools.Method(typeof(SpoilingFirst), nameof(SpoilingFirst.SortAllowMix));

        // HaulersDreamMod.Settings static getter — pushed as the 5th SortAllowMix arg.
        private static readonly MethodInfo GetSettings =
            AccessTools.PropertyGetter(typeof(HaulersDreamMod), nameof(HaulersDreamMod.Settings));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (SortBy3 == null || SortAllowMix == null || GetSettings == null)
            {
                Log.Error("[Hauler's Dream] cook spoiling-first transpiler: could not resolve target/replacement "
                          + "methods; leaving TryFindBestBillIngredientsInSet_AllowMix unpatched (vanilla order).");
                foreach (var ci in instructions) yield return ci;
                yield break;
            }

            bool patched = false;
            foreach (var ci in instructions)
            {
                // Match the single concrete call to the generic SortBy<Thing,float,int> definition.
                // Identify it structurally (declaring type + name + arity) rather than by MethodInfo
                // reference equality, which is brittle across reflection-wrapper instances.
                if (!patched && ci.opcode == OpCodes.Call && ci.operand is MethodInfo mi
                    && mi.IsGenericMethod
                    && mi.DeclaringType == typeof(GenCollection)
                    && mi.Name == "SortBy"
                    && mi.GetGenericArguments().Length == 3
                    && mi.GetParameters().Length == 3)
                {
                    // Stack at this point: [ List<Thing>, Func<Thing,float>, Func<Thing,int> ].
                    // Push the two extra args our method expects, then call it instead of SortBy.
                    yield return new CodeInstruction(OpCodes.Ldarg_1)           // bill (static-method arg index 1)
                        .MoveLabelsFrom(ci)                                     // keep any branch labels on this slot
                        .MoveBlocksFrom(ci);                                    // and any exception-block boundaries
                    yield return new CodeInstruction(OpCodes.Call, GetSettings); // HaulersDreamMod.Settings
                    yield return new CodeInstruction(OpCodes.Call, SortAllowMix);
                    patched = true;
                    continue;
                }
                yield return ci;
            }

            if (!patched)
                Log.Error("[Hauler's Dream] cook spoiling-first transpiler: SortBy call not found in "
                          + "TryFindBestBillIngredientsInSet_AllowMix; cook spoiling-first is inactive "
                          + "(vanilla order). RimWorld may have changed the method.");
        }
    }

    /// <summary>
    /// F3d (meet-in-the-middle for crafting): when the produced DoBill job will fetch an ingredient from a
    /// carrier's inventory, nudge an idle carrier to walk toward the worker so they converge faster. Never
    /// interrupts a carrier doing real work (see <see cref="SharedInventoryApproach"/>).
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_WorkGiver_DoBill_JobOnThing
    {
        static void Postfix(Job __result, Pawn pawn)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.shareForCrafting || !s.shareMeetInMiddle || pawn == null)
                return;
            if (__result == null || __result.def != JobDefOf.DoBill || __result.targetQueueB == null)
                return;

            for (int i = 0; i < __result.targetQueueB.Count; i++)
            {
                var thing = __result.targetQueueB[i].Thing;
                if (thing?.ParentHolder is Pawn_InventoryTracker)
                    SharedInventoryApproach.MaybeApproach(thing, pawn);
            }
        }
    }
}
