using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// STORAGE ROUTING — SUPPLIES-CLOSER seam (C3, While You're Up "haul before carry" for construction supplies).
    /// A Harmony postfix on <see cref="WorkGiver_ConstructDeliverResources.ResourceDeliverJobFor"/> (the same method
    /// ICD patches): when vanilla returns a hand-carry construction-supply delivery, and storage CLOSER to the build
    /// site exists for the largest stack vanilla queued, replace the carry with a relocation haul that moves that
    /// stack to the closer storage first (WYU before-carry). The pawn's next work scan re-issues the delivery and
    /// fetches from the closer storage.
    ///
    /// <para><b>C3-mf2 (read what vanilla QUEUED, not the consumed pool):</b> the candidate stacks are the job's
    /// <c>targetA</c> + <c>targetQueueA</c> (the floor resource stacks the pawn would fetch), NOT
    /// <c>resourcesAvailable</c> (the static buffer WYU consumed). <see cref="StorageRouting"/> picks the LARGEST.
    /// The consuming target is <c>targetC</c> (the constructible) / its position.</para>
    ///
    /// <para><b>G6 (no double-act with InventoryConstructDelivery):</b> guarded at PLAN time by DEFERENCE, not a
    /// blanket flag. This postfix runs LAST (<c>[HarmonyPriority(Priority.Last)]</c>) so it always sees ICD's final
    /// result: if ICD CONVERTED the delivery to its inventory-load job (the "ICD tethered/handles this" case), the
    /// <c>job.def != HaulToContainer</c> check below bails — routing never touches a stack ICD claimed. If ICD
    /// DECLINED (the delivery is still a plain vanilla HaulToContainer — ICD judged one hand-trip optimal), ICD is
    /// not handling it, so routing is free to relocate the largest stack closer for this and future fetches (WYU
    /// intent). This keeps routeSupplies functional at ICD's default-ON while never double-acting on an ICD job.</para>
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    [HarmonyPriority(Priority.Last)] // run after ICD's postfix so we observe its final converted/declined result (G6)
    public static class Patch_ResourceDeliverJobFor_Routing
    {
        static void Postfix(ref Job __result, Pawn pawn, IConstructible c, bool forced)
        {
            var s = HaulersDreamMod.Settings;
            // BYTE-INERT: master off, or this sub-feature off.
            if (s == null || !s.storageRouting || !s.routeSupplies)
                return;
            var job = __result;
            if (job == null || pawn?.Map == null)
                return;
            // Only a vanilla floor-resource HaulToContainer delivery still in place. If ICD already converted it to
            // its inventory-load job (a different HD def), this check bails — G6 deference (never relocate a stack
            // ICD is about to gather). An install job is also excluded.
            if (job.def != JobDefOf.HaulToContainer || job.haulMode != HaulMode.ToContainer || c is Blueprint_Install)
                return;
            // A player-ordered (forced) delivery must start delivering, not detour through a relocation.
            if (forced || job.playerForced)
                return;

            // The consuming target: the constructible (targetC), falling back to targetB / the original c.
            Thing needer = job.targetC.Thing ?? job.targetB.Thing ?? (c as Thing);
            if (needer == null || !needer.Spawned)
                return;
            IntVec3 consumeCell = needer.Position;
            if (!consumeCell.IsValid)
                return;

            if (!StorageRouting.MayRoute(pawn))
                return;

            // The floor stacks vanilla QUEUED to fetch (C3-mf2): targetA + targetQueueA. Build a small combined list
            // (allocation-light; bounded by the multi-pickup radius, typically 1-4 stacks).
            var planned = CollectPlannedStacks(job);
            if (planned.Count == 0)
                return;

            var routeJob = StorageRouting.TryRouteToConsumer(
                pawn, job.targetA.Thing?.def, consumeCell, planned,
                allowEqualPriority: s.routeToEqualPriority, allowStockpiles: s.routeToStockpiles);
            if (routeJob != null)
                __result = routeJob;
        }

        // targetA + targetQueueA, the floor resource stacks the delivery would fetch (WYU's "the resource it would
        // haul from" + the 5-tile multi-pickup extras). Allocation-light: a single small list per actual conversion
        // candidate (the cheap gates above have already bailed for the common no-op case).
        private static List<LocalTargetInfo> CollectPlannedStacks(Job job)
        {
            var list = new List<LocalTargetInfo>(1 + (job.targetQueueA?.Count ?? 0));
            if (job.targetA.Thing != null)
                list.Add(job.targetA);
            if (job.targetQueueA != null)
                for (int i = 0; i < job.targetQueueA.Count; i++)
                    if (job.targetQueueA[i].Thing != null)
                        list.Add(job.targetQueueA[i]);
            return list;
        }
    }
}
