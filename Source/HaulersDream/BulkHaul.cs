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
                // Key varies by exception TYPE so a second, different failure mode still gets one report.
                Log.WarningOnce($"[Hauler's Dream] Bulk-haul conversion failed for {pawn} hauling {t}: {e}", 0x4B48 ^ (e.GetType().FullName?.GetHashCode() ?? 0));
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
        // [ThreadStatic] per this assembly's convention for hook-reachable scratch state (see
        // CompHauledToInventory.tmpScoopedDefs) — lazily initialized, since ThreadStatic field
        // initializers only run on the static-ctor thread.
        [ThreadStatic] private static int cacheTick;
        [ThreadStatic] private static Dictionary<long, CachedPlan> planCache;

        // The cached Job plus the loadID it carried at insert. JobMaker.ReturnToPool → Job.Clear() sets
        // loadID = -1 and MakeJob assigns a fresh UniqueIDsManager id (decompile-verified), so a same-tick
        // pool-recycled instance — even one reused for an identically-shaped job — can never validate.
        private struct CachedPlan
        {
            public Job job;
            public int loadID;
        }

        /// <summary>
        /// Build the bulk pickup job for hauling <paramref name="primary"/>, or null when the sweep doesn't
        /// apply (vanilla job stands). PURE planning — no reservations, no designations, no world mutation —
        /// because the float menu calls JobOnThing speculatively while only BUILDING its options.
        /// </summary>
        internal static Job TryBuildBulkJob(Pawn pawn, Thing primary, Job vanillaJob, bool forced)
        {
            if (pawn == null || primary == null)
                return null;
            // The memo is keyed on TicksGame, which FREEZES while paused — but forced probes (the float
            // menu builds its options, and the click re-calls JobOnThing) run while paused. A cached null
            // rejection ("no second order queued yet") taken early in a pause would then be served after
            // the player queues the first order later in the SAME pause, turning the advertised "order a
            // second haul nearby" flow into a single haul. So for forced probes while paused, bypass the
            // memo entirely (neither read nor write) — every paused right-click re-plans fresh. Automatic
            // probes don't run while paused (the work scan is tick-driven), so they keep the memo.
            bool bypassMemo = forced && (Find.TickManager?.Paused ?? false);
            if (bypassMemo)
                return BuildBulkJob(pawn, primary, vanillaJob, forced);
            int tick = Find.TickManager?.TicksGame ?? -1;
            var cache = planCache ?? (planCache = new Dictionary<long, CachedPlan>());
            if (tick != cacheTick)
            {
                cache.Clear();
                cacheTick = tick;
            }
            long key = ((long)pawn.thingIDNumber << 32) | (uint)primary.thingIDNumber;
            if (forced)
                key = ~key; // forced and automatic plans differ (the trigger) — separate cache lines
            if (cache.TryGetValue(key, out var cached))
            {
                // The cache holds LIVE Job instances, and vanilla's JobMaker.ReturnToPool can recycle one
                // same-tick: the stored loadID is the proof of identity (Clear() resets it to -1, MakeJob
                // assigns a fresh one — a recycled instance can never match). The def/target check stays as
                // belt-and-braces. Cached nulls (negative results, the common case on a scan) serve as-is.
                if (cached.job == null)
                    return null;
                if (cached.job.loadID == cached.loadID
                    && cached.job.def == HaulersDreamDefOf.HaulersDream_BulkHaul
                    && cached.job.targetA.Thing == primary)
                    return cached.job;
                cache.Remove(key);
            }
            var plan = BuildBulkJob(pawn, primary, vanillaJob, forced);
            cache[key] = new CachedPlan { job = plan, loadID = plan?.loadID ?? -1 };
            return plan;
        }

        /// <summary>Drop every cached plan and reset the tick stamp. Called on game load (FinalizeInit):
        /// the statics survive a quickload, and an equal tick number would otherwise serve stale
        /// cross-session Job/Thing instances.</summary>
        internal static void ClearPlanCache()
        {
            // Clears the MAIN thread's instance (FinalizeInit runs there) — other threads' caches are
            // per-tick self-clearing anyway, so a stale entry there dies on its next use.
            planCache?.Clear();
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
            // Pawn-aware gate (NoOverloadFor): a non-humanlike hauler (mech lifter) the slowdown StatPart
            // never touches gets the plain carry limit, not the slowdown-for-capacity overload ceiling.
            float maxCap = MassUtility.Capacity(pawn); // under CE this reads CE's CarryWeight (CE postfix)
            float baseCap = CarryMath.EffectiveCapacity(maxCap, s.carryLimitFraction);
            float ceiling = BulkHaulPolicy.CeilingKg(s.overloadLevel, OverloadGate.NoOverloadFor(pawn, s), baseCap);
            float running = MassUtility.GearAndInventoryMass(pawn);
            float bulkRoom = CECompat.AvailableBulk(pawn); // +∞ without CE

            // The primary itself must fit in inventory under the ceiling — if it can't, hands-carry (which has
            // no mass limit) is the better plan and there is nothing to sweep on top of it.
            float primaryUnit = primary.GetStatValue(StatDefOf.Mass);
            int primaryTake = BulkHaulPolicy.CountWithinCeiling(ceiling, running, primaryUnit, primary.stackCount);
            primaryTake = Math.Min(primaryTake, CECompat.MaxFitCount(pawn, primary));
            float primaryBulk = CECompat.BulkPerUnit(primary);
            // +∞ bulkRoom = unlimited (no CE, or CE's bulk read failed fail-open); the cast of ∞/x would
            // be int.MinValue and silently kill every plan — skip the clamp instead.
            if (primaryBulk > 0f && !float.IsPositiveInfinity(bulkRoom))
                primaryTake = Math.Min(primaryTake, (int)Math.Floor(bulkRoom / primaryBulk));
            if (primaryTake <= 0)
                return null;

            running += primaryTake * primaryUnit;
            bulkRoom -= primaryTake * primaryBulk;

            // Candidate pool: everything the haul system wants hauled, near the primary. Forbidden things are
            // already absent from the lister, but area-forbiddance is PER-PAWN, so IsForbidden is re-checked.
            var pool = BuildPool(pawn, primary, map, searchRadius * PoolRadiusHops);

            // Storage-space budget per DEF (one space computation per def per plan, not per item): the per-item
            // existence check below (needAccurateResult:false) would let N same-def stacks all count against the
            // SAME one free cell, and the surplus gets floor-dropped near the pawn at unload — vanilla clamps
            // job.count to the destination group's real space (HaulToCellStorageJob). The FIRST found
            // destination's slot group serves as the def's whole budget — storage groups can differ per item
            // position, so this is an approximation (conservative: leftovers stay at their origin for the next
            // haul cycle, vanilla-like). The primary's def is seeded from the vanilla-chosen destination cell.
            var spaceLeftByDef = new Dictionary<ThingDef, int>();
            int primarySpace = StorageSpaceForDef(pawn, primary, storeCell, map);
            if (primarySpace != int.MaxValue)
                spaceLeftByDef[primary.def] = primarySpace - primaryTake;

            // Snowball: from the primary, repeatedly take the nearest eligible candidate within a hop radius of
            // the LAST taken item — so the chain naturally picks things up "on the way" rather than zig-zagging.
            var things = new List<Thing> { primary };
            var counts = new List<int> { primaryTake };
            var claimed = RouteSelection.ClaimedByOtherPawns(pawn);
            var last = primary.Position;
            while (things.Count < MaxStacks && running < ceiling - 0.0001f)
            {
                var next = TakeNearestEligible(pawn, pool, last, searchRadius, claimed, ceiling, running, bulkRoom, spaceLeftByDef, out int take);
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
            HashSet<Thing> claimed, float ceiling, float runningMass, float bulkRoom,
            Dictionary<ThingDef, int> spaceLeftByDef, out int take)
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
                // Capacity math first — pure arithmetic, vs PawnCanAutomaticallyHaulFast's region-walk
                // (CanReach): an over-heavy/over-bulky candidate never pays the pathfinding cost.
                int fits = BulkHaulPolicy.CountWithinCeiling(ceiling, runningMass, t.GetStatValue(StatDefOf.Mass), t.stackCount);
                fits = Math.Min(fits, CECompat.MaxFitCount(pawn, t)); // CE: weight AND bulk vs live inventory
                float bulkPer = CECompat.BulkPerUnit(t);
                // +∞ bulkRoom (no CE, or CE's bulk read failed fail-open) means the clamp never binds —
                // and (int)Math.Floor(∞/x) would be int.MinValue, killing every plan. Skip it outright.
                if (bulkPer > 0f && !float.IsPositiveInfinity(bulkRoom))
                    fits = Math.Min(fits, (int)Math.Floor(bulkRoom / bulkPer)); // CE: planned-but-untaken bulk too
                if (fits <= 0)
                    continue; // too heavy/bulky for the remaining room — a lighter neighbor may still fit
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced: false))
                    continue;
                var currentPriority = StoreUtility.CurrentStoragePriorityOf(t);
                if (!StoreUtility.TryFindBestBetterStorageFor(t, pawn, pawn.Map, currentPriority, pawn.Faction,
                        out IntVec3 destCell, out _, needAccurateResult: false))
                    continue;
                // Per-def storage budget (see BuildBulkJob): the first stack of a def prices its destination
                // group's real remaining space; planned stacks decrement it, and once it's exhausted further
                // same-def candidates are rejected (they'd only be floor-dropped at unload).
                if (!spaceLeftByDef.TryGetValue(t.def, out int spaceLeft))
                {
                    spaceLeft = StorageSpaceForDef(pawn, t, destCell, pawn.Map);
                    spaceLeftByDef[t.def] = spaceLeft;
                }
                if (spaceLeft != int.MaxValue)
                {
                    fits = Math.Min(fits, spaceLeft);
                    if (fits <= 0)
                        continue; // this def's storage is fully subscribed — leave the stack at its origin
                    spaceLeftByDef[t.def] = spaceLeft - fits;
                }
                take = fits;
                return t;
            }
        }

        // Hard bound on the per-group cell scan when pricing storage space: groups are typically small, and a
        // group larger than this holds more than any plan can take anyway — treat the cap as "enough".
        private const int MaxSpaceScanCells = 200;

        // The destination slot group's total remaining space for `thing`'s def, vanilla-style — the same
        // IsGoodStoreCell + GetItemStackSpaceLeftFor loop HaulAIUtility.HaulToCellStorageJob clamps job.count
        // with (decompile-verified). int.MaxValue = "no binding limit": a container destination (cell ==
        // Invalid — its capacity is enroute-managed), no slot group at the cell, a scan that hit the cell cap,
        // or already more space than a whole plan could fill (MaxStacks full stacks).
        private static int StorageSpaceForDef(Pawn pawn, Thing thing, IntVec3 cell, Map map)
        {
            if (!cell.IsValid)
                return int.MaxValue;
            var slotGroup = map.haulDestinationManager.SlotGroupAt(cell);
            if (slotGroup == null)
                return int.MaxValue;
            // Like vanilla: a storage GROUP (linked stockpiles/shelves) pools its members' cells.
            ISlotGroup group = (ISlotGroup)slotGroup.StorageGroup ?? slotGroup;
            var cells = group.CellsList;
            if (cells == null || cells.Count > MaxSpaceScanCells)
                return int.MaxValue;
            int enough = MaxStacks * Math.Max(1, thing.def.stackLimit); // no plan can place more than this
            int space = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (!StoreUtility.IsGoodStoreCell(cells[i], map, thing, pawn, pawn.Faction))
                    continue;
                space += cells[i].GetItemStackSpaceLeftFor(map, thing.def);
                if (space >= enough)
                    return int.MaxValue;
            }
            return space;
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
