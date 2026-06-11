using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure route ordering for the "plan prioritized route" feature. Given a cost matrix over nodes — the
    /// pawn (a fixed start), the work stops, and optionally a storage anchor (a fixed end) — returns the
    /// order to visit the stops that minimizes total travel. Smart routing passes a real storage end so the
    /// route minimizes the WHOLE trip including the haul-back (it "circles back" toward storage); a free end
    /// (-1) orders the stops alone (shortest harvest-only path). Exact (Held-Karp DP) for small routes,
    /// nearest-neighbour + 2-opt above the exact threshold. No Verse types — unit-tested on abstract matrices.
    /// </summary>
    public static class RouteOrderPolicy
    {
        /// <summary>Default: at/under this many stops, the order is the exact optimum (Held-Karp); above it, a heuristic.</summary>
        public const int ExactMax = 12;

        /// <summary>
        /// Order the interior stops of an open path. <paramref name="d"/> is an N×N cost matrix;
        /// <paramref name="start"/> is the fixed first node (the pawn); <paramref name="end"/> is the fixed
        /// last node (storage) or -1 for no fixed end. Returns every index except start (and except end when
        /// end&gt;=0), in visiting order. <paramref name="exactMax"/> is the stop count at/under which the order is
        /// solved EXACTLY (Held-Karp, O(2^k·k²) — keep it modest); above it, a nearest-neighbour + 2-opt heuristic.
        /// </summary>
        public static int[] Order(float[,] d, int start, int end, int exactMax = ExactMax)
        {
            if (exactMax < 1) exactMax = 1;
            int n = d.GetLength(0);
            var stops = new List<int>();
            for (int i = 0; i < n; i++)
                if (i != start && i != end)
                    stops.Add(i);
            if (stops.Count <= 1)
                return stops.ToArray();
            return stops.Count <= exactMax ? ExactOrder(d, start, end, stops) : HeuristicOrder(d, start, end, stops);
        }

        /// <summary>
        /// Order the STOP indices (0..s-1) for the route pawn → [stops] → (storage), optionally PINNING a start
        /// stop (visited first, right after the pawn) and/or an end stop (visited last, right before storage). The
        /// interior stops are ordered optimally between the effective endpoints (the pinned start, else the pawn;
        /// and the pinned end, else storage when present, else a free end). Euclidean coordinates; pure → unit-
        /// testable. <paramref name="startStop"/>/<paramref name="endStop"/> are stop indices or -1 (out-of-range
        /// is treated as unpinned); a stop can't be both (endStop is ignored when it equals startStop). With both
        /// unpinned this is the plain pawn → [TSP] → storage order.
        /// </summary>
        public static int[] OrderStops(int pawnX, int pawnZ, IReadOnlyList<int> stopXs, IReadOnlyList<int> stopZs,
            bool hasStorage, int storageX, int storageZ, int startStop, int endStop, int exactMax = ExactMax)
        {
            int s = stopXs == null ? 0 : stopXs.Count;
            var result = new int[s];
            for (int i = 0; i < s; i++) result[i] = i;
            if (s <= 1)
                return result;
            if (startStop < 0 || startStop >= s) startStop = -1;
            if (endStop < 0 || endStop >= s || endStop == startStop) endStop = -1;

            // Interior = the stops the TSP is free to order (everything except the pinned start/end).
            var interior = new List<int>(s);
            for (int i = 0; i < s; i++)
                if (i != startStop && i != endStop)
                    interior.Add(i);
            int m = interior.Count;

            // Node positions for the open path: index 0 = effective start (the start pin, else the pawn); 1..m =
            // interior stops; trailing index = effective end (the end pin, else storage) the path must reach.
            bool hasEffEnd = endStop >= 0 || hasStorage;
            int n = 1 + m + (hasEffEnd ? 1 : 0);
            var xs = new int[n];
            var zs = new int[n];
            xs[0] = startStop >= 0 ? stopXs[startStop] : pawnX;
            zs[0] = startStop >= 0 ? stopZs[startStop] : pawnZ;
            for (int i = 0; i < m; i++) { xs[1 + i] = stopXs[interior[i]]; zs[1 + i] = stopZs[interior[i]]; }
            int effEndIdx = -1;
            if (hasEffEnd)
            {
                effEndIdx = 1 + m;
                xs[effEndIdx] = endStop >= 0 ? stopXs[endStop] : storageX;
                zs[effEndIdx] = endStop >= 0 ? stopZs[endStop] : storageZ;
            }

            var d = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float dx = xs[i] - xs[j], dz = zs[i] - zs[j];
                    d[i, j] = (float)System.Math.Sqrt(dx * dx + dz * dz);
                }

            int[] order = Order(d, start: 0, end: effEndIdx, exactMax: exactMax);

            // Assemble: pinned start first (if any), then the ordered interior, then the pinned end (if any).
            int w = 0;
            if (startStop >= 0) result[w++] = startStop;
            for (int i = 0; i < order.Length; i++)
            {
                int ii = order[i] - 1; // matrix index → interior index
                if (ii >= 0 && ii < m) result[w++] = interior[ii];
            }
            if (endStop >= 0 && w < s) result[w++] = endStop;
            // Safety net: if anything was dropped (shouldn't happen), append the missing stops in index order.
            if (w < s)
            {
                var seen = new bool[s];
                for (int i = 0; i < w; i++) seen[result[i]] = true;
                for (int i = 0; i < s && w < s; i++) if (!seen[i]) result[w++] = i;
            }
            return result;
        }

        // Held-Karp open-path DP: min cost start -> (all stops, some order) -> end (end<0 = free end).
        private static int[] ExactOrder(float[,] d, int start, int end, List<int> stops)
        {
            int k = stops.Count;
            int full = 1 << k;
            var dp = new float[full, k];
            var par = new int[full, k];
            for (int m = 0; m < full; m++)
                for (int j = 0; j < k; j++) { dp[m, j] = float.PositiveInfinity; par[m, j] = -1; }
            for (int j = 0; j < k; j++)
                dp[1 << j, j] = d[start, stops[j]];

            for (int m = 0; m < full; m++)
                for (int j = 0; j < k; j++)
                {
                    if ((m & (1 << j)) == 0 || float.IsPositiveInfinity(dp[m, j]))
                        continue;
                    float baseCost = dp[m, j];
                    for (int p = 0; p < k; p++)
                    {
                        if ((m & (1 << p)) != 0)
                            continue;
                        int nm = m | (1 << p);
                        float nc = baseCost + d[stops[j], stops[p]];
                        if (nc < dp[nm, p]) { dp[nm, p] = nc; par[nm, p] = j; }
                    }
                }

            int fullMask = full - 1;
            int last = -1;
            float best = float.PositiveInfinity;
            for (int j = 0; j < k; j++)
            {
                if (float.IsPositiveInfinity(dp[fullMask, j]))
                    continue;
                float total = dp[fullMask, j] + (end >= 0 ? d[stops[j], end] : 0f);
                if (total < best) { best = total; last = j; }
            }
            if (last < 0)
                return stops.ToArray(); // all-infinite (shouldn't happen) -> arbitrary order

            var order = new int[k];
            int mask = fullMask, cur = last;
            for (int idx = k - 1; idx >= 0; idx--)
            {
                order[idx] = stops[cur];
                int prev = par[mask, cur];
                mask &= ~(1 << cur);
                cur = prev;
            }
            return order;
        }

        // Nearest-neighbour from start, then 2-opt. Symmetric-D heuristic for big routes.
        private static int[] HeuristicOrder(float[,] d, int start, int end, List<int> stops)
        {
            var remaining = new List<int>(stops);
            var tour = new List<int>(stops.Count);
            int cur = start;
            while (remaining.Count > 0)
            {
                int bi = 0;
                float bd = float.PositiveInfinity;
                for (int i = 0; i < remaining.Count; i++)
                {
                    float dist = d[cur, remaining[i]];
                    if (dist < bd) { bd = dist; bi = i; }
                }
                cur = remaining[bi];
                tour.Add(cur);
                remaining.RemoveAt(bi);
            }
            TwoOpt(d, start, end, tour);
            return tour.ToArray();
        }

        private static void TwoOpt(float[,] d, int start, int end, List<int> tour)
        {
            int n = tour.Count;
            bool improved = true;
            int guard = 0;
            while (improved && guard++ < 2000)
            {
                improved = false;
                for (int i = 0; i < n - 1; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        int a = (i == 0) ? start : tour[i - 1];
                        int b = tour[i];
                        int c = tour[j];
                        int e = (j == n - 1) ? (end >= 0 ? end : -1) : tour[j + 1];
                        float before = d[a, b] + (e >= 0 ? d[c, e] : 0f);
                        float after = d[a, c] + (e >= 0 ? d[b, e] : 0f);
                        if (after + 1e-4f < before)
                        {
                            tour.Reverse(i, j - i + 1);
                            improved = true;
                        }
                    }
            }
        }
    }
}
