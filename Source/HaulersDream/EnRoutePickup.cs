using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// EN-ROUTE PICKUP — the signature "While You're Up" mechanic, ported onto HD's own hauling. When a pawn is
    /// about to set off on a job, and a loose haulable lies roughly ALONG the way to that job, the pawn grabs it
    /// into its inventory first (as a tagged HD bulk-haul pickup, serviced by the normal storage-aware unload),
    /// so the stray item rides to storage on a trip the pawn was making anyway — zero extra round-trips.
    /// DEFAULT ON (a behavior-CHANGING feature that ships enabled); the very first line of the postfix returns
    /// when it's off, so the whole feature is fully inert when disabled.
    ///
    /// HOW: a Harmony POSTFIX on <see cref="Pawn_JobTracker.TryOpportunisticJob"/> — the single vanilla seam that
    /// already exists to prefix an opportunistic haul onto a job (decompile-verified signature
    /// <c>public Job TryOpportunisticJob(Job finalizerJob, Job job)</c>, an instance method on
    /// <see cref="Pawn_JobTracker"/>; the pawn is the <c>protected Pawn pawn</c> field, read via Harmony
    /// <c>___pawn</c>). Vanilla runs its own opportunistic search first; we only act when vanilla produced
    /// NOTHING (<c>__result == null</c>) so we never override vanilla's own opportunistic haul, only add HD's
    /// (which carries into INVENTORY — many items per trip — instead of vanilla's single hand-haul).
    ///
    /// <para><b>Trip-ratio math</b> is the pure <see cref="EnRoutePickupPolicy"/> (a faithful port of WYU's
    /// <c>OpportunityDetour.CanHaul</c>): a candidate is grabbed only if the resulting
    /// <c>pawn → thing → store → job</c> trip stays within tight multiples of the direct <c>pawn → job</c> trip.
    /// The cascade short-circuits cheap → expensive in the EXACT order the Core's
    /// <see cref="EnRoutePickupPolicy"/> XML doc prescribes (squared straight-line range gates → leg-ratio
    /// gates → the cross-cluster guards → the midway store search → the reachability/accuracy stage), with the
    /// expanding-range heuristic (<see cref="EnRoutePickupPolicy.MaxRanges"/>) ordering the expensive work
    /// most-optimistically.</para>
    ///
    /// <para><b>Conflict guards (plan G2/G3/G7/G8):</b>
    /// <list type="bullet">
    ///   <item><b>G8 no-recursion</b> — the emitted job reuses <see cref="HaulersDreamDefOf.HaulersDream_BulkHaul"/>,
    ///   whose JobDef has <c>allowOpportunisticPrefix == false</c> (the bare-bool default; no XML override).
    ///   When the caller then runs <c>StartJob(bulkJob)</c>, vanilla's <c>TryOpportunisticJob</c> re-enters and
    ///   returns null on that flag BEFORE this postfix could add another — but a postfix still runs, so the
    ///   FIRST thing this method does (after the feature gate) is the SAME <c>allowOpportunisticPrefix</c> gate
    ///   vanilla uses, so a bulk-haul job can never recursively spawn another en-route pickup.</item>
    ///   <item><b>G2 anti-double-haul</b> — a candidate is rejected if another pawn already targets it
    ///   (<see cref="RouteSelection.ClaimedByOtherPawns"/>, which skips self) OR this pawn's own current/queued
    ///   job already targets it (<see cref="OwnJobTargets"/>, the self half ClaimedByOtherPawns omits). And the
    ///   whole pass no-ops if the pawn already holds a queued HD pickup/bulk job.</item>
    ///   <item><b>G3 mutex</b> — before searching, <see cref="EnRouteMutexPolicy.MustStandDown"/> stands en-route
    ///   down whenever the pawn is at the opportunistic-UNLOAD divert point (it should shed, not accumulate).</item>
    ///   <item><b>G7 one shared building filter</b> — the midway store search runs inside a
    ///   <see cref="StorageBuildingFilter.PushContext"/>(<see cref="StorageFilterContext.Opportunistic"/>) scope
    ///   and applies the shared <see cref="StorageBuildingFilter"/> per group/cell, so a building the player
    ///   denied for opportunistic hauls is excluded (byte-inert when the filter feature is off).</item>
    /// </list></para>
    ///
    /// <para>Allocation-light: the candidate list + the per-scan store-cell cache are reused per-thread scratch
    /// (Cleared at the point of use); <see cref="EnRoutePickupPolicy.MaxRanges"/> is a stack struct; the cascade
    /// runs no per-candidate LINQ/closures. The path stages pool every <see cref="PawnPath"/>
    /// (<c>ReleaseToPool</c> in a finally).</para>
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryOpportunisticJob))]
    public static class Patch_Pawn_JobTracker_EnRoutePickup
    {
        static void Postfix(ref Job __result, Pawn ___pawn, Job job)
        {
            // BYTE-INERT WHEN OFF (the very first line): feature off -> nothing runs, zero overhead.
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enRoutePickup)
                return;
            // Master kill switch: en-route pickup is an AUTOMATIC behavior, so master-OFF suppresses it (G1 —
            // this is an intake postfix, never an unload path, so suppressing it strands nothing).
            if (!MasterEnable.Active)
                return;
            // Don't override vanilla's own opportunistic haul: only act when vanilla produced nothing.
            if (__result != null)
                return;
            if (job?.def == null || ___pawn == null)
                return;
            // G8 NO-RECURSION: mirror vanilla's own gate. A job whose def forbids the opportunistic prefix
            // (our HaulersDream_BulkHaul has allowOpportunisticPrefix == false by JobDef default) can never
            // carry an en-route pickup, so a re-entrant StartJob(bulkJob) -> TryOpportunisticJob is a no-op here.
            if (!job.def.allowOpportunisticPrefix)
                return;

            // No try/catch: a failure here is a real bug we want surfaced as a red error (Harmony lets it
            // propagate to RimWorld's handler), never silently downgraded — same policy as the BulkHaul postfix.
            var enrouteJob = TryEnRouteJob(___pawn, job);
            if (enrouteJob != null)
                __result = enrouteJob;
        }

        // The candidate working list + the per-scan store-cell memo, reused per-thread so a scan allocates
        // nothing for its working sets. [ThreadStatic] + lazy-init matches this assembly's convention for
        // hook-reachable scratch state (BulkHaul.scratchPool, CompHauledToInventory.tmpScoopedDefs): a threading
        // mod's think scan gets its own buffers. SAFETY: one TryEnRouteJob call runs to completion (it makes no
        // nested TryOpportunisticJob probe) before the next reuse, and each buffer is Cleared at the point of use
        // — never trusted to be empty from a prior call.
        [ThreadStatic] private static List<Thing> scratchHaulables;
        [ThreadStatic] private static Dictionary<Thing, IntVec3> scratchStoreCellCache;

        /// <summary>
        /// Build the en-route pickup job for the pawn about to start <paramref name="job"/>, or null when no
        /// candidate qualifies (vanilla's null result stands). PURE planning: no reservations, no designations,
        /// no world mutation (the bulk driver makes its own reservations + re-clamps at pickup) — same contract
        /// as <see cref="BulkHaul.BuildPickUpJob"/>.
        /// </summary>
        private static Job TryEnRouteJob(Pawn pawn, Job job)
        {
            var s = HaulersDreamMod.Settings;
            var map = pawn.Map;
            if (map == null || !pawn.Spawned)
                return null;

            // ELIGIBILITY — a valid HD hauler with the comp, the right race/faction/map, not drafted/forming a
            // caravan, the per-pawn auto-haul opt-out honored, and the bleeding gate. IsEligible already covers
            // race (humanlike/mech/animal), drafted (pauseWhileDrafted), incapable, and the non-home-map gate;
            // it does NOT cover faction, so check it explicitly (mirrors BulkHaul / the scoop entry points).
            if (pawn.Faction != Faction.OfPlayerSilentFail || pawn.IsQuestLodger())
                return null;
            if (!YieldRouter.IsEligible(pawn))
                return null;
            if (pawn.IsFormingCaravan())
                return null;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null || pawn.inventory == null)
                return null;
            // Per-pawn "Auto-haul yields" opt-out (the #1 gizmo): a toggled-off pawn never grabs loose loot
            // en-route (en-route IS autonomous intake), matching what the gizmo tooltip promises and the scoop /
            // sweep / bulk-haul intake gates (YieldRouter.IsCandidate / BulkHaul.TryBuildBulkJob).
            if (!comp.autoHaulYields)
                return null;
            // C1 bleeding gate (While-You're-Up parity, default ON; G1 INTAKE-only): a badly bleeding pawn should
            // get treated, not detour to grab a stray item. INTAKE-only — a pawn already carrying still unloads.
            if (!YieldRouter.FitToStartHaul(pawn))
                return null;

            // G2 SELF no-op: if the pawn already holds (current or queued) an HD pickup/bulk job, it is already
            // about to sweep loot into inventory — don't stack another en-route pickup on top.
            if (AlreadyHaulingIntoInventory(pawn))
                return null;

            // The DIRECT job target the pawn is heading to. For a DoBill the pawn walks first to an ingredient
            // (WYU OpportunityDetour.cs:100), so use the first queued ingredient's cell; otherwise targetA.
            LocalTargetInfo jobTarget = ResolveJobTarget(job);
            IntVec3 jobCell = jobTarget.Cell;
            if (!jobCell.IsValid || jobCell.IsForbidden(pawn))
                return null;
            // Mirror vanilla's "the job is right next to me" skip (TryOpportunisticJob: distance < 3 -> null):
            // there is no meaningful "on the way" for a job a few tiles away.
            if (pawn.Position.DistanceTo(jobCell) < 3f)
                return null;

            // G3 MUTEX: stand en-route down whenever the pawn is at the opportunistic-unload divert point — it
            // should shed its load, not accumulate more. Computed from the SAME live numbers the unload divert
            // reads (scooped load fraction, a storable representative cell, the divert cooldown), so the two
            // features hand off at exactly the same boundary.
            if (MustStandDownForUnload(pawn, comp, s))
                return null;

            // ----- the candidate cascade (Core EnRoutePickupPolicy interleave contract) ----------------------
            var haulables = scratchHaulables ?? (scratchHaulables = new List<Thing>());
            haulables.Clear();
            // Cast to the concrete HashSet<Thing> backing the lister (ThingsPotentiallyNeedingHauling's return
            // type is the ICollection<Thing> interface; the field is a HashSet<Thing>, decompile-verified) so the
            // copy binds the struct enumerator and boxes nothing. `as` + null fallback future-proofs the type.
            var listed = map.listerHaulables.ThingsPotentiallyNeedingHauling();
            var listedSet = listed as HashSet<Thing>;
            if (listedSet != null)
                haulables.AddRange(listedSet);
            else
                haulables.AddRange(listed);
            if (haulables.Count == 0)
                return null;
            // MP determinism: the scan returns the FIRST candidate that wins in the current range band, so its
            // outcome depends on iteration order — and the source is a HashSet whose order can differ per client.
            // Sort by thingIDNumber (a stable total order, synced via UniqueIDsManager) so every client scans the
            // candidates in the same order and picks the same en-route stack.
            haulables.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

            var storeCellCache = scratchStoreCellCache ?? (scratchStoreCellCache = new Dictionary<Thing, IntVec3>());
            storeCellCache.Clear();

            var claimed = RouteSelection.ClaimedByOtherPawns(pawn); // skips self (G2 "others" half)

            EnRoutePickupPolicy.MaxRanges ranges = default;
            ranges.Reset(); // WYU defaults (MaxStartToThing=30, ...PctOrigTrip=0.5, MaxStoreToJob=50, ...Pct=0.6)

            float pawnToJob = pawn.Position.DistanceTo(jobCell);

            try
            {
                int i = 0;
                while (haulables.Count > 0)
                {
                    if (i == haulables.Count)
                    {
                        // Band exhausted with no winner -> widen the cheap range gates and rescan, so the
                        // expensive store/path searches run in the most-optimistic (closest-first) order
                        // (WYU OpportunityDetour.cs:146-152). A bounded number of expansions: once the ranges
                        // grow past the map the next pass either finds something or hard-fails everything.
                        ranges.Expand();
                        i = 0;
                        // Safety stop: if the band has grown beyond any plausible map span and the list is still
                        // non-empty (every remaining candidate keeps RangeFailing/skip), bail rather than spin.
                        if (ranges.ExpandCount > MaxExpansions)
                            return null;
                        continue;
                    }

                    var thing = haulables[i];
                    var result = TryCandidate(pawn, thing, jobCell, pawnToJob, in ranges, claimed, comp,
                        storeCellCache, map, s, out Job picked);
                    if (picked != null)
                        return picked;

                    switch (result)
                    {
                        case CandidateOutcome.RangeFail:
                            // A straight-line RANGE failure (a squared-range gate). WYU
                            // (OpportunityDetour.cs:156-162): in Vanilla it's a HARD reject (remove + continue);
                            // in BOTH Default and Pathfinding it merely SKIPS this candidate (advance — it may
                            // pass in a wider band). The FullStop case below is reached ONLY from the PATH-COST
                            // stage in Default mode, never from a range failure.
                            if (s.enRoutePathChecker == EnRoutePathChecker.Vanilla)
                                haulables.RemoveAt(i);
                            else
                                i++;
                            continue;
                        case CandidateOutcome.HardFail:
                            haulables.RemoveAt(i); // can never qualify regardless of band
                            continue;
                        case CandidateOutcome.FullStop:
                            return null; // the Default-mode path-cost stage said "stop the whole scan"
                        default: // Skip — reached only on a winning candidate (handled by the picked != null above)
                            i++;
                            continue;
                    }
                }
                return null;
            }
            finally
            {
                haulables.Clear();
                storeCellCache.Clear();
            }
        }

        // A hard ceiling on band expansions so a fruitless scan can never spin: each expansion ×2 the ranges, so
        // ~12 expansions takes the 30-tile start cap past 120000 tiles — far beyond any RimWorld map (max 425).
        private const int MaxExpansions = 12;

        private enum CandidateOutcome { RangeFail, HardFail, FullStop, Skip }

        /// <summary>
        /// Run the full per-candidate cascade for one <paramref name="thing"/> in the current range band. On a
        /// winning candidate sets <paramref name="picked"/> to the emitted job and the return value is unused;
        /// otherwise <paramref name="picked"/> is null and the return classifies the failure for the scan loop.
        /// Faithful to <c>OpportunityDetour.CanHaul</c> (OpportunityDetour.cs:210-300), in the Core-contract order.
        /// </summary>
        private static CandidateOutcome TryCandidate(Pawn pawn, Thing thing, IntVec3 jobCell, float pawnToJob,
            in EnRoutePickupPolicy.MaxRanges ranges, HashSet<Thing> claimed, CompHauledToInventory comp,
            Dictionary<Thing, IntVec3> storeCellCache, Map map, HaulersDreamSettings s, out Job picked)
        {
            picked = null;
            if (thing == null || !thing.Spawned || thing.Map != map || thing == comp.parent)
                return CandidateOutcome.HardFail;
            // Corpses keep their own vanilla hauling flow (they don't belong in pockets) — same exclusion as the
            // bulk-haul pool / the work-spot sweep.
            if (thing is Corpse || thing.def == null || !thing.def.EverHaulable)
                return CandidateOutcome.HardFail;

            float pawnToThing = pawn.Position.DistanceTo(thing.Position);
            float thingToJob = thing.Position.DistanceTo(jobCell);

            // PHASE 1 — the cheap pawn/thing/job ratio gates, BEFORE the costly store search.
            var before = EnRoutePickupPolicy.CheckBeforeStore(pawnToThing, pawnToJob, thingToJob, in ranges);
            if (before == EnRoutePickupPolicy.EnRouteResult.RangeFail)
                return CandidateOutcome.RangeFail;
            if (before == EnRoutePickupPolicy.EnRouteResult.HardFail)
                return CandidateOutcome.HardFail;

            // CROSS-CLUSTER GUARDS, applied where they are cheapest (WYU does reservation/forbidden/
            // PawnCanAutomaticallyHaulFast after the pre-bound, before the store search — OpportunityDetour.cs:
            // 227-229). G2 first (pure set lookups), then the vanilla legality checks.
            // G2 anti-double-haul: another pawn already targets it, or THIS pawn's own current/queued job does.
            if (claimed.Contains(thing) || OwnJobTargets(pawn, thing))
                return CandidateOutcome.HardFail;
            if (map.reservationManager.FirstRespectedReserver(thing, pawn) != null)
                return CandidateOutcome.HardFail;
            if (thing.IsForbidden(pawn))
                return CandidateOutcome.HardFail;
            // Skip items the churn guard has backed off (issue #162: an unstackable with no storage gets scooped,
            // unloaded, dropped, re-scooped — an endless loop). The backoff window is brief and self-healing.
            if (HaulChurnGuard.IsBackedOff(thing))
                return CandidateOutcome.HardFail;
            // #5: leave items another mod has claimed via a designation (e.g. a Recycle This recycle/destroy order)
            // — scooping them into inventory en route hides them from that mod's spawned-only WorkGiver.
            if (ForeignOrderGuard.ClaimedByForeignOrder(thing))
                return CandidateOutcome.HardFail;
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, forced: false))
                return CandidateOutcome.HardFail;
            if (!ExtraSweepReach.Allows(pawn, thing))
                return CandidateOutcome.HardFail; // detour extra: cap reach at Some (no vacuum/fire detours)

            // STORE SEARCH — the cell CLOSEST to the thing↔job midway, ranked by MidwayDistanceSquared
            // (WYU StoreUtility.cs:246-266). Cached per scan (the pawn doesn't move within one scan, so a given
            // thing's best store cell is stable). G7: the search runs inside an Opportunistic filter context and
            // applies the shared StorageBuildingFilter per group/cell, so a denied building is excluded.
            if (!storeCellCache.TryGetValue(thing, out IntVec3 storeCell))
            {
                storeCell = FindMidwayStoreCell(pawn, thing, jobCell, map);
                storeCellCache[thing] = storeCell; // cache the miss too (Invalid) — same thing won't re-search
            }
            if (!storeCell.IsValid)
                return CandidateOutcome.HardFail; // nowhere (allowed) better to put it -> never qualifies

            float thingToStore = thing.Position.DistanceTo(storeCell);
            float storeToJob = storeCell.DistanceTo(jobCell);

            // PHASE 2 — the store→job range gates + the MaxNewLegs / MaxTotalTrip leg-ratio gates.
            var after = EnRoutePickupPolicy.CheckAfterStore(pawnToThing, pawnToJob, thingToStore, storeToJob, in ranges);
            if (after == EnRoutePickupPolicy.EnRouteResult.RangeFail)
                return CandidateOutcome.RangeFail;
            if (after == EnRoutePickupPolicy.EnRouteResult.HardFail)
                return CandidateOutcome.HardFail;

            // FINAL STAGE — the reachability/accuracy check selected by the path-checker setting (WYU
            // OpportunityDetour.cs:294-299).
            switch (s.enRoutePathChecker)
            {
                case EnRoutePathChecker.Vanilla:
                    // Bounded region-count flood (WYU WithinRegionCount, OpportunityDetour.cs:252-260 / :295).
                    // Cheap, never a full A* path. A failure HARD-fails the candidate (Vanilla => Success ? : HardFail).
                    if (!WithinRegionCount(pawn, thing, storeCell, jobCell, map))
                        return CandidateOutcome.HardFail;
                    break;
                case EnRoutePathChecker.Pathfinding:
                    // Accurate A* path costs re-fed through the SAME MaxNewLegs / MaxTotalTrip ratios (WYU
                    // WithinPathCost). A failure HARD-fails just this candidate (Pathfinding => Success ? : HardFail,
                    // OpportunityDetour.cs:296) — keep scanning the rest.
                    if (!WithinPathCost(pawn, thing, storeCell, jobCell, map))
                        return CandidateOutcome.HardFail;
                    break;
                case EnRoutePathChecker.Default:
                    // Same accurate A* check, but a FAILURE ENDS the whole scan (WYU Default => Success ? : FullStop,
                    // OpportunityDetour.cs:297): with the optimistic expanding-range ordering, the first candidate
                    // whose real path is too long signals the cheaper-first ordering has run out of plausible hauls.
                    if (!WithinPathCost(pawn, thing, storeCell, jobCell, map))
                        return CandidateOutcome.FullStop;
                    break;
            }

            // WINNER — emit the en-route pickup as a single-stack HaulersDream_BulkHaul (tagged via
            // CompHauledToInventory by the driver, serviced by the normal storage-aware unload). Clamp the
            // grabbed count to the overload carry ceiling (OverloadGate) so en-route never exceeds it; the
            // driver re-clamps to live mass/CE room at pickup.
            picked = BuildEnRouteJob(pawn, thing, jobCell, s);
            return picked != null ? CandidateOutcome.Skip : CandidateOutcome.HardFail;
        }

        /// <summary>
        /// Emit the en-route pickup job: a single-stack <see cref="HaulersDreamDefOf.HaulersDream_BulkHaul"/>
        /// loading only <paramref name="thing"/> into inventory, clamped to the overload carry ceiling. Same job
        /// shape as <see cref="BulkHaul.BuildPickUpJob"/> (targetQueueB = [thing], countQueue = [take],
        /// count = 1) so the bulk driver reads it identically and tags it via the comp. Returns null when not
        /// even one unit fits under the ceiling (the candidate then hard-fails and the scan continues).
        /// </summary>
        private static Job BuildEnRouteJob(Pawn pawn, Thing thing, IntVec3 jobCell, HaulersDreamSettings s)
        {
            // OVERLOAD CEILING — read the pawn's live capacity/mass through OverloadGate (the same gate the scoop
            // / bulk-haul / sweep use), so en-route can never load past the worth-it carry ceiling.
            int take = OverloadGate.CountToPickUp(pawn, thing, s);
            if (take <= 0)
                return null;

            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_BulkHaul, thing);
            job.targetQueueB = new List<LocalTargetInfo> { thing };
            job.countQueue = new List<int> { take };
            job.count = 1; // sentinel: Job.count defaults to -1 (reads as "broken" in several vanilla checks)
            MarkEnRoute(job, jobCell); // cosmetic: lets the report rewrite / dev overlay name the bound-for job cell
            HDLog.Dbg($"En-route: {pawn} grabs x{take} {thing.def?.label} on the way to its job.");
            return job;
        }

        // ---- cosmetic en-route marker (W6 report rewrite + dev detour overlay) ----------------------------

        // The bulk-haul jobs THIS feature emitted as en-route pickups, carrying the cell the pawn was ALREADY
        // heading to (so the inspector report can read "X (on the way to {DESTINATION})" and the dev overlay can
        // draw the carrier→pickup→job detour). A ConditionalWeakTable holds no strong ref (GC-safe) and only ever
        // has the live en-route job(s) in flight. COSMETIC ONLY — never read on a gameplay path, so byte-inert when
        // the report rewrite / overlay are off (nothing reads the marks).
        private static readonly ConditionalWeakTable<Job, EnRouteInfo> enRouteJobs =
            new ConditionalWeakTable<Job, EnRouteInfo>();

        /// <summary>Cosmetic side-data for an en-route pickup: the cell the pawn was already bound for (its job
        /// target), so the report / overlay can name/draw it. Reference-typed so the weak table needn't box an
        /// IntVec3 per pickup.</summary>
        internal sealed class EnRouteInfo
        {
            internal readonly IntVec3 jobCell;
            internal EnRouteInfo(IntVec3 jobCell) { this.jobCell = jobCell; }
        }

        private static void MarkEnRoute(Job job, IntVec3 jobCell)
        {
            if (job != null)
                enRouteJobs.GetValue(job, _ => new EnRouteInfo(jobCell));
        }

        /// <summary>True if <paramref name="job"/> is an en-route pickup this feature emitted (cosmetic gate).</summary>
        internal static bool IsEnRoute(Job job)
            => job != null && enRouteJobs.TryGetValue(job, out _);

        /// <summary>The cosmetic side-data for <paramref name="job"/> if it is an en-route pickup, else null.</summary>
        internal static EnRouteInfo EnRouteData(Job job)
            => job != null && enRouteJobs.TryGetValue(job, out var info) ? info : null;

        // ---- G2 helpers -----------------------------------------------------------------------------------

        /// <summary>True if the pawn is already running (or has queued) an HD inventory-pickup job — bulk haul or
        /// self-pickup — so en-route must not stack another pickup on top of an in-flight sweep (G2 self-guard).</summary>
        private static bool AlreadyHaulingIntoInventory(Pawn pawn)
        {
            // Membership = HdJobDefSets.NoRecursionHaulJobs (the two pure inventory-INTAKE drivers), the single
            // source of truth so a newly-added intake driver is recognized here without re-listing it inline.
            var jd = pawn.CurJobDef;
            if (jd != null && HdJobDefSets.NoRecursionHaulJobs.Contains(jd))
                return true;
            var q = pawn.jobs?.jobQueue;
            if (q != null)
                for (int k = 0; k < q.Count; k++)
                {
                    var d = q[k]?.job?.def;
                    if (d != null && HdJobDefSets.NoRecursionHaulJobs.Contains(d))
                        return true;
                }
            return false;
        }

        /// <summary>The SELF half of G2 that <see cref="RouteSelection.ClaimedByOtherPawns"/> omits (it skips
        /// self): whether THIS pawn's own current or queued job already targets <paramref name="thing"/> (any
        /// of targetA/B/C or the two target queues). Allocation-free, bounded by the pawn's own queue depth.</summary>
        private static bool OwnJobTargets(Pawn pawn, Thing thing)
        {
            if (JobTargetsThing(pawn.CurJob, thing))
                return true;
            var q = pawn.jobs?.jobQueue;
            if (q != null)
                for (int k = 0; k < q.Count; k++)
                    if (JobTargetsThing(q[k]?.job, thing))
                        return true;
            return false;
        }

        private static bool JobTargetsThing(Job j, Thing thing)
        {
            if (j == null)
                return false;
            if (j.targetA.Thing == thing || j.targetB.Thing == thing || j.targetC.Thing == thing)
                return true;
            if (QueueHas(j.targetQueueA, thing) || QueueHas(j.targetQueueB, thing))
                return true;
            return false;
        }

        private static bool QueueHas(List<LocalTargetInfo> q, Thing thing)
        {
            if (q == null)
                return false;
            for (int k = 0; k < q.Count; k++)
                if (q[k].Thing == thing)
                    return true;
            return false;
        }

        // ---- G3 mutex bridge ------------------------------------------------------------------------------

        /// <summary>
        /// G3 — stand en-route down when the pawn is at the opportunistic-unload divert point. Gathers the SAME
        /// live numbers <see cref="OpportunisticUnload.ShouldDivert"/> reads (scooped load fraction via
        /// <see cref="TrackedMassCache"/>, a storable representative store cell, the divert cooldown) and feeds
        /// them to the pure <see cref="EnRouteMutexPolicy.MustStandDown"/>. When the opportunistic-unload feature
        /// is OFF the policy is inert (the two can never both fire), so this returns false and en-route proceeds.
        /// </summary>
        private static bool MustStandDownForUnload(Pawn pawn, CompHauledToInventory comp, HaulersDreamSettings s)
        {
            bool unloadEnabled = s.opportunisticUnload && s.markForUnload;
            // Cheap exits the pure policy would reach anyway: feature off -> never stand down (skip the storage
            // search entirely). This is exactly EnRouteMutexPolicy's opportunisticUnloadEnabled==false branch,
            // hoisted so we don't pay the load-fraction / storage-cell reads when unload is off.
            if (!unloadEnabled)
                return false;

            // Capacity via the per-(pawn,tick) memo (a pure non-mutating read — the en-route postfix never
            // mutates inventory before this), the same read the unload divert path shares this tick.
            float cap = PawnMassCache.Capacity(pawn);
            if (cap <= 0f)
                return false; // no capacity -> load fraction meaningless -> en-route may proceed (sub-threshold)
            float trackedMass = TrackedMassCache.TrackedMass(pawn, comp);
            float loadFraction = trackedMass / cap;

            int now = Find.TickManager?.TicksGame ?? 0;
            bool cooldownElapsed = now - comp.lastOpportunisticUnloadTick >= OpportunisticUnload.DivertCooldownTicks;

            // A storable representative cell — the unload divert's "somewhere to drop it" precondition. Only
            // searched when the cheap gates (cooldown elapsed + load at/over the minimum) could plausibly stand
            // en-route down, so a sub-threshold / cooling-down pawn never pays the spatial search.
            if (!cooldownElapsed || loadFraction < OpportunisticUnloadPolicy.MinLoadFraction)
                return false;
            bool hasStorableCell = HasStorableTrackedCell(pawn, comp);

            return EnRouteMutexPolicy.MustStandDown(unloadEnabled, loadFraction, hasStorableCell, cooldownElapsed);
        }

        /// <summary>Whether any tracked (scooped) stack has a valid store cell — the unload divert's "somewhere
        /// to drop it" check, picking a STORABLE representative exactly like
        /// <see cref="OpportunisticUnload.ShouldDivert"/> (an un-storable rock chunk must not suppress it).</summary>
        private static bool HasStorableTrackedCell(Pawn pawn, CompHauledToInventory comp)
        {
            foreach (var t in comp.PeekHashSet())
            {
                if (t == null || t.Destroyed)
                    continue;
                StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, pawn.Map,
                    StoragePriority.Unstored, pawn.Faction, out IntVec3 cell, needAccurateResult: false);
                if (cell.IsValid)
                    return true;
            }
            return false;
        }

        // ---- job target resolution ------------------------------------------------------------------------

        /// <summary>Where the pawn is heading. A DoBill walks first to an ingredient (WYU
        /// OpportunityDetour.cs:100), so use the first queued ingredient's target; otherwise the job's
        /// targetA, falling back to the first queued targetA cell for jobs that use the queue (grow-zone
        /// harvests etc.).</summary>
        private static LocalTargetInfo ResolveJobTarget(Job job)
        {
            if (job.def == JobDefOf.DoBill && job.targetQueueB != null)
                for (int k = 0; k < job.targetQueueB.Count; k++)
                    if (job.targetQueueB[k].Cell.IsValid)
                        return job.targetQueueB[k];
            if (job.targetA.Cell.IsValid)
                return job.targetA;
            var qa = job.targetQueueA;
            if (qa != null)
                for (int k = 0; k < qa.Count; k++)
                    if (qa[k].Cell.IsValid)
                        return qa[k];
            return job.targetA;
        }

        // ---- midway store search (WYU TryFindBestBetterStoreCellFor_MidwayToTarget, StoreUtility.cs:207-273) --

        /// <summary>
        /// Find the best store cell for <paramref name="thing"/>, choosing the candidate CLOSEST to the
        /// thing↔job MIDWAY (so the carry leg stays "on the way" — WYU's whole midway-store idea). Faithful port
        /// of WYU's <c>TryFindBestBetterStoreCellFor_MidwayToTarget</c>: walk slot groups in priority order,
        /// strictly above the thing's current priority, and within each group keep the cell nearest the midway
        /// (ranked by the pure <see cref="EnRoutePickupPolicy.MidwayDistanceSquared"/> /
        /// <see cref="EnRoutePickupPolicy.MidwayBetter"/>). G7: runs inside an Opportunistic filter context and
        /// excludes any group/cell the shared <see cref="StorageBuildingFilter"/> denies (byte-inert when the
        /// filter feature is off — every query short-circuits to allow-all).
        /// </summary>
        private static IntVec3 FindMidwayStoreCell(Pawn pawn, Thing thing, IntVec3 jobCell, Map map)
        {
            var currentPriority = StoreUtility.CurrentStoragePriorityOf(thing);
            EnRoutePickupPolicy.Midway(thing.Position.x, thing.Position.y, thing.Position.z,
                jobCell.x, jobCell.y, jobCell.z, out int midX, out int _, out int midZ);

            // G7: declare the OPPORTUNISTIC purpose so the shared filter applies its opportunistic curated/deny
            // set; the BulkHaul.ScanGroup-style per-group/cell filter checks below honor it directly
            // (we enumerate groups ourselves, so the StoreUtility funnel postfix doesn't intercept this path).
            using (StorageBuildingFilter.PushContext(StorageFilterContext.Opportunistic))
            {
                bool filterActive = StorageBuildingFilter.Enabled
                    && StorageBuildingFilter.CurrentContext != StorageFilterContext.Unload;
                var filter = filterActive ? HaulersDreamMod.Settings?.storageBuildingFilter : null;

                IntVec3 closestSlot = IntVec3.Invalid;
                int closestDistSq = int.MaxValue;
                var groups = map.haulDestinationManager.AllGroupsListInPriorityOrder;
                for (int g = 0; g < groups.Count; g++)
                {
                    var slotGroup = groups[g];
                    // Skip a modded/torn SlotGroup with no settings rather than NRE on Settings below (issue #58
                    // robustness; mirrors the HaulToStack store-cell loop and its chosen-group guard). Such a
                    // group is not a valid storage destination anyway. (Settings == parent.GetStoreSettings(),
                    // so a non-null Settings also guarantees a non-null parent for slotGroup.parent below.)
                    if (slotGroup?.Settings == null)
                        continue;
                    var pr = slotGroup.Settings.Priority;
                    // Strictly better than the thing's current storage (opportunistic hauls upgrade, never lateral
                    // — WYU StoreUtility.cs:218-220 with no beforeCarry target). Unstored is never a destination.
                    if (pr <= currentPriority || pr == StoragePriority.Unstored)
                        continue;
                    if (!slotGroup.parent.Accepts(thing))
                        continue;
                    // G7 building filter: skip a whole group whose building the player denied for opportunistic.
                    if (filter != null && !filter.IsGroupAllowed(slotGroup))
                        continue;

                    var cells = slotGroup.CellsList;
                    if (cells == null)
                        continue;
                    for (int c = 0; c < cells.Count; c++)
                    {
                        var cell = cells[c];
                        int distSq = EnRoutePickupPolicy.MidwayDistanceSquared(cell.x, cell.z, midX, midZ);
                        if (!EnRoutePickupPolicy.MidwayBetter(distSq, closestDistSq))
                            continue;
                        if (!StoreUtility.IsGoodStoreCell(cell, map, thing, pawn, pawn.Faction))
                            continue;
                        // A linked StorageGroup can pool cells from MULTIPLE buildings, so a denied building's
                        // cell must be dropped individually even when its group was allowed (mirrors
                        // BulkHaul.ScanGroup). Only runs when the filter is active.
                        if (filter != null && !filter.IsCellAllowed(cell, map))
                            continue;
                        closestSlot = cell;
                        closestDistSq = distSq;
                    }
                }
                return closestSlot;
            }
        }

        // ---- path / region reachability stages ------------------------------------------------------------

        /// <summary>Vanilla path-checker: the bounded region-count flood (WYU WithinRegionCount,
        /// OpportunityDetour.cs:252-260) — pawn→thing within the start-region cap AND store→job within the
        /// store-region cap. Cheap; never a full A* path.</summary>
        private static bool WithinRegionCount(Pawn pawn, Thing thing, IntVec3 storeCell, IntVec3 jobCell, Map map)
        {
            var parms = TraverseParms.For(pawn);
            if (!pawn.Position.WithinRegions(thing.Position, map,
                    EnRoutePickupPolicy.DefaultMaxStartToThingRegionLookCount, parms))
                return false;
            if (!storeCell.WithinRegions(jobCell, map,
                    EnRoutePickupPolicy.DefaultMaxStoreToJobRegionLookCount, parms))
                return false;
            return true;
        }

        /// <summary>
        /// Accurate path-checker (Default / Pathfinding): pathfind the four legs and re-feed the real A* costs
        /// through the pure <see cref="EnRoutePickupPolicy.WithinPathLegBounds"/> (the same MaxNewLegs /
        /// MaxTotalTrip ratios — WYU WithinPathCost, OpportunityDetour.cs:262-292). A leg that can't be reached
        /// (cost 0 / not found) fails. Uses the MC1.6 pathfinder
        /// (<c>map.pathFinder.FindPathNow(start, target, pawn, tuning, peMode)</c> -> <see cref="PawnPath"/> with
        /// <c>.TotalCost</c>/<c>.Found</c>; ALWAYS released to the pool). Inherits Perfect Pathfinding's accuracy
        /// automatically when present (same entry point — see <see cref="PerfectPathfindingCompat"/>).
        /// </summary>
        private static bool WithinPathCost(Pawn pawn, Thing thing, IntVec3 storeCell, IntVec3 jobCell, Map map)
        {
            float pawnToThingCost = PathCost(map, pawn, pawn.Position, thing, PathEndMode.ClosestTouch);
            if (pawnToThingCost <= 0f)
                return false;
            float storeToJobCost = PathCost(map, pawn, storeCell, jobCell, PathEndMode.Touch);
            if (storeToJobCost <= 0f)
                return false;
            float pawnToJobCost = PathCost(map, pawn, pawn.Position, jobCell, PathEndMode.Touch);
            if (pawnToJobCost <= 0f)
                return false;
            // The new-legs bound only needs the three legs above (WYU OpportunityDetour.cs:282-283); the
            // thing→store leg is only needed for the total-trip bound, so compute it only if the new-legs bound
            // passes (matches WYU's short-circuit order at OpportunityDetour.cs:282-289).
            if (pawnToThingCost + storeToJobCost > pawnToJobCost * EnRoutePickupPolicy.DefaultMaxNewLegsPctOrigTrip)
                return false;
            float thingToStoreCost = PathCost(map, pawn, thing.Position, storeCell, PathEndMode.ClosestTouch);
            if (thingToStoreCost <= 0f)
                return false;
            return EnRoutePickupPolicy.WithinPathLegBounds(
                pawnToThingCost, thingToStoreCost, storeToJobCost, pawnToJobCost);
        }

        /// <summary>One A* leg cost via the MC1.6 pathfinder, pooling the path. Returns 0 when no path exists
        /// (WYU treats a 0/NotFound cost as "leg unreachable" -> reject). <paramref name="destThing"/> overload
        /// targets the thing (vanilla pathfinds to a LocalTargetInfo of the thing for ClosestTouch).</summary>
        private static float PathCost(Map map, Pawn pawn, IntVec3 start, Thing destThing, PathEndMode peMode)
        {
            PawnPath path = map.pathFinder.FindPathNow(start, destThing, pawn, null, peMode);
            try
            {
                return path != null && path.Found ? path.TotalCost : 0f;
            }
            finally
            {
                path?.ReleaseToPool();
            }
        }

        /// <summary>One A* leg cost to a CELL target (WYU always pathfinds to an IntVec3 LocalTargetInfo —
        /// StoreUtility.cs note at OpportunityDetour.cs:265-266; passing a bare thing for an edifice cell throws,
        /// so cell legs use this overload). Pools the path; 0 == unreachable.</summary>
        private static float PathCost(Map map, Pawn pawn, IntVec3 start, IntVec3 destCell, PathEndMode peMode)
        {
            PawnPath path = map.pathFinder.FindPathNow(start, destCell, pawn, null, peMode);
            try
            {
                return path != null && path.Found ? path.TotalCost : 0f;
            }
            finally
            {
                path?.ReleaseToPool();
            }
        }
    }
}
