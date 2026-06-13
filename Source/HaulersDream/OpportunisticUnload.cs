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

            // A representative scooped good -> the storage we'd unload it to, plus the total scooped mass.
            float cap = MassUtility.Capacity(pawn);
            if (cap <= 0f)
                return false;
            Thing sample = null;
            float trackedMass = 0f;
            foreach (var t in tracked)
            {
                if (t == null || t.Destroyed)
                    continue;
                if (sample == null)
                    sample = t;
                trackedMass += t.stackCount * t.GetStatValue(StatDefOf.Mass);
            }
            if (sample == null)
                return false;

            if (!StoreUtility.TryFindBestBetterStoreCellFor(sample, pawn, pawn.Map,
                    StoragePriority.Unstored, pawn.Faction, out IntVec3 storageCell, needAccurateResult: false))
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
            // Same non-home-map stance as the automatic checker: don't dump the load at the pawn's
            // feet on a storage-less encounter map; it unloads at home.
            if (!s.enableOnNonHomeMaps && !pawn.Map.IsPlayerHome)
                return null;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return null;
            var tracked = comp.GetHashSet();
            if (tracked.Count == 0)
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
