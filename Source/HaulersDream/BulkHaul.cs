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
        [ThreadStatic] private static Dictionary<ISlotGroup, StorageGroupBudget> scratchGroupBudgets;

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
        // jobState is the forced-probe freshness token (see JobStateSignature), unused (0) for automatic
        // entries, which stay fresh purely by the per-tick clear below.
        private struct CachedPlan
        {
            public Job job;
            public int loadID;
            public int jobState;
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
        internal static Job TryBuildBulkJob(Pawn pawn, Thing primary, Job vanillaJob, bool forced, bool forceSweep = false)
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
                // RimIOT compat (#177 + #184): on the AUTOMATIC path, never UPGRADE a haul of a stack RimIOT owns
                // into an into-inventory bulk sweep. That is either a stack in a logistic-network cell (#177: the
                // forced unload re-deposits it, HaulToStack re-partitions it, the scan re-lists it) OR a stack in the
                // ground apron of a powered interface terminal (#184: a full network drops its overflow there, and HD
                // re-pockets + re-unloads it forever). Returning null leaves __result untouched so vanilla's own
                // single haul-out still stands, and RimIOT's own logic converges once HD stops re-picking it up.
                // FORCED player orders ("Haul everything nearby", "Pick up X") use BuildBulkJobForced / BuildPickUpJob
                // and never reach this branch, so an explicit order on a RimIOT stack is still honored. IsPresent
                // short-circuits before any work when RimIOT is absent, so this line is byte-identical then.
                if (RimIOTCompat.IsPresent && RimIOTCompat.IsRimIOTHandledCell(pawn.Map, primary.Position))
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
            // and no heavy scan runs. It's a SUPERSET of the build's AUTOMATIC accept set: on the automatic path
            // (!forced) a bulk plan needs a nearby sweepable, which this gate confirms, so it never suppresses a plan
            // the automatic build would have made. A lone-stack plan with nothing else in the pool comes only from a
            // FORCED probe (the "Haul everything nearby" button / a secondTasked takeover — forced == true, which
            // skips this gate) or the oversized-primary-in-inventory carve-out (which HasPotentialBulkWork itself lets
            // through). The automatic "Haul Urgently" haul is forceSweep BUT forced == false, so it too passes this
            // gate; its only lone-stack case is a post-gate claim race (a neighbor was present when the gate ran but
            // got swept away before the pool build), which the count<2 branch resolves by deferring a lone bulky stack
            // to hands under CE. Skipped for forced probes: the float menu / player orders aren't the hot scan path,
            // and a forced single-stack order can legitimately build with nothing else nearby (oversized-in-inventory,
            // secondTasked takeover).
            if (!forced && !HasPotentialBulkWork(pawn, primary, vanillaJob))
                return null;
            // The memo is keyed on TicksGame, which FREEZES while paused, and a forced probe (the float
            // menu's hover preview calls HasJobOnThing then JobOnThing again to build the option; clicking
            // it calls JobOnThing once more; re-opening the menu or re-clicking while still deciding does
            // it all again) re-enters this many times a second while paused, for the SAME pawn and primary,
            // with nothing about the world actually changed between calls. The previous fix bypassed the
            // memo entirely for forced-while-paused to avoid a narrower bug (a cached "no second order yet"
            // rejection outliving the player queuing that second order later in the same pause), but that
            // made every one of those repeat probes redo the full pool + snowball + per-stack storage-search
            // build from scratch, hundreds of times within a couple of real seconds. That is the ~10-second
            // "Standing" freeze still reported after the earlier fix (issue #160): the debug log shows this
            // exact pattern, a single paused ordering interaction producing 300-900+ identical rebuilds for
            // one pawn, and it compounds with several pawns/orders happening at once.
            //
            // Fix: while paused, still serve the cache, but gate a forced entry's freshness on this pawn's
            // JOB STATE (JobStateSignature) instead of the frozen tick. SecondTaskedNearby (the only thing a
            // forced plan's outcome depends on beyond the primary itself) reads only this pawn's current job
            // and job queue, and neither can change without either a tick advancing (already covered by the
            // per-tick clear) or a new/replaced job appearing on this pawn (which the signature catches:
            // queueing bumps the queue length, a takeover/interrupt swaps in a new current-job loadID). So a
            // genuinely new situation still rebuilds fresh, the exact correctness the old bypass protected,
            // while a repeated probe with nothing queued in between now hits the cache instead of paying the
            // full cost again.
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
            if (forceSweep)
                key ^= unchecked((long)0x5F5F5F5F5F5F5F5FL); // a forceSweep (urgent-haul) plan differs from an ordinary one
            int jobState = forced ? JobStateSignature(pawn) : 0;
            if (cache.TryGetValue(key, out var cached) && (!forced || cached.jobState == jobState))
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
            }
            var plan = BuildBulkJob(pawn, primary, vanillaJob, forced, forceSweep);
            cache[key] = new CachedPlan { job = plan, loadID = plan?.loadID ?? -1, jobState = jobState };
            return plan;
        }

        /// <summary>The forced-plan cache-freshness token for <paramref name="pawn"/>: its current job's loadID
        /// combined with its queued-job count. Everything <see cref="SecondTaskedNearby"/> reads (the current
        /// job and the job queue) can only change via a new/replaced job on this pawn or a tick advancing, and a
        /// new/replaced job always moves one of these two numbers, so an unchanged signature means an unchanged
        /// answer for any other primary sharing this pawn, see the memo comment in <see cref="TryBuildBulkJob"/>.
        /// </summary>
        private static int JobStateSignature(Pawn pawn)
        {
            int curLoadId = pawn.CurJob?.loadID ?? -1;
            int queueCount = pawn.jobs?.jobQueue?.Count ?? 0;
            return unchecked(curLoadId * 397 ^ queueCount);
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
            scratchGroupBudgets?.Clear();
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
        /// enumerator, so it's allocation-free; worst case is O(haulables) when nothing is near. A SUPERSET of the
        /// build's AUTOMATIC-path accept set: on the automatic path the build yields a plan with NO nearby sweepable
        /// only via a FORCED probe (forceSweep / secondTasked with forced == true, which skips this gate) or the
        /// oversized-primary-in-inventory carve-out (handled below). The automatic Haul-Urgently haul is forceSweep
        /// but forced == false, so it passes this gate too; its only lone-stack case is a post-gate claim race, not a
        /// gate miss. So this never rejects a plan the automatic build would have produced.
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
            // RimIOT compat (#177 + #184): hoist the present latch once so the per-candidate RimIOT check below pays a
            // single field read (not a property call) per stack, and nothing at all when RimIOT is absent.
            bool rimIOTPresent = RimIOTCompat.IsPresent;
            foreach (var t in (HashSet<Thing>)map.listerHaulables.ThingsPotentiallyNeedingHauling())
            {
                if (t == null || t == primary || !t.Spawned || t.Map != map || t is Corpse)
                    continue;
                if (t.def == null || !t.def.EverHaulable)
                    continue;
                // RimIOT compat (#177 + #184): a stack RimIOT owns (in a logistic-network cell, or in a powered
                // interface's ground apron) is never a bulk-sweep candidate, so it must not make this cheap gate
                // report "worth sweeping" and trigger the heavy scan. Keeps the gate a SUPERSET of the build's accept
                // set (the build's pool excludes the same stacks), so it never suppresses a plan the build produces.
                if (rimIOTPresent && RimIOTCompat.IsRimIOTHandledCell(map, t.Position))
                    continue;
                // #187a: a keep-on-corpse tainted rag the pool build (BuildPoolInto) will skip must not make this
                // cheap gate report "worth sweeping" and trigger the heavy scan — keeps the gate a SUPERSET of the
                // build's accept set (both skip exactly these), so it never suppresses a plan the build produces.
                if (CorpseStripper.ShouldLeaveTaintedApparel(t, s))
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
            // search-radius anchor), and ResolveGroupBudget finds no slot group at it -> null (unbounded) -> the
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

            // #214 (the RimIOT terminal haul/unload loop, the ROOT fix). When RimIOT is active and this haul's
            // DESTINATION is a logistic-network cell, DON'T upgrade it to an HD bulk haul; leave vanilla's
            // HaulToCell standing. RimIOT owns delivery INTO its own network end to end (walk to the item ->
            // carry -> terminal -> its own DepositHelper), and its StartPath retarget
            // (Patch_StartPath_NetworkItemRedirect, priority 200) rewrites the pickup target of ANY job that
            // walks to a network-def thing into a network-INTERNAL stack. HD's bulk driver then pockets that
            // network stack out of the container (a distance-free SplitOff) and force-unloads it straight back,
            // net-zero, with every job reporting Succeeded so no failure-keyed guard can see it (issues
            // #177/#184/#192/#214). The bulk upgrade is the ONLY job shape RimIOT can turn into that loop:
            // vanilla's HaulToCell keeps a CELL (not a thing) in targetB, so it fails RimIOT's retarget match and
            // RimIOT delivers it correctly instead. Automatic path only (!forced): a one-shot player order can't
            // infinite-loop and is the player's explicit choice. Extends #177's "cede network storage to RimIOT"
            // from picking-up-FROM a network to delivering-INTO one. Tradeoff: an oversized network-bound stack
            // hand-hauls in armfuls instead of one inventory trip (slower, but correct, and only into a RimIOT
            // network). Inert without RimIOT (IsActive is false) and for forced orders.
            if (!forced && RimIOTCompat.IsActive && RimIOTCompat.IsNetworkManagedCell(map, storeCell))
            {
                HDLog.Dbg($"{pawn} bulk-haul into a RimIOT network declined ({primary.LabelShort} -> {storeCell}); "
                          + "left to vanilla + RimIOT so the terminal haul/unload loop can't form.");
                return null;
            }

            float searchRadius = Math.Max(MinSearchRadius,
                (storeCell - primary.Position).LengthHorizontal * SearchRangeFraction);

            // Decide what this order actually does. A forced single order stays single under SecondTasked; the
            // queue scan only matters (and only runs) for forced orders, since automatic hauls always sweep.
            // Resolved BEFORE any mass/CE math so a rejected order never forces a CE inventory recompute. The
            // three outcomes (see BulkHaulPolicy.DecideOrderedHaul): VanillaSingle keeps vanilla's single
            // hand-haul; InventorySingleStack rides ONE oversized stack through inventory with no neighborhood
            // sweep (#223: bug 2's one-trip benefit without letting an oversized stack imply a sweep, which is
            // what made every forced haul behave like "Haul everything nearby" under stack-size mods);
            // SweepNeighbors runs the full bulk sweep (forceSweep, automatic, Always, or a genuine second order).
            bool secondTasked = forced && SecondTaskedNearby(pawn, primary, searchRadius);
            var plan = BulkHaulPolicy.DecideOrderedHaul(s.bulkHaulTrigger, forced, forceSweep, secondTasked,
                forced && partialHandHaul && s.haulOversizedInInventory);
            if (plan == BulkHaulPolicy.OrderedHaulPlan.VanillaSingle)
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

            // #115: under Combat Extended, a very BULKY item (e.g. a 22-bulk cannon shell) fits FEWER units in
            // INVENTORY (CE caps inventory by weight AND bulk) than a plain hands-carry would (vanilla
            // MaxStackSpaceEver, handCap above, is stack/volume-limited only; CE does not bulk-limit the carry
            // tracker). Converting such a haul into an into-inventory sweep then delivers ONE round per trip where a
            // vanilla hand-haul carries a full armful, the reported "hauls CE ammo into the shelf one round at a
            // time". When inventory would carry fewer units OF THIS STACK than hands, DON'T convert: return null so
            // vanilla's single hand-haul __result stands (it carries more, in one trip; the hands are a separate
            // capacity, so this is safe even for a pawn already holding an inventory load). forceSweep (the explicit
            // "Haul everything nearby" order) is exempt, the player asked to sweep. Gated on CE: without it inventory
            // and hands share the one mass/volume limit, so a small primaryTake arises only for an already-loaded
            // pawn, where declining would wrongly abort a legitimate accumulation.
            //
            // #124 (previously overlooked): the hands side must be clamped by the stack's LIVE COUNT, because
            // primaryTake already is (CountWithinCeiling and CE's CanFitInInventory both cap at stackCount). A rock
            // chunk under a chunk-stacking mod (stackLimit raised above 1; every field chunk still its own 1-count
            // stack) otherwise compared fit 1 against def armful N and declined EVERY automatic chunk haul, one
            // hand-haul per chunk, while the forceSweep order (which skips this guard) swept fine. Hands cannot move
            // more than the whole stack either, so when the whole stack fits in inventory the sweep is never worse
            // and the guard now only fires on a genuine partial fit of a multi-unit stack. Bulky ammo stays declined
            // whenever hands would move more of its stack than inventory fits (the #115 trickle); only a stack that
            // fits WHOLE in inventory now converts, where converting also sweeps the neighbors on top.
            if (BulkHaulPolicy.InventoryHaulWorseThanHands(CECompat.IsActive, forceSweep, primaryTake, handCap, primary.stackCount))
                return null;

            // Per-destination-GROUP storage budgets for this plan (#138): ONE shared budget per storage group,
            // so several defs bound for the SAME group draw from one pool of empty cells instead of each pricing
            // the group's full free space (the cross-def over-haul the reporter saw). Reused per-thread scratch,
            // Cleared here (never trusted empty from a prior call). The primary is priced + committed first, so
            // every swept extra sees the room it already claimed.
            var budgets = scratchGroupBudgets ?? (scratchGroupBudgets = new Dictionary<ISlotGroup, StorageGroupBudget>());
            budgets.Clear();

            // Destination space for the primary's def across the chosen storage GROUP (all its cells), read here
            // (before the swept extras) so the #114 clamp can use it before the pickup commits. int.MaxValue means
            // "more than any plan can take" (a large deficit, a group over the scan cap, or no slot group at the
            // cell); 0 means the group is filtered out for this pawn (a denied storage-building filter).
            var primaryBudget = ResolveGroupBudget(pawn, primary, storeCell, map, budgets, out bool primaryDenied);
            int primarySpace = primaryDenied ? 0 : (primaryBudget?.AvailableFor(primary.def) ?? int.MaxValue);

            // #114: if the primary is ALREADY in valid storage (a lower-priority store being UPGRADED to a better
            // one), clamp the pickup to the destination's real remaining space. Otherwise HD pockets the whole source
            // stack, drops only what fits at the destination, and the unload carries the excess right back to the
            // origin store — the wasteful round trip the reporter saw as the high-priority store filling "piecemeal"
            // (many pawns each haul a full stack, drop two or three, carry the rest back). Loose loot (not yet in
            // valid storage) is deliberately NOT clamped: pocketing the whole stack and letting the unload distribute
            // the remainder across OTHER stores is the intended sweep (leaving it on the ground would be worse), and
            // primarySpace is int.MaxValue when the destination has a large deficit, so this binds only when the
            // destination genuinely has little room — exactly the over-haul case.
            if (primarySpace != int.MaxValue && primary.IsInValidStorage())
            {
                primaryTake = Math.Min(primaryTake, primarySpace);
                if (primaryTake <= 0)
                    return null;
            }

            running += primaryTake * primaryUnit;
            bulkRoom -= primaryTake * primaryBulk;

            // The plan's working sets. things/counts ALWAYS start as just the primary (the bulk job's vehicle),
            // whatever the plan; they are copied into the fresh job-owned queues at the end and never handed out
            // themselves. Reused per-thread (Cleared + seeded with the primary).
            var things = scratchThings ?? (scratchThings = new List<Thing>());
            var counts = scratchCounts ?? (scratchCounts = new List<int>());
            things.Clear();
            counts.Clear();
            things.Add(primary);
            counts.Add(primaryTake);

            // #223: only a SweepNeighbors plan pulls in the neighborhood. An InventorySingleStack plan (a forced
            // oversized single order under the SecondTasked trigger) delivers JUST the primary in one inventory
            // trip and must NOT sweep, so it leaves things == [primary] and falls straight through to the
            // single-stack tail below (whose else branch builds the oversized inventory plan, or degrades to a
            // vanilla hand-haul when inventory isn't worth it). This is the fix: an ordered haul no longer sweeps
            // merely because the clicked stack was oversized.
            if (plan == BulkHaulPolicy.OrderedHaulPlan.SweepNeighbors)
            {
                // Candidate pool: everything the haul system wants hauled, near the primary. Forbidden things are
                // already absent from the lister, but area-forbiddance is PER-PAWN, so IsForbidden is re-checked.
                // Reuse the per-thread scratch list (filled fresh below); the work scan builds this for every
                // distinct candidate it probes in a tick, so a fresh allocation per call was the hot-path GC cost.
                var pool = scratchPool ?? (scratchPool = new List<Thing>());
                BuildPoolInto(pool, pawn, primary, map, searchRadius * PoolRadiusHops);

                // Commit the primary's take to its group budget so swept extras (any def, not just the primary's)
                // see the room it has already claimed. When the #114 clamp bound primaryTake to the group's space
                // this leaves the group fully subscribed; for un-clamped loose loot it debits the whole pocketed
                // stack (the pre-#138 per-def seed did the same, only per def; now the empty cells are shared).
                if (!primaryDenied)
                    primaryBudget?.Consume(primary.def, primaryTake);

                // Snowball: from the primary, repeatedly take the nearest eligible candidate within a hop radius of
                // the LAST taken item, so the chain naturally picks things up "on the way" rather than zig-zagging.
                var claimed = RouteSelection.ClaimedByOtherPawns(pawn);
                var last = primary.Position;
                while (things.Count < MaxStacks && running < ceiling - 0.0001f)
                {
                    var next = TakeNearestEligible(pawn, pool, last, searchRadius, claimed, ceiling, running, bulkRoom, budgets, out int take);
                    if (next == null)
                        break;
                    things.Add(next);
                    counts.Add(take);
                    running += take * next.GetStatValue(StatDefOf.Mass);
                    bulkRoom -= take * CECompat.BulkPerUnit(next);
                    last = next.Position;
                }
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
                    // A forceSweep order that collapsed to a lone stack (the cluster it would have swept was claimed
                    // away by another hauler, or the button was clicked over an already-swept area). Two callers:
                    //  - a player-ORDERED (forced) sweep — the "Haul everything nearby" button — MUST always yield a
                    //    bulk job, never degrade to a vanilla single hand-haul (the reported "second click hauls one
                    //    stack solo" bug: the neighbors were already swept/reserved, so pool == 1, and the old
                    //    `forceSweep || haulOversizedInInventory` gate then required the lone stack to be OVERSIZED).
                    //    Build the single-item bulk regardless of size (the takeover prefix can still fold it into a
                    //    running sweep).
                    //  - the AUTOMATIC "Haul Urgently" scan (forced == false, forceSweep == true via the urgent-haul
                    //    postfix) must NOT re-open the CE #115 trickle. The multi-stack guard at ~#469 was EXEMPTED for
                    //    forceSweep so the urgent CLUSTER sweep could run; here, with the pool collapsed to a lone
                    //    BULKY stack, re-apply that same CE compare (passing forceSweep == false runs the real compare)
                    //    so a stack that hands carry better stays a hand-haul — exactly as a non-forceSweep automatic
                    //    haul would have declined at #469. Parity, no trickle; a lone non-bulky/oversized stack (hands
                    //    NOT better) still backpacks in one trip.
                    if (!forced && BulkHaulPolicy.InventoryHaulWorseThanHands(
                            CECompat.IsActive, false, primaryTake, handCap, primary.stackCount))
                        return null;
                    // Clamp to real storage space so a lone oversized stack never plans more than the destination can
                    // take (no stranding); return null only when there is genuinely no room at all.
                    counts[0] = Math.Min(counts[0], deliverable);
                    if (counts[0] <= 0)
                        return null;
                }
                else
                {
                    // Normally a lone primary hauls best in hands (vanilla). EXCEPTION (bug 2): if the stack is too
                    // big for one armful AND storage can take more than one hand-trip would deliver, route it through
                    // inventory so the WHOLE stack moves in one trip instead of leaving part behind. deliverable is
                    // clamped to the destination group's real space (primaryBudget.AvailableFor) so we never strand it.
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
            // #214 general net-zero success-loop backstop: record this AUTOMATIC re-anchor on `primary`. If the
            // same stack is re-built into a bulk haul repeatedly without shrinking (a foreign mod returning it),
            // HaulChurnGuard backs it off the scan and surfaces it: the class-level net for any re-fetcher, beyond
            // the RimIOT-specific gates. Forced one-shot orders can't infinite-loop, so they're excluded.
            if (!forced)
                HaulChurnGuard.NoteBulkAnchor(primary);
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
            // Mark this AS a genuine "take into inventory" order: vanilla's own field for exactly that concept
            // (its "Pick up" float-menu writes the same 120, per PickupDelayPolicy.VanillaDelayTicks), never read
            // by this driver's OWN toils otherwise. JobDriver_BulkHaul reads it back to decide the pickup-pause
            // context (issue #159/#156): unlike playerForced (true for EVERY player order this driver ever runs,
            // including "Prioritize hauling"/"Haul everything nearby", which vanilla hauls to storage instantly),
            // this field is set ONLY here, so it can't be confused with a storage-bound sweep.
            job.takeInventoryDelay = PickupDelayPolicy.VanillaDelayTicks;
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
        /// <param name="keepCount">Units the player chose to keep (from the order slider, #197), or &lt;= 0 to keep
        /// the whole clicked stack. Capped to the stack size and the pawn's carry ceiling.</param>
        internal static Job BuildKeepJob(Pawn pawn, Thing clicked, int keepCount = -1)
        {
            if (pawn == null || clicked == null || !clicked.Spawned)
                return null;
            var s = HaulersDreamMod.Settings;
            if (s == null || pawn.Map == null)
                return null;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return null;
            int planned = keepCount > 0 ? keepCount : clicked.stackCount;
            int take = MassClampedTake(pawn, clicked, planned, s);
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
        /// <param name="keepCount">Units the player chose to keep (from the order slider, #197), or &lt;= 0 to keep
        /// the whole clicked stack. Capped to the stack size and the pawn's carry ceiling.</param>
        internal static Job BuildKeepFromContainerJob(Pawn pawn, Thing clicked, Thing container, int keepCount = -1)
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
            int planned = keepCount > 0 ? keepCount : clicked.stackCount;
            int take = MassClampedTake(pawn, clicked, planned, s);
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
            // RimIOT compat (#177 + #184): a stack RimIOT owns is never pocketed as a swept extra. It either sits in
            // a logistic-network cell (#177: HD leaves the network's contents to RimIOT, which consolidates them; the
            // sweep+re-deposit is the loop) or in the ground apron of a powered interface terminal (#184: a full
            // network's overflow drop, which HD re-pockets + re-unloads forever). Hoist the present latch once so the
            // per-candidate check pays a single field read per stack, and nothing at all when RimIOT is absent.
            bool rimIOTPresent = RimIOTCompat.IsPresent;
            // #187a: settings for the tainted-apparel keep gate (ShouldLeaveTaintedApparel), read once per scan.
            var s = HaulersDreamMod.Settings;
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
                    if (rimIOTPresent && RimIOTCompat.IsRimIOTHandledCell(map, t.Position))
                        continue; // RimIOT owns its network cells + interface apron (see the hoist comment above)
                    if (CorpseStripper.ShouldLeaveTaintedApparel(t, s))
                        continue; // a keep-on-corpse tainted rag (LeaveOnCorpse / DropAndForbid) — never swept
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
                if (rimIOTPresent && RimIOTCompat.IsRimIOTHandledCell(map, t.Position))
                    continue; // RimIOT owns its network cells + interface apron (see the hoist comment above)
                if (CorpseStripper.ShouldLeaveTaintedApparel(t, s))
                    continue; // a keep-on-corpse tainted rag (LeaveOnCorpse / DropAndForbid) — never swept
                pool.Add(t);
            }
        }

        // The nearest pool candidate within `radius` of `from` that passes the full eligibility + capacity
        // gates. Removes the chosen (and any permanently-ineligible) candidates from the pool as it scans.
        private static Thing TakeNearestEligible(Pawn pawn, List<Thing> pool, IntVec3 from, float radius,
            HashSet<Thing> claimed, float ceiling, float runningMass, float bulkRoom,
            Dictionary<ISlotGroup, StorageGroupBudget> budgets, out int take)
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
                // Per-GROUP storage budget (see BuildBulkJob, #138): the first stack targeting a group prices its
                // real remaining space (empty cells shared across defs, partial stacks per def); planned stacks
                // decrement it, and once a def's share is exhausted further candidates of it are rejected (they'd
                // only be floor-dropped at unload). Different defs bound for the same group no longer each price
                // its full free space, the cross-def over-haul the reporter saw.
                var budget = ResolveGroupBudget(pawn, t, destCell, pawn.Map, budgets, out bool denied);
                if (denied)
                    continue; // this stack's destination group is filtered out for the pawn, leave it at origin
                int spaceLeft = budget?.AvailableFor(t.def) ?? int.MaxValue;
                if (spaceLeft != int.MaxValue)
                {
                    fits = Math.Min(fits, spaceLeft);
                    if (fits <= 0)
                        continue; // the group is fully subscribed for this def, leave the stack at its origin
                }
                budget?.Consume(t.def, fits);
                take = fits;
                return t;
            }
        }

        // Hard bound on the per-group cell scan when pricing storage space: groups are typically small, and a
        // group larger than this holds more than any plan can take anyway — treat the cap as "enough".
        private const int MaxSpaceScanCells = 200;

        // Resolve (and cache per plan) the shared budget for the storage GROUP at `cell`, pricing `thing`'s def
        // into it if not already priced. Returns null for an UNBOUNDED destination (no cell-grid clamp): a
        // container destination (cell == Invalid, its capacity is enroute-managed) or no slot group at the cell.
        // Sets `denied` = true when the group is filtered out for this pawn (a denied storage-building filter),
        // the same outcome the old StorageSpaceForDef reported as 0 space. The budget's empty cells are shared
        // across every def bound for the group (#138); partial stacks stay per def.
        private static StorageGroupBudget ResolveGroupBudget(Pawn pawn, Thing thing, IntVec3 cell, Map map,
            Dictionary<ISlotGroup, StorageGroupBudget> budgets, out bool denied)
        {
            denied = false;
            if (!cell.IsValid)
                return null;
            var slotGroup = map.haulDestinationManager.SlotGroupAt(cell);
            if (slotGroup == null)
                return null;
            // Storage-building filter (plan G4): this prices storage via SlotGroupAt + IsGoodStoreCell (NOT
            // TryFindBestBetter*), so the storage-filter funnel postfix can never reach it: apply the building
            // filter HERE. Both guards short-circuit before any new work, so when the feature master is off
            // (StorageBuildingFilter.Enabled == false) OR the current context is the allow-all sentinel (Unload),
            // NO filter call is made and the scan is byte-identical to a build without the filter.
            bool filterActive = StorageBuildingFilter.Enabled
                && StorageBuildingFilter.CurrentContext != StorageFilterContext.Unload;
            var filter = filterActive ? HaulersDreamMod.Settings?.storageBuildingFilter : null;
            if (filter != null && !filter.IsGroupAllowed(slotGroup))
            {
                denied = true;
                return null;
            }
            // Like vanilla: a storage GROUP (linked stockpiles/shelves) pools its members' cells.
            ISlotGroup group = (ISlotGroup)slotGroup.StorageGroup ?? slotGroup;
            if (budgets.TryGetValue(group, out var budget))
            {
                // Group already scanned this plan (its shared empty-cell count is fixed); just price this def
                // into it the first time it appears, so its partial-stack room + per-cell capacity are known.
                if (!budget.Unbounded && !budget.IsPriced(thing.def))
                {
                    ScanGroup(pawn, thing, group, map, filter, out _, out int partial, out int perCell, out _);
                    budget.PriceDef(thing.def, partial, perCell);
                }
                return budget;
            }
            ScanGroup(pawn, thing, group, map, filter, out int emptyCells, out int partialSpace, out int perCellCap,
                out bool unbounded);
            budget = new StorageGroupBudget(unbounded ? int.MaxValue : emptyCells);
            if (!unbounded)
                budget.PriceDef(thing.def, partialSpace, perCellCap);
            budgets[group] = budget;
            return budget;
        }

        // Scan a storage GROUP once for `thing`'s def, splitting its remaining space into the SHARED empty-cell
        // pool (count + per-cell capacity) and this def's PER-DEF partial-stack room, vanilla-style, the same
        // IsGoodStoreCell + GetItemStackSpaceLeftFor pricing HaulAIUtility.HaulToCellStorageJob uses
        // (decompile-verified). `unbounded` = "no binding limit": no cells / a group over the scan cap, or
        // already more space than a whole plan could fill (MaxStacks full stacks). An empty cell (no item at it)
        // joins the shared pool at its per-cell capacity; a cell already holding this def contributes its top-up
        // room as partial; a cell full for this def (or holding another def) contributes nothing.
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
        //    A deep-storage cell reports its whole multi-stack capacity, so per-cell capacity above stackLimit
        //    is captured; when a def opens such an empty cell the plan claims the WHOLE cell (its leftover stays
        //    that def's top-up room), slightly conservative for a deep cell that would accept a second def, but
        //    never an over-haul.
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
        private static void ScanGroup(Pawn pawn, Thing thing, ISlotGroup group, Map map, StorageBuildingFilter filter,
            out int emptyCells, out int partialSpace, out int perCellCapacity, out bool unbounded)
        {
            int stackLimit = Math.Max(1, thing.def.stackLimit);
            emptyCells = 0;
            partialSpace = 0;
            perCellCapacity = stackLimit;
            unbounded = false;
            var cells = group.CellsList;
            if (cells == null || cells.Count > MaxSpaceScanCells)
            {
                unbounded = true;
                return;
            }
            long enough = (long)MaxStacks * stackLimit; // no plan can place more than this
            long emptyUnits = 0;
            long partial = 0;
            int emptyCount = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (!StoreUtility.IsGoodStoreCell(c, map, thing, pawn, pawn.Faction))
                    continue;
                // A linked StorageGroup can pool cells from MULTIPLE buildings, so a denied building's cells must
                // be dropped individually even when the originating group was allowed. Only runs when the filter
                // is active (filterActive ⇒ non-Unload context + feature on); off-path is byte-identical.
                if (filter != null && !filter.IsCellAllowed(c, map))
                    continue;
                int space = c.GetItemStackSpaceLeftFor(map, thing.def);
                if (space <= 0)
                    continue; // full for this def (holds a full stack of it, or another def), no room here
                if (c.GetFirstItem(map) == null)
                {
                    emptyCount++;
                    emptyUnits += space; // an empty cell: its whole capacity feeds the SHARED pool
                }
                else
                {
                    partial += space; // a partial stack of this def: top-up room reserved to this def
                }
            }
            // A group with more room than any single plan could fill needs no clamp (matches the old int.MaxValue).
            if (partial + emptyUnits >= enough)
            {
                unbounded = true;
                return;
            }
            emptyCells = emptyCount;
            partialSpace = (int)partial;
            // Average per-cell capacity of the empty cells (== stackLimit for uniform vanilla cells; larger for
            // deep storage). Used to convert a claimed empty cell into the def's leftover top-up room.
            perCellCapacity = emptyCount > 0 ? Math.Max(1, (int)(emptyUnits / emptyCount)) : stackLimit;
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
