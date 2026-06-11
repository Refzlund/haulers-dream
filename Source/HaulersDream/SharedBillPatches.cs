using System;
using System.Collections.Generic;
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

    /// <summary>The ingredient chooser. We add carried stock to its candidate list exactly once per search.</summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredientsInSet")]
    public static class Patch_WorkGiver_DoBill_TryFindBestBillIngredientsInSet
    {
        static void Prefix(List<Thing> availableThings, Bill bill)
        {
            if (Patch_WorkGiver_DoBill_TryFindBestBillIngredients.AddedThisSearch)
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.shareForCrafting || availableThings == null)
                return;
            var worker = Patch_WorkGiver_DoBill_TryFindBestBillIngredients.CurrentWorker;
            if (worker == null || bill?.recipe == null)
                return;

            InventoryShare.AddSharableStacksForBill(worker, bill, availableThings);
            Patch_WorkGiver_DoBill_TryFindBestBillIngredients.AddedThisSearch = true;
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
