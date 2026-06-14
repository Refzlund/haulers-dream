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
            if (pawn.InBed() || pawn.IsFormingCaravan() || pawn.mindState?.duty != null)
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
            // Caravan / away map: no player storage — put the load onto a pack animal before resting/eating/
            // relaxing at the campsite, the same way the home pawn puts it in storage. Same downtime trigger
            // and per-activity toggle (markForUnload + the `enabled` gate above); the caravan toggle + carrier
            // availability gate live inside TryGetOpportunisticLoadJob (which also makes its own reservations /
            // NotifyDiverted). When no usable pack animal is reachable it returns null -> loot rides home.
            if (!pawn.Map.IsPlayerHome)
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
            var dc = def?.driverClass;
            if (dc == null)
                return false;
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
        internal static bool ShouldDivert(Pawn pawn, Job workJob, bool runOver = false)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.opportunisticUnload || !s.markForUnload)
                return false;
            if (pawn?.Map == null || workJob?.def == null || pawn.Drafted || workJob.playerForced)
                return false; // never defer player-prioritized work
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

            // Where the pawn is heading. Most work jobs set targetA; grow-zone harvests use targetQueueA
            // (targetA is Invalid), so fall back to the first queued cell.
            IntVec3 target = workJob.targetA.Cell;
            if (!target.IsValid)
            {
                var queue = workJob.targetQueueA;
                if (queue != null && queue.Count > 0)
                    target = queue[0].Cell;
            }
            if (!target.IsValid)
                return false;

            // The total scooped mass, plus the storage we'd unload to. Pick a STORABLE tracked item as the
            // storage-cell representative: keying off an arbitrary first item meant an un-storable one (e.g. a
            // rock chunk, which no default stockpile accepts) suppressed the WHOLE pass-by divert even when the
            // other carried goods could be dropped en route.
            float cap = MassUtility.Capacity(pawn);
            if (cap <= 0f)
                return false;
            float trackedMass = 0f;
            IntVec3 storageCell = IntVec3.Invalid;
            foreach (var t in tracked)
            {
                if (t == null || t.Destroyed)
                    continue;
                trackedMass += t.stackCount * t.GetStatValue(StatDefOf.Mass);
                if (!storageCell.IsValid)
                    StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, pawn.Map,
                        StoragePriority.Unstored, pawn.Faction, out storageCell, needAccurateResult: false);
            }
            // Nothing storable to divert toward -> let the trip proceed un-diverted; the end-of-run and
            // meal/recreation checkpoint triggers (both storage-independent) still make the unload trip,
            // and the unload driver itself desperately-stores the un-storable items.
            if (!storageCell.IsValid)
                return false;

            int pawnToTarget = CellDist(pawn.Position, target);
            int pawnToStorage = CellDist(pawn.Position, storageCell);
            int storageToTarget = CellDist(storageCell, target);
            float loadFraction = trackedMass / cap;

            // Run-end (switched to non-yield work): relaxed criteria — shed the load at nearby storage even on
            // a short hop. Otherwise (continuing a yield run / a haul): the strict "real journey on the way" bar.
            return runOver
                ? OpportunisticUnloadPolicy.ShouldUnloadOnRunEnd(pawnToTarget, pawnToStorage, storageToTarget, loadFraction)
                : OpportunisticUnloadPolicy.ShouldUnloadOnWay(pawnToTarget, pawnToStorage, storageToTarget, loadFraction);
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

            // Caravan / away map: no player storage — make the consolidated trip onto a pack animal instead of
            // storage, on the SAME end-of-run timing the home pawn uses (the settle gate above is shared). The
            // caravan toggle + eligibility + carrier + cooldown gates (and the reservations / NotifyDiverted)
            // live inside TryGetOpportunisticLoadJob; it returns null (loot rides home) when no usable pack
            // animal is reachable. The work node wraps any non-null job into its ThinkResult unchanged.
            if (!pawn.Map.IsPlayerHome)
                return PackAnimalLoad.TryGetOpportunisticLoadJob(pawn);

            // At least one tracked stack must be in inventory and reservable, or the job ends
            // Incompletable instantly and this would re-issue every think cycle (same guard as the
            // vanilla-unload substitution patch).
            var inner = pawn.inventory?.innerContainer;
            if (inner == null)
                return null;
            bool anyUnloadable = false;
            foreach (var t in tracked)
            {
                if (t != null && inner.Contains(t) && pawn.CanReserve(t))
                {
                    anyUnloadable = true;
                    break;
                }
            }

            bool alreadyUnloading = pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_UnloadInventory
                                    || PawnUnloadChecker.HasQueuedUnload(pawn);
            int now = Find.TickManager?.TicksGame ?? 0;

            // Honor the anti-livelock backoff here too: this end-of-run path is ALWAYS automatic (never a forced
            // gizmo), so a pawn whose last unload moved nothing (an un-pullable item) must not fire a no-op unload
            // at every work-run boundary either. Cleared by progress / a fresh tag, same as the checker.
            if (now < comp.unloadBackoffUntilTick)
                return null;

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
