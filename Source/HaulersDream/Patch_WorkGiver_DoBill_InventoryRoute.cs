using System.Collections.Generic;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Replace an ordinary multi-stack bill selection with a quantity-preserving gather. The selected job
    /// owns all ingredient rows, including existing inventory; its successful driver starts native DoBill
    /// directly. Candidate construction has no reservations, actor dispatch or external handoff state.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_WorkGiver_DoBill_InventoryRoute
    {
        [HarmonyPriority(Priority.Low)] // batch conversion first; ingredient relocation still runs last
        static void Postfix(ref Job __result, Pawn pawn, Thing thing, bool forced)
        {
            var settings = HaulersDreamMod.Settings;
            if (!MasterEnable.Active || settings == null
                || !GatherOwnershipPolicy.PlainGatherEnabled(settings.inventoryCraftDeliver,
                    settings.shareForCrafting, settings.markForUnload)) return;
            var original = __result;
            if (original == null || original.def != JobDefOf.DoBill || original.bill?.recipe == null
                || forced || original.playerForced || !IsEligibleCrafter(pawn)) return;
            if (!BillRouteGate.MayRouteToInventory(original.targetA.Thing)
                || BillRouteGate.ChosenIngredientsUnstackable(original)
                || CommonSenseCompat.GathersIngredients || BillPrepTracker.ShouldSkip(pawn)) return;
            // A configured HD batch owns its lifecycle, including its native fallback when currently infeasible.
            if (HaulersDreamGameComponent.Instance?.IsBatchBill(original.bill) == true
                && !CommonSenseCompat.BatchSuppressedByCommonSense) return;
            // Only new ordinary selections: preserve specialized/unfinished/foreign continuation state natively.
            if (original.bill.recipe.ignoreIngredientCountTakeEntireStacks
                || !original.placedThings.NullOrEmpty() || !original.targetQueueA.NullOrEmpty()
                || original.targetB.IsValid || original.targetC.IsValid) return;
            var queue = original.targetQueueB;
            var counts = original.countQueue;
            if (queue == null || counts == null || queue.Count != counts.Count) return;
            int floorCount = 0;
            bool canPickUp = false;
            for (int i = 0; i < queue.Count; i++)
            {
                var t = queue[i].Thing;
                if (t == null || t.Destroyed || counts[i] <= 0 || counts[i] > t.stackCount
                    || t is UnfinishedThing) return;
                if (!t.Spawned) continue;
                floorCount++;
                if (!canPickUp && (!OverloadGate.NoOverload(settings)
                    || OverloadGate.CountToPickUp(pawn, t, settings) > 0)) canPickUp = true;
            }
            if (floorCount < 2 || !canPickUp) return;
            var prep = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_GatherBillIngredients, original.targetA);
            BillGatherContract.CopyContext(original, prep);
            prep.targetQueueB = new List<LocalTargetInfo>(queue);
            prep.countQueue = new List<int>(counts);
            __result = prep;
        }

        private static bool IsEligibleCrafter(Pawn pawn)
        {
            return pawn != null && !pawn.Drafted && BillRouteGate.WorkerMayShareCraft(pawn)
                && pawn.inventory != null && pawn.GetComp<CompHauledToInventory>() != null
                && YieldRouter.IsEligible(pawn);
        }
    }
}
