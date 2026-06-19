using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure planning for batching more queued construction needers into a single hauling trip. Vanilla
    /// only batches needers within 8 tiles, so a pawn building a long fence carries ~9 wood at a time and
    /// shuttles back and forth. This decides how much to carry (up to the pawn's HAND capacity — carrying
    /// in hands has no move-speed penalty, so a bigger load is strictly better) and how many extra needers
    /// and resource stacks to attach.
    ///
    /// The game layer gathers the candidates (reachable/reservable, nearest-first, positive amounts only)
    /// and commits the result; this just does the accumulation arithmetic so it is unit-testable.
    /// </summary>
    public static class ConstructionBatchMath
    {
        /// <param name="handCapacity">Max units carryable in hands this trip (carryTracker.MaxStackSpaceEver).</param>
        /// <param name="currentCount">What the vanilla job already plans to carry (its batched need).</param>
        /// <param name="currentResourceAvailable">Resource units already attached to the job (targetA + queueA).</param>
        /// <param name="extraNeederSpaces">Additional reachable needers' per-needer space, nearest-first, all &gt; 0.</param>
        /// <param name="extraResourceCounts">Additional reachable resource stacks' counts, nearest-first, all &gt; 0.</param>
        /// <param name="finalCount">The carry count to set (&gt;= currentCount, never above hand capacity or available resource).</param>
        /// <param name="neederTake">How many leading entries of <paramref name="extraNeederSpaces"/> to attach.</param>
        /// <param name="resourceTake">How many leading entries of <paramref name="extraResourceCounts"/> to attach.</param>
        public static void Plan(
            int handCapacity, int currentCount, int currentResourceAvailable,
            IReadOnlyList<int> extraNeederSpaces, IReadOnlyList<int> extraResourceCounts,
            out int finalCount, out int neederTake, out int resourceTake)
        {
            finalCount = currentCount < 0 ? 0 : currentCount;
            neederTake = 0;
            resourceTake = 0;
            if (handCapacity <= currentCount)
                return; // hands already at the trip ceiling — nothing to gain

            // 1) Attach more needers until the batched demand reaches hand capacity.
            int demand = currentCount;
            int n = extraNeederSpaces?.Count ?? 0;
            for (int i = 0; i < n && demand < handCapacity; i++)
            {
                int sp = extraNeederSpaces[i];
                if (sp <= 0) continue; // defensive; caller pre-filters
                demand += sp;
                neederTake = i + 1;
            }
            int desired = Math.Min(demand, handCapacity);
            if (desired <= currentCount)
            {
                neederTake = 0;
                return; // no extra demand found nearby
            }

            // 2) Attach more resource stacks only if what's already in the job can't cover the desired load.
            int resource = currentResourceAvailable;
            int m = extraResourceCounts?.Count ?? 0;
            for (int i = 0; i < m && resource < desired; i++)
            {
                int rc = extraResourceCounts[i];
                if (rc <= 0) continue;
                resource += rc;
                resourceTake = i + 1;
            }

            finalCount = Math.Min(Math.Min(desired, resource), handCapacity);
            if (finalCount <= currentCount)
            {
                // Not enough reachable resource to raise the load at all — change nothing.
                finalCount = currentCount;
                neederTake = 0;
                resourceTake = 0;
            }
        }

        /// <summary>
        /// Index of the candidate cell NEAREST <paramref name="currentX"/>/<paramref name="currentZ"/> by squared
        /// horizontal distance, with ties resolved to the LOWEST index (the earliest-queued, for a stable,
        /// deterministic pick). Returns -1 for an empty / null candidate set.
        ///
        /// Used by the multi-site construction-delivery driver to deliver to the closest still-needing site FROM
        /// WHERE THE PAWN IS STANDING on each hop — a greedy nearest-neighbour route — instead of strict FIFO over
        /// a queue ordered by distance from a single fixed anchor. The fixed-anchor order made a builder zig-zag
        /// across a wall/fence line (closest-to-anchor, then the sites concentrically AROUND the filled ones,
        /// alternating sides), turning a short walk into long back-and-forth trips; re-anchoring on the pawn each
        /// hop keeps every leg short. <paramref name="xs"/> and <paramref name="zs"/> are parallel (same length).
        /// </summary>
        public static int NextNearestIndex(IReadOnlyList<int> xs, IReadOnlyList<int> zs, int currentX, int currentZ)
        {
            // Both lists are required and parallel (same length); a null in either yields no candidate.
            int count = (xs == null || zs == null) ? 0 : xs.Count;
            int best = -1;
            long bestDistSq = long.MaxValue;
            for (int i = 0; i < count; i++)
            {
                long dx = xs[i] - currentX;
                long dz = zs[i] - currentZ;
                long distSq = dx * dx + dz * dz;
                if (distSq < bestDistSq) // strict < keeps the lowest index on a tie
                {
                    bestDistSq = distSq;
                    best = i;
                }
            }
            return best;
        }
    }
}
