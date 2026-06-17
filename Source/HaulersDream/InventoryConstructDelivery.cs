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
    // ORDERING CONTRACT (see the full chain in SharedInventoryPatches.cs): ICD runs AFTER F3b (High=600) and BEFORE
    // both Batch (Low=200) and Routing (Last). ICD converts a vanilla floor HaulToContainer to its inventory-overload
    // job; Batch and Routing both bail on a non-HaulToContainer __result, so they must observe ICD's FINAL
    // converted/declined result. Pinned Normal (400) explicitly — it is numerically the default, but pinning makes the
    // "ICD before Batch" precedence DECLARED rather than coincidental (both previously sat at the default 400, so their
    // relative order was undefined). ICD is order-independent w.r.t. F3b: their action domains are disjoint (F3b builds
    // an inventory-fetch job ICD ignores via the Pawn_InventoryTracker guard; ICD only converts a SPAWNED floor stack).
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    [HarmonyPriority(Priority.Normal)] // run after F3b's floor-empty fetch, before Batch/Routing observe the result
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
                if (s.verboseLogging)
                    HDLog.Dbg($"inv-deliver skip: handCap<=0 for {def.label} ({pawn})");
                return null;
            }

            int frameNeed = (forced || !(c is IHaulEnroute enroute))
                ? c.ThingCountNeeded(def)
                : enroute.GetSpaceRemainingWithEnroute(def, pawn);
            if (frameNeed <= 0)
                return null;

            // Mass / overload capacity. Computed up-front (not just before the gather) because the multi-site
            // cluster scan below bounds itself to ONE overloaded trip's worth of demand — there is no point
            // queuing nearby sites a single trip can't serve. Reads go through the per-(pawn,tick) memo: this is a
            // read-only job-giver decision (not a mutate-then-reread loop), so even a small auto delivery that
            // bails below doesn't pay a fresh GearAndInventoryMass apparel+inventory walk.
            var mass = PawnMassCache.MassInfo(pawn);
            float maxCap = mass.Capacity;
            if (maxCap <= 0f)
                return null;
            float baseCap = CarryMath.EffectiveCapacity(maxCap, s.carryLimitFraction);
            float cur = mass.CurrentMass;
            float unit = def.GetStatValueAbstract(StatDefOf.Mass);
            if (unit <= 0f)
                return null;
            // Pawn-aware (NoOverloadFor): strict / slider-Off / CE — and an ANIMAL (non-mech non-humanlike,
            // never slowed by StatPart) must not gather past its plain limit penalty-free. Player mechs DO
            // overload here and are slowed for it, like colonists.
            int level = OverloadGate.NoOverloadFor(pawn, s) ? OverloadTuning.OffLevel : s.overloadLevel;
            // The most units of def this pawn could carry in ONE overloaded trip (mass-limited, demand-unbounded).
            int maxLoadUnits = ConstructDeliveryPlan.GatherCeiling(level, maxCap, baseCap, cur, unit, int.MaxValue);

            // MULTI-SITE cluster (non-route only): deliver one inventory load to MANY nearby same-material sites.
            // CRITICAL: vanilla's FindNearbyNeeders caps its batch (targetB + targetQueueB) at ONE hand-load of
            // demand — it breaks once neededTotal >= resTotalAvailable, itself capped at MaxStackSpaceEver(def) —
            // so a pawn relying on vanilla's cluster could never load inventory for more sites than a single
            // armful already covers. THAT is the "missed 6 nearby walls" bug. So HD discovers the cluster ITSELF:
            // seed with vanilla's batch (when present), then scan same-material constructibles near the primary,
            // nearest-first, up to one overloaded trip's worth. The driver iterates the needer queue and delivers
            // to each. Route stops gather for the whole route via RouteDemandByDef below and never take this
            // branch (RouteIntent != None). Falls through to the single-needer logic when no real cluster exists.
            var clusterNeeders = (List<Thing>)null;
            int clusterNeed = 0;
            bool multiSite = false;
            if (s.multiSiteConstructDeliver && RouteIntent == ConstructRouteIntent.None && maxLoadUnits > handCap)
            {
                clusterNeeders = new List<Thing>();
                AddClusterNeeder(needer, def, pawn, forced, clusterNeeders, ref clusterNeed); // primary (= targetC, the clicked/scanned site)
                // Seed with vanilla's already-found nearby cluster (known-valid). Vanilla's targetB is the member
                // NEAREST THE RESOURCE — a DISTINCT needer from c — and targetQueueB the rest of its 8-tile batch.
                // AddClusterNeeder dedups, so these are no-ops when they coincide with c or each other.
                if (vanillaJob != null)
                {
                    AddClusterNeeder(vanillaJob.targetB.Thing, def, pawn, forced, clusterNeeders, ref clusterNeed);
                    if (vanillaJob.targetQueueB != null)
                        for (int i = 0; i < vanillaJob.targetQueueB.Count; i++)
                            AddClusterNeeder(vanillaJob.targetQueueB[i].Thing, def, pawn, forced, clusterNeeders, ref clusterNeed);
                }
                // HD's own discovery (the actual fix): same-material sites near the primary that vanilla's
                // one-armful batch left out, nearest-first, until the combined demand fills one trip.
                ScanNearbyNeeders(pawn, needer, def, forced, maxLoadUnits, clusterNeeders, ref clusterNeed);
                // Worth-it: 2+ distinct sites still needing material AND combined demand beats one hand-load.
                multiSite = ConstructDeliveryPlan.MultiSiteWorthIt(clusterNeeders.Count, clusterNeed, handCap);
                if (!multiSite)
                    clusterNeeders = null; // not taking the branch — discard so the job stays single-needer
            }

            // An ORDERED (forced) delivery normally converts — even a small one-hand-trip load — because only
            // our driver carries the tether/route hooks. NOT always trip-neutral though: see the cluster
            // exception below. Auto deliveries keep the "hands already optimal" fast path. The multi-site branch
            // (worth-it gate already requires clusterNeed > handCap across 2+ sites) is never a "one hand-trip".
            if (!multiSite && !forced && frameNeed <= handCap)
            {
                if (s.verboseLogging)
                    HDLog.Dbg($"inv-deliver skip: need {frameNeed} <= handCap {handCap} for {def.label} → {needer.LabelShort} (one hand-trip suffices)");
                return null; // a single hand-trip already satisfies it
            }
            // Plain right-click order (not a route stop) whose remaining need fits ONE hand-stack, where the
            // vanilla job already batches nearby needers (targetQueueB = its 8-tile cluster — right-clicking
            // one wall of a 15-wall line delivers the whole cluster): keep vanilla. Our single-needer
            // conversion would deliver to one needer and discard the rest of the cluster, COSTING trips.
            // Route stops (RouteIntent != None) still always convert — routes depend on per-stop jobs.
            // A multi-site conversion carries the whole cluster, so it does NOT discard it -> doesn't skip.
            if (!multiSite && forced && RouteIntent == ConstructRouteIntent.None && frameNeed <= handCap
                && vanillaJob != null && vanillaJob.targetQueueB != null && vanillaJob.targetQueueB.Count > 0)
            {
                if (s.verboseLogging)
                    HDLog.Dbg($"inv-deliver skip: forced one-hand order for {def.label} → {needer.LabelShort}, " +
                              $"vanilla batches {vanillaJob.targetQueueB.Count} more needer(s) (keep vanilla cluster delivery)");
                return null;
            }

            // A haul+build ROUTE gathers for the WHOLE route in this first sweep, not just this stop — the route
            // executor publishes the per-def total; later stops then deliver from the kept inventory. A MULTI-SITE
            // cluster gathers for the whole cluster's combined demand the same way (the driver delivers to each).
            int gatherNeed = multiSite ? clusterNeed : frameNeed;
            if (RouteIntent == ConstructRouteIntent.HaulBuild && RouteDemandByDef != null
                && RouteDemandByDef.TryGetValue(def, out int routeNeed) && routeNeed > gatherNeed)
                gatherNeed = routeNeed;

            int ceiling = ConstructDeliveryPlan.GatherCeiling(level, maxCap, baseCap, cur, unit, gatherNeed);
            if (!forced && ceiling <= handCap)
            {
                if (s.verboseLogging)
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
            // For a multi-site cluster the demand is the whole cluster's combined need, not one frame's.
            int loadDemand = multiSite ? clusterNeed : frameNeed;
            int targetLoad = forced
                ? UnityEngine.Mathf.Min(gatherNeed, available)
                : ConstructDeliveryPlan.PlanLoad(level, maxCap, baseCap, cur, unit, loadDemand, handCap, available);
            if (targetLoad <= 0)
            {
                if (s.verboseLogging)
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
            // MULTI-SITE: the OTHER cluster needers (everything except the primary, in vanilla's nearest-first
            // order) ride in targetQueueB; the driver delivers to the primary, then walks each in turn. Single-
            // needer jobs leave targetQueueB null (unchanged). targetQueueA is the resource stacks, a separate
            // queue (TargetIndex.A), so there is no collision with the needer queue (TargetIndex.B). Filtered by
            // identity vs the primary rather than index, so it is correct even if the primary weren't [0].
            if (multiSite && clusterNeeders != null && clusterNeeders.Count > 1)
            {
                job.targetQueueB = new List<LocalTargetInfo>();
                for (int i = 0; i < clusterNeeders.Count; i++)
                    if (clusterNeeders[i] != needer)
                        job.targetQueueB.Add(clusterNeeders[i]);
            }
            job.count = targetLoad;
            job.haulMode = HaulMode.ToContainer;

            HDLog.Dbg(multiSite
                ? $"{pawn} inventory-delivers {targetLoad}x {def.label} to a {clusterNeeders.Count}-site cluster " +
                  $"(primary {needer.LabelShort}, cluster need {clusterNeed}, hand cap {handCap}, {stacks.Count} stacks)."
                : $"{pawn} inventory-delivers {targetLoad}x {def.label} to {needer.LabelShort} " +
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

        /// <summary>
        /// Add <paramref name="cand"/> to the cluster if it is a live constructible still needing <paramref name="def"/>,
        /// not already in the list. Accumulates its enroute-aware remaining need into <paramref name="clusterNeed"/>.
        /// Mirrors the single-needer frameNeed math (forced -> raw need, auto -> space-remaining-with-enroute) so the
        /// cluster's combined demand is computed the same way per site.
        /// </summary>
        private static void AddClusterNeeder(Thing cand, ThingDef def, Pawn pawn, bool forced,
            List<Thing> outNeeders, ref int clusterNeed)
        {
            if (cand == null || cand.Destroyed || !cand.Spawned || !(cand is IConstructible ic)
                || outNeeders.Contains(cand))
                return;
            int need = (forced || !(cand is IHaulEnroute ie))
                ? ic.ThingCountNeeded(def)
                : ie.GetSpaceRemainingWithEnroute(def, pawn);
            if (need <= 0)
                return;
            outNeeders.Add(cand);
            clusterNeed += need;
        }

        /// <summary>Radius (cells) around the primary site within which HD looks for additional same-material
        /// construction sites to batch into one inventory load. Generous (vanilla batches only an 8-tile radius),
        /// but the real bound is one overloaded trip's worth of demand, applied nearest-first.</summary>
        private const float ClusterScanRadius = 24f;

        /// <summary>
        /// HD-owned nearby-needer discovery for multi-site delivery. Vanilla's FindNearbyNeeders caps its batch at
        /// ONE hand-load of demand, so the pawn could otherwise never load inventory for more sites than a single
        /// armful covers. Here we scan same-material constructibles ourselves — blueprints + frames within
        /// <see cref="ClusterScanRadius"/> of <paramref name="primary"/>, NEAREST-FIRST — accumulating each site's
        /// remaining need into <paramref name="clusterNeed"/> until it reaches <paramref name="maxLoadUnits"/> (one
        /// overloaded trip's worth; the driver re-serves any overflow on its next job). The (more expensive)
        /// constructability check is applied lazily in nearest-first order, so a big build only pays it for the
        /// closest sites until the load fills.
        /// </summary>
        private static void ScanNearbyNeeders(Pawn pawn, Thing primary, ThingDef def, bool forced, int maxLoadUnits,
            List<Thing> outNeeders, ref int clusterNeed)
        {
            if (clusterNeed >= maxLoadUnits)
                return;
            var map = pawn.Map;
            var anchor = primary.Position;
            float radiusSq = ClusterScanRadius * ClusterScanRadius;
            var scan = new List<Thing>();
            CollectConstructibles(map, ThingRequestGroup.Blueprint, def, pawn, anchor, radiusSq, outNeeders, scan);
            CollectConstructibles(map, ThingRequestGroup.BuildingFrame, def, pawn, anchor, radiusSq, outNeeders, scan);
            // Nearest-first so the closest sites are queued before the load ceiling is reached.
            scan.Sort((a, b) =>
                (a.Position - anchor).LengthHorizontalSquared.CompareTo((b.Position - anchor).LengthHorizontalSquared));
            for (int i = 0; i < scan.Count && clusterNeed < maxLoadUnits; i++)
            {
                var t = scan[i];
                // Mirror vanilla IsNewValidNearbyNeeder: deliverable/constructible by this pawn (skills unchecked;
                // forced:false — these are opportunistic extras, not the explicitly clicked target).
                if (!GenConstruct.CanConstruct(t, pawn, checkSkills: false, forced: false, JobDefOf.HaulToContainer))
                    continue;
                AddClusterNeeder(t, def, pawn, forced, outNeeders, ref clusterNeed);
            }
        }

        /// <summary>
        /// Append spawned constructibles of <paramref name="group"/> that still need <paramref name="def"/>, lie
        /// within <paramref name="radiusSq"/> of <paramref name="anchor"/>, aren't already queued, and pass the
        /// cheap nearby-needer validity (player faction, not forbidden, not a Blueprint_Install). The expensive
        /// <c>GenConstruct.CanConstruct</c> + need/dedup are finalized by the caller after the nearest-first sort.
        /// </summary>
        private static void CollectConstructibles(Map map, ThingRequestGroup group, ThingDef def, Pawn pawn,
            IntVec3 anchor, float radiusSq, List<Thing> alreadyQueued, List<Thing> outScan)
        {
            var things = map.listerThings.ThingsInGroup(group);
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                if (t == null || !t.Spawned || t is Blueprint_Install || !(t is IConstructible ic))
                    continue;
                if ((t.Position - anchor).LengthHorizontalSquared > radiusSq)
                    continue;
                if (t.Faction != pawn.Faction || t.IsForbidden(pawn))
                    continue;
                if (ic.ThingCountNeeded(def) <= 0)
                    continue;
                if (!alreadyQueued.Contains(t))
                    outScan.Add(t);
            }
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
