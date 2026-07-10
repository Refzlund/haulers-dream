using HaulersDream.Core;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Pass-by" unloading: when a pawn carrying scooped goods is about to set off on a real journey for a
    /// work job and its storage is roughly on the way, it drops the load off now instead of carrying it
    /// across the map and making a dedicated trip later. The decision math is the pure
    /// <see cref="OpportunisticUnloadPolicy"/>; this gathers the live numbers.
    /// </summary>
    internal static class OpportunisticUnload
    {
        // Short cooldown so a (rare) unload that doesn't clear the load can't cause a tight divert loop.
        // Shared with the caravan pack-animal offload (PackAnimalLoad.TryGetOpportunisticLoadJob), which stamps
        // the same lastOpportunisticUnloadTick via NotifyDiverted.
        internal const int DivertCooldownTicks = 250;

        // Reused snapshot of the tracked set for the storage-cell representative pick in ShouldDivert /
        // ShouldUnloadBeforeRelocation: the loop walks it in thingIDNumber order and breaks on the first storable
        // stack, so the chosen representative cell is identical across MP clients (a raw HashSet enumeration could
        // pick a different first-storable per client → a divergent should-unload boolean → a divergent job).
        // [ThreadStatic] + lazy-init matches the repo's hook-reachable-scratch convention; both consumers run on the
        // main think loop, Clear at use, never trusted empty, and each fully iterates the snapshot before returning
        // (no re-entrant snapshot), so sharing one buffer across the two is sound.
        [System.ThreadStatic] private static System.Collections.Generic.List<Thing> scratchRep;

        // "Put it away before relaxing": bypass the accumulate window so a pawn drops its load before downtime
        // (rest / recreation / eating) instead of carrying it to bed / the dinner table / the rec room (the
        // player's natural expectation). The window still applies while the pawn is working or idle between
        // work, so a continuous/intermittent yield run still loads up — these only matter once the pawn stops
        // working and heads into the matching downtime. There are two checks because the trigger context differs:

        /// <summary>STATE check: the pawn is CURRENTLY in a downtime job (eating / sleeping / recreation) whose
        /// toggle is on. Used by the interval / idle backstop, which may run while the pawn is still WORKING —
        /// a merely tired-but-still-mining pawn must NOT count (it's working, not relaxing), so we look at the
        /// actual current job, never the need level. Catches a pawn that fell asleep / sat down to eat with a
        /// load before the end-of-run trigger could fire (it then unloads on wake / after the meal).</summary>
        internal static bool IsInDowntimeJob(Pawn pawn, HaulersDreamSettings s)
        {
            if (pawn == null || s == null)
                return false;
            if (s.unloadBeforeEating && pawn.CurJobDef == JobDefOf.Ingest)
                return true;
            if (s.unloadBeforeSleep && pawn.jobs?.curDriver is JobDriver_LayDown)
                return true;
            if (s.unloadBeforeLeisure && pawn.CurJobDef?.joyKind != null)
                return true;
            return false;
        }

        /// <summary>ENTERING check: the pawn just ran out of WORK and its needs/schedule say it's about to rest /
        /// recreate / eat (toggle on). Used ONLY by the end-of-run trigger, which fires while the pawn is between
        /// jobs with no work available — so a low need genuinely means "about to relax," not "interrupt active
        /// work." Also true if it has already entered a downtime job. This is what lets the pawn unload BEFORE it
        /// lies down / sits at the table, rather than after.</summary>
        internal static bool IsEnteringDowntime(Pawn pawn, HaulersDreamSettings s)
        {
            if (pawn == null || s == null)
                return false;
            var needs = pawn.needs;
            if (s.unloadBeforeEating && needs?.food != null && needs.food.CurCategory >= HungerCategory.Hungry)
                return true;
            if (s.unloadBeforeSleep && needs?.rest != null && needs.rest.CurCategory >= RestCategory.Tired)
                return true;
            if (s.unloadBeforeLeisure && pawn.timetable != null && pawn.timetable.CurrentAssignment == TimeAssignmentDefOf.Joy)
                return true;
            return IsInDowntimeJob(pawn, s);
        }

        /// <summary>
        /// Returns an unload job to run BEFORE the pawn enters a downtime activity, or null. This is what
        /// actually delivers "put it away before relaxing": RimWorld's think tree evaluates the rest / food /
        /// joy job-givers ABOVE work, so a tired/hungry pawn heads into downtime without the work node (and
        /// thus the end-of-run trigger) ever being reached. The need-giver postfixes call this and swap their
        /// rest/eat/joy job for the returned unload; once the pawn unloads (and is empty), the need-giver
        /// re-fires next determination and the pawn rests/eats/relaxes normally. <paramref name="enabled"/> is
        /// the per-activity toggle. Gated exactly like the other automatic unloads (eligible, home, awake, not
        /// drafted/downed, something above keep-stock to unload, storage reservable) plus the divert cooldown,
        /// so a failed or futile trip can't loop — and the caller applies a severity gate (a critically tired /
        /// starving pawn skips the detour and sleeps/eats now).
        /// </summary>
        internal static Job TryGetPreDowntimeUnloadJob(Pawn pawn, bool enabled)
        {
            var s = HaulersDreamMod.Settings;
            if (!enabled || s == null || !s.markForUnload)
                return null;
            if (pawn?.Map == null || pawn.jobs == null || pawn.Drafted || pawn.Downed || !pawn.Spawned)
                return null;
            if (pawn.Faction != Faction.OfPlayerSilentFail || !YieldRouter.IsEligible(pawn))
                return null;
            // Don't yank a pawn out of a lord-driven activity (party / ritual / gathering — it carries a duty),
            // out of a bed it's resting in (medical / already lying down — the GetJoyInBed path inherits this
            // base TryGiveJob), or out of caravan formation on the home map. The interval / idle safety net
            // still unloads such a pawn AFTER its current activity, without interrupting it.
            if (ProtectedWork.IsRestingPatient(pawn) || pawn.IsFormingCaravan() || pawn.mindState?.duty != null)
                return null;
            if (pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_UnloadInventory || PawnUnloadChecker.HasQueuedUnload(pawn))
                return null;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return null;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - comp.lastOpportunisticUnloadTick < DivertCooldownTicks)
                return null; // a recent (possibly failed) divert — let the pawn rest/eat this time; retry after the cooldown
            // Adopt foreign surplus too (matches the other unload paths), then require something actually unloadable.
            // Global toggle adopts all surplus; otherwise only defs with an explicit surplus-producing per-item rule.
            bool adoptAll = s.unloadAllSurplus;
            if (!pawn.IsFormingCaravan() && (adoptAll || s.HasAnySurplusProducingRule))
                PawnUnloadChecker.AdoptSurplusInventory(pawn, comp, adoptAll);
            // Caravan / away map with no player storage — put the load onto a pack animal before resting/eating/
            // relaxing at the campsite, the same way the home pawn puts it in storage. Same downtime trigger
            // and per-activity toggle (markForUnload + the `enabled` gate above); the caravan toggle + carrier
            // availability gate live inside TryGetOpportunisticLoadJob (which also makes its own reservations /
            // NotifyDiverted). When no usable pack animal is reachable it returns null -> loot rides home. Any
            // non-home map WITH player storage (a VF RV interior) falls through to the storage-unload job
            // below instead (ShouldUnloadToStorage true), so the pawn sheds into its shelves before downtime.
            if (!MapGate.ShouldUnloadToStorage(pawn.Map))
                return PackAnimalLoad.TryGetOpportunisticLoadJob(pawn);
            if (!PawnUnloadChecker.AnyUnloadable(pawn, comp.GetHashSet()))
                return null;
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadInventory);
            if (!job.TryMakePreToilReservations(pawn, false))
                return null;
            NotifyDiverted(pawn);
            HDLog.Dbg($"{pawn} unloading before downtime ({comp.GetHashSet().Count} tracked).");
            return job;
        }

        /// <summary>
        /// True when picking <paramref name="def"/> means the pawn is STILL in an active accumulate run, so it
        /// must NOT be diverted to unload (F38): yield-producing work (mine / deconstruct / plant harvest+cut /
        /// deep-drill / gather animal resources / strip), OR a storage-bound haul (which already delivers the
        /// pack to storage — bulk-haul sweeps it along). Every OTHER work job means the yield run is OVER, and
        /// the pawn should shed its load before the unrelated work. Classified by the job's driverClass so it
        /// mirrors YieldRouter's producer set exactly and composes with modded jobs subclassing those drivers.
        /// </summary>
        internal static bool IsYieldOrHaulJobDef(JobDef def)
        {
            if (def == null || def.driverClass == null)
                return false;
            // A JobDef's driverClass is immutable once defs are loaded, and the 9 IsAssignableFrom walks below are
            // pure type-hierarchy checks — so the (def -> bool) answer is fixed for the whole session. Memoize it
            // in a static dictionary so this per-pawn-scan call (every work-found scan) becomes one dictionary
            // lookup instead of 9 reflection walks. Keyed on the JobDef (cheap reference key); the set of defs is
            // small and bounded, so the cache never grows unbounded.
            if (yieldOrHaulCache.TryGetValue(def, out var cached))
                return cached;
            bool result = ComputeIsYieldOrHaulJobDef(def.driverClass);
            yieldOrHaulCache[def] = result;
            return result;
        }

        // Per-JobDef memo of IsYieldOrHaulJobDef (driverClass assignability is immutable per def -> safe forever).
        // Reached from the per-pawn work scan, which a threading mod (e.g. RimThreaded) may fan onto worker
        // threads, so use a ConcurrentDictionary: lock-free reads, and a racing double-compute is harmless
        // because the value is a pure function of the immutable driverClass. Single-threaded behaviour is
        // identical to the prior plain Dictionary.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<JobDef, bool> yieldOrHaulCache =
            new System.Collections.Concurrent.ConcurrentDictionary<JobDef, bool>();

        private static bool ComputeIsYieldOrHaulJobDef(System.Type dc)
        {
            return typeof(JobDriver_PlantWork).IsAssignableFrom(dc)
                || typeof(JobDriver_Mine).IsAssignableFrom(dc)
                || typeof(JobDriver_OperateDeepDrill).IsAssignableFrom(dc)
                || typeof(JobDriver_GatherAnimalBodyResources).IsAssignableFrom(dc)
                || typeof(JobDriver_Strip).IsAssignableFrom(dc)
                || typeof(JobDriver_Deconstruct).IsAssignableFrom(dc)
                || typeof(JobDriver_HaulToCell).IsAssignableFrom(dc)
                || typeof(JobDriver_HaulToContainer).IsAssignableFrom(dc)
                || typeof(JobDriver_BulkHaul).IsAssignableFrom(dc);
        }

        /// <param name="runOver">The pawn just picked a NON-yield, NON-haul job, so its accumulate run is over —
        /// use the relaxed run-end criteria (drop a worthwhile load at nearby storage even on a short hop)
        /// instead of the strict "real journey on the way" bar. Settle-window-independent by design.</param>
        internal static bool ShouldDivert(Pawn pawn, Job workJob, bool runOver = false, bool protectedZeroDetourOnly = false)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.markForUnload)
                return false;
            // The general on-the-way unload (protectedZeroDetourOnly == false) is gated by the opportunisticUnload
            // toggle. The protected-work pass-by (issue #107) is its OWN feature, controlled solely by its unloadDetour
            // setting (Off disables it, checked in that branch below), so it does NOT require opportunisticUnload: a
            // doctor can shed a load during elective surgery even with the general on-the-way unload turned off.
            if (!protectedZeroDetourOnly && !s.opportunisticUnload)
                return false;
            if (pawn?.Map == null || workJob?.def == null || pawn.Drafted || workJob.playerForced)
                return false; // never defer player-prioritized work
            // Never divert off a resting patient (see ProtectedWork). Defensive: a patient shouldn't be handed a
            // work job, but its 211-tick job-override re-scan can.
            if (ProtectedWork.IsRestingPatient(pawn))
                return false;
            // Protected work (medical / rescue / firefighting) normally never diverts to unload (issue #107). The
            // ONE exception is the protected zero-detour path: the caller (the work postfix) has vetted this as
            // NON-emergency protected work and allows a shed ONLY when storage is essentially on the way (the
            // zero-detour geometry gate below), so it never DELAYS the work. Every other caller still hard-blocks
            // (this method also covers the non-emergency Doctor/Warden givers, notably rescue, by workgiver).
            if (!protectedZeroDetourOnly && ProtectedWork.IsProtected(workJob, false))
                return false;
            if (workJob.def == HaulersDreamDefOf.HaulersDream_UnloadInventory
                || pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_UnloadInventory)
                return false;
            // Never divert a pawn that is mid bill-prep-gather — its tagged load IS the ingredients it's about to
            // craft with; dropping them at storage would waste the sweep. (Diverting BEFORE a fresh prep starts,
            // to shed an unrelated old load, stays allowed: that's workJob == the prep, not CurJobDef.)
            if (pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_BillPrepGather)
                return false;
            // Never divert before one of HD's OWN jobs: bulk-haul / unload / pack-load already deliver the
            // load to storage themselves, and the construct-delivery / bill-prep / batch-craft jobs USE the
            // carried materials — unloading first would be a redundant trip or would rob the job of its
            // ingredients (it would break inventory construct/bill delivery). Identified by the driver living
            // in this assembly, so it covers every HD job without enumerating them.
            if (workJob.def.driverClass != null && workJob.def.driverClass.Assembly == typeof(OpportunisticUnload).Assembly)
                return false;

            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return false;
            var tracked = comp.GetHashSet();
            if (tracked.Count == 0)
                return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - comp.lastOpportunisticUnloadTick < DivertCooldownTicks)
                return false;
            // Same class of gap #152 fixed on the end-of-run trigger: a tracked stack existing isn't enough,
            // it must have SURPLUS above the pawn's keep-stock, or the unload this divert builds finds nothing
            // to move (FirstUnloadableThing skips every keep-stock stack), ends instantly, and, before the
            // per-job-transition throttle above, this trigger re-ran its expensive search on every subsequent
            // target. Routed through the SAME PawnUnloadChecker.AnyUnloadable every other unload trigger uses.
            if (!PawnUnloadChecker.AnyUnloadable(pawn, tracked))
                return false;

            // SETTLE gate for the run-OVER path (mirrors TryGetEndOfRunUnloadJob's settle): the pawn picking ONE
            // non-yield job does NOT mean its yield run is over. In a busy colony the work scan constantly hands a
            // just-scooped miner/harvester a nearby cleaning/other job for a tick, and the relaxed run-end criteria
            // (no minimum-trip floor) then diverted it home to unload after a SINGLE mined/harvested cell — the
            // reported "mine 1 block, run all the way back" / "run back to clean 1 thing", far short of a full pack.
            // While the pawn is still actively scooping (lastYieldTick within unloadGraceTicks) it is MID-RUN: keep
            // its load and let it accumulate toward the smart-overload ceiling (the at-ceiling trigger handles a
            // genuinely-full pawn; the interval/idle backstop catches an idle one). Treat the run as over for the
            // relaxed criteria ONLY once it has settled — so a brief non-yield detour no longer ends the run. When
            // entering downtime (rest/eat/recreate) the settle is bypassed (put the load away first), matching the
            // end-of-run path. The strict continuing-yield path (runOver=false, ShouldUnloadOnWay's trip floor) is
            // unchanged — a continuing mine/harvest run was already never interrupted between adjacent cells.
            // (Bypassed on the protected zero-detour path: the settle window is about not ending a YIELD run early,
            // which is irrelevant to a doctor on protected work, where a free pass-by drop is worth taking.)
            if (!protectedZeroDetourOnly && runOver && !IsEnteringDowntime(pawn, s)
                && now - comp.lastYieldTick < s.unloadGraceTicks)
                return false;

            // Where the pawn is heading. Most work jobs set targetA; grow-zone harvests use targetQueueA
            // (targetA is Invalid), so fall back to the first queued cell.
            IntVec3 target = workJob.targetA.Cell;
            // Protected zero-detour: measure the pass-by against the pawn's FIRST real destination. An
            // ingredient-gathering job (a surgery / craft DoBill) walks OUT to fetch its first ingredient (the
            // medicine / materials) BEFORE returning to the bill-giver it is already standing next to, so that
            // outbound leg is the one where storage may sit on the way (the reported "grabbing that medicine" trip).
            // Only for a spawned map ingredient (an inventory-sourced one has no outbound leg).
            if (protectedZeroDetourOnly)
            {
                var qB = workJob.targetQueueB;
                if (qB != null && qB.Count > 0 && qB[0].HasThing && qB[0].Thing.Spawned)
                    target = qB[0].Thing.Position;
            }
            if (!target.IsValid)
            {
                var queue = workJob.targetQueueA;
                if (queue != null && queue.Count > 0)
                    target = queue[0].Cell;
            }
            if (!target.IsValid)
                return false;

            // The total scooped mass. Read it from the per-(pawn,tick) memo (TrackedMassCache) so repeated
            // same-tick scans (the work-found divert check + the end-of-run path on the same pawn) share one
            // GetStatValue(Mass) walk over the tracked set instead of re-walking per scan.
            float cap = CarryCapacity.Of(pawn);
            if (cap <= 0f)
                return false;
            float trackedMass = TrackedMassCache.TrackedMass(pawn, comp);
            float loadFraction = trackedMass / cap;

            // CHEAP pre-gate BEFORE the expensive storage search: if the load fraction (or capacity / tracked
            // count / cooldown — already enforced above, re-asserted here for completeness) can't possibly clear
            // the full ShouldUnloadOnWay/OnRunEnd bar, skip TryFindBestBetterStoreCellFor entirely. This only
            // short-circuits a divert the full math would reject anyway (both full paths bail below
            // MinLoadFraction), so behavior is identical — the storage search just no longer runs for the common
            // "carrying too little to bother" case.
            // (Skipped on the protected zero-detour path: a free pass-by is worth taking at ANY load size, so its
            // decision is the pure geometry below, not the load-fraction bar this pre-gate enforces for the normal
            // "worth a detour" path.)
            if (!protectedZeroDetourOnly && !OpportunisticUnloadPolicy.ShouldAttemptDivert(
                    loadFraction, now - comp.lastOpportunisticUnloadTick >= DivertCooldownTicks,
                    tracked.Count, cap > 0f))
                return false;

            // Claim the cooldown the MOMENT the expensive search below is about to run, whether it ends up
            // finding a divert or not (issue #160-class hitch). A pawn on a continuing yield run (harvesting a
            // zone, mining/deconstructing through many adjacent targets) re-enters this check on EVERY job
            // transition, once per target; once its load crosses MinLoadFraction, the pre-gate above passes on
            // every single one of those transitions, so without this stamp TryFindBestBetterStoreCellFor
            // (below) reran on every plant/vein/wall for the whole run, with no backoff, for every pawn doing it
            // at once: a real per-tick cost, not just a logic hiccup, and the reported "colonists standing"
            // stutter scales with both the target count and the number of simultaneous harvesters. Stamping
            // here (not only on an ACCEPTED divert, which is all NotifyDiverted covered before) bounds the
            // search to once per DivertCooldownTicks per pawn regardless of the outcome, matching this field's
            // own documented purpose ("prevents a divert loop"), just extended to a REJECTED attempt too.
            comp.lastOpportunisticUnloadTick = now;

            // The storage we'd unload to. Pick a STORABLE tracked item as the storage-cell representative: keying
            // off an arbitrary first item meant an un-storable one (e.g. a rock chunk, which no default stockpile
            // accepts) suppressed the WHOLE pass-by divert even when the other carried goods could be dropped en
            // route. Deferred until after the load-fraction pre-gate above (the spatial search is the costliest
            // step here).
            IntVec3 storageCell = IntVec3.Invalid;
            // MP determinism: process tagged stacks in thingIDNumber order so a capacity-bound loop deposits/drops the same subset on every client.
            // (Here: pick the storage-cell representative deterministically — break on the first storable stack in ID
            // order so every client resolves the SAME storageCell and the same should-unload decision.)
            var rep = scratchRep ?? (scratchRep = new System.Collections.Generic.List<Thing>());
            rep.Clear();
            rep.AddRange(tracked);
            rep.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            for (int ri = 0; ri < rep.Count; ri++)
            {
                var t = rep[ri];
                if (t == null || t.Destroyed)
                    continue;
                StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, pawn.Map,
                    StoragePriority.Unstored, pawn.Faction, out storageCell, needAccurateResult: false);
                if (storageCell.IsValid)
                    break;
            }
            // Nothing storable to divert toward -> let the trip proceed un-diverted; the end-of-run and
            // meal/recreation checkpoint triggers (both storage-independent) still make the unload trip,
            // and the unload driver itself desperately-stores the un-storable items.
            if (!storageCell.IsValid)
                return false;

            int pawnToTarget = CellDist(pawn.Position, target);
            int pawnToStorage = CellDist(pawn.Position, storageCell);
            int storageToTarget = CellDist(storageCell, target);

            // Protected-work unload detour: the pawn is on non-emergency protected work (elective surgery / rescue /
            // warden), so shed the scooped load only when storage is within the player's UNLOAD detour budget
            // (unloadDetour, separate from the grab-on-the-way pickup budget). Issue #107: at the Short default this
            // is about a 4-tile pass-by, not a real delay; Off disables it entirely so the load is carried through
            // the whole task; Standard/Long give a doctor progressively more room to drop off, trading a small
            // deliberate surgery delay for fewer trips. No trip/load floor: within budget, take it.
            if (protectedZeroDetourOnly)
            {
                var detour = HaulersDreamMod.Settings?.unloadDetour ?? OpportunisticDetour.Short;
                if (detour == OpportunisticDetour.Off)
                    return false;
                return OpportunisticUnloadPolicy.ShouldUnloadZeroDetour(pawnToTarget, pawnToStorage, storageToTarget,
                    OpportunisticUnloadPolicy.DetourBudgetTiles(detour));
            }
            // Run-end (switched to non-yield work): relaxed criteria — shed the load at nearby storage even on
            // a short hop. Otherwise (continuing a yield run / a haul): the strict "real journey on the way" bar.
            return runOver
                ? OpportunisticUnloadPolicy.ShouldUnloadOnRunEnd(pawnToTarget, pawnToStorage, storageToTarget, loadFraction)
                : OpportunisticUnloadPolicy.ShouldUnloadOnWay(pawnToTarget, pawnToStorage, storageToTarget, loadFraction);
        }

        /// <summary>
        /// KEEP WORKING WHEN FULL (opt-in, default OFF) — the weighted "unload before a long relocation" rule.
        /// A full pawn that kept working (its full-trigger was suppressed in
        /// <see cref="YieldRouter.MaybeUnloadBecauseFull"/>) sheds its load at storage BEFORE walking to its
        /// next work target only when that pays off: carrying the load onward costs <c>distToNextWork·drag</c>
        /// while detouring to storage costs <c>distToStorage·drag</c> and then travels empty — so unload first
        /// when the next task is farther than the dropoff (the pure <see cref="KeepWorkingPolicy"/>). It only
        /// applies while the pawn is actually OVERLOADED (paying the drag); an at/under-capacity pawn keeps its
        /// load and works. Independent of <c>opportunisticUnload</c> (that's a separate, on-the-way feature) but
        /// shares the same safety gates (auto-unload master, drafted/forced, the divert cooldown, an HD-job skip,
        /// and storage existence). Returns false when the toggle is OFF, so the work-found path is unchanged.
        /// </summary>
        internal static bool ShouldUnloadBeforeRelocation(Pawn pawn, Job workJob)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.keepWorkingWhenFull || !s.markForUnload)
                return false;
            if (pawn?.Map == null || !pawn.Map.IsPlayerHome || workJob?.def == null)
                return false;
            // Never defer a player-prioritized job, and never divert while drafted.
            if (pawn.Drafted || workJob.playerForced)
                return false;
            // Never relocate-unload off EMERGENCY / medical / rescue / firefighting work or a resting patient
            // (mirrors ShouldDivert; see ProtectedWork). This feature is opt-in (keepWorkingWhenFull, default OFF),
            // but the same medical guard applies.
            if (ProtectedWork.IsProtected(workJob, false) || ProtectedWork.IsRestingPatient(pawn))
                return false;
            if (workJob.def == HaulersDreamDefOf.HaulersDream_UnloadInventory
                || pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_UnloadInventory
                || pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_BillPrepGather)
                return false;
            // Never divert before one of HD's OWN jobs (bulk-haul / unload / pack-load / construct-delivery /
            // bill-prep): they deliver or USE the carried load themselves. Identified by the driver assembly,
            // exactly like ShouldDivert.
            if (workJob.def.driverClass != null && workJob.def.driverClass.Assembly == typeof(OpportunisticUnload).Assembly)
                return false;

            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null || comp.PeekHashSet().Count == 0)
                return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - comp.lastOpportunisticUnloadTick < DivertCooldownTicks)
                return false; // a recent (possibly failed) divert — don't loop
            // Same class of gap #152 fixed on the end-of-run trigger (see ShouldDivert above): a tracked stack
            // existing isn't enough, it must have surplus above keep-stock, or this rule keeps re-checking (and
            // re-running the expensive search below) an overloaded pawn whose whole pack is personal stock.
            if (!PawnUnloadChecker.AnyUnloadable(pawn, comp.PeekHashSet()))
                return false;

            // Is the pawn actually overloaded right now (paying the move-speed drag)? Mirror StatPart_Overload's
            // signal: encumbrance ratio > 1 -> the overload speed factor is < 1. The rule only pays off then.
            // Stands down (speedFactor 1 -> rule returns false) whenever overload is off/strict/CE, since
            // OverloadGate.NoOverloadFor pins the effective level to Off and SpeedFactor(Off, ...) == 1.
            var mass = PawnMassCache.MassInfo(pawn);
            float cap = mass.Capacity;
            if (cap <= 0f)
                return false;
            float ratio = mass.CurrentMass / cap;
            int level = OverloadGate.NoOverloadFor(pawn, s) ? Core.OverloadTuning.OffLevel : s.overloadLevel;
            float speedFactor = Core.OverloadTuning.SpeedFactor(level, ratio);
            if (speedFactor >= 1f)
                return false; // at/under capacity (or overload disabled) -> carrying is free -> keep working

            // Where the pawn is heading. Most work jobs set targetA; grow-zone harvests use targetQueueA.
            IntVec3 target = workJob.targetA.Cell;
            if (!target.IsValid)
            {
                var queue = workJob.targetQueueA;
                if (queue != null && queue.Count > 0)
                    target = queue[0].Cell;
            }
            if (!target.IsValid)
                return false;

            // Claim the cooldown before the expensive search below, win or lose, same reasoning as ShouldDivert
            // (issue #160-class hitch): a pawn that STAYS overloaded through many consecutive yield targets would
            // otherwise re-run TryFindBestBetterStoreCellFor on every single one, with no backoff, for as long as
            // it keeps failing the KeepWorkingPolicy distance math.
            comp.lastOpportunisticUnloadTick = now;

            // The storage we'd unload to — the nearest accepting cell for any STORABLE tracked stack (same
            // representative-pick as ShouldDivert: an un-storable rock chunk must not suppress the whole rule).
            IntVec3 storageCell = IntVec3.Invalid;
            // MP determinism: process tagged stacks in thingIDNumber order so a capacity-bound loop deposits/drops the same subset on every client.
            // (Here: pick the storage-cell representative deterministically — break on the first storable stack in ID
            // order so every client resolves the SAME storageCell and the same should-unload decision.)
            var rep = scratchRep ?? (scratchRep = new System.Collections.Generic.List<Thing>());
            rep.Clear();
            // PeekHashSet (no self-heal) may hold null tags; skip nulls so the sort comparator never NPEs
            // (the loop still re-checks Destroyed per item).
            foreach (var t in comp.PeekHashSet())
                if (t != null)
                    rep.Add(t);
            rep.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            for (int ri = 0; ri < rep.Count; ri++)
            {
                var t = rep[ri];
                if (t == null || t.Destroyed)
                    continue;
                StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, pawn.Map,
                    StoragePriority.Unstored, pawn.Faction, out storageCell, needAccurateResult: false);
                if (storageCell.IsValid)
                    break;
            }
            if (!storageCell.IsValid)
                return false; // nowhere to unload toward -> just keep working; later triggers still unload

            int distToNextWork = CellDist(pawn.Position, target);
            int distToStorage = CellDist(pawn.Position, storageCell);
            return KeepWorkingPolicy.ShouldUnloadBeforeNext(
                speedFactor, distToNextWork, distToStorage, s.keepWorkingMarginCells);
        }

        internal static void NotifyDiverted(Pawn pawn)
        {
            var comp = pawn?.GetComp<CompHauledToInventory>();
            if (comp != null)
                comp.lastOpportunisticUnloadTick = Find.TickManager?.TicksGame ?? 0;
        }

        /// <summary>
        /// End-of-work-run unload: the work scan found NOTHING for a pawn carrying scooped goods — the
        /// run is over, so the pawn makes its consolidated unload trip NOW, before drifting off to
        /// recreation/wandering with a full backpack. Returns the ready unload job (reservations made)
        /// or null. Issued as the work node's own think result, which lands it exactly where vanilla
        /// puts UnloadEverything trips: after work, before leisure — needs the priority sorter ranks
        /// above work (urgent food, rest) still win. Gates are the pure
        /// <see cref="Core.UnloadPolicy.EndOfRunUnloadAllowed"/>; shares the divert cooldown so a
        /// failing trip can't re-issue in a tight loop.
        /// </summary>
        internal static Job TryGetEndOfRunUnloadJob(Pawn pawn)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || pawn?.Map == null || pawn.jobs == null)
                return null;
            if (pawn.Faction != Faction.OfPlayerSilentFail)
                return null;
            // Never issue an end-of-run unload to a resting patient. A patient in bed re-runs its think tree every
            // ~211 ticks (LayDown's job-override check); if it carries tagged cargo, JobGiver_Work finds no work and
            // reaches this end-of-run path, which would stand the patient up to unload — then the medical think tree
            // lays it back down, and it thrashes (the reported "stands up / lies down"). See ProtectedWork.
            if (ProtectedWork.IsRestingPatient(pawn))
                return null;
            // The auto-unload master switch (off = gizmo-only). Required for BOTH the home storage unload (the
            // home path re-checks it via EndOfRunUnloadAllowed below) and the caravan pack-animal offload, so
            // it gates the non-home fork too — matching the interval/idle path (gated on markForUnload upstream)
            // and the before-downtime path. Hoisted here so the caravan fork below is governed by it.
            if (!s.markForUnload)
                return null;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return null;
            var tracked = comp.GetHashSet();
            if (tracked.Count == 0)
                return null;

            // SETTLE gate: an empty work scan is NOT the end of the run by itself — in a busy colony work
            // momentarily "runs dry" for a pawn between items (another pawn grabbed the next job, a 1-tick
            // scan miss) constantly. Unloading then made a trip per item and defeated the overload-and-
            // accumulate design. Only treat the run as OVER once the pawn has stopped picking things up for
            // the settle period (unloadGraceTicks): while it's still actively scooping (lastYieldTick recent)
            // it keeps accumulating toward the smart-overload ceiling; the at-ceiling trigger handles a full
            // load immediately, and a genuinely-idle pawn is also caught by the interval / idle backstop.
            // EXCEPTION: when the pawn is about to REST / RECREATE / EAT (and that toggle is on), bypass the
            // settle — it should put the load away before relaxing, not carry it to bed / the dinner table.
            // (Safe to read needs here: the end-of-run trigger only fires when there is NO work for the pawn,
            // so a low need means it's genuinely heading into downtime, not pausing active work.)
            int settle = IsEnteringDowntime(pawn, s) ? 0 : s.unloadGraceTicks;
            if (settle > 0 && (Find.TickManager?.TicksGame ?? 0) - comp.lastYieldTick < settle)
                return null;

            // Caravan / away map with no player storage — make the consolidated trip onto a pack animal instead of
            // storage, on the SAME end-of-run timing the home pawn uses (the settle gate above is shared). The
            // caravan toggle + eligibility + carrier + cooldown gates (and the reservations / NotifyDiverted)
            // live inside TryGetOpportunisticLoadJob; it returns null (loot rides home) when no usable pack
            // animal is reachable. The work node wraps any non-null job into its ThinkResult unchanged. Any non-home
            // map WITH player storage (a VF RV interior) falls through to the storage-unload job below
            // (ShouldUnloadToStorage true), delivering the end-of-run load into its shelves.
            if (!MapGate.ShouldUnloadToStorage(pawn.Map))
                return PackAnimalLoad.TryGetOpportunisticLoadJob(pawn);

            // At least one tracked stack must have SURPLUS above the pawn's keep-stock, or the unload finds
            // nothing to move (the driver's FirstUnloadableThing skips every keep-stock stack), ends the job,
            // and this trigger re-issues on every cooldown forever: the pawn paces on a keep-stock-only load
            // instead of drifting off to leisure (issue #152). Routed through the SAME
            // PawnUnloadChecker.AnyUnloadable that every OTHER unload trigger and the driver's surplus math use,
            // so the gate and the driver can never disagree again. This end-of-run path alone re-checked
            // present-and-reservable WITHOUT the surplus test, so a tracked stack that was entirely personal
            // food / drugs / inventoryStock / CE loadout slipped the gate and looped.
            bool anyUnloadable = PawnUnloadChecker.AnyUnloadable(pawn, tracked);

            bool alreadyUnloading = pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_UnloadInventory
                                    || PawnUnloadChecker.HasQueuedUnload(pawn);
            int now = Find.TickManager?.TicksGame ?? 0;

            if (!Core.UnloadPolicy.EndOfRunUnloadAllowed(
                    s.markForUnload, YieldRouter.IsEligible(pawn), pawn.Drafted,
                    tracked.Count, anyUnloadable, alreadyUnloading,
                    now - comp.lastOpportunisticUnloadTick, DivertCooldownTicks))
                return null;

            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadInventory);
            if (!job.TryMakePreToilReservations(pawn, false))
                return null;
            NotifyDiverted(pawn);
            HDLog.Dbg($"{pawn} work ran dry with {tracked.Count} tracked stacks — unloading before leisure.");
            return job;
        }

        private static int CellDist(IntVec3 a, IntVec3 b)
            => Mathf.RoundToInt((a - b).LengthHorizontal);
    }
}
