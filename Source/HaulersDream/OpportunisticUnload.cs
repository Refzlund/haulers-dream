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

        private static int CellDist(IntVec3 a, IntVec3 b)
            => Mathf.RoundToInt((a - b).LengthHorizontal);
    }
}
