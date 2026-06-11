using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class RouteOrderPolicyTests
    {
        // Build a Euclidean cost matrix from 2D points so the cases read intuitively.
        private static float[,] Matrix(params (float x, float y)[] pts)
        {
            int n = pts.Length;
            var d = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float dx = pts[i].x - pts[j].x, dy = pts[i].y - pts[j].y;
                    d[i, j] = (float)Math.Sqrt(dx * dx + dy * dy);
                }
            return d;
        }

        private static float PathCost(float[,] d, int start, int end, IList<int> order)
        {
            float c = 0f;
            int prev = start;
            foreach (int node in order) { c += d[prev, node]; prev = node; }
            if (end >= 0) c += d[prev, end];
            return c;
        }

        // Independent brute-force optimum over all permutations of the interior stops.
        private static float BruteForceOptimum(float[,] d, int start, int end, List<int> stops)
        {
            float best = float.PositiveInfinity;
            foreach (var perm in Permutations(stops))
                best = Math.Min(best, PathCost(d, start, end, perm));
            return best;
        }

        private static IEnumerable<List<int>> Permutations(List<int> items)
        {
            if (items.Count <= 1) { yield return new List<int>(items); yield break; }
            for (int i = 0; i < items.Count; i++)
            {
                var rest = new List<int>(items);
                int picked = rest[i];
                rest.RemoveAt(i);
                foreach (var p in Permutations(rest))
                {
                    p.Insert(0, picked);
                    yield return p;
                }
            }
        }

        private static List<int> InteriorStops(int n, int start, int end)
        {
            var s = new List<int>();
            for (int i = 0; i < n; i++)
                if (i != start && i != end) s.Add(i);
            return s;
        }

        [Test]
        public void OrdersCollinearStops_NearestFirst_FreeEnd()
        {
            // start at origin; stops at x = 10, 5, 1. Optimal harvest-only path walks outward: 1, 5, 10.
            var d = Matrix((0, 0), (10, 0), (5, 0), (1, 0));
            var order = RouteOrderPolicy.Order(d, start: 0, end: -1);
            Assert.That(order, Is.EqualTo(new[] { 3, 2, 1 }));
        }

        [Test]
        public void ExactMatchesBruteForce_OnRandomSmallInstances_FreeEnd()
        {
            var rng = new Random(12345);
            for (int trial = 0; trial < 40; trial++)
            {
                int n = rng.Next(3, 9); // 3..8 nodes → ≤7 interior stops (brute force stays fast; same exact DP path)
                var pts = new (float, float)[n];
                for (int i = 0; i < n; i++) pts[i] = ((float)rng.NextDouble() * 100, (float)rng.NextDouble() * 100);
                var d = Matrix(pts);
                int start = rng.Next(n);
                var stops = InteriorStops(n, start, -1);

                var order = RouteOrderPolicy.Order(d, start, -1);
                AssertValidPermutation(order, stops);
                float got = PathCost(d, start, -1, order);
                float opt = BruteForceOptimum(d, start, -1, stops);
                Assert.That(got, Is.EqualTo(opt).Within(1e-3f), $"trial {trial}: exact order is not optimal");
            }
        }

        [Test]
        public void ExactMatchesBruteForce_OnRandomSmallInstances_FixedEnd()
        {
            var rng = new Random(98765);
            for (int trial = 0; trial < 40; trial++)
            {
                int n = rng.Next(4, 10); // start + end + ≤7 interior stops (brute force stays fast)
                var pts = new (float, float)[n];
                for (int i = 0; i < n; i++) pts[i] = ((float)rng.NextDouble() * 100, (float)rng.NextDouble() * 100);
                var d = Matrix(pts);
                int start = 0, end = n - 1; // fixed end (storage anchor)
                var stops = InteriorStops(n, start, end);
                if (stops.Count > RouteOrderPolicy.ExactMax) continue;

                var order = RouteOrderPolicy.Order(d, start, end);
                AssertValidPermutation(order, stops);
                float got = PathCost(d, start, end, order);
                float opt = BruteForceOptimum(d, start, end, stops);
                Assert.That(got, Is.EqualTo(opt).Within(1e-3f), $"trial {trial}: exact fixed-end order is not optimal");
            }
        }

        [Test]
        public void StorageAnchor_FlipsVisitingOrder()
        {
            // Start S=(0,0); stops A=(10,0), B=(10,10), C=(0,8). With a free end the shortest open path is
            // C→B→A (start by the near top-left corner). Anchoring the end on storage placed next to C (0,9)
            // flips it to A→B→C so the route terminates right by the stockpile — exactly the "circle back" goal.
            var free = Matrix((0, 0), (10, 0), (10, 10), (0, 8));
            Assert.That(RouteOrderPolicy.Order(free, start: 0, end: -1), Is.EqualTo(new[] { 3, 2, 1 }));

            var anchored = Matrix((0, 0), (10, 0), (10, 10), (0, 8), (0, 9));
            Assert.That(RouteOrderPolicy.Order(anchored, start: 0, end: 4), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void HeuristicProducesValidTour_AndBeatsIdentity_OnLargeInstances()
        {
            var rng = new Random(2024);
            for (int trial = 0; trial < 10; trial++)
            {
                int n = rng.Next(RouteOrderPolicy.ExactMax + 2, RouteOrderPolicy.ExactMax + 18); // forces the heuristic
                var pts = new (float, float)[n];
                for (int i = 0; i < n; i++) pts[i] = ((float)rng.NextDouble() * 100, (float)rng.NextDouble() * 100);
                var d = Matrix(pts);
                int start = 0;
                var stops = InteriorStops(n, start, -1);

                var order = RouteOrderPolicy.Order(d, start, -1);
                AssertValidPermutation(order, stops);

                float got = PathCost(d, start, -1, order);
                float identity = PathCost(d, start, -1, stops); // natural index order
                Assert.That(got, Is.LessThanOrEqualTo(identity + 1e-3f), $"trial {trial}: heuristic worse than identity order");
            }
        }

        [Test]
        public void ExactDP_AtBoundary_ProducesValidPermutation()
        {
            // The 8..12-stop exact range isn't brute-forced (too many perms); just confirm the DP path at the
            // ExactMax boundary returns every stop exactly once and never worse than the identity order.
            var rng = new Random(777);
            int n = RouteOrderPolicy.ExactMax + 1; // start + ExactMax interior stops
            var pts = new (float, float)[n];
            for (int i = 0; i < n; i++) pts[i] = ((float)rng.NextDouble() * 100, (float)rng.NextDouble() * 100);
            var d = Matrix(pts);
            var stops = InteriorStops(n, 0, -1);
            var order = RouteOrderPolicy.Order(d, 0, -1);
            AssertValidPermutation(order, stops);
            Assert.That(PathCost(d, 0, -1, order), Is.LessThanOrEqualTo(PathCost(d, 0, -1, stops) + 1e-3f));
        }

        [Test]
        public void SingleStop_ReturnedAsIs()
        {
            var d = Matrix((0, 0), (5, 5));
            Assert.That(RouteOrderPolicy.Order(d, 0, -1), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void NoStops_ReturnsEmpty()
        {
            var d = Matrix((0, 0), (5, 5)); // node 1 is the fixed end → no interior stops
            Assert.That(RouteOrderPolicy.Order(d, 0, 1), Is.Empty);
        }

        private static void AssertValidPermutation(int[] order, List<int> stops)
        {
            Assert.That(order.Length, Is.EqualTo(stops.Count), "order length");
            Assert.That(order.Distinct().Count(), Is.EqualTo(order.Length), "order has duplicates");
            Assert.That(order.OrderBy(x => x), Is.EqualTo(stops.OrderBy(x => x)), "order is not a permutation of the stops");
        }

        // ── OrderStops: pinned start/end ─────────────────────────────────────────────────────────────────

        private static float StopRouteCost(int px, int pz, int[] xs, int[] zs, bool hasStorage, int sx, int sz, IList<int> order)
        {
            float c = 0f;
            int curX = px, curZ = pz;
            foreach (int idx in order)
            {
                c += (float)Math.Sqrt(Sq(xs[idx] - curX) + Sq(zs[idx] - curZ));
                curX = xs[idx]; curZ = zs[idx];
            }
            if (hasStorage)
                c += (float)Math.Sqrt(Sq(sx - curX) + Sq(sz - curZ));
            return c;
        }

        private static int Sq(int v) => v * v;

        // Brute-force the cheapest full stop order honouring the pins (start first, end last) for the given layout.
        private static float BruteForceStopOptimum(int px, int pz, int[] xs, int[] zs, bool hasStorage, int sx, int sz,
            int startStop, int endStop)
        {
            int s = xs.Length;
            var interior = new List<int>();
            for (int i = 0; i < s; i++)
                if (i != startStop && i != endStop) interior.Add(i);
            float best = float.PositiveInfinity;
            foreach (var perm in Permutations(interior))
            {
                var full = new List<int>(s);
                if (startStop >= 0) full.Add(startStop);
                full.AddRange(perm);
                if (endStop >= 0) full.Add(endStop);
                best = Math.Min(best, StopRouteCost(px, pz, xs, zs, hasStorage, sx, sz, full));
            }
            return best;
        }

        [Test]
        public void OrderStops_NoPins_OrdersOutwardLikePlainRoute()
        {
            // pawn at origin; stops at x = 10, 5, 1. With no pins / no storage the optimal walk is 1 → 5 → 10.
            var order = RouteOrderPolicy.OrderStops(0, 0, new[] { 10, 5, 1 }, new[] { 0, 0, 0 },
                hasStorage: false, 0, 0, startStop: -1, endStop: -1);
            Assert.That(order, Is.EqualTo(new[] { 2, 1, 0 }));
        }

        [Test]
        public void OrderStops_PinnedStart_IsVisitedFirst()
        {
            // stops A=(1,0) B=(5,0) C=(10,0); pin the FAR stop C (index 2) as start → route begins at C then sweeps back.
            var order = RouteOrderPolicy.OrderStops(0, 0, new[] { 1, 5, 10 }, new[] { 0, 0, 0 },
                hasStorage: false, 0, 0, startStop: 2, endStop: -1);
            Assert.That(order[0], Is.EqualTo(2), "pinned start must be visited first");
            Assert.That(order, Is.EqualTo(new[] { 2, 1, 0 }));
        }

        [Test]
        public void OrderStops_PinnedEnd_IsVisitedLast()
        {
            // pawn (0,0); A=(10,0) B=(10,10) C=(0,10); pin A (index 0) as the END. Cheapest is pawn→C→B→A.
            var order = RouteOrderPolicy.OrderStops(0, 0, new[] { 10, 10, 0 }, new[] { 0, 10, 10 },
                hasStorage: false, 0, 0, startStop: -1, endStop: 0);
            Assert.That(order[order.Length - 1], Is.EqualTo(0), "pinned end must be visited last");
            Assert.That(order, Is.EqualTo(new[] { 2, 1, 0 }));
        }

        [Test]
        public void OrderStops_BothPins_RespectedAndValid()
        {
            var order = RouteOrderPolicy.OrderStops(0, 0, new[] { 10, 10, 0, 5 }, new[] { 0, 10, 10, 5 },
                hasStorage: false, 0, 0, startStop: 2, endStop: 0);
            Assert.That(order[0], Is.EqualTo(2));
            Assert.That(order[order.Length - 1], Is.EqualTo(0));
            Assert.That(order.Distinct().Count(), Is.EqualTo(4));
        }

        [Test]
        public void OrderStops_StartEqualsEnd_TreatsEndAsUnpinned()
        {
            // start and end both index 1 → end is ignored; index 1 is the start (first), the rest free.
            var order = RouteOrderPolicy.OrderStops(0, 0, new[] { 1, 5, 10 }, new[] { 0, 0, 0 },
                hasStorage: false, 0, 0, startStop: 1, endStop: 1);
            Assert.That(order[0], Is.EqualTo(1));
            Assert.That(order.Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void OrderStops_OutOfRangePins_TreatedAsUnpinned()
        {
            var order = RouteOrderPolicy.OrderStops(0, 0, new[] { 10, 5, 1 }, new[] { 0, 0, 0 },
                hasStorage: false, 0, 0, startStop: 99, endStop: -7);
            Assert.That(order, Is.EqualTo(new[] { 2, 1, 0 }), "out-of-range pins must degrade to the plain order");
        }

        [Test]
        public void OrderStops_SingleAndEmpty_AreSafe()
        {
            Assert.That(RouteOrderPolicy.OrderStops(0, 0, new[] { 3 }, new[] { 4 }, false, 0, 0, -1, -1), Is.EqualTo(new[] { 0 }));
            Assert.That(RouteOrderPolicy.OrderStops(0, 0, new int[0], new int[0], true, 5, 5, -1, -1), Is.Empty);
            // a single stop pinned as start is still just itself
            Assert.That(RouteOrderPolicy.OrderStops(0, 0, new[] { 3 }, new[] { 4 }, false, 0, 0, 0, -1), Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void OrderStops_HeuristicBranch_HonoursPins_AndStaysAValidPermutation()
        {
            // > ExactMax interior stops forces the NN+2-opt branch. Pins are applied in the assembly step (outside
            // the TSP), so start-first / end-last must hold regardless of which branch ordered the interior.
            var rng = new Random(31415);
            for (int trial = 0; trial < 25; trial++)
            {
                int s = RouteOrderPolicy.ExactMax + rng.Next(4, 12); // interior stays > ExactMax after the two pins
                var xs = new int[s]; var zs = new int[s];
                for (int i = 0; i < s; i++) { xs[i] = rng.Next(-100, 100); zs[i] = rng.Next(-100, 100); }
                int px = rng.Next(-100, 100), pz = rng.Next(-100, 100);
                int startStop = rng.Next(s);
                int endStop; do { endStop = rng.Next(s); } while (endStop == startStop);

                var order = RouteOrderPolicy.OrderStops(px, pz, xs, zs, rng.Next(2) == 0, rng.Next(-100, 100),
                    rng.Next(-100, 100), startStop, endStop);

                Assert.That(order.Length, Is.EqualTo(s), $"trial {trial}: length");
                Assert.That(order.Distinct().Count(), Is.EqualTo(s), $"trial {trial}: duplicates");
                Assert.That(order[0], Is.EqualTo(startStop), $"trial {trial}: start pin (heuristic branch)");
                Assert.That(order[s - 1], Is.EqualTo(endStop), $"trial {trial}: end pin (heuristic branch)");
            }
        }

        [Test]
        public void OrderStops_MatchesBruteForce_WithRandomPinsAndStorage()
        {
            var rng = new Random(424242);
            for (int trial = 0; trial < 300; trial++)
            {
                int s = rng.Next(2, 7); // small enough to brute-force; interior ≤ 6 stays in the exact DP branch
                var xs = new int[s]; var zs = new int[s];
                for (int i = 0; i < s; i++) { xs[i] = rng.Next(-50, 50); zs[i] = rng.Next(-50, 50); }
                int px = rng.Next(-50, 50), pz = rng.Next(-50, 50);
                bool hasStorage = rng.Next(2) == 0;
                int sx = rng.Next(-50, 50), sz = rng.Next(-50, 50);
                int startStop = rng.Next(3) == 0 ? -1 : rng.Next(s);
                int endStop = rng.Next(3) == 0 ? -1 : rng.Next(s);
                if (endStop == startStop) endStop = -1;

                var order = RouteOrderPolicy.OrderStops(px, pz, xs, zs, hasStorage, sx, sz, startStop, endStop);

                // valid permutation of all stops
                Assert.That(order.Length, Is.EqualTo(s), $"trial {trial}: length");
                Assert.That(order.Distinct().Count(), Is.EqualTo(s), $"trial {trial}: duplicates");
                // pins honoured
                if (startStop >= 0) Assert.That(order[0], Is.EqualTo(startStop), $"trial {trial}: start pin");
                if (endStop >= 0) Assert.That(order[s - 1], Is.EqualTo(endStop), $"trial {trial}: end pin");
                // optimal for the pinned layout
                float got = StopRouteCost(px, pz, xs, zs, hasStorage, sx, sz, order);
                float opt = BruteForceStopOptimum(px, pz, xs, zs, hasStorage, sx, sz, startStop, endStop);
                Assert.That(got, Is.EqualTo(opt).Within(1e-2f), $"trial {trial}: pinned order not optimal");
            }
        }

        [Test]
        public void ExactMax_ParamIsHonored_BothBranchesReturnValidTours()
        {
            // 5 interior stops (matrix indices 1..5), pawn at index 0, free end.
            var d = Matrix((0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0));
            var stops = new List<int> { 1, 2, 3, 4, 5 };

            // exactMax high → exact Held-Karp → must equal the brute-force optimum.
            int[] exact = RouteOrderPolicy.Order(d, start: 0, end: -1, exactMax: 12);
            AssertValidPermutation(exact, stops);
            float opt = BruteForceOptimum(d, 0, -1, stops);
            Assert.That(PathCost(d, 0, -1, exact.ToList()), Is.EqualTo(opt).Within(1e-3f), "exact branch must be optimal");

            // exactMax below the stop count → heuristic branch → still a valid tour (and on a colinear set, optimal).
            int[] heur = RouteOrderPolicy.Order(d, start: 0, end: -1, exactMax: 3);
            AssertValidPermutation(heur, stops);
            Assert.That(PathCost(d, 0, -1, heur.ToList()), Is.GreaterThanOrEqualTo(opt - 1e-3f), "heuristic ≥ optimum");
        }
    }
}
