using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure travel-budget cost for the "plan prioritized route" feature's Max-travel limit.
    ///
    /// REPLACES the old "cumulative distance between distance-RANKED stops" span, which ZIGZAGGED and badly
    /// over-counted: the stops are gathered in order of distance to the CLICKED anchor (a ranking), so two stops
    /// adjacent in that order can sit on OPPOSITE sides of the anchor — summing the leg between them measured a
    /// back-and-forth walk, not the real route. ~12 such legs of 30–60 cells each blew past a 496-cell budget
    /// even on a patch only tens of cells across, trimming obvious nearby targets.
    ///
    /// Instead this measures the length of an ACTUAL route through the stops, built by CHEAPEST INSERTION from
    /// the pawn, HARVEST-ONLY (no haul-back to storage — folding the return leg into the budget would let a far
    /// storage cell blow the entire budget on a tiny patch, reproducing the very bug we're fixing).
    ///
    /// MONOTONICITY (the project's sacred invariant — it has regressed three times): the cheapest-insertion path
    /// length is monotone NON-DECREASING in the number of stops BY CONSTRUCTION. Inserting a stop between path
    /// points a,b adds <c>d(a,new)+d(new,b)-d(a,b) ≥ 0</c> (triangle inequality on Euclidean distances), or
    /// appends <c>d(last,new) ≥ 0</c> — a non-negative delta every time. So the cost array is non-decreasing, and
    /// "keep the largest prefix within the budget" is a threshold on a non-decreasing sequence: a larger budget
    /// can only keep MORE stops. And because the stops are processed in the prefix-stable gather order, the cost
    /// of the first K stops depends only on those K — extending the candidate set (more Amount / allow-unmarked)
    /// never reshuffles or drops an existing prefix. Euclidean-only (no Verse, no pathfinding) → unit-testable.
    /// </summary>
    public static class RouteBudget
    {
        // NOTE: production (RoutePlanner.ComputeLegs) builds a distance MATRIX and uses the matrix variants
        // (GreedyPlanMatrix / PrefixRouteCostMatrix) so the same code path serves both the straight-line and the
        // walking-path distance basis. The coordinate variants below (PrefixRouteCosts / GreedyPlan) are the
        // reference implementation — kept for the unit tests, including the oracle that pins GreedyPlanMatrix on a
        // Euclidean matrix to GreedyPlan. They are equivalent to the matrix variants on a Euclidean matrix.

        /// <summary>
        /// cost[k] = the route's length BEYOND the must-include base, for the cheapest-insertion open path from
        /// the pawn through the first (k+1) stops (in gather order), k = 0 .. stops-1. The first
        /// <paramref name="forcedCount"/> stops are the MUST-INCLUDE set (the clicked anchor plus any targets the
        /// player explicitly picked); their insertion deltas — and the pawn's approach to them — are NOT charged
        /// to "Max travel". The pawn is going to those no matter what; the budget measures how far it then ROAMS
        /// to gather the rest. So cost[k] = 0 for k &lt; forcedCount, then accrues the auto stops' deltas. This
        /// matches the feature's "span" semantics, keeps must-include targets un-trimmable, and avoids over-
        /// trimming a near patch when the pawn is far away. Non-decreasing in k (each charged insertion adds a
        /// non-negative delta; the un-charged forced prefix stays at 0). A running max guards the contract should
        /// the insertion heuristic ever be swapped for one that isn't self-monotone.
        /// </summary>
        public static float[] PrefixRouteCosts(float pawnX, float pawnZ, IReadOnlyList<int> stopXs, IReadOnlyList<int> stopZs, int forcedCount = 1)
        {
            int n = stopXs == null ? 0 : stopXs.Count;
            var cost = new float[n];
            if (n == 0)
                return cost;
            if (forcedCount < 1) forcedCount = 1;      // always free the anchor's own approach (the first edge)
            if (forcedCount > n) forcedCount = n;       // all stops forced → every cost stays 0 (nothing trims them)

            // Open path as parallel point lists; index 0 is the fixed pawn start.
            var px = new List<float>(n + 1) { pawnX };
            var pz = new List<float>(n + 1) { pawnZ };
            float roam = 0f; // length beyond the must-include base (forced stops' deltas are not counted)
            for (int k = 0; k < n; k++)
            {
                CheapestInsertion(px, pz, stopXs[k], stopZs[k], out float bestDelta, out int bestPos);
                px.Insert(bestPos, stopXs[k]);
                pz.Insert(bestPos, stopZs[k]);
                if (k >= forcedCount)
                    roam += bestDelta > 0f ? bestDelta : 0f; // delta is non-negative in theory; clamp fp noise
                cost[k] = roam; // the first forcedCount stops (must-include base) cost 0 — never trimmed
            }

            for (int k = 1; k < n; k++)
                if (cost[k] < cost[k - 1])
                    cost[k] = cost[k - 1];
            return cost;
        }

        /// <summary>
        /// The largest prefix length K (1 .. <paramref name="cost"/>.Count) whose route cost stays within
        /// <paramref name="budget"/>, but never fewer than <paramref name="minKeep"/> (the must-include count —
        /// those stops are forced in regardless of the budget). <paramref name="cost"/> must be non-decreasing
        /// (as produced by <see cref="PrefixRouteCosts"/>, whose first minKeep entries are 0 anyway). A
        /// non-positive or infinite budget means "no limit" (the full count). Always returns at least 1 when
        /// there is any stop — you clicked the anchor, so it is always kept.
        /// </summary>
        public static int LargestPrefixWithin(IReadOnlyList<float> cost, float budget, int minKeep = 1)
        {
            int n = cost == null ? 0 : cost.Count;
            if (n == 0)
                return 0;
            if (budget <= 0f || float.IsPositiveInfinity(budget))
                return n;
            int kept = 0;
            for (int k = 0; k < n; k++)
            {
                if (cost[k] <= budget)
                    kept = k + 1;
                else
                    break; // non-decreasing: once over budget, every later prefix is too
            }
            if (kept < minKeep) kept = minKeep; // force the must-include stops in even if they exceed the budget
            if (kept < 1) kept = 1;             // and always at least the clicked anchor
            if (kept > n) kept = n;
            return kept;
        }

        /// <summary>
        /// GREEDY cheapest-insertion plan — the "most stops for the least travel" selector. Starting from the
        /// pawn, it first weaves in the must-include base (the first <paramref name="forcedCount"/> stops, kept
        /// free), then repeatedly adds whichever REMAINING stop increases the route length the LEAST. Returns the
        /// add-<paramref name="order"/> (a permutation: forced first, then auto cheapest-first) and the cumulative
        /// route length BEYOND the forced base after each add (<paramref name="cost"/>[k] = 0 while adding forced).
        ///
        /// This replaces "keep the stops nearest the clicked anchor" — which kept a stop merely NEAR the anchor
        /// even when it was a long DETOUR off the route, while dropping a stop that was farther from the anchor but
        /// cheap to weave in. Feeding <paramref name="order"/> as the gather order + <paramref name="cost"/> as the
        /// budget makes the Max-travel trim pick the stops that maximise coverage per cell walked. <c>cost</c> is
        /// non-decreasing by construction (each add's delta ≥ 0 under the triangle inequality), so the budget trim
        /// stays MONOTONE. (Orienteering is NP-hard; greedy cheapest-insertion is the standard, near-optimal
        /// heuristic — and far better than anchor-distance.) Euclidean-only → no Verse, unit-testable.
        /// </summary>
        public static void GreedyPlan(float pawnX, float pawnZ, IReadOnlyList<int> stopXs, IReadOnlyList<int> stopZs,
            int forcedCount, out int[] order, out float[] cost)
        {
            int n = stopXs == null ? 0 : stopXs.Count;
            order = new int[n];
            cost = new float[n];
            if (n == 0)
                return;
            if (forcedCount < 1) forcedCount = 1;
            if (forcedCount > n) forcedCount = n;

            var px = new List<float>(n + 1) { pawnX };
            var pz = new List<float>(n + 1) { pawnZ };
            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++)
                remaining.Add(i);

            float roam = 0f;
            for (int added = 0; added < n; added++)
            {
                // Add ALL must-include stops first (cheapest among themselves, free), then the auto stops cheapest-
                // first. The phase filter keeps forced and auto from mixing so forced always lead the order.
                bool forcedPhase = added < forcedCount;
                int bestR = -1, bestPos = -1;
                float bestDelta = float.PositiveInfinity;
                for (int r = 0; r < remaining.Count; r++)
                {
                    int s = remaining[r];
                    if (forcedPhase != (s < forcedCount))
                        continue; // forced phase → only forced stops; auto phase → only auto stops
                    CheapestInsertion(px, pz, stopXs[s], stopZs[s], out float delta, out int pos);
                    if (delta < bestDelta) { bestDelta = delta; bestR = r; bestPos = pos; }
                }
                if (bestR < 0) // shouldn't happen (the phase always has a candidate); be safe
                {
                    bestR = 0; bestPos = px.Count; bestDelta = 0f;
                }
                int chosen = remaining[bestR];
                px.Insert(bestPos, stopXs[chosen]);
                pz.Insert(bestPos, stopZs[chosen]);
                remaining.RemoveAt(bestR);
                if (chosen >= forcedCount)
                    roam += bestDelta > 0f ? bestDelta : 0f;
                order[added] = chosen;
                cost[added] = roam; // 0 while adding the forced base
            }

            for (int k = 1; k < n; k++)
                if (cost[k] < cost[k - 1])
                    cost[k] = cost[k - 1];
        }

        /// <summary>
        /// The same greedy cheapest-insertion plan as <see cref="GreedyPlan"/>, but over an explicit distance
        /// MATRIX instead of Euclidean coordinates — used for the "walking path" distance basis, where
        /// <paramref name="dist"/>[a,b] is the real pathfound distance (index 0 = pawn, 1..n = the n stops). So a
        /// target across a river is treated as far, not near. Pathfound distances satisfy the triangle inequality,
        /// so the insertion deltas stay ≥ 0 and <paramref name="cost"/> stays monotone (same contract as GreedyPlan).
        /// <paramref name="order"/>/<paramref name="cost"/> are indexed over the n stops (stop s ↔ matrix index s+1).
        /// </summary>
        public static void GreedyPlanMatrix(float[,] dist, int forcedCount, out int[] order, out float[] cost)
            => GreedyPlanMatrixWithEnd(dist, forcedCount, -1, out order, out cost);

        /// <summary>
        /// The greedy cheapest-insertion plan with an optional FIXED END node — the "circle back toward storage"
        /// selector for smart routing. When <paramref name="endIdx"/> is &lt; 0 this is exactly
        /// <see cref="GreedyPlanMatrix"/>: an OPEN path from the pawn (free end). When <paramref name="endIdx"/> is
        /// a matrix index (the storage cell, placed last in the matrix), the route skeleton is the pawn → … →
        /// storage path, and each stop is inserted at its cheapest BETWEEN position — so a stop sitting on the way
        /// back to storage has a near-ZERO insertion detour and is selected FIRST, instead of looking far/expensive
        /// the way it does on an open pawn→stops path. The cost then measures the DETOUR beyond the haul-to-storage
        /// trip (the base pawn→storage edge is never charged), which is monotone (each detour ≥ 0) and far-storage-
        /// safe (a distant stockpile only sets the baseline, it can't blow the budget). <paramref name="order"/>/
        /// <paramref name="cost"/> are indexed over the n stops (stop s ↔ matrix index s+1; endIdx = n+1).
        /// </summary>
        public static void GreedyPlanMatrixWithEnd(float[,] dist, int forcedCount, int endIdx, out int[] order, out float[] cost)
        {
            int n = endIdx >= 0 ? endIdx - 1 : (dist == null ? 1 : dist.GetLength(0)) - 1; // stops; matrix index 0 = pawn
            if (n < 0) n = 0;
            order = new int[n];
            cost = new float[n];
            if (n == 0)
                return;
            if (forcedCount < 1) forcedCount = 1;
            if (forcedCount > n) forcedCount = n;

            bool allowAppend = endIdx < 0;
            // Route skeleton (matrix indices visited so far): open paths start at the pawn (0); fixed-end paths
            // start [pawn(0), storage(endIdx)] so storage always stays last and stops weave in on the way there.
            var route = endIdx >= 0 ? new List<int>(n + 2) { 0, endIdx } : new List<int>(n + 1) { 0 };
            var remaining = new List<int>(n);
            for (int s = 0; s < n; s++)
                remaining.Add(s); // stop indices 0..n-1

            float roam = 0f;
            for (int added = 0; added < n; added++)
            {
                bool forcedPhase = added < forcedCount;
                int bestR = -1, bestPos = -1;
                float bestDelta = float.PositiveInfinity;
                for (int r = 0; r < remaining.Count; r++)
                {
                    int s = remaining[r];
                    if (forcedPhase != (s < forcedCount))
                        continue;
                    int cand = s + 1; // matrix index of this stop
                    CheapestInsertionMatrix(dist, route, cand, allowAppend, out float delta, out int pos);
                    if (delta < bestDelta) { bestDelta = delta; bestR = r; bestPos = pos; }
                }
                if (bestR < 0) // unreachable (the phase always has a candidate); recompute a valid position to be safe
                {
                    bestR = 0;
                    CheapestInsertionMatrix(dist, route, remaining[0] + 1, allowAppend, out bestDelta, out bestPos);
                }
                int chosen = remaining[bestR];
                route.Insert(bestPos, chosen + 1);
                remaining.RemoveAt(bestR);
                if (chosen >= forcedCount)
                    roam += bestDelta > 0f ? bestDelta : 0f;
                order[added] = chosen;
                cost[added] = roam;
            }

            for (int k = 1; k < n; k++)
                if (cost[k] < cost[k - 1])
                    cost[k] = cost[k - 1];
        }

        /// <summary>
        /// Budget cost for a FIXED gather <paramref name="order"/> (e.g. the "nearest" selection), using the
        /// distance MATRIX — inserts each stop in the given order at its cheapest matrix position and accrues the
        /// added length beyond the forced base. The matrix-distance analogue of <see cref="PrefixRouteCosts"/>.
        /// </summary>
        public static void PrefixRouteCostMatrix(float[,] dist, int[] order, int forcedCount, out float[] cost)
            => PrefixRouteCostMatrixWithEnd(dist, order, forcedCount, -1, out cost);

        /// <summary>
        /// Budget cost for a FIXED gather <paramref name="order"/> with an optional FIXED END node — the matrix
        /// analogue of <see cref="GreedyPlanMatrixWithEnd"/> for the "nearest" selection method. When
        /// <paramref name="endIdx"/> &lt; 0 it is <see cref="PrefixRouteCostMatrix"/> (open path); when it is the
        /// storage matrix index, each stop is spliced into the pawn → … → storage path (detour cost), so the
        /// "nearest" method also values way-back stops under smart routing.
        /// </summary>
        public static void PrefixRouteCostMatrixWithEnd(float[,] dist, int[] order, int forcedCount, int endIdx, out float[] cost)
        {
            int n = order == null ? 0 : order.Length;
            cost = new float[n];
            if (n == 0)
                return;
            if (forcedCount < 1) forcedCount = 1;
            if (forcedCount > n) forcedCount = n;
            bool allowAppend = endIdx < 0;
            var route = endIdx >= 0 ? new List<int>(n + 2) { 0, endIdx } : new List<int>(n + 1) { 0 };
            float roam = 0f;
            for (int k = 0; k < n; k++)
            {
                int cand = order[k] + 1;
                CheapestInsertionMatrix(dist, route, cand, allowAppend, out float delta, out int pos);
                route.Insert(pos, cand);
                if (k >= forcedCount)
                    roam += delta > 0f ? delta : 0f;
                cost[k] = roam;
            }
            for (int k = 1; k < n; k++)
                if (cost[k] < cost[k - 1])
                    cost[k] = cost[k - 1];
        }

        // Cheapest place to splice `cand` into `route` (matrix indices). When <paramref name="allowAppend"/> the
        // route is OPEN (free end) and `cand` may be appended after the last node (delta = the single edge to it);
        // when false the route has a FIXED END (storage) that must stay last, so `cand` may only be inserted
        // BETWEEN two existing nodes (the detour delta) — never after the end. The fixed-end route always carries
        // ≥ 2 nodes ([pawn, end]), so the between-loop always yields a valid position.
        private static void CheapestInsertionMatrix(float[,] dist, List<int> route, int cand, bool allowAppend, out float bestDelta, out int bestPos)
        {
            bestDelta = float.PositiveInfinity;
            bestPos = allowAppend ? route.Count : -1;
            for (int i = 0; i < route.Count - 1; i++)
            {
                float delta = dist[route[i], cand] + dist[cand, route[i + 1]] - dist[route[i], route[i + 1]];
                if (delta < bestDelta) { bestDelta = delta; bestPos = i + 1; }
            }
            if (allowAppend)
            {
                float appendDelta = dist[route[route.Count - 1], cand];
                if (appendDelta < bestDelta) { bestDelta = appendDelta; bestPos = route.Count; }
            }
        }

        // Cheapest place to splice a new point into the open path px/pz: between two consecutive points (the
        // detour delta) or appended at the end. Returns the added length (≥ 0) and the insert index.
        private static void CheapestInsertion(List<float> px, List<float> pz, float nx, float nz, out float bestDelta, out int bestPos)
        {
            bestDelta = float.PositiveInfinity;
            bestPos = px.Count; // default: append after the last point
            for (int i = 0; i < px.Count - 1; i++)
            {
                float delta = Dist(px[i], pz[i], nx, nz)
                              + Dist(nx, nz, px[i + 1], pz[i + 1])
                              - Dist(px[i], pz[i], px[i + 1], pz[i + 1]);
                if (delta < bestDelta) { bestDelta = delta; bestPos = i + 1; }
            }
            float appendDelta = Dist(px[px.Count - 1], pz[pz.Count - 1], nx, nz);
            if (appendDelta < bestDelta) { bestDelta = appendDelta; bestPos = px.Count; }
        }

        private static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
