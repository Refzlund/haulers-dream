using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Sectional grow-zone harvest (Steam / #187b): vanilla <c>WorkGiver_GrowerHarvest.JobOnCell</c> packs up to
    /// ~40 nearest-first plant cells into ONE <c>Harvest</c> job's <c>targetQueueA</c>, drained by a single
    /// <c>JobDriver_PlantWork</c> — so under "Drop &amp; haul" a producer's deferred self-pickup, which can only
    /// run once its job ENDS, doesn't fire until the WHOLE field is harvested, piling the field's output on the
    /// floor first. Capping the queue to a small NEAREST-cell section makes the run end (and the collection fire)
    /// sooner, so a large field is swept up in sections. Pure; the WorkGiver postfix trims <c>targetQueueA</c> to
    /// the returned keep-count and the pawn re-scans for the next (again nearest-first) section, so no harvest
    /// progress is lost — only already-nearest-sorted tail cells move to the next run.
    /// </summary>
    public static class HarvestSectionPolicy
    {
        /// <summary>How many of a Harvest job's queued plant cells to KEEP so one run is a bounded section.</summary>
        /// <param name="count">The queued cell count vanilla produced (<c>targetQueueA.Count</c>), already sorted
        /// nearest-first — so the kept prefix is the tightest cluster around the pawn.</param>
        /// <param name="sectionSize">The section cap: the most cells a single run should harvest before the job
        /// ends and the yields are collected. A non-positive value DISABLES the cap.</param>
        /// <returns>The whole <paramref name="count"/> when it already fits within <paramref name="sectionSize"/>
        /// (nothing to trim), else exactly <paramref name="sectionSize"/>. A non-positive
        /// <paramref name="sectionSize"/> returns <paramref name="count"/> unchanged, so an invalid cap can never
        /// trim the entire queue away.</returns>
        public static int Cap(int count, int sectionSize)
        {
            if (sectionSize <= 0)
                return count; // no/invalid cap -> leave the queue exactly as vanilla built it
            return Math.Min(count, sectionSize);
        }
    }
}
