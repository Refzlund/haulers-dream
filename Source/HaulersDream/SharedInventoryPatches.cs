using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// F3a: count carried (tagged) materials toward construction/crafting availability, so a frame or
    /// bill becomes prioritizable when the only stock is in a colonist's inventory. This is the exact
    /// gate that otherwise reports "no materials" when nothing is on the ground.
    /// </summary>
    [HarmonyPatch(typeof(ItemAvailability), nameof(ItemAvailability.ThingsAvailableAnywhere))]
    public static class Patch_ItemAvailability_ThingsAvailableAnywhere
    {
        static void Postfix(ref bool __result, ThingDef need, int amount, Pawn pawn)
        {
            if (__result)
                return;
            var s = HaulersDreamMod.Settings;
            // ThingsAvailableAnywhere is the construction/build availability gate (WorkGiver_ConstructDeliverResources);
            // bills don't use it. Each share-source contributes independently behind its own setting.
            if (s == null || pawn?.Map == null)
                return;
            int avail = 0;
            if (s.shareForBuilding)
                avail += InventoryShare.CountSharable(pawn.Map, pawn, need);            // colonists' scooped inventory
            if (s.shareHandHauledToStorage)
                avail += CarriedHaulShare.CountStorageBoundCarried(pawn.Map, pawn, need); // a colonist hand-hauling to storage
            // Conservative: only claim availability when carried/scooped stock alone covers the amount.
            if (avail > 0 && avail >= amount)
                __result = true;
        }
    }

    /// <summary>
    /// F3b: when the vanilla construction-delivery search finds no resource on the floor, deliver
    /// from a carrier's inventory instead. We build a stock <c>HaulToContainer</c> job whose target
    /// is the carrier's inventory stack — the vanilla driver already walks to the carrier
    /// (GotoThing canGotoSpawnedParent) and pulls it out (StartCarryThing canTakeFromInventory),
    /// then delivers + handles enroute accounting. Floor resources always win (we only act on null).
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    public static class Patch_ResourceDeliverJobFor
    {
        static void Postfix(ref Job __result, Pawn pawn, IConstructible c, bool forced)
        {
            if (__result != null)
                return; // vanilla found a floor resource -> leave it
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.shareForBuilding || pawn?.Map == null || c == null || c is Blueprint_Install)
                return;

            foreach (var need in c.TotalMaterialCost())
            {
                int needed = (forced || !(c is IHaulEnroute enroute))
                    ? c.ThingCountNeeded(need.thingDef)
                    : enroute.GetSpaceRemainingWithEnroute(need.thingDef, pawn);
                if (needed <= 0)
                    continue;

                var stack = InventoryShare.FindSharableStack(pawn.Map, pawn, need.thingDef);
                if (stack == null)
                    continue;

                var job = JobMaker.MakeJob(JobDefOf.HaulToContainer);
                job.targetA = stack;            // the carrier's inventory stack
                job.targetB = (Thing)c;         // the needer (frame / blueprint)
                job.targetC = (Thing)c;         // primary needer
                job.count = Mathf.Min(needed, stack.stackCount);
                job.haulMode = HaulMode.ToContainer;

                if (s.shareMeetInMiddle)
                    SharedInventoryApproach.MaybeApproach(stack, pawn);

                __result = job;
                HDLog.Dbg($"{pawn} will fetch {job.count}x {need.thingDef.label} from {(stack.ParentHolder as Pawn_InventoryTracker)?.pawn} for {c}.");
                return;
            }
        }
    }

    /// <summary>
    /// Hand-haul share (opt-in): when no floor stock and no shareable inventory cover a construction need,
    /// but a colonist is hand-hauling the material TO STORAGE, build a job to claim it from that hauler in
    /// transit. Low priority so the floor (vanilla) and inventory-fetch (F3b) sources are always tried
    /// first — claiming from a working hauler is the last resort. See <see cref="CarriedHaulShare"/>.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    [HarmonyPriority(Priority.Low)]
    public static class Patch_ResourceDeliverJobFor_HandHaul
    {
        static void Postfix(ref Job __result, Pawn pawn, IConstructible c, bool forced)
        {
            if (__result != null)
                return; // floor or shareable inventory already covered it
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.shareHandHauledToStorage || pawn?.Map == null || c == null || c is Blueprint_Install)
                return;
            if (!(c is Thing needer) || needer.Destroyed || !needer.Spawned)
                return;

            foreach (var need in c.TotalMaterialCost())
            {
                int needed = (forced || !(c is IHaulEnroute enroute))
                    ? c.ThingCountNeeded(need.thingDef)
                    : enroute.GetSpaceRemainingWithEnroute(need.thingDef, pawn);
                if (needed <= 0)
                    continue;

                var carried = CarriedHaulShare.FindCarriedStack(pawn.Map, pawn, need.thingDef, needer, needed, out var hauler);
                if (carried == null || hauler == null)
                    continue;

                var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_ClaimFromHauler);
                job.targetA = hauler;          // the hand-hauler (reserved -> single claimant)
                job.targetB = needer;          // the needer (frame / blueprint)
                job.targetC = needer;
                job.count = Mathf.Min(needed, carried.stackCount);
                __result = job;
                HDLog.Dbg($"{pawn} will claim {job.count}x {need.thingDef.label} from hand-hauler {hauler.LabelShort} for {needer.LabelShort}.");
                return;
            }
        }
    }

    /// <summary>
    /// F5: batch more queued same-material construction needers into one delivery trip, up to the pawn's
    /// hand capacity (which carries no move-speed penalty). Fixes the "builds a long fence by shuttling ~9
    /// wood at a time" back-and-forth: vanilla only batches needers within 8 tiles. Only the floor-resource
    /// delivery is expanded (not our inventory-fetch job above, and not minified-building installs); the
    /// vanilla driver already delivers a hand-load to many needers. See <see cref="ConstructionBatch"/>.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    public static class Patch_ResourceDeliverJobFor_Batch
    {
        static void Postfix(ref Job __result, Pawn pawn, IConstructible c, bool forced)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.batchWorkDeliveries || __result == null || pawn?.Map == null)
                return;
            if (__result.def != JobDefOf.HaulToContainer || __result.haulMode != HaulMode.ToContainer || c is Blueprint_Install)
                return;
            var carried = __result.targetA.Thing;
            // Only floor-resource deliveries: a spawned ground stack (not an inventory fetch, not a minified building).
            if (carried == null || !carried.Spawned || carried.ParentHolder is Pawn_InventoryTracker)
                return;
            ConstructionBatch.Expand(pawn, __result, carried.def, forced);
        }
    }
}
