using System;
using System.Collections.Generic;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// BULK HAULING — the native Pick-Up-And-Haul: when a pawn is sent to haul one item to a stockpile, it
    /// immediately plans to pick up everything haulable AROUND it into its inventory too, walks the short
    /// pickup chain, and then makes ONE storage trip (the existing storage-aware unload pass) instead of one
    /// hand-carry round-trip per item. Fully automatic — no button, no designation: the plan is built the
    /// moment the haul job is created (work scan or right-click order), not after the first pickup.
    ///
    /// HOW: a postfix on <see cref="WorkGiver_HaulGeneral.JobOnThing"/> — the single funnel both the automatic
    /// work scan and the float menu's "Prioritize hauling" go through (decompile-verified; forced orders call
    /// the same method with forced:true). When vanilla hands back a HaulToCell job and a sweep is worth it,
    /// we swap it for a <see cref="JobDriver_BulkHaul"/> whose target queue is the full pickup plan. When the
    /// sweep isn't possible (nothing else around, no inventory room, trigger says no) the vanilla job stands —
    /// fail-open, hands-carry is the best plan for a single stack anyway.
    ///
    /// HOW MUCH (the "worth it over more round-trips" math): the smart-overload model. Carrying more saves
    /// trips but slows the pawn past 100% capacity; <see cref="OverloadTuning.MaxOverloadRatio"/> is the
    /// break-even encumbrance where the slowdown starts costing more time than the trip it saves, so the
    /// sweep loads up to ratio × the configured carry limit (<see cref="BulkHaulPolicy.CeilingKg"/>) and no
    /// further. Strict carry weight / overload-off cap at exactly 100%; the "no slowdown" stop carries freely.
    ///
    /// WHEN (the two-option trigger, <see cref="BulkHaulTrigger"/>): automatic hauls always sweep — every
    /// nearby haulable is already tasked by the hauling work itself. A player-ORDERED haul sweeps under
    /// "Always"; under "SecondTasked" (default) only when another haul order is queued nearby — so ordering
    /// a single haul truly hauls just that one thing (the finer-control option).
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_HaulGeneral), nameof(WorkGiver_HaulGeneral.JobOnThing))]
    public static class Patch_WorkGiver_HaulGeneral_BulkHaul
    {
        static void Postfix(ref Job __result, Pawn pawn, Thing t, bool forced)
        {
            try
            {
                var bulk = BulkHaul.TryBuildBulkJob(pawn, t, __result, forced);
                if (bulk != null)
                    __result = bulk;
            }
            catch (Exception e)
            {
                Log.WarningOnce($"[Hauler's Dream] Bulk-haul conversion failed for {pawn} hauling {t}: {e}", 0x4B48);
                // fail-open: the vanilla single haul stands
            }
        }
    }

    public static class BulkHaul
    {
        // Per-hop search radius floor and the fraction of the haul distance used as the hop radius —
        // "around" scales with how far the trip is anyway (a long trip justifies a wider sweep), exactly
        // the Pick-Up-And-Haul constants so the sweep area matches the behavior players know.
        private const float MinSearchRadius = 12f;
        private const float SearchRangeFraction = 0.5f;

        // Hard bound on stacks per sweep (keeps the chain walk + the unload pass + job targets bounded);
        // anything beyond is simply the next haul cycle's work.
        private const int MaxStacks = 24;

        // The candidate pool is pre-filtered to this many hop-radii around the primary, so the snowball
        // can pick things up "on the way" without wandering across the map on a dense field.
        private const float PoolRadiusHops = 4f;

        // Per-tick plan memo. The work scan probes HasJobOnThing (= JobOnThing != null) for EVERY haulable
        // candidate it considers, and the float menu calls JobOnThing once building the option and once on
        // click — each probe would otherwise run the full pool + storage scan below and throw the result
        // away. One generation per tick: same (pawn, primary, forced) within a tick returns the cached plan
        // (null rejections included — those are the common case on a scan).
        private static int cacheTick = -1;
        private static readonly Dictionary<long, Job> planCache = new Dictionary<long, Job>();

        /// <summary>
        /// Build the bulk pickup job for hauling <paramref name="primary"/>, or null when the sweep doesn't
        /// apply (vanilla job stands). PURE planning — no reservations, no designations, no world mutation —
        /// because the float menu calls JobOnThing speculatively while only BUILDING its options.
        /// </summary>
        internal static Job TryBuildBulkJob(Pawn pawn, Thing primary, Job vanillaJob, bool forced)
        {
            if (pawn == null || primary == null)
                return null;
            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick != cacheTick)
            {
                planCache.Clear();
                cacheTick = tick;
            }
            long key = ((long)pawn.thingIDNumber << 32) | (uint)primary.thingIDNumber;
            if (forced)
                key = ~key; // forced and automatic plans differ (the trigger) — separate cache lines
            if (planCache.TryGetValue(key, out var cached))
            {
                // The cache holds LIVE Job instances, and vanilla's JobMaker.ReturnToPool can recycle one
                // same-tick: a pooled-but-unreused job has def == null; a recycled one carries a foreign
                // def/target — both invalidate the entry (rebuild below). Cached nulls (negative results,
                // the common case on a scan) stay served as-is.
                if (cached == null || (cached.def == HaulersDreamDefOf.HaulersDream_BulkHaul && cached.targetA.Thing == primary))
                    return cached;
                planCache.Remove(key);
            }
            var plan = BuildBulkJob(pawn, primary, vanillaJob, forced);
            planCache[key] = plan;
            return plan;
        }

        /// <summary>Drop every cached plan and reset the tick stamp. Called on game load (FinalizeInit):
        /// the statics survive a quickload, and an equal tick number would otherwise serve stale
        /// cross-session Job/Thing instances.</summary>
        internal static void ClearPlanCache()
        {
            planCache.Clear();
            cacheTick = -1;
        }

        private static Job BuildBulkJob(Pawn pawn, Thing primary, Job vanillaJob, bool forced)
        {
            if (vanillaJob == null || vanillaJob.def != JobDefOf.HaulToCell)
                return null; // container destinations (graves, pods) keep their dedicated vanilla flow
            var s = HaulersDreamMod.Settings;
            var map = pawn?.Map;
            if (s == null || !s.bulkHaul || map == null || primary == null || !primary.Spawned)
                return null;
            // Same map gate as YieldRouter.IsCandidate: with the mod disabled on non-home maps a sweep must
            // not fire there either — the driver's finish unload is forced:true and bypasses the checker's gate.
            if (!s.enableOnNonHomeMaps && !map.IsPlayerHome)
                return null;
            if (pawn.Faction != Faction.OfPlayerSilentFail || pawn.IsQuestLodger())
                return null;
            if (pawn.RaceProps != null && pawn.RaceProps.IsMechanoid && !s.allowMechanoids)
                return null;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return null;

            var storeCell = vanillaJob.targetB.Cell;
            float searchRadius = Math.Max(MinSearchRadius,
                (storeCell - primary.Position).LengthHorizontal * SearchRangeFraction);

            // The finer-control trigger: a forced single order stays single under SecondTasked. (The queue
            // scan only matters — and only runs — for forced orders; automatic hauls always sweep.) Checked
            // BEFORE any mass/CE math so a rejected probe never forces a CE inventory recompute.
            bool secondTasked = forced && SecondTaskedNearby(pawn, primary, searchRadius);
            if (!BulkHaulPolicy.TriggerSatisfied(s.bulkHaulTrigger, forced, secondTasked))
                return null;

            // The worth-it mass ceiling (smart-overload break-even; 100% under strict/off; ∞ at "no slowdown").
            // Under Combat Extended the strict path always applies (OverloadGate.NoOverload) and CE's BULK
            // dimension is tracked alongside weight, so a plan never promises more than CE lets the pawn carry.
            float maxCap = MassUtility.Capacity(pawn); // under CE this reads CE's CarryWeight (CE postfix)
            float baseCap = CarryMath.EffectiveCapacity(maxCap, s.carryLimitFraction);
            float ceiling = BulkHaulPolicy.CeilingKg(s.overloadLevel, OverloadGate.NoOverload(s), baseCap);
            float running = MassUtility.GearAndInventoryMass(pawn);
            float bulkRoom = CECompat.AvailableBulk(pawn); // +∞ without CE

            // The primary itself must fit in inventory under the ceiling — if it can't, hands-carry (which has
            // no mass limit) is the better plan and there is nothing to sweep on top of it.
            float primaryUnit = primary.GetStatValue(StatDefOf.Mass);
            int primaryTake = BulkHaulPolicy.CountWithinCeiling(ceiling, running, primaryUnit, primary.stackCount);
            primaryTake = Math.Min(primaryTake, CECompat.MaxFitCount(pawn, primary));
            float primaryBulk = CECompat.BulkPerUnit(primary);
            if (primaryBulk > 0f)
                primaryTake = Math.Min(primaryTake, (int)Math.Floor(bulkRoom / primaryBulk));
            if (primaryTake <= 0)
                return null;

            running += primaryTake * primaryUnit;
            bulkRoom -= primaryTake * primaryBulk;

            // Candidate pool: everything the haul system wants hauled, near the primary. Forbidden things are
            // already absent from the lister, but area-forbiddance is PER-PAWN, so IsForbidden is re-checked.
            var pool = BuildPool(pawn, primary, map, searchRadius * PoolRadiusHops);

            // Snowball: from the primary, repeatedly take the nearest eligible candidate within a hop radius of
            // the LAST taken item — so the chain naturally picks things up "on the way" rather than zig-zagging.
            var things = new List<Thing> { primary };
            var counts = new List<int> { primaryTake };
            var claimed = RouteSelection.ClaimedByOtherPawns(pawn);
            var last = primary.Position;
            while (things.Count < MaxStacks && running < ceiling - 0.0001f)
            {
                var next = TakeNearestEligible(pawn, pool, last, searchRadius, claimed, ceiling, running, bulkRoom, out int take);
                if (next == null)
                    break;
                things.Add(next);
                counts.Add(take);
                running += take * next.GetStatValue(StatDefOf.Mass);
                bulkRoom -= take * CECompat.BulkPerUnit(next);
                last = next.Position;
            }

            if (things.Count < 2)
                return null; // nothing else around — a single stack hauls best in hands (vanilla)

            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_BulkHaul, primary);
            job.targetQueueB = new List<LocalTargetInfo>(things.Count);
            job.countQueue = new List<int>(counts);
            for (int i = 0; i < things.Count; i++)
                job.targetQueueB.Add(things[i]);
            job.count = 1; // sentinel: Job.count defaults to -1, which reads as "broken" in several vanilla checks
            if (s.verboseLogging) // Dbg re-checks, but don't build the interpolated string on every silent success
                HDLog.Dbg($"BulkHaul: {pawn} sweeping {things.Count} stacks (~{running:0.#}kg / ceiling {(float.IsPositiveInfinity(ceiling) ? -1 : ceiling):0.#}kg, forced={forced}).");
            return job;
        }

        // Everything in the haul lister that could plausibly join this sweep, pre-filtered cheap.
        private static List<Thing> BuildPool(Pawn pawn, Thing primary, Map map, float poolRadius)
        {
            var pool = new List<Thing>();
            float radiusSq = poolRadius * poolRadius;
            foreach (var t in map.listerHaulables.ThingsPotentiallyNeedingHauling())
            {
                if (t == null || t == primary || !t.Spawned || t.Map != map)
                    continue;
                if (t is Corpse)
                    continue; // corpse hauling keeps its own vanilla flow (and corpses don't belong in pockets)
                if (!t.def.EverHaulable)
                    continue;
                if ((t.Position - primary.Position).LengthHorizontalSquared > radiusSq)
                    continue;
                pool.Add(t);
            }
            return pool;
        }

        // The nearest pool candidate within `radius` of `from` that passes the full eligibility + capacity
        // gates. Removes the chosen (and any permanently-ineligible) candidates from the pool as it scans.
        private static Thing TakeNearestEligible(Pawn pawn, List<Thing> pool, IntVec3 from, float radius,
            HashSet<Thing> claimed, float ceiling, float runningMass, float bulkRoom, out int take)
        {
            take = 0;
            float radiusSq = radius * radius;
            while (true)
            {
                int bestIdx = -1;
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < pool.Count; i++)
                {
                    float d = (pool[i].Position - from).LengthHorizontalSquared;
                    if (d <= radiusSq && d < bestDistSq)
                    {
                        bestDistSq = d;
                        bestIdx = i;
                    }
                }
                if (bestIdx < 0)
                    return null;
                var t = pool[bestIdx];
                pool.RemoveAt(bestIdx); // taken or rejected — either way it leaves the pool

                // Eligibility — never sweep what another pawn is en route to, what this pawn can't legally
                // haul (per-pawn area forbiddance, reservations, burning, social-propriety, bill-bound), or
                // what has no better storage to go to (it would strand in inventory).
                if (t.IsForbidden(pawn) || claimed.Contains(t))
                    continue;
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced: false))
                    continue;
                int fits = BulkHaulPolicy.CountWithinCeiling(ceiling, runningMass, t.GetStatValue(StatDefOf.Mass), t.stackCount);
                fits = Math.Min(fits, CECompat.MaxFitCount(pawn, t)); // CE: weight AND bulk vs live inventory
                float bulkPer = CECompat.BulkPerUnit(t);
                if (bulkPer > 0f)
                    fits = Math.Min(fits, (int)Math.Floor(bulkRoom / bulkPer)); // CE: planned-but-untaken bulk too
                if (fits <= 0)
                    continue; // too heavy/bulky for the remaining room — a lighter neighbor may still fit
                var currentPriority = StoreUtility.CurrentStoragePriorityOf(t);
                if (!StoreUtility.TryFindBestBetterStorageFor(t, pawn, pawn.Map, currentPriority, pawn.Faction,
                        out _, out _, needAccurateResult: false))
                    continue;
                take = fits;
                return t;
            }
        }

        // "A second nearby item has been tasked": another haul order on this pawn (current or queued) whose
        // target sits within the sweep radius of the primary — i.e. the player explicitly asked for more
        // than one haul around here, so the sweep is what they meant.
        private static bool SecondTaskedNearby(Pawn pawn, Thing primary, float radius)
        {
            if (pawn.jobs == null)
                return false;
            float radiusSq = radius * radius;
            if (IsNearbyHaulOrder(pawn.CurJob, primary, radiusSq))
                return true;
            var q = pawn.jobs.jobQueue;
            if (q != null)
                for (int i = 0; i < q.Count; i++)
                    if (IsNearbyHaulOrder(q[i]?.job, primary, radiusSq))
                        return true;
            return false;
        }

        private static bool IsNearbyHaulOrder(Job j, Thing primary, float radiusSq)
        {
            if (j == null)
                return false;
            // Only a player ORDER counts — the pawn's current AUTOMATIC haul must not satisfy "a second
            // tasked order". Vanilla TryTakeOrderedJob sets playerForced=true BEFORE queueing
            // (decompile-verified), so shift-queued orders still pass.
            if (!j.playerForced)
                return false;
            if (j.def != JobDefOf.HaulToCell && j.def != JobDefOf.HaulToContainer
                && j.def != HaulersDreamDefOf.HaulersDream_BulkHaul)
                return false;
            var target = j.targetA.Thing;
            return target != null && target != primary && target.Spawned
                   && (target.Position - primary.Position).LengthHorizontalSquared <= radiusSq;
        }
    }
}
