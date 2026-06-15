using System.Collections.Generic;
using HaulersDream.Core;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// F3a: count carried materials toward construction availability, so a frame becomes prioritizable when
    /// the only stock is in inventory. This is the exact gate (WorkGiver_ConstructDeliverResources) that
    /// otherwise reports "no materials" when nothing is on the ground — and the LOAD-BEARING hook for
    /// build-from-inventory: forcing it true is what makes vanilla OFFER a deliver job on a bare caravan map.
    ///
    /// Sources, all disjoint and additive EXCEPT the two inventory paths which read the SAME innerContainer:
    /// - hand-hauled-to-storage (a colonist's hands in transit) — additive.
    /// - inventory: ORGANIC (whole innerContainer, build-from-inventory) is a SUPERSET of TAGGED (scooped
    ///   stock, shareForBuilding), so the organic term REPLACES the tagged term rather than adding to it —
    ///   never count one physical stack twice.
    /// The final gate (full vs partial) is the pure <see cref="BuildFromInventorySource.IsAvailable"/>.
    /// </summary>
    [HarmonyPatch(typeof(ItemAvailability), nameof(ItemAvailability.ThingsAvailableAnywhere))]
    public static class Patch_ItemAvailability_ThingsAvailableAnywhere
    {
        static void Postfix(ref bool __result, ThingDef need, int amount, Pawn pawn)
        {
            if (__result)
                return;
            var s = HaulersDreamMod.Settings;
            if (s == null || pawn?.Map == null)
                return;

            int avail = 0;
            if (s.shareHandHauledToStorage)
                avail += CarriedHaulShare.CountStorageBoundCarried(pawn.Map, pawn, need); // a colonist hand-hauling to storage

            // Inventory: organic (superset) REPLACES tagged for the same physical stacks — never both.
            if (s.buildFromInventory)
                avail += OrganicInventoryShare.CountOrganic(pawn.Map, pawn, need);    // whole innerContainer (own/other/pack)
            else if (s.shareForBuilding)
                avail += InventoryShare.CountSharable(pawn.Map, pawn, need);          // legacy: HD-tagged scooped stock only

            // Partial relaxation only applies on the organic path (the partial setting requires build-from-inventory).
            bool allowPartial = s.buildFromInventory && s.buildFromInventoryPartial;
            if (BuildFromInventorySource.IsAvailable(avail, amount, allowPartial))
                __result = true;
        }
    }

    /// <summary>
    /// F3b: when the vanilla construction-delivery search finds no resource on the floor, deliver
    /// from a carrier's inventory instead. We build a stock <c>HaulToContainer</c> job whose target
    /// is the carrier's inventory stack — the vanilla driver already walks to the carrier
    /// (GotoThing canGotoSpawnedParent) and pulls it out (StartCarryThing canTakeFromInventory),
    /// then delivers + handles enroute accounting. Floor resources always win (we only act on null).
    ///
    /// Source: when build-from-inventory is on, the whole ORGANIC innerContainer (own/other/pack — a
    /// superset of HD-tagged stock; this is the caravan-steel-on-a-raid path); otherwise the legacy
    /// HD-tagged scooped stock (shareForBuilding). <c>count = Min(needed, stackCount)</c> already yields a
    /// partial delivery when only a partial stack exists, so the partial-build setting needs no extra
    /// delivery code here — the frame simply advances with whatever the single stack provides.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    public static class Patch_ResourceDeliverJobFor
    {
        static void Postfix(ref Job __result, Pawn pawn, IConstructible c, bool forced)
        {
            if (__result != null)
                return; // vanilla found a floor resource -> leave it
            var s = HaulersDreamMod.Settings;
            if (s == null || (!s.buildFromInventory && !s.shareForBuilding) || pawn?.Map == null
                || c == null || c is Blueprint_Install)
                return;

            foreach (var need in c.TotalMaterialCost())
            {
                int needed = (forced || !(c is IHaulEnroute enroute))
                    ? c.ThingCountNeeded(need.thingDef)
                    : enroute.GetSpaceRemainingWithEnroute(need.thingDef, pawn);
                if (needed <= 0)
                    continue;

                // Organic (own/other/pack, untagged superset) when build-from-inventory is on; else legacy tagged.
                var stack = s.buildFromInventory
                    ? OrganicInventoryShare.FindOrganicStack(pawn.Map, pawn, need.thingDef)
                    : InventoryShare.FindSharableStack(pawn.Map, pawn, need.thingDef);
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
    /// Partial-build gate (opt-in, default OFF — it changes vanilla's all-or-nothing semantics). Mirrors the
    /// reference mod's <c>NothingNearbyDummy</c>: vanilla <c>FindAvailableNearbyResources</c> assumes the chosen
    /// resource is a spawned floor stack and aggregates a 5-tile cluster around it. When the chosen resource is
    /// NOT spawned (it came from an inventory), this forces <c>resTotalAvailable = firstFoundResource.stackCount</c>
    /// and the static <c>resourcesAvailable</c> list to just that one stack, then skips vanilla so a frame can be
    /// progressed with a partial inventory stack (vanilla would otherwise refuse a sub-need delivery).
    ///
    /// SELF-NO-OPS when build-from-inventory or its partial sub-toggle is off — vanilla's gate is byte-identical
    /// for everyone who hasn't opted in. For a spawned (floor) resource it ALWAYS returns true (vanilla
    /// unchanged). NOTE: in HD's postfix architecture the organic deliver path (F3b) builds its OWN
    /// HaulToContainer job and never routes through this method, so this prefix is a defensive parity hook for any
    /// path that DOES feed a non-spawned resource into vanilla's job builder; the partial DELIVERY itself is
    /// already handled by F3b's <c>count = Min(needed, stackCount)</c>. <c>resourcesAvailable</c> is a private
    /// STATIC field in RW 1.6 (decompile-verified), so it's reached via AccessTools, not a Harmony instance arg.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "FindAvailableNearbyResources")]
    public static class Patch_FindAvailableNearbyResources
    {
        private static readonly AccessTools.FieldRef<List<Thing>> ResourcesAvailable =
            AccessTools.StaticFieldRefAccess<List<Thing>>(
                AccessTools.Field(typeof(WorkGiver_ConstructDeliverResources), "resourcesAvailable"));

        static bool Prefix(Thing firstFoundResource, ref int resTotalAvailable)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.buildFromInventory || !s.buildFromInventoryPartial)
                return true; // not opted in -> vanilla unchanged
            if (firstFoundResource == null || firstFoundResource.Spawned)
                return true; // a floor stack -> vanilla's cluster aggregation is correct

            // Non-spawned (inventory) resource: deliver just this one stack's worth, skip the cluster scan.
            var list = ResourcesAvailable();
            if (list == null)
                return true; // field reflection failed -> fail safe to vanilla rather than crash
            list.Clear();
            list.Add(firstFoundResource);
            resTotalAvailable = firstFoundResource.stackCount;
            return false; // skip vanilla
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
