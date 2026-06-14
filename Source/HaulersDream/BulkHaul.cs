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
            // No try/catch: a failure here is a real bug we want surfaced as a red error (Harmony lets the
            // exception propagate to RimWorld's handler), not silently downgraded to a one-time warning.
            var bulk = BulkHaul.TryBuildBulkJob(pawn, t, __result, forced);
            if (bulk != null)
                __result = bulk;
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

        private static Job BuildBulkJob(Pawn pawn, Thing primary, Job vanillaJob, bool forced, bool forceSweep = false)
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
            // Pawn-type gate UNIFIED with the scoop + auto-unload sides (YieldRouter.IsEligible →
            // EligibilityPolicy): only {humanlike colonists, allowed colony mechs} may sweep into inventory.
            // This was previously only "IsMechanoid && !allowMechanoids", which let a NON-mechanoid pawn that
            // is also non-humanlike (a modded robot/android worker race on the Pawn class with a <comps> node,
            // or any future mod that grants animals colony hauling) get stacks swept into its inventory — while
            // the auto-unload side (PawnUnloadChecker → YieldRouter.IsEligible) refuses to empty a non-humanlike
            // non-mech pawn, stranding the load: a black hole. IsEligible still returns allowMechanoids for
            // mechs, so the intended mech-lifter bulk haul is unchanged; it adds the humanlike/animal/robot
            // distinction (and the drafted/incapable rules) so scoop, bulk-haul, and unload are provably
            // symmetric — whatever bulk-haul loads, the unload side can service. (Vanilla animals never reach
            // this postfix anyway — they have no workSettings and haul via JobGiver_Haul, not the work scan —
            // so for them this is defense-in-depth; the live risk is a non-mech robot-worker race.)
            if (!YieldRouter.IsEligible(pawn))
                return null;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return null;

            // BUG 2: how many of this def the pawn can carry in ONE armful (vanilla per-stack carry cap =
            // min(stackLimit, CarryingCapacity / VolumePerUnit) — the exact clamp Toils_Haul.StartCarryThing and
            // HaulAIUtility.HaulToCellStorageJob apply). A stack bigger than this would otherwise be hand-hauled
            // PARTIALLY (e.g. 72 of a 75 steel stack), leaving the remainder for another trip; routing it through
            // mass-limited inventory delivers the whole stack in one trip (decided below once storage is known).
            int handCap = pawn.carryTracker?.MaxStackSpaceEver(primary.def) ?? primary.def.stackLimit;
            bool partialHandHaul = primary.stackCount > handCap;

            var storeCell = vanillaJob.targetB.Cell;
            float searchRadius = Math.Max(MinSearchRadius,
                (storeCell - primary.Position).LengthHorizontal * SearchRangeFraction);

            // The finer-control trigger: a forced single order stays single under SecondTasked. (The queue
            // scan only matters — and only runs — for forced orders; automatic hauls always sweep.) Checked
            // BEFORE any mass/CE math so a rejected probe never forces a CE inventory recompute.
            // Carve-outs: forceSweep (the explicit "Haul everything nearby" button) always sweeps; and a forced
            // order of an OVERSIZED stack rides inventory (bug 2) even with no second order tasked.
            bool secondTasked = forced && SecondTaskedNearby(pawn, primary, searchRadius);
            if (!forceSweep
                && !(forced && partialHandHaul && s.haulOversizedInInventory)
                && !BulkHaulPolicy.TriggerSatisfied(s.bulkHaulTrigger, forced, secondTasked))
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

            // A nearby FIRST player order exists (secondTasked) — but it may already be CARRIED in this pawn's
            // hands, which despawns it and removes it from the lister, so the ground sweep found only the primary
            // (things.Count == 1). Build the single-item bulk anyway: it is the vehicle the takeover folds that
            // first stack into (TryTakeoverSecondOrder → TakeOverSoloHaul drops the carried stack and appends it).
            // Without this, the carried first order can never be re-detected as a "second item" and the takeover
            // never fires — the exact reported bug 1. (The on-ground race already worked because A was still in
            // the pool.) For an Always-trigger or automatic haul secondTasked is false, so this never widens those.
            if (things.Count < 2 && !secondTasked)
            {
                // Normally a lone primary hauls best in hands (vanilla). EXCEPTION (bug 2): if the stack is too
                // big for one armful AND storage can take more than one hand-trip would deliver, route it through
                // inventory so the WHOLE stack moves in one trip instead of leaving part behind. deliverable is
                // clamped to the destination group's real space (StorageSpaceForDef) so we never strand the rest.
                int deliverable = primarySpace == int.MaxValue ? primaryTake : Math.Min(primaryTake, primarySpace);
                bool wantOversized = forceSweep || s.haulOversizedInInventory;
                if (!(wantOversized && BulkHaulPolicy.OversizedStackWorthInventory(primary.stackCount, handCap, deliverable)))
                    return null;
                counts[0] = Math.Min(counts[0], deliverable);
                if (counts[0] <= 0)
                    return null;
            }

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

        /// <summary>
        /// The explicit "Haul everything nearby" order (see <see cref="FloatMenuOptionProvider_HaulNearby"/>):
        /// build a bulk sweep from the clicked item with the SecondTasked trigger pre-satisfied, so it always
        /// sweeps regardless of the setting / second-order requirement. Returns null when there's no storage,
        /// a container destination, or nothing worth sweeping (the caller then falls back to a plain forced
        /// haul). Bypasses the per-tick plan cache (calls the builder directly) — same as it's not a work-scan probe.
        /// </summary>
        internal static Job BuildBulkJobForced(Pawn pawn, Thing clicked)
        {
            if (pawn == null || clicked == null)
                return null;
            var vanilla = HaulAIUtility.HaulToStorageJob(pawn, clicked, forced: true);
            if (vanilla == null || vanilla.def != JobDefOf.HaulToCell)
                return null; // no storage / container destination -> caller falls back to the vanilla haul
            return BuildBulkJob(pawn, clicked, vanilla, forced: true, forceSweep: true);
        }

        // Everything in the haul lister that could plausibly join this sweep, pre-filtered cheap.
        // internal: reused by PackAnimalLoad's bulk pack-animal sweep (same pool, different destination).
        internal static List<Thing> BuildPool(Pawn pawn, Thing primary, Map map, float poolRadius)
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

        // ---- "second order takes over immediately" (the player ordered a 2nd nearby haul under SecondTasked) ----

        /// <summary>
        /// The player just ordered a haul, and under <see cref="BulkHaulTrigger.SecondTasked"/> the JobOnThing
        /// postfix already turned it into a bulk sweep (because a nearby FIRST order existed — that's the only
        /// way SecondTaskedNearby is true). This makes that sweep TAKE OVER IMMEDIATELY instead of waiting
        /// behind the still-solo first haul: if a sweep is already running, the new item folds into it; if the
        /// pawn is still hauling the surgical first item solo (and that item is part of this sweep), the solo
        /// haul is interrupted and the sweep starts now (preserving any unrelated queued work). Returns true if
        /// it handled the order (the caller skips vanilla); false to fall through to vanilla unchanged.
        /// Mirrors the F35 pack-animal coalesce (<see cref="PackAnimalLoad.FindActiveLoadJob"/> /
        /// <see cref="PackAnimalLoad.AppendToLoadJob"/>); the bulk driver re-reads targetQueueB each load cycle,
        /// so an append is swept on the next pass (<see cref="JobDriver_BulkHaul.IsStillLoading"/>).
        /// </summary>
        internal static bool TryTakeoverSecondOrder(Pawn pawn, Job incoming, JobTag? tag, ref bool result)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.bulkHaul || pawn?.jobs == null || incoming == null)
                return false;

            var curJob = pawn.CurJob;
            bool incomingIsBulk = incoming.def == HaulersDreamDefOf.HaulersDream_BulkHaul;
            bool curIsLoadingBulk = curJob != null && curJob.def == HaulersDreamDefOf.HaulersDream_BulkHaul
                && pawn.jobs.curDriver is JobDriver_BulkHaul bd && bd.IsStillLoading;
            // The surgical first haul to fold in: the pawn's CURRENT job is a player-forced single HaulToCell.
            // Resolve its target whether it's still on the ground (en route) OR already in hands. CRUCIAL (the
            // real bug-1 cause): once the first stack is picked up, vanilla sets curJob.targetA to the carried
            // thing and DESPAWNS the ground stack, so it is gone from the haulables lister and can NEVER be in
            // the 2nd order's sweep queue — so we must NOT require queue membership; we fold it in explicitly
            // (below). We only sanity-check that it's in the same neighborhood as this sweep (the bulk exists
            // because SecondTaskedNearby found a player-forced order within the sweep radius a few ticks ago).
            Thing soloTarget = null;
            if (!curIsLoadingBulk && curJob != null && curJob.def == JobDefOf.HaulToCell && curJob.playerForced)
            {
                var a = pawn.carryTracker?.CarriedThing ?? curJob.targetA.Thing;
                var primary = incoming.targetA.Thing;
                if (a != null && primary != null && pawn.Map != null)
                {
                    float r = TakeoverNearbyRadius(pawn, primary);
                    if ((a.PositionHeld - primary.PositionHeld).LengthHorizontalSquared <= r * r)
                        soloTarget = a;
                }
            }

            switch (BulkHaulPolicy.DecideTakeover(s.bulkHaulTrigger, incomingIsBulk, curIsLoadingBulk, soloTarget != null))
            {
                case BulkHaulPolicy.BulkTakeoverAction.AppendToActiveBulk:
                {
                    // 3rd+ order: fold the newly-clicked item into the running sweep — one trip. The driver
                    // re-reads targetQueueB next loadDecide cycle and walks to it; it reserves per-stack itself.
                    var t = incoming.targetA.Thing;
                    AppendToBulkJob(curJob, t, t?.stackCount ?? 0);
                    result = true;
                    return true;
                }
                case BulkHaulPolicy.BulkTakeoverAction.TakeOverSoloHaul:
                {
                    // Respect vanilla's interrupt gate: vanilla's TryTakeOrderedJob only interrupts the current
                    // job when IsCurrentJobPlayerInterruptible() (for a player-forced HaulToCell this is false
                    // only when the pawn is on fire). If it can't be interrupted now, don't force the takeover —
                    // fall through so vanilla handles the order exactly as it would (queue it).
                    if (!pawn.jobs.IsCurrentJobPlayerInterruptible())
                        return false;
                    // 2nd order: the surgical first haul's target is already a sweep member, so interrupt it and
                    // start the bulk NOW. Replicates vanilla TryTakeOrderedJob's interrupt path (set
                    // playerForced/playerInterruptedForced, cancel a busy stance, reserve, EnqueueFirst, end the
                    // current job) — but WITHOUT ClearQueuedJobs, so any unrelated queued work the player set up
                    // survives behind the sweep. (Vanilla sets playerForced in the body we're skipping.)
                    incoming.playerForced = true;
                    if (!incoming.TryMakePreToilReservations(pawn, false))
                        return false; // couldn't reserve (target stolen since menu-build) -> let vanilla handle it
                    // Fold the in-progress first item INTO the sweep so it rides along in one trip (the bug-1
                    // fix — it is otherwise absent from the sweep queue once carried). On the ground: append it
                    // directly. In hands: drop it near the pawn (capturing the resulting/merged stack) so the
                    // bulk re-collects it into inventory — CleanupCurrentJob would drop it anyway, we just capture
                    // the reference to queue it. Done AFTER the reservation succeeds so a failed reserve never
                    // half-mutates (leaves the solo haul untouched).
                    if (soloTarget != null && soloTarget.Spawned)
                        AppendToBulkJob(incoming, soloTarget, soloTarget.stackCount);
                    else if (pawn.carryTracker?.CarriedThing != null
                             && pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing dropped)
                             && dropped != null)
                        AppendToBulkJob(incoming, dropped, dropped.stackCount);
                    curJob.playerInterruptedForced = true;
                    pawn.stances?.CancelBusyStanceSoft();
                    pawn.jobs.jobQueue.EnqueueFirst(incoming, tag ?? JobTag.Misc);
                    pawn.jobs.curDriver.EndJobWith(JobCondition.InterruptForced); // ends the solo haul, starts the bulk
                    HDLog.Dbg($"{pawn} second haul order — bulk sweep takes over now (folding in the first haul).");
                    result = true;
                    return true;
                }
                default:
                    return false; // pass through to vanilla (lone order, unrelated current job, Always trigger, etc.)
            }
        }

        /// <summary>The radius within which the in-progress first haul counts as "in this sweep's neighborhood"
        /// (so the takeover folds it in). The bulk job keeps no destination cell — its TargetB is the driver's
        /// scratch slot — so recompute the sweep radius from the primary's storage exactly as BuildBulkJob does,
        /// then widen to the pool radius so a pawn that has already carried the first item part-way toward
        /// storage still qualifies. (The bulk only EXISTS because SecondTaskedNearby(primary) found a player-
        /// forced order within this radius moments ago, so a related first haul is within it by construction.)</summary>
        private static float TakeoverNearbyRadius(Pawn pawn, Thing primary)
        {
            StoreUtility.TryFindBestBetterStoreCellFor(primary, pawn, pawn.Map,
                StoreUtility.CurrentStoragePriorityOf(primary), pawn.Faction, out IntVec3 storeCell, needAccurateResult: false);
            float dist = storeCell.IsValid ? (storeCell - primary.Position).LengthHorizontal : MinSearchRadius;
            return Math.Max(MinSearchRadius, dist * SearchRangeFraction) * PoolRadiusHops;
        }

        /// <summary>Append a stack to a (current or queued) bulk job's pickup queue — dedup, lazily init, count
        /// clamped at pickup by the driver. Direct mirror of <see cref="PackAnimalLoad.AppendToLoadJob"/>.</summary>
        internal static void AppendToBulkJob(Job job, Thing thing, int count)
        {
            if (job == null || thing == null)
                return;
            if (job.targetQueueB == null)
                job.targetQueueB = new List<LocalTargetInfo>();
            if (job.countQueue == null)
                job.countQueue = new List<int>();
            for (int i = 0; i < job.targetQueueB.Count; i++)
                if (job.targetQueueB[i].Thing == thing)
                    return; // already queued in this job
            job.targetQueueB.Add(thing);
            job.countQueue.Add(count > 0 ? count : thing.stackCount);
        }

        // "A second nearby item has been tasked": another haul order on this pawn (current or queued) whose
        // target sits within the sweep radius of the primary — i.e. the player explicitly asked for more
        // than one haul around here, so the sweep is what they meant.
        private static bool SecondTaskedNearby(Pawn pawn, Thing primary, float radius)
        {
            if (pawn.jobs == null)
                return false;
            float radiusSq = radius * radius;
            if (IsNearbyHaulOrder(pawn, pawn.CurJob, primary, radiusSq))
                return true;
            var q = pawn.jobs.jobQueue;
            if (q != null)
                for (int i = 0; i < q.Count; i++)
                    if (IsNearbyHaulOrder(pawn, q[i]?.job, primary, radiusSq))
                        return true;
            return false;
        }

        private static bool IsNearbyHaulOrder(Pawn pawn, Job j, Thing primary, float radiusSq)
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
            if (target == null || target == primary)
                return false;
            // Carried-aware: once the pawn has already picked the first stack fully INTO HANDS,
            // Toils_Haul.StartCarryThing retargets the job's targetA to carryTracker.CarriedThing, which is
            // despawned (Thing.SplitOff with count>=stackCount despawns the ground stack — decompile-verified).
            // The OLD `target.Spawned` gate therefore made the carried first order invisible, so the 2nd nearby
            // order never became a bulk job and the takeover never fired (the exact reported bug). Treat the
            // carried first stack as located at the pawn's cell (Thing.PositionHeld returns the holder's root
            // cell for a carried thing — decompile-verified) and keep the spawned path for grounded/queued orders.
            if (!target.Spawned && target != pawn.carryTracker?.CarriedThing)
                return false;
            return (target.PositionHeld - primary.PositionHeld).LengthHorizontalSquared <= radiusSq;
        }
    }
}
