namespace HaulersDream.Core
{
    /// <summary>
    /// The fair-share split for a bulk-load claim when SEVERAL pawns stand ready to load the same manifest (a portal
    /// or transporter boarding lord: everyone's only task is "load this, then enter"). Without it, the first asker's
    /// claim is bounded only by its own carry ceiling, and the smart-overload ceiling (275 percent of capacity by
    /// default, unbounded at level 0) routinely swallows an entire dungeon-loot manifest, so one pawn hauls while the
    /// rest idle. This was the previously-overlooked term in the claim sizing: every bound was per-PAWN (stack,
    /// manifest, ledger, carry, trip mass); none was per-PEER.
    ///
    /// The split divides MASS, not per-def units: per-def division cannot bound a claim over many small defs (dungeon
    /// loot is largely stackCount-1 uniques, where a per-def ceil quota of 1 still lets one pawn claim every def),
    /// while mass is comparable across defs and the sweep already runs a mass budget it can be clamped with.
    /// No game types; the runtime feeds it pool masses and a spawned-pawn count. Pure and deterministic (Multiplayer
    /// runs it in sim on every client).
    /// </summary>
    public static class LoadFairShare
    {
        /// <summary>
        /// The mass budget one asker's claim may cover: the claimable pool mass divided evenly across the loaders,
        /// floored to one HEAVIEST unit so every single claimable item always fits inside one share (no starvation
        /// while unclaimed goods remain, and no item orphaned because every share is smaller than it).
        /// </summary>
        /// <param name="claimableMassKg">Total mass (kg) of what THIS asker could claim right now: pool stacks of
        /// claimable defs, each counted up to the def's remaining claimable units. At most 0 when everything left is
        /// massless or already claimed.</param>
        /// <param name="heaviestUnitMassKg">Unit mass (kg) of the heaviest single claimable item in that pool, the
        /// no-starvation floor. Flooring to the HEAVIEST (not lightest) unit makes every claimable stack
        /// unit-affordable within one share, so the fairness clamp alone can never mass-starve a pick: a
        /// lightest-unit floor could leave a heavy item unclaimable by the whole crew when the raw share fell below
        /// its unit mass (say a sculpture heavier than the per-pawn split, with the light item that set the floor
        /// sitting unreachable behind a wall). The pawn's own trip budget still caps what it physically carries.
        /// Non-positive values (no massive item seen) disable the floor.</param>
        /// <param name="loaderCount">How many pawns the pool is split across: the asker plus every other ready
        /// co-loader that holds NO live claim on this task (claim holders' slices are already excluded from the
        /// claimable mass). Values below 2 mean the asker is alone.</param>
        /// <returns><see cref="float.PositiveInfinity"/> when no clamp applies (a lone loader keeps today's exact
        /// behavior; an all-massless pool has nothing measurable to divide), else
        /// <c>max(claimableMassKg / loaderCount, heaviestUnitMassKg)</c>.</returns>
        public static float ShareMassBudget(float claimableMassKg, float heaviestUnitMassKg, int loaderCount)
        {
            // A lone loader is never clamped: the fair share of one is everything, and returning the sentinel keeps
            // the single-pawn planner byte-identical to the pre-fairness behavior.
            if (loaderCount <= 1)
                return float.PositiveInfinity;

            // Nothing measurable to divide (empty or all-massless pool): don't clamp. The sweep's other bounds
            // (claim units, carry ceiling, CE bulk) still apply; a 0 budget here would wrongly sweep NOTHING because
            // the sweep loop stops the moment its mass budget is spent.
            if (claimableMassKg <= 0f)
                return float.PositiveInfinity;

            float share = claimableMassKg / loaderCount;

            // No-starvation floor: every loader can always claim at least one unit of ANY claimable item, including
            // the heaviest. Without it a remainder split many ways yields a budget below an item's unit mass, that
            // item becomes unclaimable for the whole crew, and the haul stalls into the vanilla one-stack fallback.
            if (heaviestUnitMassKg > 0f && share < heaviestUnitMassKg)
                share = heaviestUnitMassKg;
            return share;
        }
    }
}
