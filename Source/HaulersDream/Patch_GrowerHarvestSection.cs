using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Sectional grow-zone harvest (Steam / #187b). Vanilla <see cref="WorkGiver_GrowerHarvest.JobOnCell"/>
    /// packs up to ~40 nearest-first plant cells into ONE <see cref="JobDefOf.Harvest"/> job's
    /// <c>targetQueueA</c> (decompile-verified: it <c>AddQueuedTarget</c>s up to 40 radial cells, then
    /// <c>SortBy(DistanceToSquared(pawn.Position))</c> once &gt;= 3 are queued), which a single
    /// <c>JobDriver_PlantWork</c> drains before the run ends. Under "Drop &amp; haul" the producer's deferred
    /// self-pickup can only fire when that job ENDS, so a whole field's yield piles on the floor before it is
    /// collected (the reported "auto-harvest drops a big pile, then collects it all at once").
    ///
    /// This postfix caps the queue to a small NEAREST section (<see cref="SectionSize"/>), so a big field is
    /// harvested — and swept up — in sections instead of one late sweep. NO harvest progress is lost: vanilla
    /// already sorted <c>targetQueueA</c> nearest-first, so only the FARTHEST (tail) cells are dropped from THIS
    /// job, and the pawn's next <c>JobOnCell</c> scan re-queues them as the next (again nearest-first) section.
    /// <c>targetQueueB</c> / <c>countQueue</c> are untouched (a Harvest job doesn't use them).
    ///
    /// Scoped to the exact case that benefits: an AUTONOMOUS harvest (not a player-forced "Prioritize" order,
    /// which stays at vanilla's full batch so a plant-work-disabled pawn still clears the field in that one
    /// order), HD active on the pawn's map, a scoop-eligible producer, and THIS field's yield behavior (Harvest
    /// for crops, Logging for a tree/cactus zone — classified from the plant at the work cell exactly as
    /// <see cref="YieldRouter"/> does) set to DropThenHaul — the only mode whose collection waits for the job to
    /// end. DirectToInventory pockets in the GenPlace prefix (no floor pile, unaffected) and Disabled = vanilla;
    /// both are left at the full field, byte-identical. A non-Harvest result or a short queue also returns
    /// unchanged.
    ///
    /// No try/catch: a WorkGiver postfix throw is already sandboxed by vanilla's per-WorkGiver catch inside
    /// <c>JobGiver_Work.TryIssueJobPackage</c>, and a bug here should surface as a red error, not be swallowed —
    /// matching <see cref="Patch_HaulUrgently_BulkHaul"/>.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_GrowerHarvest), nameof(WorkGiver_GrowerHarvest.JobOnCell))]
    public static class Patch_GrowerHarvestSection
    {
        // The most plant cells one auto-harvest run collects before the job ends and its yields are swept up.
        // Small enough that a field is collected in visible sections (the "big pile then one sweep" becomes
        // several small sweeps); large enough that the per-section re-scan overhead stays negligible.
        private const int SectionSize = 8;

        static void Postfix(ref Job __result, Pawn pawn, IntVec3 c, bool forced)
        {
            if (__result == null || __result.def != JobDefOf.Harvest)
                return; // not the batched grow-zone harvest job we cap (e.g. a cut job) -> vanilla
            if (forced)
                return; // a player right-click "Prioritize harvesting" is a supervised, one-shot order: keep
                        // vanilla's full ~40-plant batch so a pawn with plant work disabled in its worktab still
                        // clears the whole field in that one order (capping it would strand the tail). The
                        // sectional sweep targets the AUTONOMOUS grow-zone path (forced == false), which the next
                        // scan keeps re-issuing anyway, so it still collects in sections.
            var queue = __result.targetQueueA;
            if (queue == null || queue.Count <= SectionSize)
                return; // already a single section (or shorter) -> nothing to trim

            var map = pawn?.Map;
            if (map == null || !MapGate.HdActiveOnMap(map) || !YieldRouter.IsCandidate(pawn))
                return; // HD stood down on this map, or this pawn never scoops -> leave vanilla's full field

            var s = HaulersDreamMod.Settings;
            if (s == null)
                return;
            // Classify THIS field's yield the same way YieldRouter does (Harvest -> Logging for a tree/cactus),
            // reading the plant at the work cell — the queue is a homogeneous wanted-plant cluster, so c is
            // representative. Only cap when that yield's behavior is DropThenHaul (collection waits for the job
            // to end). Direct pockets in the prefix and Disabled = vanilla, so both keep the full field.
            var plant = c.GetPlant(map);
            var type = plant != null && plant.def.plant.IsTree ? HaulSourceType.Logging : HaulSourceType.Harvest;
            if (s.BehaviorFor(type) != YieldBehavior.DropThenHaul)
                return;

            // Keep the nearest section; drop the already-sorted farther tail (re-queued by the next scan).
            int keep = HarvestSectionPolicy.Cap(queue.Count, SectionSize);
            queue.RemoveRange(keep, queue.Count - keep);
        }
    }
}
