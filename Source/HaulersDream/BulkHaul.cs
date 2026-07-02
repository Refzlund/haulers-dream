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
            // A failure here is a real bug: it must stay a visible red error, never a silent downgrade. The
            // Finalizer below does exactly that (logs with HD + pawn context, then RETHROWS — see HDGuard), so the
            // fault still propagates to RimWorld's handler but is now attributable instead of an anonymous stack.
            var bulk = BulkHaul.TryBuildBulkJob(pawn, t, __result, forced);
            if (bulk != null)
                __result = bulk;
        }

        // Seam guard (fix/mix): WorkGiver_HaulGeneral.JobOnThing is the funnel for BOTH the automatic haul scan and
        // forced "Prioritize hauling". A throw here would break this pawn's hauling job entirely with no
        // HD-attributable trace — log it (with the pawn), then rethrow so it still surfaces.
        static System.Exception Finalizer(System.Exception __exception, Pawn pawn)
            => HDGuard.SeamThrew(__exception, "WorkGiver_HaulGeneral.JobOnThing (HD bulk-haul)", pawn,
                "this pawn's hauling job could not be built this scan.");
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

        // Reused per-call scratch so the (frequent) cache-miss build allocates nothing for its working sets —
        // the work scan calls BuildBulkJob for EVERY distinct candidate it probes in a tick (each a cache miss),
        // and a fresh List/Dictionary per call was pure GC pressure on the hot scan path. [ThreadStatic] +
        // lazy-init matches the planCache convention (a threading-mod work scan gets its own buffers).
        // SAFETY: a single BuildBulkJob call runs to completion (no re-entrancy — it makes no nested JobOnThing
        // probe) before the next reuse, so sharing these across calls on one thread is sound. Each is Cleared at
        // the point of use, never trusted to be empty from a prior call.
        [ThreadStatic] private static List<Thing> scratchPool;
        [ThreadStatic] private static Dictionary<ThingDef, int> scratchSpaceLeftByDef;

        // The snowball working sets (things/counts) — reused per BuildBulkJob call instead of a fresh List per
        // probe. The job-OWNED targetQueueB/countQueue are still allocated fresh below (the Job pool owns + scribes
        // them); these two are only the transient build scratch, copied INTO the job lists at the end. Cleared at the
        // point of use, never trusted empty. SAFETY: a single BuildBulkJob runs to completion (no nested JobOnThing
        // probe) before the next reuse, so sharing on one thread is sound — same contract as scratchPool above.
        [ThreadStatic] private static List<Thing> scratchThings;
        [ThreadStatic] private static List<int> scratchCounts;

        // The cached Job plus the loadID it carried at insert. JobMaker.ReturnToPool → Job.Clear() sets
        // loadID = -1 and MakeJob assigns a fresh UniqueIDsManager id (decompile-verified), so a same-tick
        // pool-recycled instance — even one reused for an identically-shaped job — can never validate.
        private struct CachedPlan
        {
            public Job job;
            public int loadID;
        }

        // Self-register the per-session plan-cache clear so the game-load hygiene sweep can never forget it (see
        // CacheRegistry). The static ctor runs once, the first time any BulkHaul member is touched — which is also
        // the only way the cache can hold cross-session data — so a never-used cache is simply never registered.
        static BulkHaul() => CacheRegistry.Register(ClearPlanCache);

        /// <summary>
        /// Build the bulk pickup job for hauling <paramref name="primary"/>, or null when the sweep doesn't
        /// apply (vanilla job stands). PURE planning — no reservations, no designations, no world mutation —
        /// because the float menu calls JobOnThing speculatively while only BUILDING its options.
        /// </summary>
        internal static Job TryBuildBulkJob(Pawn pawn, Thing primary, Job vanillaJob, bool forced)
        {
            if (pawn == null || primary == null)
                return null;
            // Per-pawn "Auto-haul yields" opt-out (the #1 gizmo): on the AUTOMATIC path (the haul work-scan
            // postfix and the animal hook, both forced:false) a pawn toggled OFF must NOT have its ordinary
            // hauls transformed into nearby-loot inventory sweeps — same gate IsCandidate applies to the
            // scoop/self-pickup paths, and what the gizmo tooltip promises. FORCED player orders ("Haul
            // everything nearby", "Pick up X") use separate entry points (BuildBulkJobForced/BuildPickUpJob)
            // and intentionally override the standing toggle.
            if (!forced)
            {
                // C5 master gate (G1 INTAKE-only): with the master switch off, HD stops UPGRADING ordinary
                // automatic hauls into nearby-loot bulk sweeps — the vanilla single haul __result still stands
                // (the postfix leaves it untouched on null). This rejects only the autonomous bulk UPGRADE; it
                // is NOT an unload path (the bulk driver's finish-unload only runs once a sweep has loaded), so a
                // pawn already carrying a swept load is unaffected. FORCED player orders ("Haul everything
                // nearby", "Pick up X") use BuildBulkJobForced/BuildPickUpJob, which never reach this branch.
                // Cheapest possible early-out, before the comp/bleed reads.
                if (!MasterEnable.Active)
                    return null;
                var optOut = pawn.GetComp<CompHauledToInventory>();
                if (optOut != null && !optOut.autoHaulYields)
                    return null;
                // C1 bleeding gate (While-You're-Up parity, default ON; G1 INTAKE-only): on the AUTOMATIC path
                // a badly bleeding pawn must NOT have its ordinary haul transformed into a nearby-loot sweep —
                // it should get treated, not load up. FORCED player orders ("Haul everything nearby", "Pick up
                // X") use BuildBulkJobForced/BuildPickUpJob, which never reach this branch, so an explicit order
                // still sweeps when bleeding (the player asked for it). This rejects only the bulk UPGRADE; the
                // vanilla single haul __result still stands (the postfix leaves it untouched on null), so a
                // bleeding pawn isn't blocked from hauling outright — and no unload path is affected.
                if (!YieldRouter.FitToStartHaul(pawn))
                    return null;
                // #5: don't convert a haul whose target another mod has CLAIMED via a designation (e.g. Recycle
                // This's recycle/destroy order) into an into-inventory sweep — that despawns the item and hides it
                // from that mod's WorkGiver (which scans only SPAWNED designated things), stalling the order. Let
                // vanilla haul it instead (to storage, where it stays spawned + designated + processable). Automatic
                // path only; an explicit player order on such an item (forced) is the player's deliberate choice.
                if (ForeignOrderGuard.ClaimedByForeignOrder(primary))
                    return null;
            }
            // CHEAP FRONT GATE (microstutter fix): the work scan calls JobOnThing for every haulable candidate
            // it considers, and on a cache miss the build below runs the full pool enumeration + ClaimedByOtherPawns
            // (scans every colony pawn's job queue) + storage scans — far too expensive to run per candidate when
            // the answer is almost always "no sweep". HasPotentialBulkWork is a cheap reject: feature on + comp
            // present + eligible + at least one OTHER haulable within the sweep pool radius. It early-returns on
            // the first nearby haulable (so it's near-instant on a dense field) and is allocation-free (it iterates
            // the lister's HashSet via the struct enumerator and builds no list); worst case is O(haulables) when
            // nothing is near. Mirrors TransportLoad.HasPotentialBulkWork. When it rejects, the vanilla job stands
            // and no heavy scan runs. It's a SUPERSET of the build's AUTOMATIC accept set: a plan with NO nearby sweepable is
            // produced only by forceSweep / secondTasked (forced-only, and forced probes skip this gate) or the
            // oversized-primary-in-inventory carve-out (which HasPotentialBulkWork itself lets through), so it
            // never suppresses a plan the automatic build would have made. Skipped for forced probes: the float
            // menu / player orders aren't the hot scan path, and a forced single-stack order can legitimately
            // build with nothing else nearby (oversized-in-inventory, secondTasked takeover).
            if (!forced && !HasPotentialBulkWork(pawn, primary, vanillaJob))
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
            // Drop any cross-session Thing references the scratch buffers still hold (they're Cleared again
            // at the next build before being read, so this is hygiene, not correctness).
            scratchPool?.Clear();
            scratchSpaceLeftByDef?.Clear();
            scratchThings?.Clear();
            scratchCounts?.Clear();
            // RouteSelection's per-(pawn,tick) claimed-set memo is now cleared DIRECTLY by the game-load hygiene
            // sweep (it self-registers its ClearClaimedCache with CacheRegistry), so the former transitive call
            // from here is gone — the registry is the single source of truth and the two caches are decoupled.
        }

        /// <summary>
        /// Cheap "is a bulk sweep even possible here?" reject for the AUTOMATIC work-scan path — run BEFORE the
        /// expensive pool/claimed/storage scans so a candidate that can't sweep costs only this. Mirrors
        /// <see cref="TransportLoad.HasPotentialBulkWork"/>: feature on, the comp present, the pawn auto-eligible,
        /// a HaulToCell destination (containers keep vanilla flow), on an allowed map, and at least one OTHER
        /// haulable within the same pool radius the build would use. Returns on the FIRST nearby hit (so it's
        /// near-instant on a dense field) and builds no list — it scans the lister's HashSet via the struct
        /// enumerator, so it's allocation-free; worst case is O(haulables) when nothing is near. A SUPERSET of the build's AUTOMATIC-path accept set: the build yields a
        /// plan with NO nearby sweepable only via forceSweep / secondTasked (both forced-only, and forced probes
        /// skip this gate entirely) or the oversized-primary-in-inventory carve-out (handled below), so this
        /// never rejects a plan the automatic build would have produced.
        /// </summary>
        private static bool HasPotentialBulkWork(Pawn pawn, Thing primary, Job vanillaJob)
        {
            if (vanillaJob == null || vanillaJob.def != JobDefOf.HaulToCell)
                return false; // container destinations (graves, pods) keep their dedicated vanilla flow
            var s = HaulersDreamMod.Settings;
            var map = pawn?.Map;
            if (s == null || !s.bulkHaul || map == null || primary == null || !primary.Spawned)
                return false;
            if (!MapGate.HdActiveOnMap(map))
                return false;
            if (pawn.Faction != Faction.OfPlayerSilentFail || pawn.IsQuestLodger())
                return false;
            // Pawn-type gate, identical to BuildBulkJob (kept symmetric with scoop + unload via IsEligible).
            if (!YieldRouter.IsEligible(pawn))
                return false;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return false;

            // CARVE-OUT (bug 2): a single OVERSIZED stack (bigger than one armful) routes through inventory even
            // with nothing else nearby, so the whole stack moves in one trip. This applies on the AUTOMATIC path
            // too (not gated on forced), so the gate must let it through and defer to the build (which prices the
            // destination's real space via OversizedStackWorthInventory). Cheap: MaxStackSpaceEver is arithmetic.
            if (s.haulOversizedInInventory)
            {
                int handCap = pawn.carryTracker?.MaxStackSpaceEver(primary.def) ?? primary.def.stackLimit;
                if (primary.stackCount > handCap)
                    return true;
            }

            // The SAME pool radius the build derives, so this gate can never be narrower than the real sweep:
            // searchRadius = max(floor, store-distance × fraction); pool = searchRadius × hops. The store cell
            // is already resolved on the vanilla job (targetB) — no extra storage lookup here.
            var storeCell = vanillaJob.targetB.Cell;
            float searchRadius = Math.Max(MinSearchRadius,
                (storeCell - primary.Position).LengthHorizontal * SearchRangeFraction);
            float poolRadiusSq = (searchRadius * PoolRadiusHops) * (searchRadius * PoolRadiusHops);

            // First nearby OTHER haulable wins — bounded by an early return, no pool list, no per-item storage
            // scan. Matches BuildPool's cheap pre-filter (spawned item, same map, not the primary, not a corpse,
            // EverHaulable, within pool radius). The deeper eligibility (forbidden / claimed / capacity / storage)
            // is the build's job; here we only need to know the heavy scan is worth running at all.
            // Cast to the concrete HashSet<Thing> the lister returns (its ThingsPotentiallyNeedingHauling
            // return type is the ICollection<Thing> interface, decompile-verified) so the foreach binds the
            // struct enumerator and boxes nothing on this hot per-candidate gate.
            foreach (var t in (HashSet<Thing>)map.listerHaulables.ThingsPotentiallyNeedingHauling())
            {
                if (t == null || t == primary || !t.Spawned || t.Map != map || t is Corpse)
                    continue;
                if (t.def == null || !t.def.EverHaulable)
                    continue;
                if ((t.Position - primary.Position).LengthHorizontalSquared <= poolRadiusSq)
                    return true;
            }
            return false;
        }

        private static Job BuildBulkJob(Pawn pawn, Thing primary, Job vanillaJob, bool forced, bool forceSweep = false)
        {
            // A CONTAINER destination (a grave-destined corpse, container storage) is accepted ONLY for the
            // explicit "Haul everything nearby" order (forceSweep): the anchor is pocketed like anything else and
            // the unload's container branch delivers it. The AUTOMATIC scan and the forced single-order takeover
            // stay cell-only (HasPotentialBulkWork gates the scan the same way), so ordinary container-storage
            // hauls keep their dedicated vanilla flow. targetB.Cell below is the container's PositionHeld (a fine
            // search-radius anchor), and StorageSpaceForDef finds no slot group at it -> int.MaxValue -> the
            // primary's def simply gets no plan-time storage budget (the unload re-clamps at delivery anyway).
            if (vanillaJob == null
                || (vanillaJob.def != JobDefOf.HaulToCell
                    && !(forceSweep && vanillaJob.def == JobDefOf.HaulToContainer)))
                return null;
            var s = HaulersDreamMod.Settings;
            var map = pawn?.Map;
            if (s == null || !s.bulkHaul || map == null || primary == null || !primary.Spawned)
                return null;
            // Same map gate as YieldRouter.IsCandidate: with the mod disabled on non-home maps a sweep must
            // not fire there either — the driver's finish unload is forced:true and bypasses the checker's gate.
            if (!MapGate.HdActiveOnMap(map))
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
            // Pawn-aware gate (NoOverloadFor): only an ANIMAL (non-mech non-humanlike) stands down to the
            // plain carry limit; player humanlikes AND mechs overload to the break-even ceiling and are
            // slowed for it by StatPart_Overload (decision model and slowdown stay in lockstep).
            float maxCap = CarryCapacity.Of(pawn); // MassUtility.Capacity ×HD mech mult; under CE reads CE's CarryWeight
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
            // Reuse the per-thread scratch list (filled fresh below) — the work scan builds this for every
            // distinct candidate it probes in a tick, so a fresh allocation per call was the hot-path GC cost.
            var pool = scratchPool ?? (scratchPool = new List<Thing>());
            BuildPoolInto(pool, pawn, primary, map, searchRadius * PoolRadiusHops);

            // Storage-space budget per DEF (one space computation per def per plan, not per item): the per-item
            // existence check below (needAccurateResult:false) would let N same-def stacks all count against the
            // SAME one free cell, and the surplus gets floor-dropped near the pawn at unload — vanilla clamps
            // job.count to the destination group's real space (HaulToCellStorageJob). The FIRST found
            // destination's slot group serves as the def's whole budget — storage groups can differ per item
            // position, so this is an approximation (conservative: leftovers stay at their origin for the next
            // haul cycle, vanilla-like). The primary's def is seeded from the vanilla-chosen destination cell.
            // Reused per-thread scratch, Cleared here (never trusted to be empty from a prior call).
            var spaceLeftByDef = scratchSpaceLeftByDef ?? (scratchSpaceLeftByDef = new Dictionary<ThingDef, int>());
            spaceLeftByDef.Clear();
            int primarySpace = StorageSpaceForDef(pawn, primary, storeCell, map);
            if (primarySpace != int.MaxValue)
                spaceLeftByDef[primary.def] = primarySpace - primaryTake;

            // Snowball: from the primary, repeatedly take the nearest eligible candidate within a hop radius of
            // the LAST taken item — so the chain naturally picks things up "on the way" rather than zig-zagging.
            // Reused per-thread working sets (Cleared + seeded with the primary), copied into the fresh job-owned
            // queues at the end — never handed out themselves.
            var things = scratchThings ?? (scratchThings = new List<Thing>());
            var counts = scratchCounts ?? (scratchCounts = new List<int>());
            things.Clear();
            counts.Clear();
            things.Add(primary);
            counts.Add(primaryTake);
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
            if (things.Count < 2)
            {
                int deliverable = primarySpace == int.MaxValue ? primaryTake : Math.Min(primaryTake, primarySpace);
                if (secondTasked)
                {
                    // bug 1: a nearby FIRST player order exists but may already be CARRIED in this pawn's hands,
                    // which despawns it and removes it from the lister, so the ground sweep found only the primary.
                    // Keep the single-item bulk as the vehicle the takeover folds that carried stack into
                    // (TryTakeoverSecondOrder → TakeOverSoloHaul). count stays primaryTake (the takeover, not
                    // storage space, governs what rides along). For Always/automatic hauls secondTasked is false.
                }
                else if (forceSweep)
                {
                    // The explicit "Haul everything nearby" button MUST always yield a bulk job — never degrade to
                    // a vanilla single hand-haul. The reported bug: shift-clicking the button a second time found
                    // the neighbors already swept/reserved by the first sweep, so the ground pool was empty
                    // (things.Count == 1); the old `forceSweep || haulOversizedInInventory` gate then still required
                    // the lone clicked stack to be OVERSIZED, so a normal stack fell through to `return null` and
                    // the caller hauled it solo as "haul Nx steel". Build the single-item bulk regardless of size so
                    // the order stays "haul everything nearby" (and the takeover prefix can fold it into a running
                    // sweep). Clamp to real storage space so a lone oversized clicked stack never plans more than the
                    // destination can take (no stranding); return null only when there is genuinely no room at all
                    // (the caller then falls back to a plain forced haul).
                    counts[0] = Math.Min(counts[0], deliverable);
                    if (counts[0] <= 0)
                        return null;
                }
                else
                {
                    // Normally a lone primary hauls best in hands (vanilla). EXCEPTION (bug 2): if the stack is too
                    // big for one armful AND storage can take more than one hand-trip would deliver, route it through
                    // inventory so the WHOLE stack moves in one trip instead of leaving part behind. deliverable is
                    // clamped to the destination group's real space (StorageSpaceForDef) so we never strand the rest.
                    if (!(s.haulOversizedInInventory && BulkHaulPolicy.OversizedStackWorthInventory(primary.stackCount, handCap, deliverable)))
                        return null;
                    counts[0] = Math.Min(counts[0], deliverable);
                    if (counts[0] <= 0)
                        return null;
                }
            }

            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_BulkHaul, primary);
            job.targetQueueB = new List<LocalTargetInfo>(things.Count);
            job.countQueue = new List<int>(counts);
            for (int i = 0; i < things.Count; i++)
                job.targetQueueB.Add(things[i]);
            job.count = 1; // sentinel: Job.count defaults to -1, which reads as "broken" in several vanilla checks
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
            // Accept a CONTAINER destination too (a grave-destined corpse, container storage): the explicit
            // "Haul everything nearby" order previously degraded to a plain single hand-haul there — the player
            // asked for a sweep and got no sweep. The bulk driver pockets the anchor like anything else and the
            // unload's container branch delivers it (graves included). No storage at all still returns null
            // (the caller falls back to the vanilla haul, whose fail reason explains it).
            if (vanilla == null || (vanilla.def != JobDefOf.HaulToCell && vanilla.def != JobDefOf.HaulToContainer))
                return null;
            return BuildBulkJob(pawn, clicked, vanilla, forced: true, forceSweep: true);
        }

        /// <summary>
        /// The explicit "Pick up X" order (see <see cref="FloatMenuOptionProvider_PickUpIntoInventory"/>): build a
        /// SINGLE-stack <see cref="JobDriver_BulkHaul"/> job that loads ONLY the clicked stack into the pawn's
        /// inventory — tagged on <see cref="CompHauledToInventory"/> and serviced by the normal storage-aware
        /// unload pass — instead of a raw untagged TakeInventory (which would be a black hole under the default
        /// unloadAllSurplus=false). Distinct from <see cref="BuildBulkJobForced"/>: NO nearby sweep, just the one
        /// clicked stack. PURE planning — no reservations/mutation (the menu builds options speculatively) — exactly
        /// like the other forced builders; the driver re-clamps the count to live mass/CE room at pickup.
        ///
        /// PUAH-parity (THE fix): the clicked stack is picked into inventory REGARDLESS of whether there is a better
        /// storage destination right now — steel already in its best stockpile, or no accepting stockpile at all. The
        /// whole stack is requested into inventory, MASS/CE-limited ONLY, NOT stack- and NOT storage-limited. The
        /// tagged stock then just sits in inventory until the player makes somewhere to put it; the storage-aware
        /// unload pass services it then (it re-finds storage and clamps placement to the destination's real space at
        /// unload time, so a partial-fit never over-loads a near-full stockpile — the surplus rides back in inventory
        /// rather than stranding), and <see cref="Alert_CannotUnloadInventory"/> Condition A surfaces a genuinely
        /// no-destination load as a red alert — never a silent black hole. (The previous behavior REQUIRED a storage
        /// destination and clamped the pickup to its remaining space, so a click on a stack with no better storage
        /// no-op'd with a misleading "nothing to haul nearby" toast — the bug this fixes.) Returns null only when not
        /// one more unit fits in inventory under the carry ceiling (the caller then falls back to a plain forced
        /// hand-haul, which has no mass limit, and only if THAT has no destination is the order truly impossible).
        /// </summary>
        internal static Job BuildPickUpJob(Pawn pawn, Thing clicked)
        {
            if (pawn == null || clicked == null || !clicked.Spawned)
                return null;
            var s = HaulersDreamMod.Settings;
            var map = pawn.Map;
            if (s == null || map == null)
                return null;
            // Same map gate as the sweep builder: with the mod inert on non-home maps the driver's forced finish
            // unload would have no storage to flush to there (the provider already gates on this, but keep the
            // builder self-consistent in case it's reached another way).
            if (!MapGate.HdActiveOnMap(map))
                return null;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return null;

            // NO storage requirement (the whole point of this fix): PUAH picks the clicked stack into inventory
            // whether or not it has somewhere better to go right now — steel already in its best stockpile, or no
            // accepting stockpile at all. The tagged load is serviced by the storage-aware unload pass later (which
            // re-finds and re-clamps to real storage at unload time), and Alert_CannotUnloadInventory (Condition A)
            // surfaces a genuinely no-destination load as a red alert — never a silent black hole. So the pickup is
            // limited ONLY by what the pawn can carry (mass + CE), NOT by stack limit and NOT by storage space. The
            // whole clicked stack is requested; the unload pass clamps placement to the destination's real space.

            // Clamp to the worth-it mass ceiling for this pawn (per-pawn base cap × the overload break-even ratio),
            // exactly as BuildBulkJob prices the primary, so a single oversized/heavy stack never plans more than the
            // pawn can actually carry into inventory. Under CE also clamp to CE's live weight+bulk fit.
            int take = MassClampedTake(pawn, clicked, clicked.stackCount, s);
            if (take <= 0)
                return null; // nothing fits in inventory under the ceiling -> caller hand-hauls it

            // A single-stack bulk job: the clicked stack is the primary (index 0), so the driver treats it with
            // vanilla semantics (it's what the order assigned — never skipped for being in valid storage, and a
            // forbidden primary is still taken because forcing means it). One entry in the pickup queue; count = 1
            // is the Job.count sentinel the bulk driver/vanilla checks expect (the real per-stack counts live in
            // countQueue). Built by the same JobMaker path as BuildBulkJob so the driver re-reads it identically.
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_BulkHaul, clicked);
            job.targetQueueB = new List<LocalTargetInfo> { clicked };
            job.countQueue = new List<int> { take };
            job.count = 1;
            return job;
        }

        /// <summary>The number of units of <paramref name="thing"/> a single pickup/keep should take into
        /// <paramref name="pawn"/>'s inventory, clamped to the worth-it mass ceiling (per-pawn base cap × the overload
        /// break-even ratio) and — under Combat Extended — CE's live weight+bulk fit. Shared by the "Pick up X" and
        /// "Keep X in inventory" builders and re-applied live at pickup time by their drivers. <paramref name="planned"/>
        /// caps the request (the clicked stack's size for a full take).</summary>
        internal static int MassClampedTake(Pawn pawn, Thing thing, int planned, HaulersDreamSettings s)
        {
            if (pawn == null || thing == null || s == null)
                return 0;
            float maxCap = CarryCapacity.Of(pawn);
            float baseCap = CarryMath.EffectiveCapacity(maxCap, s.carryLimitFraction);
            float ceiling = BulkHaulPolicy.CeilingKg(s.overloadLevel, OverloadGate.NoOverloadFor(pawn, s), baseCap);
            float running = MassUtility.GearAndInventoryMass(pawn);
            int take = BulkHaulPolicy.CountWithinCeiling(ceiling, running, thing.GetStatValue(StatDefOf.Mass),
                Math.Min(planned, thing.stackCount));
            take = Math.Min(take, CECompat.MaxFitCount(pawn, thing));
            float bulkPer = CECompat.BulkPerUnit(thing);
            float bulkRoom = CECompat.AvailableBulk(pawn); // +∞ without CE
            // +∞ bulkRoom = unlimited (no CE / fail-open); (int)Math.Floor(∞/x) would be int.MinValue and kill the
            // plan — skip the clamp instead. Mirrors BuildBulkJob's primary bulk clamp exactly.
            if (bulkPer > 0f && !float.IsPositiveInfinity(bulkRoom))
                take = Math.Min(take, (int)Math.Floor(bulkRoom / bulkPer));
            return take;
        }

        /// <summary>
        /// The "Keep X in inventory" order (see <see cref="FloatMenuOptionProvider_KeepInInventory"/>): build a
        /// single-target <see cref="JobDriver_KeepInInventory"/> job that takes the clicked stack into the pawn's
        /// inventory as a KEPT item (recorded on <see cref="CompHauledToInventory"/>), which HD's unload never hauls
        /// away and vanilla's drop-unused never sheds. The counterpart to <see cref="BuildPickUpJob"/> ("pick up to
        /// haul") — here the item is HELD, not stored, so there is NO map/storage gate (a pawn can hold an item on any
        /// map). Mass/CE-clamped to what the pawn can carry; returns null when not one more unit fits under the ceiling.
        /// </summary>
        internal static Job BuildKeepJob(Pawn pawn, Thing clicked)
        {
            if (pawn == null || clicked == null || !clicked.Spawned)
                return null;
            var s = HaulersDreamMod.Settings;
            if (s == null || pawn.Map == null)
                return null;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return null;
            int take = MassClampedTake(pawn, clicked, clicked.stackCount, s);
            if (take <= 0)
                return null; // nothing fits in inventory under the ceiling
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_KeepInInventory, clicked);
            job.count = take;
            return job;
        }

        /// <summary>
        /// The "Keep X in inventory" order for an item held INSIDE a spawned container building (vanilla's egg box —
        /// the only vanilla def with containedItemsSelectable — or a modded container that flags its contents):
        /// the same job as <see cref="BuildKeepJob"/>, with the container as target B so
        /// <see cref="JobDriver_KeepInInventory"/> takes its container branch — walk to the container and pull the
        /// clamped count straight from its inner ThingOwner, instead of scooping off the ground. PURE planning like
        /// every builder here (the item's continued presence in the container is re-verified live at the take);
        /// mass/CE-clamped identically; returns null when the item already left the container or not one more unit
        /// fits under the carry ceiling.
        /// </summary>
        internal static Job BuildKeepFromContainerJob(Pawn pawn, Thing clicked, Thing container)
        {
            if (pawn == null || clicked == null || container == null || !container.Spawned)
                return null;
            var s = HaulersDreamMod.Settings;
            if (s == null || pawn.Map == null || container.Map != pawn.Map)
                return null;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return null;
            // Still inside that container right now (the float menu builds options speculatively; the driver
            // re-verifies at the take, so a mid-walk removal degrades to a quiet no-op there).
            var inner = container.TryGetInnerInteractableThingOwner();
            if (inner == null || !inner.Contains(clicked))
                return null;
            int take = MassClampedTake(pawn, clicked, clicked.stackCount, s);
            if (take <= 0)
                return null; // nothing fits in inventory under the ceiling
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_KeepInInventory, clicked, container);
            job.count = take;
            return job;
        }

        // Everything in the haul lister that could plausibly join this sweep, pre-filtered cheap.
        // internal: reused by PackAnimalLoad's bulk pack-animal sweep + TransportLoad (same pool, different
        // destination) — those callers OWN the returned list, so this allocates a fresh one for them.
        internal static List<Thing> BuildPool(Pawn pawn, Thing primary, Map map, float poolRadius)
        {
            var pool = new List<Thing>();
            BuildPoolInto(pool, pawn, primary, map, poolRadius);
            return pool;
        }

        // Fill (Clearing first) the provided buffer with the candidate pool — lets the hot bulk-haul path reuse
        // a per-thread scratch list instead of allocating one per JobOnThing probe. Same filter as BuildPool.
        private static void BuildPoolInto(List<Thing> pool, Pawn pawn, Thing primary, Map map, float poolRadius)
        {
            pool.Clear();
            float radiusSq = poolRadius * poolRadius;
            // Cast to the concrete HashSet<Thing> backing the lister (ThingsPotentiallyNeedingHauling's return type is
            // the ICollection<Thing> interface; the field is a HashSet<Thing>, decompile-verified) so the foreach binds
            // the struct enumerator and boxes nothing on this per-pawn-scan pool build. `as` + null fallback to the
            // interface foreach future-proofs against a backing-type change (then degrades to the boxed enumerator).
            var haulables = map.listerHaulables.ThingsPotentiallyNeedingHauling();
            var haulableSet = haulables as HashSet<Thing>;
            if (haulableSet != null)
            {
                foreach (var t in haulableSet)
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
                return;
            }
            foreach (var t in haulables)
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
                    // MP determinism: break distance ties by thingIDNumber so all clients pick the same stack
                    // (HashSet iteration order can differ per client).
                    if (d <= radiusSq && (d < bestDistSq
                        || (d == bestDistSq && bestIdx >= 0 && pool[i].thingIDNumber < pool[bestIdx].thingIDNumber)))
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
                // haul (per-pawn area forbiddance, reservations, burning, social-propriety, bill-bound), what
                // another mod has claimed via a designation (#5: a Recycle This item — scooping it into inventory
                // would hide it from that mod's spawned-only WorkGiver), or what has no better storage to go to.
                if (t.IsForbidden(pawn) || claimed.Contains(t) || ForeignOrderGuard.ClaimedByForeignOrder(t))
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
                // Bonus extra: never path a vacuum-/fire-concerned pawn through a Deadly region for a swept
                // candidate (PawnCanAutomaticallyHaulFast reaches at NormalMaxDanger, which is Deadly while the
                // plan is built under a forced job / open float menu — that exemption is for the clicked primary).
                if (!ExtraSweepReach.Allows(pawn, t))
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
        //
        // STORAGE-MOD COMPATIBILITY BY CONSTRUCTION (no references, no reflection — verified against the
        // LWM Deep Storage / KanbanStockpile / SatisfiedStorage / Adaptive Storage Framework sources):
        //  * ACCEPTANCE is honored exactly: the IsGoodStoreCell gate runs NoStorageBlockersIn, which every one
        //    of those mods patches (ASF transpiles it; LWM prefixes it; Kanban postfixes it for ssl/srt;
        //    SatisfiedStorage replaces it with its refill-hysteresis gate). So a cell those mods call "full"
        //    is skipped here — we never sweep toward storage they reject.
        //  * RAW PER-CELL CAPACITY is honored exactly: GetItemStackSpaceLeftFor reads Building.MaxItemsInCell ->
        //    GridsUtility.GetMaxItemsAllowedInCell, the single seam vanilla maxItemsInCell, LWM's
        //    CompDeepStorage.MaxNumberStacks (prefix) and ASF's per-cell limit (transpile) all funnel through.
        //  * The only residual is a NUMERIC over-estimate for mods whose count cap lives OFF this seam:
        //    Kanban's `mss` (max similar stacks) + `srt` partials sit only on the per-THING HaulToStorageJob
        //    count clamp; SatisfiedStorage's fill-line has no count clamp at all; LWM mass-limited shelves are
        //    mass-blind here. This is a SAFE UPPER BOUND, never an under-estimate, and it self-corrects with no
        //    strand: the deposit re-gate (JobDriver_UnloadHauledInventory.FindTargetOrDrop re-runs the same
        //    mod-aware TryFindBestBetterStorageFor per carried stack; PlaceHauledThingInCell re-targets any
        //    remainder) deposits what each mod-capped cell actually accepts and re-routes / floor-drops the rest
        //    for normal hauling — bounded one-cycle churn, never a black hole. So NO per-mod compat patch is
        //    needed for any of them.
        //  * DO NOT "tighten" this by clamping with HaulAIUtility.HaulToCellStorageJob/HaulToStorageJob.count:
        //    that count is PER-THING (clamped to one stack's stackCount), so Math.Min-ing it in would cap the
        //    whole bulk sweep to a single armful and cripple bulk hauling — the over-estimate above is the
        //    correct, deliberate design (the deposit re-gate is the authority).
        private static int StorageSpaceForDef(Pawn pawn, Thing thing, IntVec3 cell, Map map)
        {
            if (!cell.IsValid)
                return int.MaxValue;
            var slotGroup = map.haulDestinationManager.SlotGroupAt(cell);
            if (slotGroup == null)
                return int.MaxValue;
            // This method prices storage via SlotGroupAt + IsGoodStoreCell (NOT TryFindBestBetter*), so the
            // storage-filter funnel postfix can never reach it — apply the building filter HERE (plan G4:
            // BulkHaul.StorageSpaceForDef filtered internally). Both guards short-circuit before any new work,
            // so when the feature master is off (StorageBuildingFilter.Enabled == false) OR the current context
            // is the allow-all sentinel (Unload), NO filter call is made and the scan is byte-identical to today.
            bool filterActive = StorageBuildingFilter.Enabled
                && StorageBuildingFilter.CurrentContext != StorageFilterContext.Unload;
            var filter = filterActive ? HaulersDreamMod.Settings?.storageBuildingFilter : null;
            // If the destination group's building is denied in this context, it offers no usable space at all —
            // report zero so the candidate def is rejected (the same outcome as a fully-subscribed destination).
            if (filter != null && !filter.IsGroupAllowed(slotGroup))
                return 0;
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
                // A linked StorageGroup can pool cells from MULTIPLE buildings, so a denied building's cells
                // must be dropped individually even when the originating group was allowed. Only runs when the
                // filter is active (filterActive ⇒ non-Unload context + feature on); off-path is byte-identical.
                if (filter != null && !filter.IsCellAllowed(cells[i], map))
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
