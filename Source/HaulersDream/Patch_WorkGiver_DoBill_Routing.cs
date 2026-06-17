using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// STORAGE ROUTING — INGREDIENTS-CLOSER seam (C3, While You're Up "haul before carry" for crafting ingredients).
    /// A Harmony postfix on <see cref="WorkGiver_DoBill.JobOnThing"/> (the same method BillPrepGather patches): when
    /// vanilla returns a DoBill job whose chosen ingredient stacks could be re-stored CLOSER to the bench, replace
    /// the bill job with a relocation haul that moves the largest such stack to the closer storage first (WYU
    /// before-carry). The pawn's next work scan re-issues the bill and fetches the ingredients from the closer
    /// storage. The re-validated bill seam (the live <see cref="WorkGiver_DoBill"/> job is what supplies the
    /// ingredient stacks + counts), so the relocation only ever touches stacks this exact bill planned to consume.
    ///
    /// <para><b>G6 (no double-act with BillPrepGather / BatchCraft):</b> guarded at PLAN time by DEFERENCE, not a
    /// blanket flag. This postfix runs LAST (<c>[HarmonyPriority(Priority.Last)]</c>) so it always sees the other HD
    /// DoBill postfixes' final result: if BillPrepGather (<see cref="Patch_WorkGiver_DoBill_InventoryRoute"/>) or
    /// the batch-craft route converted the job to an HD job, the <c>job.def != DoBill</c> check below bails — routing
    /// never touches a stack BillPrep planned to sweep. If they DECLINED (the job is still a plain DoBill — e.g. only
    /// one floor stack, BillPrep's cooldown, or batching off), routing is free to relocate the largest ingredient
    /// stack closer (WYU intent). Keeps routeIngredients functional at BillPrep's default-ON while never
    /// double-acting on its gather job.</para>
    ///
    /// <para><b>G2:</b> every candidate stack is, by construction, a target of this LIVE job (it comes from the
    /// job's own <c>targetQueueB</c>), and <see cref="StorageRouting"/> additionally rejects stacks another pawn
    /// already claimed.</para>
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    [HarmonyPriority(Priority.Last)] // run after BillPrep/Batch postfixes so we observe their final result (G6)
    public static class Patch_WorkGiver_DoBill_Routing
    {
        static void Postfix(ref Job __result, Pawn pawn, Thing thing, bool forced)
        {
            var s = HaulersDreamMod.Settings;
            // BYTE-INERT: master off, or this sub-feature off.
            if (s == null || !s.storageRouting || !s.routeIngredients)
                return;
            var job = __result;
            if (job == null || pawn?.Map == null)
                return;
            // Only a vanilla crafting DoBill job still in place. If BillPrep / batch already converted it to an HD
            // job (a different def), this check bails — G6 deference (never relocate a stack BillPrep will sweep).
            if (job.def != JobDefOf.DoBill || job.bill?.recipe == null)
                return;
            // A player-ordered craft must start crafting, not detour through a relocation.
            if (forced || job.playerForced)
                return;
            // Workbenches only — never a Pawn bill giver (surgery) or other special giver (matches BillPrep).
            if (!(job.targetA.Thing is Building_WorkTable))
                return;

            // The consuming target: the workbench cell (where ingredients are carried to).
            var bench = job.targetA.Thing;
            IntVec3 consumeCell = bench.Position;
            if (!consumeCell.IsValid)
                return;

            if (!StorageRouting.MayRoute(pawn))
                return;

            // The ingredient stacks vanilla chose (targetQueueB) — what the bill planned to fetch. Only FLOOR stacks
            // (skip anything already in inventory / unfinished things — a relocation can't help those).
            var planned = CollectPlannedIngredients(job);
            if (planned.Count == 0)
                return;

            var routeJob = StorageRouting.TryRouteToConsumer(
                pawn, null, consumeCell, planned,
                allowEqualPriority: s.routeToEqualPriority, allowStockpiles: s.routeToStockpiles);
            if (routeJob != null)
                __result = routeJob;
        }

        // The bill's chosen ingredient stacks (targetQueueB), restricted to spawned FLOOR stacks. An ingredient
        // already in a pawn's inventory, or an UnfinishedThing resume target, can't be relocated to closer storage —
        // skip them so the largest-stack pick can't land on one. G2 (this-job ownership) holds by construction:
        // every element here IS a target of the live job.
        private static List<LocalTargetInfo> CollectPlannedIngredients(Job job)
        {
            var q = job.targetQueueB;
            var list = new List<LocalTargetInfo>(q?.Count ?? 0);
            if (q != null)
                for (int i = 0; i < q.Count; i++)
                {
                    var t = q[i].Thing;
                    if (t != null && t.Spawned && !(t.ParentHolder is Pawn_InventoryTracker) && !(t is UnfinishedThing))
                        list.Add(q[i]);
                }
            return list;
        }
    }
}
