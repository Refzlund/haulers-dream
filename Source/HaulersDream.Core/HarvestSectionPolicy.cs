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
        /// <summary>
        /// A harvest section: the number of plant yields collected as one visible batch. Shared so the two paths
        /// that make a field harvest visible stay in lockstep — the grow-zone job cap (<see cref="Cap"/>) and the
        /// clustered single-plant collection hold (<see cref="ShouldCollectNow"/>) both mean "8 plants per section".
        /// </summary>
        public const int SectionSize = 8;

        /// <summary>
        /// Cluster radius in cells: a harvest counts as continuing a cluster only when it lands within this
        /// Chebyshev distance of the pawn's PREVIOUS harvest (a (2*R+1)² square around it). "Within 2 blocks."
        /// </summary>
        public const int ClusterRadius = 2;

        /// <summary>
        /// How stale the previous-harvest marker may be (ticks) and still count as the same continuous run. A gap
        /// longer than this means the pawn stopped harvesting and came back, so the next harvest is a fresh
        /// isolated one (collected immediately), not a cluster continuation. ~10 in-game seconds — comfortably
        /// longer than the walk-plus-work gap between two adjacent designated plants, short enough that a harvest
        /// after an unrelated detour is treated as isolated.
        /// </summary>
        public const int ClusterRecencyTicks = 600;

        /// <summary>
        /// Is this harvest CONTINUING a cluster the pawn is already working — i.e. close enough to, and soon enough
        /// after, its previous harvest to be "a lot of harvest next to each other"? Only a clustered harvest holds
        /// for a sectioned sweep; an isolated one (no recent previous harvest, or one more than
        /// <see cref="ClusterRadius"/> away) is collected immediately so the pawn doesn't drop it and wander off.
        /// </summary>
        /// <param name="hasPrevious">Whether the pawn has a recorded previous harvest to compare against at all
        /// (false on the first harvest of a session/after load -> never clustered -> immediate).</param>
        /// <param name="dxAbs">Absolute x-cell distance from the previous harvest to this one.</param>
        /// <param name="dzAbs">Absolute z-cell distance from the previous harvest to this one.</param>
        /// <param name="ticksSincePrevious">Ticks elapsed since the previous harvest (a non-negative game-tick
        /// delta; a negative value, which a clock reset could produce, reads as not-recent -> not clustered).</param>
        /// <param name="radius">The cluster radius (<see cref="ClusterRadius"/>); a non-positive value clusters
        /// only an exact-same-cell repeat.</param>
        /// <param name="recencyTicks">The recency window (<see cref="ClusterRecencyTicks"/>).</param>
        /// <returns>True when the previous harvest is both within <paramref name="radius"/> (Chebyshev) and no
        /// older than <paramref name="recencyTicks"/> — the pawn is working a contiguous patch.</returns>
        public static bool IsClustered(bool hasPrevious, int dxAbs, int dzAbs, int ticksSincePrevious,
            int radius, int recencyTicks)
        {
            if (!hasPrevious)
                return false;
            if (ticksSincePrevious < 0 || ticksSincePrevious > recencyTicks)
                return false; // no recent previous harvest -> a fresh isolated one
            return dxAbs <= radius && dzAbs <= radius; // Chebyshev "within radius blocks"
        }

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

        /// <summary>
        /// Single-plant harvest/cut collection cadence (Steam consistency follow-up). A grow-zone field packs many
        /// plants into ONE job, so its yields visibly PILE on the floor before the run-end sweep. But each "order →
        /// harvest" / "cut plants" is its OWN one-plant job (<c>HarvestDesignated</c> / <c>CutPlantDesignated</c>),
        /// so collecting on every drop pockets each yield the instant its one-plant job ends. The player wants BOTH
        /// consistency with the field AND that a lone harvest isn't dropped-and-abandoned:
        /// <list type="bullet">
        /// <item>A CLUSTERED harvest (within <see cref="ClusterRadius"/> of the previous one — "a lot of harvest
        /// next to each other") holds its pickup until a full section has piled, then sweeps the section — the
        /// visible pile-and-sweep of a field.</item>
        /// <item>An ISOLATED harvest (no recent nearby previous — a one-off order, or the first of a run) is
        /// collected IMMEDIATELY: it still drops visibly, but the same pawn scoops it right away instead of
        /// wandering off and leaving it.</item>
        /// </list>
        /// Every non-plant producer (mining, deep drill, deconstruct, animal, strip, uninstall, fishing) collects
        /// each drop immediately, unchanged.
        /// </summary>
        /// <param name="isPlantWork">True when the producing pawn's current job is plant work (harvest or cut,
        /// whether a grow-zone <c>Harvest</c> or a designated order) — the only producer that piles into sections.</param>
        /// <param name="clustered">Whether this harvest continues a cluster (see <see cref="IsClustered"/>). Only a
        /// clustered plant-work harvest holds for a section; an isolated one collects now.</param>
        /// <param name="pendingCount">Fresh drops the producer has recorded but not yet collected (its pending queue
        /// size AFTER recording the current drop).</param>
        /// <param name="sectionSize">A visible section's worth of drops (<see cref="SectionSize"/>). A non-positive
        /// value disables the hold, so every drop collects immediately (the pre-policy behavior).</param>
        /// <returns>True to start the self-pickup now; false to keep piling (a clustered plant-work harvest still
        /// below the section size).</returns>
        public static bool ShouldCollectNow(bool isPlantWork, bool clustered, int pendingCount, int sectionSize)
        {
            if (!isPlantWork || !clustered || sectionSize <= 0)
                return true; // non-plant, an isolated harvest, or the hold disabled -> collect this drop now
            return pendingCount >= sectionSize; // clustered plant work: wait for a full visible section
        }
    }
}
