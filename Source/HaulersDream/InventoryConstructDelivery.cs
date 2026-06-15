using System.Collections.Generic;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Trigger for inventory-based construction delivery: when vanilla returns a hand-carry delivery to a
    /// SINGLE needer that wants more than one hand-stack of one material (a geothermal generator's 340
    /// steel, a big sculpture, a turret), replace it with a <see cref="JobDriver_OverloadConstructDeliver"/>
    /// job that loads the material into the inventory (mass-limited, can overload) and makes far fewer
    /// trips. Only fires when the carry math says inventory beats hands; otherwise the vanilla hand-carry
    /// — including the F5 needer batching — runs unchanged. See <see cref="InventoryConstructDelivery"/>.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    public static class Patch_ResourceDeliverJobFor_Inventory
    {
        static void Postfix(ref Job __result, Pawn pawn, IConstructible c, bool forced)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.inventoryConstructDeliver || __result == null || pawn?.Map == null)
                return;
            // Only a vanilla floor-resource HaulToContainer delivery: not our own job, not the F3b
            // inventory-fetch, not a minified-building install.
            if (__result.def != JobDefOf.HaulToContainer || __result.haulMode != HaulMode.ToContainer || c is Blueprint_Install)
                return;
            var carried = __result.targetA.Thing;
            if (carried == null || !carried.Spawned || carried.ParentHolder is Pawn_InventoryTracker)
                return;

            // Pass the RESOURCE stack vanilla picked (the nearest reservable stack of this material to the pawn —
            // i.e. the stockpile it would haul from), so the inventory load gathers around the STOCKPILE, wherever
            // it is, NOT around the build site. The build site is often far from the stockpile.
            // A FORCED (player-ordered) delivery becomes the TETHERED haul+build job — "prioritize constructing"
            // then hauls AND builds as one continuous task — unless a route explicitly asked for haul-only,
            // or (for plain right-click orders) the tether is disabled in mod options. A route's haul+build
            // is governed by its OWN dialog checkbox, so it always tethers regardless of the global setting.
            var intent = InventoryConstructDelivery.RouteIntent;
            bool tether = forced && intent != ConstructRouteIntent.HaulOnly
                          && (intent == ConstructRouteIntent.HaulBuild
                              || HaulersDreamMod.Settings == null || HaulersDreamMod.Settings.orderedConstructTether);
            var job = InventoryConstructDelivery.TryBuild(pawn, c, carried, forced, tether, __result);
            if (job != null)
                __result = job;
        }
    }

    /// <summary>What a route asked its per-stop construction jobs to be (set transiently around JobOnThing).</summary>
    public enum ConstructRouteIntent { None, HaulOnly, HaulBuild }

    /// <summary>
    /// Builds the inventory-delivery job: decides (via the pure <see cref="ConstructDeliveryPlan"/>) whether
    /// inventory beats hands for this needer + material + pawn, gathers enough nearby floor stock, and
    /// assembles a single-needer <c>HaulersDream_OverloadConstructDeliver</c> job. Returns null to leave
    /// the vanilla hand-carry in place.
    /// </summary>
    internal static class InventoryConstructDelivery
    {
        /// <summary>Set by the route executor around its per-stop JobOnThing calls so the conversion above knows
        /// whether the route wants haul-only or haul+build stops. ThreadStatic, always reset by the setter scope.</summary>
        [System.ThreadStatic] internal static ConstructRouteIntent RouteIntent;

        /// <summary>Per-def TOTAL demand of the whole route being built (haul+build only) — lets the first stop's
        /// job GATHER the entire route's material in one sweep instead of just its own stop's need. Set/cleared by
        /// the route executor alongside <see cref="RouteIntent"/>.</summary>
        [System.ThreadStatic] internal static Dictionary<ThingDef, int> RouteDemandByDef;

        internal static Job TryBuild(Pawn pawn, IConstructible c, Thing resource, bool forced, bool tetherBuild = false,
            Job vanillaJob = null)
        {
            var s = HaulersDreamMod.Settings;
            ThingDef def = resource?.def;
            if (s == null || def == null || pawn?.carryTracker == null || pawn.Map == null)
                return null;
            if (!(c is Thing needer) || needer.Destroyed || !needer.Spawned)
                return null;

            int handCap = pawn.carryTracker.MaxStackSpaceEver(def);
            if (handCap <= 0)
            {
                HDLog.Dbg($"inv-deliver skip: handCap<=0 for {def.label} ({pawn})");
                return null;
            }

            int frameNeed = (forced || !(c is IHaulEnroute enroute))
                ? c.ThingCountNeeded(def)
                : enroute.GetSpaceRemainingWithEnroute(def, pawn);
            if (frameNeed <= 0)
                return null;
            // An ORDERED (forced) delivery normally converts — even a small one-hand-trip load — because only
            // our driver carries the tether/route hooks. NOT always trip-neutral though: see the cluster
            // exception below. Auto deliveries keep the "hands already optimal" fast path.
            if (!forced && frameNeed <= handCap)
            {
                HDLog.Dbg($"inv-deliver skip: need {frameNeed} <= handCap {handCap} for {def.label} → {needer.LabelShort} (one hand-trip suffices)");
                return null; // a single hand-trip already satisfies it
            }
            // Plain right-click order (not a route stop) whose remaining need fits ONE hand-stack, where the
            // vanilla job already batches nearby needers (targetQueueB = its 8-tile cluster — right-clicking
            // one wall of a 15-wall line delivers the whole cluster): keep vanilla. Our single-needer
            // conversion would deliver to one needer and discard the rest of the cluster, COSTING trips.
            // Route stops (RouteIntent != None) still always convert — routes depend on per-stop jobs.
            if (forced && RouteIntent == ConstructRouteIntent.None && frameNeed <= handCap
                && vanillaJob != null && vanillaJob.targetQueueB != null && vanillaJob.targetQueueB.Count > 0)
            {
                HDLog.Dbg($"inv-deliver skip: forced one-hand order for {def.label} → {needer.LabelShort}, " +
                          $"vanilla batches {vanillaJob.targetQueueB.Count} more needer(s) (keep vanilla cluster delivery)");
                return null;
            }

            // A haul+build ROUTE gathers for the WHOLE route in this first sweep, not just this stop — the route
            // executor publishes the per-def total; later stops then deliver from the kept inventory.
            int gatherNeed = frameNeed;
            if (RouteIntent == ConstructRouteIntent.HaulBuild && RouteDemandByDef != null
                && RouteDemandByDef.TryGetValue(def, out int routeNeed) && routeNeed > gatherNeed)
                gatherNeed = routeNeed;

            float maxCap = MassUtility.Capacity(pawn);
            if (maxCap <= 0f)
                return null;
            float baseCap = CarryMath.EffectiveCapacity(maxCap, s.carryLimitFraction);
            float cur = MassUtility.GearAndInventoryMass(pawn);
            float unit = def.GetStatValueAbstract(StatDefOf.Mass);
            if (unit <= 0f)
                return null;
            // Pawn-aware (NoOverloadFor): strict / slider-Off / CE — and an ANIMAL (non-mech non-humanlike,
            // never slowed by StatPart) must not gather past its plain limit penalty-free. Player mechs DO
            // overload here and are slowed for it, like colonists.
            int level = OverloadGate.NoOverloadFor(pawn, s) ? OverloadTuning.OffLevel : s.overloadLevel;

            int ceiling = ConstructDeliveryPlan.GatherCeiling(level, maxCap, baseCap, cur, unit, gatherNeed);
            if (!forced && ceiling <= handCap)
            {
                HDLog.Dbg($"inv-deliver skip: ceiling {ceiling} <= handCap {handCap} for {def.label} (cur mass {cur:0.0}/{baseCap:0.0}kg, unit {unit:0.###})");
                return null; // can't carry more than one hand-load right now -> hands are already optimal
            }
            if (forced && ceiling < 1)
                ceiling = handCap; // ordered: always proceed with at least a hand-load's worth of gathering

            // Gather around the STOCKPILE (the resource vanilla found), wherever it is on the map — the build site
            // is frequently far from where the material is stored, and the pawn loads from the stockpile.
            var stacks = new List<Thing>();
            int available = Gather(pawn, def, resource.Position, ceiling, stacks);

            // An ordered delivery loads whatever is actually available toward the full need (the driver's take
            // toil still applies the smart-overload ceiling per pickup); the auto path keeps the planned math.
            int targetLoad = forced
                ? UnityEngine.Mathf.Min(gatherNeed, available)
                : ConstructDeliveryPlan.PlanLoad(level, maxCap, baseCap, cur, unit, frameNeed, handCap, available);
            if (targetLoad <= 0)
            {
                HDLog.Dbg($"inv-deliver skip: targetLoad 0 for {def.label} → {needer.LabelShort} " +
                          $"(available {available} in {stacks.Count} stacks near stockpile, need {frameNeed}, handCap {handCap}, ceiling {ceiling})");
                return null;
            }

            TrimToCount(stacks, targetLoad);
            if (stacks.Count == 0)
                return null;

            var job = JobMaker.MakeJob(tetherBuild
                ? HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild
                : HaulersDreamDefOf.HaulersDream_OverloadConstructDeliver);
            // targetA = the primary stack so the driver's reservation gates the job (if another pawn has
            // grabbed it, TryMakePreToilReservations fails and vanilla re-runs — no no-op loop). ALL stacks
            // (including the primary) go in the queue; the driver pops them as it loads, overwriting the
            // transient targetA. The needer is both container (B) and primary needer (C).
            job.targetA = stacks[0];
            job.targetQueueA = new List<LocalTargetInfo>();
            for (int i = 0; i < stacks.Count; i++)
                job.targetQueueA.Add(stacks[i]);
            job.targetB = needer;
            job.targetC = needer;
            job.count = targetLoad;
            job.haulMode = HaulMode.ToContainer;

            HDLog.Dbg($"{pawn} inventory-delivers {targetLoad}x {def.label} to {needer.LabelShort} " +
                      $"(need {frameNeed}, hand cap {handCap}, {stacks.Count} stacks).");
            return job;
        }

        /// <summary>
        /// Reservable floor stacks of <paramref name="def"/> map-wide, nearest the stockpile (<paramref name="anchor"/>
        /// = the resource vanilla picked) first, accumulated until <paramref name="unitCap"/> units. Returns units
        /// gathered. There is deliberately NO distance cap to the build site — the material lives in the stockpile,
        /// which is usually nowhere near the build, and the nearest-first + unit-cap naturally keeps the load to the
        /// closest cluster (it only reaches farther stacks if the nearest cluster can't fill the load).
        /// </summary>
        private static int Gather(Pawn pawn, ThingDef def, IntVec3 anchor, int unitCap, List<Thing> outStacks)
        {
            var all = pawn.Map.listerThings.ThingsOfDef(def);
            var candidates = new List<Thing>();
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || t.IsForbidden(pawn))
                    continue;
                candidates.Add(t);
            }
            candidates.Sort((a, b) =>
                (a.Position - anchor).LengthHorizontalSquared.CompareTo((b.Position - anchor).LengthHorizontalSquared));

            int got = 0;
            for (int i = 0; i < candidates.Count && got < unitCap; i++)
            {
                var t = candidates[i];
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, false) || !pawn.CanReserve(t))
                    continue;
                outStacks.Add(t);
                got += t.stackCount;
            }
            return got;
        }

        /// <summary>
        /// An ordered HAUL-ONLY delivery to <paramref name="site"/> (blueprint/frame): pick the first still-missing
        /// material that has reachable stock and build the inventory-delivery job for it (no build tether). For the
        /// "get materials TO a high-priority site now" float-menu order — works even while the site can't be
        /// constructed yet, and for pawns who can haul but not build.
        /// </summary>
        internal static Job TryBuildHaulOnlyOrder(Pawn pawn, Thing site)
        {
            if (!(site is IConstructible c) || pawn?.Map == null)
                return null;
            var def = FirstNeededDefWithStock(pawn, c, out Thing stack);
            if (def == null || stack == null)
                return null;
            return TryBuild(pawn, c, stack, forced: true, tetherBuild: false);
        }

        /// <summary>Does the site still need any material that exists reachable on the map? (The menu gate.)</summary>
        internal static bool AnyNeededMaterialAvailable(Pawn pawn, Thing site)
            => site is IConstructible c && FirstNeededDefWithStock(pawn, c, out _) != null;

        private static ThingDef FirstNeededDefWithStock(Pawn pawn, IConstructible c, out Thing nearestStack)
        {
            nearestStack = null;
            var costs = c.TotalMaterialCost();
            if (costs == null)
                return null;
            for (int i = 0; i < costs.Count; i++)
            {
                var def = costs[i]?.thingDef;
                if (def == null || c.ThingCountNeeded(def) <= 0)
                    continue;
                var all = pawn.Map.listerThings.ThingsOfDef(def);
                Thing best = null;
                int bestDist = int.MaxValue;
                for (int j = 0; j < all.Count; j++)
                {
                    var t = all[j];
                    if (t == null || !t.Spawned || t.IsForbidden(pawn) || !pawn.CanReserve(t))
                        continue;
                    if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, false))
                        continue;
                    int d = (t.Position - pawn.Position).LengthHorizontalSquared;
                    if (d < bestDist) { bestDist = d; best = t; }
                }
                if (best != null)
                {
                    nearestStack = best;
                    return def;
                }
            }
            return null;
        }

        /// <summary>Drop trailing stacks once the kept stacks already cover <paramref name="target"/> units.</summary>
        private static void TrimToCount(List<Thing> stacks, int target)
        {
            int sum = 0, keep = 0;
            for (; keep < stacks.Count && sum < target; keep++)
                sum += stacks[keep].stackCount;
            if (keep < stacks.Count)
                stacks.RemoveRange(keep, stacks.Count - keep);
        }
    }
}
