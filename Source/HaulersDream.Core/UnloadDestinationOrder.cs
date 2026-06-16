namespace HaulersDream.Core
{
    /// <summary>
    /// Pure ordering for the "closest-destination-first" unload (WYU "efficient unloading" parity, re-expressed
    /// on HD's own unload driver — no PUAH dependency). The consolidated unload visits one carried stack per pick
    /// and re-scans after each delivery; this decides which carried stack to pick NEXT so the pawn empties the
    /// NEAREST storage destination first (instead of zig-zagging in category→defName order with no proximity term).
    ///
    /// The Verse side resolves each candidate's storage destination cell and the squared int distance from the
    /// pawn to that cell, then feeds the two distances plus the existing category/defName keys here. Smaller
    /// distance wins; on EQUAL distance (two stacks bound to the same / equidistant destination) — or when a
    /// distance is INVALID (no resolvable destination, fed as <see cref="NoDestination"/>) — it falls back to the
    /// established <see cref="SelectFirstByCategoryThenDef.LessThan"/> tiebreak, so the within-destination order is
    /// byte-identical to today's min-scan and the result stays deterministic / stable.
    ///
    /// No game types — unit-tested headlessly. The comparison takes only primitives.
    /// </summary>
    public static class UnloadDestinationOrder
    {
        /// <summary>The squared-distance sentinel the caller passes when a candidate has NO resolvable storage
        /// destination this pick (TryFindBestBetterStorageFor / desperate-cell came up empty). Sorts LAST among
        /// candidates that DO have a destination; two no-destination candidates compare purely by the
        /// category→defName tiebreak (so their relative order is the same stable order as today).</summary>
        public const int NoDestination = int.MaxValue;

        /// <summary>
        /// True iff candidate A should be picked BEFORE candidate B for the closest-destination-first unload.
        ///
        /// Ordering: strictly-smaller resolved-destination distance wins. On equal distance (incl. two
        /// <see cref="NoDestination"/> sentinels, which are equal) fall back to
        /// <see cref="SelectFirstByCategoryThenDef.LessThan"/> on (categoryIndex, defName) — the exact tiebreak the
        /// OFF path uses — so equidistant / same-destination stacks keep the established category→defName order and
        /// the whole comparison stays a STRICT order (replaces the running best only on a strictly-smaller key,
        /// preserving the first-seen-among-equals stability the OFF min-scan guarantees).
        /// </summary>
        /// <param name="distSqA">Pawn→destination squared int distance for A (or <see cref="NoDestination"/>).</param>
        /// <param name="catA">A's FirstThingCategory index (or <see cref="SelectFirstByCategoryThenDef.NoCategory"/>).</param>
        /// <param name="defA">A's defName (the ordinal tiebreak key).</param>
        public static bool Less(int distSqA, int catA, string defA, int distSqB, int catB, string defB)
        {
            if (distSqA != distSqB)
                return distSqA < distSqB;
            // Equal distance (or both have no destination) -> identical to the OFF path's tiebreak.
            return SelectFirstByCategoryThenDef.LessThan(catA, defA, catB, defB);
        }
    }
}
