using HaulersDream.Core;
using RimWorld;
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
        private const int DivertCooldownTicks = 250;

        internal static bool ShouldDivert(Pawn pawn, Job workJob)
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

            return OpportunisticUnloadPolicy.ShouldUnloadOnWay(pawnToTarget, pawnToStorage, storageToTarget, loadFraction);
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
            // Non-home / temporary map: no player storage to unload to (a storage-unload would no-op or, pre-fix,
            // drop at feet). Loot stays in inventory (rides home) and pack-animal loading is handled separately.
            // Suppressed regardless of enableOnNonHomeMaps (which gates scooping, not this).
            if (pawn.Map != null && !pawn.Map.IsPlayerHome)
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
            int settle = s.unloadGraceTicks;
            if (settle > 0 && (Find.TickManager?.TicksGame ?? 0) - comp.lastYieldTick < settle)
                return null;

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
