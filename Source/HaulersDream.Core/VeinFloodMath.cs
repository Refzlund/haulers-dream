using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>A map cell as a pure (x,z) pair — no Verse dependency, so the flood math is unit-testable.</summary>
    public readonly struct RouteCell : IEquatable<RouteCell>
    {
        public readonly int X;
        public readonly int Z;
        public RouteCell(int x, int z) { X = x; Z = z; }
        public bool Equals(RouteCell o) => X == o.X && Z == o.Z;
        public override bool Equals(object o) => o is RouteCell c && Equals(c);
        public override int GetHashCode() => (X * 397) ^ Z;
        public override string ToString() => $"({X},{Z})";
    }

    /// <summary>
    /// Pure flood-fill for the "Touching / vein" route mode: from a seed cell, walk the contiguous cluster
    /// of cells that belong to the vein (8-way adjacency), breadth-first, returning up to <paramref name="cap"/>
    /// cells in flood order (nearest-by-hops first, seed first). Used to pick a contiguous run of same-kind
    /// targets — an ore vein, a patch of plants. No Verse types — unit-tested on abstract cell sets.
    /// </summary>
    public static class VeinFloodMath
    {
        // 8-way neighbour offsets (E, NE, N, NW, W, SW, S, SE).
        private static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] DZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

        /// <param name="members">every cell that is part of the vein (the same-kind set).</param>
        /// <param name="seed">the clicked cell; must be in <paramref name="members"/> for a non-empty result.</param>
        /// <param name="cap">max cells to return; &lt;= 0 means unbounded (the whole connected cluster).</param>
        public static List<RouteCell> FloodOrder(HashSet<RouteCell> members, RouteCell seed, int cap)
        {
            var result = new List<RouteCell>();
            if (members == null || !members.Contains(seed) || cap == 0)
                return result;

            var seen = new HashSet<RouteCell> { seed };
            var queue = new Queue<RouteCell>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                result.Add(c);
                if (cap > 0 && result.Count >= cap)
                    break;
                for (int i = 0; i < 8; i++)
                {
                    var n = new RouteCell(c.X + DX[i], c.Z + DZ[i]);
                    if (!seen.Contains(n) && members.Contains(n))
                    {
                        seen.Add(n);
                        queue.Enqueue(n);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// True if any cell in <paramref name="cells"/> is 8-adjacent to a cell in <paramref name="others"/>.
        /// Used by the vein route to detect when the visible cluster touches a hidden (fogged) same-kind cell,
        /// i.e. the vein continues into unexplored fog — so the dialog can caution that not all of it is shown.
        /// </summary>
        public static bool AnyAdjacent(IEnumerable<RouteCell> cells, HashSet<RouteCell> others)
        {
            if (cells == null || others == null || others.Count == 0)
                return false;
            foreach (var c in cells)
                for (int i = 0; i < 8; i++)
                    if (others.Contains(new RouteCell(c.X + DX[i], c.Z + DZ[i])))
                        return true;
            return false;
        }
    }
}
