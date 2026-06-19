using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Makes AUTOMATIC crafting bills gather their ingredients in ONE inventory sweep instead of one hand-carry trip
    /// per stack. When vanilla <see cref="WorkGiver_DoBill"/> hands back a DoBill job that would fetch ingredients
    /// from two or more separate floor stacks (= two or more trips), we swap it for a
    /// <see cref="JobDriver_BillPrepGather"/> that loads those exact stacks into the pawn's inventory (tagged) and
    /// ends at the bench. The next work scan re-issues the same bill, and — via the existing shared-inventory bill
    /// patch — vanilla's OWN DoBill then sources the ingredients from the pawn's inventory (the chooser ranks by
    /// PositionHeld distance to the bench, and the pawn is standing there). Vanilla does 100% of the crafting, so
    /// the recipe flow (placedThings, unfinished things, consumption, products) is untouched — duplication is
    /// impossible by construction. This supersedes the retired direct-craft conversion (JobDriver_InventoryDoBill),
    /// which could finish a recipe without consuming ingredients because vanilla only records placedThings for its
    /// own JobDefOf.DoBill.
    ///
    /// Gates: setting on (and shareForCrafting, which the relay depends on); never a player-ordered (forced) job —
    /// converting one would break the ordered "craft now" continuity; workbenches only (never surgery on a Pawn);
    /// ≥2 floor stacks (else no trip saved); eligible carrier pawns; per-pawn cooldown after an empty sweep so a
    /// blocked gather can't ping-pong (it falls back to vanilla multi-trip — fail-open).
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_WorkGiver_DoBill_InventoryRoute
    {
        static void Postfix(ref Job __result, Pawn pawn, Thing thing, bool forced)
        {
            // Cheap gates FIRST so the per-pawn-scan reflection in CommonSenseCompat.OwnsDoBillFlow only runs when
            // the feature is actually engaged (a real convertible DoBill job + the feature on). These checks are
            // pure field reads / a ref compare and would short-circuit the postfix anyway, so reordering them ahead
            // of the cede check is behaviour-identical — when any of them bails, OwnsDoBillFlow's value is moot.
            var s = HaulersDreamMod.Settings;
            // markForUnload is required too: the relay's "leftovers are never stranded" safety story depends on
            // the unload backstop reclaiming tagged stock if the craft never happens.
            if (s == null || !s.inventoryCraftDeliver || !s.shareForCrafting || !s.markForUnload)
                return;
            var job = __result;
            if (job == null || job.def != JobDefOf.DoBill || job.bill?.recipe == null)
                return;
            if (forced || job.playerForced)
                return; // a player-ordered craft must start crafting, not detour through a gather job
            // Common Sense owns the vanilla DoBill driver (its MakeNewToils Prefix re-deposits ingredients to the
            // bench floor) — cede the gather flow to it so HD doesn't double-gather and create a re-haul loop.
            // (Moved below the cheap gates above: the reflective toggle read now happens only on a convertible job.)
            if (CommonSenseCompat.OwnsDoBillFlow)
                return;
            // Workbenches only — never a Pawn bill giver (surgery) / other special giver, and never an autonomous
            // worktable (mech gestator family): those DEPOSIT ingredients into the building's own container from the
            // carry tracker, which HD's load-into-inventory relay can't satisfy. See BillRouteGate.
            if (!BillRouteGate.MayRouteToInventory(job.targetA.Thing))
                return;
            if (BillPrepTracker.ShouldSkip(pawn))
                return; // last sweep loaded nothing — let vanilla run multi-trip rather than ping-pong
            if (!IsEligibleCrafter(pawn))
                return;
            // The pawn already holds TAGGED stock usable for this bill (e.g. a finished sweep whose carried stacks
            // the chooser didn't pick — an AllowMix recipe ranking them oddly, or stock left from a prior bill).
            // Converting again would gather MORE on top — an unbounded over-gather loop. Let vanilla run instead:
            // it natively collects mixed inventory+floor queues, and leftovers go back via the unload pass.
            if (HoldsTaggedStockForBill(pawn, job.bill))
                return;

            // Collect the floor ingredient stacks vanilla would shuttle one-per-trip. Fewer than two = no trips
            // saved (and an unfinished-thing resume has the UFT in the queue, not floor stacks) → leave it.
            var queue = job.targetQueueB;
            var counts = job.countQueue;
            if (queue == null || counts == null || counts.Count != queue.Count)
                return;
            var floorIdx = new List<int>();
            for (int i = 0; i < queue.Count; i++)
            {
                var t = queue[i].Thing;
                if (t != null && t.Spawned && counts[i] > 0
                    && !(t.ParentHolder is Pawn_InventoryTracker) && !(t is UnfinishedThing))
                    floorIdx.Add(i);
            }
            if (floorIdx.Count < 2)
                return;

            var prep = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_BillPrepGather, job.targetA);
            prep.count = 1; // sentinel only (amounts come from countQueue): Job.count defaults to -1 and vanilla
                            // TakeToInventory's ErrorCheckForCarry red-errors on count <= 0
            prep.bill = job.bill; // for the validity FailOn only — the prep never runs the recipe
            prep.targetQueueB = new List<LocalTargetInfo>(floorIdx.Count);
            prep.countQueue = new List<int>(floorIdx.Count);
            for (int i = 0; i < floorIdx.Count; i++)
            {
                prep.targetQueueB.Add(queue[floorIdx[i]]);
                prep.countQueue.Add(counts[floorIdx[i]]);
            }
            __result = prep;

            if (s.verboseLogging)
                HDLog.Dbg($"[BillPrep] {pawn} gathers {floorIdx.Count} ingredient stacks for {job.bill.recipe.defName} in one sweep.");
        }

        private static bool IsEligibleCrafter(Pawn pawn)
        {
            // #4: HD's gather-into-inventory relay is a colonist scoop feature — exclude MECH workers
            // unconditionally (not only via allowMechanoids), matching the ingredient-share injection. Keeping the
            // whole share-for-crafting feature consistently mech-excluded means HD can never half-engage a mech's
            // bill (gather a batch into inventory the next scan won't source from) and never feed a mech DoBill
            // that dead-ends into the "10 jobs in one tick" loop. See BillRouteGate.WorkerMayShareCraft.
            if (!BillRouteGate.WorkerMayShareCraft(pawn))
                return false;
            if (pawn == null || pawn.GetComp<CompHauledToInventory>() == null)
                return false;
            if (pawn.Drafted && (HaulersDreamMod.Settings?.pauseWhileDrafted ?? true))
                return false;
            return YieldRouter.IsEligible(pawn);
        }

        /// <summary>Does the pawn already carry tagged stock this bill could use? (The re-gather loop guard.)</summary>
        private static bool HoldsTaggedStockForBill(Pawn pawn, Bill bill)
        {
            var comp = pawn.GetComp<CompHauledToInventory>();
            var owner = pawn.inventory?.innerContainer;
            if (comp == null || owner == null || bill?.recipe == null)
                return false;
            foreach (var tagged in comp.GetHashSet())
                if (tagged != null && owner.Contains(tagged) && InventoryShare.IsUsableForBill(tagged, bill))
                    return true;
            return false;
        }
    }
}
