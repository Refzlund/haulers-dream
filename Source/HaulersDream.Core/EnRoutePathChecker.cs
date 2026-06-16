namespace HaulersDream.Core
{
    /// <summary>
    /// How strictly the Verse layer confirms a candidate en-route haul's "is the store roughly along the
    /// path?" check, after the cheap straight-line ratio cascade (<see cref="EnRoutePickupPolicy"/>) has
    /// already accepted it. Faithful port of While You're Up's <c>Settings.PathCheckerEnum</c>
    /// (<c>Settings.cs:422</c>) and the way <c>OpportunityDetour.CanHaul</c> dispatches on it
    /// (<c>OpportunityDetour.cs:294-299</c>).
    ///
    /// <para>The cascade short-circuits cheap → expensive (squared straight-line range checks first, then
    /// the unsquared leg-ratio checks, then THIS reachability/accuracy stage last), so the only difference
    /// between the values is how the FINAL stage treats a candidate and what it does on a range-only
    /// failure. See the per-value docs.</para>
    /// </summary>
    public enum EnRoutePathChecker
    {
        /// <summary>
        /// CHEAP region-count check only — WYU <c>PathCheckerEnum.Vanilla</c>
        /// (<c>OpportunityDetour.cs:295</c>, region gate <c>WithinRegionCount</c> at
        /// <c>OpportunityDetour.cs:252-260</c>). The Verse layer confirms the store is "along the way" by a
        /// bounded region-flood from pawn→thing and store→job (the <c>MaxStartToThingRegionLookCount</c> /
        /// <c>MaxStoreToJobRegionLookCount</c> caps), never a full A* path. Fastest, least accurate. In this
        /// mode a straight-line range-only failure is treated as a HARD reject of the candidate (WYU routes
        /// <c>RangeFail</c> straight to <c>HardFail</c> — <c>OpportunityDetour.cs:157-159</c>).
        /// </summary>
        Vanilla,

        /// <summary>
        /// ACCURATE A* path-cost check, and a range-only failure STOPS the whole scan — WYU
        /// <c>PathCheckerEnum.Default</c> (the WYU default value; <c>OpportunityDetour.cs:297</c>, path gate
        /// <c>WithinPathCost</c> at <c>OpportunityDetour.cs:262-292</c>). The Verse layer pathfinds the new
        /// legs and applies the same <c>MaxNewLegs</c> / <c>MaxTotalTrip</c> ratios to the real path costs.
        /// A range-only failure does NOT discard the candidate; it ends the candidate loop early
        /// (<c>FullStop</c> — <c>OpportunityDetour.cs:166-167</c>), because with the optimistic expanding
        /// range heuristic a candidate failing the current range band means the cheaper-first ordering has
        /// run out of plausible candidates. Best balance of accuracy and cost; this is the recommended
        /// default.
        /// </summary>
        Default,

        /// <summary>
        /// ACCURATE A* path-cost check, but a range-only failure merely SKIPS this candidate and keeps
        /// scanning — WYU <c>PathCheckerEnum.Pathfinding</c> (<c>OpportunityDetour.cs:296</c>, same
        /// <c>WithinPathCost</c> gate as <see cref="Default"/>). Identical reachability accuracy to
        /// <see cref="Default"/>, but it never short-circuits on a range failure: every remaining candidate
        /// is pathfound. Most thorough, most expensive.
        /// </summary>
        Pathfinding
    }
}
