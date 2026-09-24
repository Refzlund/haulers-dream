using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>Recover only demonstrably held visit cargo. Recovery never resumes the cancelled operation.</summary>
    internal static class BulkUnloadRecovery
    {
        internal static void Queue(Pawn pawn, IEnumerable<Thing> cargo, Thing handTail, int handCount, bool playerForced)
        {
            if (pawn?.jobs == null || !pawn.Spawned || pawn.Dead || pawn.inventory == null) return;
            var comp = pawn.GetComp<CompHauledToInventory>();
            bool inventoryCargo = false;
            if (cargo != null)
                foreach (var thing in cargo)
                    if (thing != null && !thing.Destroyed && pawn.inventory.innerContainer.Contains(thing))
                    { comp?.RegisterHauledItem(thing); inventoryCargo |= InventorySurplus.SurplusOf(pawn, thing) > 0; }
            if (handTail != null && !handTail.Destroyed)
            {
                if (pawn.inventory.innerContainer.Contains(handTail))
                { comp?.RegisterHauledItem(handTail); inventoryCargo |= InventorySurplus.SurplusOf(pawn, handTail) > 0; }
                else if (!pawn.Drafted && pawn.carryTracker?.innerContainer.Contains(handTail) == true && handTail.stackCount <= handCount)
                {
                    // Replacement/queued player commands keep their position. This is a storage recovery,
                    // never an authority token to continue taking things from the former carrier/hold.
                    var haul = HaulAIUtility.HaulToStorageJob(pawn, handTail, forced: true);
                    if (haul != null)
                    { haul.playerForced = playerForced; pawn.jobs.jobQueue.EnqueueLast(haul, JobTag.Misc); }
                }
                // DraftController sets drafted=true before ending this job. Never create a drafted haul:
                // native cleanup subsequently drops hands (carryThingAfterJob=false). That is retained
                // world cargo, not a completed delivery; inventory above stays tagged for later recovery.
            }
            if (!inventoryCargo || pawn.Drafted || PawnUnloadChecker.HasQueuedUnload(pawn)) return;
            // The general checker also inserts a pending SelfPickup ahead of queued work. A cancelled
            // transport visit must not do that: append only its ordinary recovery, preserving queue order.
            Job recovery = MapGate.ShouldUnloadToStorage(pawn.Map)
                ? JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadInventory)
                : PackAnimalLoad.TryGetOpportunisticLoadJob(pawn);
            if (recovery != null) pawn.jobs.jobQueue.EnqueueLast(recovery, JobTag.Misc);
        }
    }
}
