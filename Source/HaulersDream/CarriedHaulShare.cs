using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Share hand-hauled-to-storage" (opt-in, <see cref="HaulersDreamSettings.shareHandHauledToStorage"/>):
    /// a worker that needs a material may claim the stack a colonist is carrying in its HANDS to a stockpile,
    /// meeting it in transit, instead of waiting for it to reach storage and fetching it from there. Only a
    /// GENERIC haul-to-storage stack is fair game — never one being delivered to a specific build, bill,
    /// refuel or install (those are positively excluded by the job def + haul mode). The clean handoff (take
    /// from the hauler's carryTracker, ending its job only on a full take) lives in
    /// <see cref="JobDriver_ClaimFromHauler"/>; this is the scanner that finds eligible carriers.
    /// </summary>
    internal static class CarriedHaulShare
    {
        // After a claim, the WORKER waits this long before intercepting again (so a partial take can't
        // immediately re-intercept). Mirrors OpportunisticUnload's divert cooldown.
        private const int CooldownTicks = 250;

        /// <summary>
        /// The stack <paramref name="carrier"/> is hand-hauling TO STORAGE right now (claimable by
        /// <paramref name="worker"/>), or null. The discriminator is a <c>HaulToCell</c> job in
        /// <c>HaulMode.ToCellStorage</c> — the sole vanilla producer of which is the generic
        /// haul-to-stockpile path, so frames/bills/refuel/install (all <c>HaulToContainer</c>) and
        /// "haul aside" (<c>ToCellNonStorage</c>) are excluded by construction.
        /// </summary>
        internal static Thing StorageBoundCarried(Pawn carrier, Pawn worker)
        {
            if (carrier == null || carrier == worker || !carrier.Spawned || carrier.Dead
                || carrier.Downed || carrier.Drafted || carrier.InMentalState)
                return null;
            var carried = carrier.carryTracker?.CarriedThing;
            if (carried == null || carried.def == null || !carried.def.EverHaulable)
                return null;
            var job = carrier.CurJob;
            if (job == null || job.playerForced) // never override an explicit player haul order
                return null;
            if (job.def != JobDefOf.HaulToCell || job.haulMode != HaulMode.ToCellStorage)
                return null;
            if (job.GetTarget(TargetIndex.A).Thing != carried)
                return null; // claim only the exact stack this haul job is depositing
            IntVec3 dest = job.targetB.Cell;
            if (!dest.IsValid || dest.GetSlotGroup(carrier.Map) == null)
                return null; // live re-confirm the destination is a storage cell
            return carried;
        }

        // Per-tick result cache for CountStorageBoundCarried: the construct-deliver availability scan calls it once
        // per missing-material def per builder — a colony-wide pawn walk each time. Within one tick the answer for
        // a given (worker, def) cannot change, so cache it (cleared whenever the tick advances). Mirrors
        // InventoryShare.CountSharable / OrganicInventoryShare.CountOrganic's countCache pattern exactly.
        private static int countCacheTick = -1;
        private static readonly Dictionary<long, int> countCache = new Dictionary<long, int>();

        /// <summary>Total units of <paramref name="def"/> being hand-hauled to storage on this map — for the
        /// availability gate. Per-tick cached (a (worker, def) answer is invariant within a tick).</summary>
        internal static int CountStorageBoundCarried(Map map, Pawn worker, ThingDef def)
        {
            if (map == null || worker == null || def == null)
                return 0;

            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick != countCacheTick)
            {
                countCacheTick = tick;
                countCache.Clear();
            }
            long key = ((long)worker.thingIDNumber << 32) | (uint)def.shortHash;
            if (countCache.TryGetValue(key, out int cached))
                return cached;

            int total = 0;
            var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                var carried = StorageBoundCarried(pawns[i], worker);
                if (carried != null && carried.def == def)
                    total += carried.stackCount;
            }
            countCache[key] = total;
            return total;
        }

        /// <summary>
        /// The closest reachable hauler whose storage-bound stack of <paramref name="def"/> is worth
        /// intercepting to satisfy <paramref name="needer"/> (needing <paramref name="needed"/> units), or
        /// null. <paramref name="carrier"/> is the chosen hauler. Single-claimant via <c>CanReserve(hauler)</c>
        /// + a worker cooldown; only fires when the pure <see cref="CarriedInterceptPolicy"/> says it saves a trip.
        /// </summary>
        internal static Thing FindCarriedStack(Map map, Pawn worker, ThingDef def, Thing needer, int needed, out Pawn carrier)
        {
            carrier = null;
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.shareHandHauledToStorage || map == null || worker == null || def == null
                || needer == null || needed <= 0)
                return null;

            int now = Find.TickManager?.TicksGame ?? 0;
            var wcomp = worker.GetComp<CompHauledToInventory>();
            if (wcomp != null && now - wcomp.lastInterceptedTick < CooldownTicks)
                return null; // worker just intercepted -> let it finish before chasing another hauler

            // Short-circuit: if nobody is hand-hauling this def to storage right now (per-tick cached count == 0),
            // the find cannot succeed — skip the colony walk + per-carrier reach/reserve/intercept checks entirely.
            if (CountStorageBoundCarried(map, worker, def) <= 0)
                return null;

            var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            Thing best = null;
            int bestDist = int.MaxValue;
            for (int i = 0; i < pawns.Count; i++)
            {
                var hauler = pawns[i];
                var carried = StorageBoundCarried(hauler, worker);
                if (carried == null || carried.def != def)
                    continue;
                if (!worker.CanReserve(hauler)) // another worker is already claiming this hauler
                    continue;
                if (!worker.CanReach(hauler, PathEndMode.Touch, Danger.Some))
                    continue;

                IntVec3 storeCell = hauler.CurJob.targetB.Cell;
                int workerToHauler = CellDist(worker.Position, hauler.Position);
                int haulerToStorage = CellDist(hauler.Position, storeCell);
                int haulerToNeeder = CellDist(hauler.Position, needer.Position);
                int storageToNeeder = CellDist(storeCell, needer.Position);
                float frac = Mathf.Min(carried.stackCount, needed) / (float)needed;
                if (!CarriedInterceptPolicy.ShouldIntercept(workerToHauler, haulerToStorage, haulerToNeeder, storageToNeeder, frac))
                    continue;

                if (workerToHauler < bestDist)
                {
                    bestDist = workerToHauler;
                    best = carried;
                    carrier = hauler;
                }
            }
            return best;
        }

        private static int CellDist(IntVec3 a, IntVec3 b) => Mathf.RoundToInt((a - b).LengthHorizontal);
    }
}
