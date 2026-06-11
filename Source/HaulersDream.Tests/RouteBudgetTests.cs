using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the Max-travel budget's two sacred properties — MONOTONICITY (a larger budget never keeps fewer
    /// stops; extending the candidate set never reshuffles an existing prefix) and that it measures a REAL route
    /// length, not the old distance-rank zigzag that over-counted and trimmed obvious nearby targets.
    /// </summary>
    [TestFixture]
    public class RouteBudgetTests
    {
        private static float[] Costs(int px, int pz, params (int x, int z)[] stops)
        {
            var xs = new List<int>();
            var zs = new List<int>();
            foreach (var s in stops) { xs.Add(s.x); zs.Add(s.z); }
            return RouteBudget.PrefixRouteCosts(px, pz, xs, zs);
        }

        [Test]
        public void PrefixRouteCosts_IsMonotoneNonDecreasing_AcrossExactAndHeuristicSizes()
        {
            var rng = new Random(12345);
            for (int trial = 0; trial < 500; trial++)
            {
                int n = 1 + rng.Next(30); // up to 30 stops — well past the exact/heuristic TSP boundary
                int px = rng.Next(-100, 100), pz = rng.Next(-100, 100);
                var xs = new List<int>(n);
                var zs = new List<int>(n);
                for (int i = 0; i < n; i++) { xs.Add(rng.Next(-100, 100)); zs.Add(rng.Next(-100, 100)); }
                var cost = RouteBudget.PrefixRouteCosts(px, pz, xs, zs);
                Assert.That(cost.Length, Is.EqualTo(n));
                for (int k = 1; k < n; k++)
                    Assert.That(cost[k], Is.GreaterThanOrEqualTo(cost[k - 1] - 1e-3f),
                        $"trial {trial}: cost must be non-decreasing, broke at k={k} ({cost[k - 1]} -> {cost[k]})");
            }
        }

        [Test]
        public void PrefixRouteCosts_PrefixStable_WhenCandidateSetGrows()
        {
            // The route gathers stops in a prefix-stable order, so the cost of the first N stops must be IDENTICAL
            // whether the candidate set has N or N+k stops — otherwise raising Amount / allowing unmarked could
            // reshuffle the budget and drop a stop (the historical non-monotonicity bug).
            var rng = new Random(777);
            for (int trial = 0; trial < 200; trial++)
            {
                int baseN = 1 + rng.Next(15);
                int extra = rng.Next(10);
                int px = rng.Next(-50, 50), pz = rng.Next(-50, 50);
                var xs = new List<int>();
                var zs = new List<int>();
                for (int i = 0; i < baseN + extra; i++) { xs.Add(rng.Next(-50, 50)); zs.Add(rng.Next(-50, 50)); }

                var small = RouteBudget.PrefixRouteCosts(px, pz, xs.GetRange(0, baseN), zs.GetRange(0, baseN));
                var big = RouteBudget.PrefixRouteCosts(px, pz, xs, zs);
                for (int k = 0; k < baseN; k++)
                    Assert.That(big[k], Is.EqualTo(small[k]).Within(1e-3f),
                        $"trial {trial}: prefix cost diverged at k={k} when the set grew by {extra}");
            }
        }

        [Test]
        public void LargestPrefixWithin_IsMonotoneInBudget()
        {
            var cost = new float[] { 5f, 12f, 12f, 30f, 55f }; // a non-decreasing cost curve
            // Sweep POSITIVE finite budgets (the slider min is 20; budget<=0 is the separate "no limit" sentinel).
            int prev = 0;
            for (float b = 1f; b <= 80f; b += 1f)
            {
                int kept = RouteBudget.LargestPrefixWithin(cost, b);
                Assert.That(kept, Is.GreaterThanOrEqualTo(prev), $"raising the budget to {b} dropped a stop ({prev} -> {kept})");
                prev = kept;
            }
            Assert.That(RouteBudget.LargestPrefixWithin(cost, 80f), Is.EqualTo(5), "a generous budget keeps all stops");
        }

        [Test]
        public void LargestPrefixWithin_FloorsToOne_EvenWhenFirstStopExceedsBudget()
        {
            var cost = new float[] { 40f, 80f }; // even the anchor's approach exceeds a tight budget
            Assert.That(RouteBudget.LargestPrefixWithin(cost, 10f), Is.EqualTo(1), "you clicked the anchor — always keep it");
        }

        [Test]
        public void LargestPrefixWithin_NoLimitAndEmpty()
        {
            var cost = new float[] { 5f, 12f, 30f };
            Assert.That(RouteBudget.LargestPrefixWithin(cost, float.PositiveInfinity), Is.EqualTo(3), "+inf = no limit");
            Assert.That(RouteBudget.LargestPrefixWithin(cost, 0f), Is.EqualTo(3), "non-positive = no limit");
            Assert.That(RouteBudget.LargestPrefixWithin(new float[0], 50f), Is.EqualTo(0), "no stops -> keep 0");
            Assert.That(RouteBudget.LargestPrefixWithin(null, 50f), Is.EqualTo(0));
        }

        [Test]
        public void DensePatch_FitsWellUnderASmallBudget()
        {
            // The screenshot scenario: ~16 bushes packed into a ~10x10 patch, pawn just outside it. The real route
            // through ALL of them is a few tens of cells — so a 496-cell "Max travel" must keep every one (the old
            // zigzag span wrongly trimmed it). Build a 4x4 grid of bushes 3 cells apart.
            var stops = new List<(int x, int z)>();
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    stops.Add((100 + i * 3, 100 + j * 3));
            var cost = Costs(96, 100, stops.ToArray());

            Assert.That(cost.Length, Is.EqualTo(16));
            Assert.That(cost[15], Is.LessThan(120f), "a tight 4x4 patch's real route is short");
            Assert.That(RouteBudget.LargestPrefixWithin(cost, 496f), Is.EqualTo(16), "496 cells must cover the whole small patch");
        }

        [Test]
        public void ForcedPrefix_CostsZero_AndIsNeverTrimmed()
        {
            // 3 must-include picks (forcedCount=3) followed by 4 auto stops. The forced prefix is "free" (cost 0)
            // so it's never trimmed, and the budget only limits how far the AUTO stops roam beyond it.
            var stops = new List<(int x, int z)>
            {
                (100, 100), (140, 100), (100, 140),     // 3 must-include (spread out — would be expensive)
                (101, 101), (102, 101), (103, 101), (104, 101), // 4 nearby auto stops
            };
            var xs = new List<int>(); var zs = new List<int>();
            foreach (var s in stops) { xs.Add(s.x); zs.Add(s.z); }
            var cost = RouteBudget.PrefixRouteCosts(0, 0, xs, zs, forcedCount: 3);

            Assert.That(cost[0], Is.EqualTo(0f), "forced stop 0 is free");
            Assert.That(cost[1], Is.EqualTo(0f), "forced stop 1 is free");
            Assert.That(cost[2], Is.EqualTo(0f), "forced stop 2 is free");
            Assert.That(cost[3], Is.GreaterThanOrEqualTo(0f));
            // Even a tiny budget keeps all 3 forced stops (minKeep), plus whatever auto stops fit.
            Assert.That(RouteBudget.LargestPrefixWithin(cost, 1f, minKeep: 3), Is.GreaterThanOrEqualTo(3),
                "the 3 must-include stops are kept regardless of how small the budget is");
            // A generous budget keeps all 7.
            Assert.That(RouteBudget.LargestPrefixWithin(cost, 500f, minKeep: 3), Is.EqualTo(7));
        }

        [Test]
        public void LargestPrefixWithin_FloorsToMinKeep_AndStaysMonotone()
        {
            var cost = new float[] { 0f, 0f, 5f, 40f }; // forcedCount=2 (first two free), then auto
            // Below the first auto cost, kept floors to minKeep=2 (the forced stops), never less.
            Assert.That(RouteBudget.LargestPrefixWithin(cost, 1f, minKeep: 2), Is.EqualTo(2));
            Assert.That(RouteBudget.LargestPrefixWithin(cost, 5f, minKeep: 2), Is.EqualTo(3));
            Assert.That(RouteBudget.LargestPrefixWithin(cost, 40f, minKeep: 2), Is.EqualTo(4));
            // Monotone in budget with a fixed minKeep.
            int prev = 0;
            for (float b = 1f; b <= 60f; b += 1f)
            {
                int kept = RouteBudget.LargestPrefixWithin(cost, b, minKeep: 2);
                Assert.That(kept, Is.GreaterThanOrEqualTo(prev));
                prev = kept;
            }
        }

        [Test]
        public void PrefixRouteCosts_ForcedCount_StillMonotoneAndPrefixStable()
        {
            var rng = new Random(909);
            for (int trial = 0; trial < 200; trial++)
            {
                int n = 2 + rng.Next(20);
                int forced = 1 + rng.Next(System.Math.Min(n, 5));
                var xs = new List<int>(); var zs = new List<int>();
                for (int i = 0; i < n; i++) { xs.Add(rng.Next(-60, 60)); zs.Add(rng.Next(-60, 60)); }
                var cost = RouteBudget.PrefixRouteCosts(rng.Next(-60, 60), rng.Next(-60, 60), xs, zs, forced);
                for (int k = 0; k < forced; k++)
                    Assert.That(cost[k], Is.EqualTo(0f), $"trial {trial}: forced prefix must be free at k={k}");
                for (int k = 1; k < n; k++)
                    Assert.That(cost[k], Is.GreaterThanOrEqualTo(cost[k - 1] - 1e-3f), $"trial {trial}: non-monotone at k={k}");
            }
        }

        [Test]
        public void GreedyPlan_ForcedLeadAndCostMonotone()
        {
            var rng = new Random(2024);
            for (int trial = 0; trial < 300; trial++)
            {
                int n = 2 + rng.Next(20);
                int forced = 1 + rng.Next(System.Math.Min(n, 4));
                var xs = new List<int>(); var zs = new List<int>();
                for (int i = 0; i < n; i++) { xs.Add(rng.Next(-80, 80)); zs.Add(rng.Next(-80, 80)); }
                RouteBudget.GreedyPlan(rng.Next(-80, 80), rng.Next(-80, 80), xs, zs, forced, out int[] order, out float[] cost);

                // order is a permutation of [0..n)
                var seen = new bool[n];
                Assert.That(order.Length, Is.EqualTo(n));
                foreach (int idx in order) { Assert.That(idx, Is.InRange(0, n - 1)); Assert.That(seen[idx], Is.False); seen[idx] = true; }
                // the forced stops (indices < forced) lead the order
                for (int k = 0; k < forced; k++)
                    Assert.That(order[k], Is.LessThan(forced), $"trial {trial}: forced stop must lead at k={k}");
                // cost: 0 for the forced prefix, non-decreasing after
                for (int k = 0; k < forced; k++)
                    Assert.That(cost[k], Is.EqualTo(0f), $"trial {trial}: forced cost must be 0 at k={k}");
                for (int k = 1; k < n; k++)
                    Assert.That(cost[k], Is.GreaterThanOrEqualTo(cost[k - 1] - 1e-3f), $"trial {trial}: non-monotone at k={k}");
            }
        }

        [Test]
        public void GreedyPlan_FitsMoreStopsThanAnchorDistance_ForTheSameBudget()
        {
            // The "most stops for the least travel" case. Pawn at origin; the anchor (index 0, forced) is far
            // south. Stop Y sits ON THE WAY from the pawn to the anchor (≈free to insert); X1/X2 cluster up at
            // the anchor. The OLD order ranks by distance-to-ANCHOR → [anchor, X1, X2, Y] (Y last, it's far from
            // the anchor) and under a tight budget keeps only the anchor cluster, dropping the nearly-free Y.
            // GREEDY adds Y first (cheapest insertion), so the same budget fits MORE stops.
            var greedyXs = new List<int> { 0, 0, 5, -5 };   // index 0 = anchor (forced)
            var greedyZs = new List<int> { 100, 50, 100, 100 };
            RouteBudget.GreedyPlan(0, 0, greedyXs, greedyZs, 1, out int[] _, out float[] greedyCost);

            // The old anchor-distance order for the same points: anchor, X1(d=5), X2(d=5), Y(d=50).
            var oldXs = new List<int> { 0, 5, -5, 0 };
            var oldZs = new List<int> { 100, 100, 100, 50 };
            var oldCost = RouteBudget.PrefixRouteCosts(0, 0, oldXs, oldZs, 1);

            const float budget = 8f;
            int greedyKept = RouteBudget.LargestPrefixWithin(greedyCost, budget, 1);
            int oldKept = RouteBudget.LargestPrefixWithin(oldCost, budget, 1);
            Assert.That(greedyKept, Is.GreaterThan(oldKept),
                $"greedy should fit more stops (greedy={greedyKept}, anchor-distance={oldKept}) for budget {budget}");
        }

        [Test]
        public void GreedyPlan_BudgetTrimIsMonotoneInTheBudget()
        {
            var rng = new Random(5151);
            for (int trial = 0; trial < 100; trial++)
            {
                int n = 4 + rng.Next(14);
                var xs = new List<int>(); var zs = new List<int>();
                for (int i = 0; i < n; i++) { xs.Add(rng.Next(-60, 60)); zs.Add(rng.Next(-60, 60)); }
                RouteBudget.GreedyPlan(rng.Next(-60, 60), rng.Next(-60, 60), xs, zs, 1, out int[] _, out float[] cost);
                int prev = 0;
                for (float b = 1f; b <= 400f; b += 3f)
                {
                    int kept = RouteBudget.LargestPrefixWithin(cost, b, 1);
                    Assert.That(kept, Is.GreaterThanOrEqualTo(prev), $"trial {trial}: budget {b} dropped a stop");
                    prev = kept;
                }
            }
        }

        private static float[,] EuclidMatrix(int px, int pz, IReadOnlyList<int> xs, IReadOnlyList<int> zs)
        {
            int n = xs.Count;
            var mx = new int[n + 1]; var mz = new int[n + 1];
            mx[0] = px; mz[0] = pz;
            for (int i = 0; i < n; i++) { mx[i + 1] = xs[i]; mz[i + 1] = zs[i]; }
            var d = new float[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    float dx = mx[i] - mx[j], dz2 = mz[i] - mz[j];
                    d[i, j] = (float)System.Math.Sqrt(dx * dx + dz2 * dz2);
                }
            return d;
        }

        // [pawn, stops…, storage] Euclidean matrix — storage at the last index (n+1), the fixed END node for the
        // smart-routing "circle back toward storage" selection (endIdx = n+1).
        private static float[,] EuclidMatrixWithStorage(int px, int pz, IReadOnlyList<int> xs, IReadOnlyList<int> zs, int sx, int sz)
        {
            int n = xs.Count;
            var mx = new int[n + 2]; var mz = new int[n + 2];
            mx[0] = px; mz[0] = pz;
            for (int i = 0; i < n; i++) { mx[i + 1] = xs[i]; mz[i + 1] = zs[i]; }
            mx[n + 1] = sx; mz[n + 1] = sz;
            var d = new float[n + 2, n + 2];
            for (int i = 0; i < n + 2; i++)
                for (int j = 0; j < n + 2; j++)
                {
                    float dx = mx[i] - mx[j], dz2 = mz[i] - mz[j];
                    d[i, j] = (float)System.Math.Sqrt(dx * dx + dz2 * dz2);
                }
            return d;
        }

        [Test]
        public void GreedyPlanMatrix_MatchesCoordGreedy_OnAEuclideanMatrix()
        {
            // The matrix greedy (used for the "walking path" distance basis) must reduce to the coordinate greedy
            // when fed a Euclidean matrix — same algorithm, same distances → identical order + cost.
            var rng = new Random(31337);
            for (int trial = 0; trial < 250; trial++)
            {
                int n = 2 + rng.Next(15);
                int forced = 1 + rng.Next(System.Math.Min(n, 4));
                int px = rng.Next(-50, 50), pz = rng.Next(-50, 50);
                var xs = new List<int>(); var zs = new List<int>();
                for (int i = 0; i < n; i++) { xs.Add(rng.Next(-50, 50)); zs.Add(rng.Next(-50, 50)); }

                RouteBudget.GreedyPlan(px, pz, xs, zs, forced, out int[] o1, out float[] c1);
                RouteBudget.GreedyPlanMatrix(EuclidMatrix(px, pz, xs, zs), forced, out int[] o2, out float[] c2);

                Assert.That(o2, Is.EqualTo(o1), $"trial {trial}: order mismatch");
                for (int k = 0; k < n; k++)
                    Assert.That(c2[k], Is.EqualTo(c1[k]).Within(1e-2f), $"trial {trial}: cost mismatch at k={k}");
            }
        }

        [Test]
        public void GreedyPlanMatrix_HandlesAsymmetricPathDistances_StaysForcedFirstAndMonotone()
        {
            // Pathfound distances can be slightly asymmetric (one-way terrain); the greedy must still produce a
            // valid forced-first permutation with non-decreasing cost.
            var rng = new Random(99);
            for (int trial = 0; trial < 150; trial++)
            {
                int n = 2 + rng.Next(12);
                int forced = 1 + rng.Next(System.Math.Min(n, 3));
                var d = new float[n + 1, n + 1];
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n; j++)
                        d[i, j] = i == j ? 0f : 1f + rng.Next(50) + (rng.Next(2) == 0 ? 0.3f : 0f); // mild asymmetry
                RouteBudget.GreedyPlanMatrix(d, forced, out int[] order, out float[] cost);

                var seen = new bool[n];
                foreach (int idx in order) { Assert.That(seen[idx], Is.False); seen[idx] = true; }
                for (int k = 0; k < forced; k++)
                    Assert.That(order[k], Is.LessThan(forced), $"trial {trial}: forced must lead at k={k}");
                for (int k = 1; k < n; k++)
                    Assert.That(cost[k], Is.GreaterThanOrEqualTo(cost[k - 1] - 1e-3f), $"trial {trial}: non-monotone at k={k}");
            }
        }

        [Test]
        public void PrefixRouteCostMatrix_ForcedFreeAndMonotone()
        {
            var rng = new Random(424242);
            for (int trial = 0; trial < 200; trial++)
            {
                int n = 2 + rng.Next(15);
                int forced = 1 + rng.Next(System.Math.Min(n, 4));
                var xs = new List<int>(); var zs = new List<int>();
                for (int i = 0; i < n; i++) { xs.Add(rng.Next(-50, 50)); zs.Add(rng.Next(-50, 50)); }
                var d = EuclidMatrix(rng.Next(-50, 50), rng.Next(-50, 50), xs, zs);

                var order = new int[n];
                for (int i = 0; i < n; i++) order[i] = i; // any fixed order (e.g. the "nearest" ordering)
                RouteBudget.PrefixRouteCostMatrix(d, order, forced, out float[] cost);

                for (int k = 0; k < forced; k++)
                    Assert.That(cost[k], Is.EqualTo(0f), $"trial {trial}: forced cost must be 0 at k={k}");
                for (int k = 1; k < n; k++)
                    Assert.That(cost[k], Is.GreaterThanOrEqualTo(cost[k - 1] - 1e-3f), $"trial {trial}: non-monotone at k={k}");
            }
        }

        [Test]
        public void CheapestInsertion_BeatsTheOldZigzagSpan_OnAnAnchorRankedScatter()
        {
            // Stops gathered in distance-to-anchor RANK order zigzag around the anchor: rank-adjacent stops are on
            // opposite sides. The OLD budget summed the legs between consecutive RANKS (a back-and-forth walk); the
            // new cheapest-insertion route is far shorter. Anchor at origin; alternate +/- offsets growing outward.
            var stops = new (int x, int z)[]
            {
                (0, 0),    // anchor (rank 0)
                (-3, 1),   // rank 1 (left)
                (3, -1),   // rank 2 (right)  -> rank1->rank2 leg crosses the anchor
                (-5, 2),   // rank 3 (left)
                (5, -2),   // rank 4 (right)
                (-7, 1),   // rank 5 (left)
                (7, -1),   // rank 6 (right)
            };

            // OLD metric: cumulative pathless span = sum of consecutive-RANK Euclidean legs (excluding the approach).
            double oldSpan = 0;
            for (int i = 2; i < stops.Length; i++)
            {
                double dx = stops[i].x - stops[i - 1].x, dz = stops[i].z - stops[i - 1].z;
                oldSpan += Math.Sqrt(dx * dx + dz * dz);
            }

            var cost = Costs(0, 0, stops);
            float newRoute = cost[cost.Length - 1];

            Assert.That(newRoute, Is.LessThan((float)oldSpan),
                $"cheapest-insertion route ({newRoute:0.0}) must beat the zigzag span ({oldSpan:0.0})");
        }

        [Test]
        public void GreedyPlanMatrixWithEnd_OpenEnd_MatchesIndependentCoordGreedy()
        {
            // The open-end (endIdx < 0) path must reproduce the non-smart selection. Assert against the INDEPENDENT
            // coordinate reference GreedyPlan (a separately-written implementation), NOT GreedyPlanMatrix — which now
            // just delegates to WithEnd(-1), so comparing the two would be tautological and catch no regression.
            var rng = new Random(7);
            for (int trial = 0; trial < 250; trial++)
            {
                int n = 2 + rng.Next(15);
                int forced = 1 + rng.Next(Math.Min(n, 4));
                int px = rng.Next(-50, 50), pz = rng.Next(-50, 50);
                var xs = new List<int>(); var zs = new List<int>();
                for (int i = 0; i < n; i++) { xs.Add(rng.Next(-50, 50)); zs.Add(rng.Next(-50, 50)); }

                RouteBudget.GreedyPlan(px, pz, xs, zs, forced, out int[] o1, out float[] c1);
                RouteBudget.GreedyPlanMatrixWithEnd(EuclidMatrix(px, pz, xs, zs), forced, -1, out int[] o2, out float[] c2);
                Assert.That(o2, Is.EqualTo(o1), $"trial {trial}: open-end order must match the coordinate greedy");
                for (int k = 0; k < n; k++)
                    Assert.That(c2[k], Is.EqualTo(c1[k]).Within(1e-2f), $"trial {trial}: open-end cost mismatch at k={k}");
            }
        }

        [Test]
        public void GreedyPlanMatrixWithEnd_PicksUpWayBackStop_WhereStorageBlindDoesNot()
        {
            // THE FIX (user report: "missing obvious quick wins on the route back to storage"). A 3-bush cluster sits
            // by the pawn; storage is far on the +x axis; one extra bush sits HALFWAY on the pawn→storage line — a
            // "quick win" the pawn drives straight past on the way back. forced = 1 (the clicked anchor = stop 0).
            var xs = new List<int> { 5, 5, 5, 50 };   // stop 0 anchor (5,0); 2 cluster bushes; stop 3 = the way-back bush
            var zs = new List<int> { 0, 6, -6, 1 };
            const int pawnX = 0, pawnZ = 0, storX = 100, storZ = 0;

            // Storage-BLIND (open path pawn→stops): the way-back bush is far from the cluster → big insertion cost.
            RouteBudget.GreedyPlanMatrixWithEnd(EuclidMatrix(pawnX, pawnZ, xs, zs), 1, -1, out int[] _, out float[] blindCost);
            // Storage-AWARE (route through storage): the way-back bush is ON the pawn→storage line → ~0 detour.
            RouteBudget.GreedyPlanMatrixWithEnd(EuclidMatrixWithStorage(pawnX, pawnZ, xs, zs, storX, storZ), 1, xs.Count + 1,
                out int[] awareOrder, out float[] awareCost);

            // The full route cost (all 4 stops) is FAR lower when storage-aware — the way-back bush became free.
            float blindFull = blindCost[blindCost.Length - 1];
            float awareFull = awareCost[awareCost.Length - 1];
            Assert.That(awareFull, Is.LessThan(blindFull * 0.5f),
                $"storage-aware full cost ({awareFull:0.0}) must be far below storage-blind ({blindFull:0.0})");

            // Under a modest budget that fits the cluster's small detours but not a ~45-cell append, storage-aware
            // KEEPS all four stops (incl. the way-back bush) while storage-blind trims it.
            const float budget = 25f;
            int blindKept = RouteBudget.LargestPrefixWithin(blindCost, budget, 1);
            int awareKept = RouteBudget.LargestPrefixWithin(awareCost, budget, 1);
            Assert.That(awareKept, Is.EqualTo(4), "storage-aware must keep all four stops within the budget");
            Assert.That(blindKept, Is.LessThan(4), "storage-blind must trim the way-back stop at the same budget");

            // And the way-back bush (stop index 3) is actually among the storage-aware selection's adds.
            Assert.That(Array.IndexOf(awareOrder, 3), Is.GreaterThanOrEqualTo(0).And.LessThan(awareKept),
                "the way-back bush must be one of the kept storage-aware stops");
        }

        [Test]
        public void GreedyPlanMatrixWithEnd_FixedEnd_IsForcedFirstAndMonotone()
        {
            // The fixed-end (storage) greedy keeps the sacred contract: forced stops lead, cost non-decreasing.
            var rng = new Random(2027);
            for (int trial = 0; trial < 250; trial++)
            {
                int n = 2 + rng.Next(14);
                int forced = 1 + rng.Next(Math.Min(n, 4));
                var xs = new List<int>(); var zs = new List<int>();
                for (int i = 0; i < n; i++) { xs.Add(rng.Next(-60, 60)); zs.Add(rng.Next(-60, 60)); }
                var d = EuclidMatrixWithStorage(rng.Next(-60, 60), rng.Next(-60, 60), xs, zs, rng.Next(-60, 60), rng.Next(-60, 60));

                RouteBudget.GreedyPlanMatrixWithEnd(d, forced, n + 1, out int[] order, out float[] cost);

                var seen = new bool[n];
                foreach (int idx in order) { Assert.That(seen[idx], Is.False, $"trial {trial}: dup {idx}"); seen[idx] = true; }
                for (int k = 0; k < forced; k++)
                    Assert.That(order[k], Is.LessThan(forced), $"trial {trial}: forced must lead at k={k}");
                for (int k = 0; k < forced; k++)
                    Assert.That(cost[k], Is.EqualTo(0f), $"trial {trial}: forced cost must be 0 at k={k}");
                for (int k = 1; k < n; k++)
                    Assert.That(cost[k], Is.GreaterThanOrEqualTo(cost[k - 1] - 1e-3f), $"trial {trial}: non-monotone at k={k}");
            }
        }

        [Test]
        public void LargestPrefixWithin_StaysMonotoneInBudget_OnFixedEndCost()
        {
            // The Max-travel trim must remain monotone on the storage-aware (detour) cost: a larger budget never
            // keeps fewer stops. (The sacred invariant, exercised on the new cost array.)
            var rng = new Random(8088);
            for (int trial = 0; trial < 200; trial++)
            {
                int n = 2 + rng.Next(14);
                int forced = 1 + rng.Next(Math.Min(n, 3));
                var xs = new List<int>(); var zs = new List<int>();
                for (int i = 0; i < n; i++) { xs.Add(rng.Next(-60, 60)); zs.Add(rng.Next(-60, 60)); }
                var d = EuclidMatrixWithStorage(rng.Next(-60, 60), rng.Next(-60, 60), xs, zs, rng.Next(-60, 60), rng.Next(-60, 60));
                RouteBudget.GreedyPlanMatrixWithEnd(d, forced, n + 1, out int[] _, out float[] cost);

                int prev = 0;
                for (int b = 1; b <= 400; b += 7)
                {
                    int kept = RouteBudget.LargestPrefixWithin(cost, b, forced);
                    Assert.That(kept, Is.GreaterThanOrEqualTo(prev), $"trial {trial}: budget {b} dropped a stop");
                    prev = kept;
                }
            }
        }

        [Test]
        public void PrefixRouteCostMatrixWithEnd_FixedEnd_ForcedFreeAndMonotone()
        {
            // The "nearest" selection method's fixed-order cost, routed through storage, stays forced-free + monotone.
            var rng = new Random(5150);
            for (int trial = 0; trial < 200; trial++)
            {
                int n = 2 + rng.Next(14);
                int forced = 1 + rng.Next(Math.Min(n, 4));
                var xs = new List<int>(); var zs = new List<int>();
                for (int i = 0; i < n; i++) { xs.Add(rng.Next(-60, 60)); zs.Add(rng.Next(-60, 60)); }
                var d = EuclidMatrixWithStorage(rng.Next(-60, 60), rng.Next(-60, 60), xs, zs, rng.Next(-60, 60), rng.Next(-60, 60));

                var order = new int[n];
                for (int i = 0; i < n; i++) order[i] = i;
                RouteBudget.PrefixRouteCostMatrixWithEnd(d, order, forced, n + 1, out float[] cost);

                for (int k = 0; k < forced; k++)
                    Assert.That(cost[k], Is.EqualTo(0f), $"trial {trial}: forced cost must be 0 at k={k}");
                for (int k = 1; k < n; k++)
                    Assert.That(cost[k], Is.GreaterThanOrEqualTo(cost[k - 1] - 1e-3f), $"trial {trial}: non-monotone at k={k}");
            }
        }
    }
}
