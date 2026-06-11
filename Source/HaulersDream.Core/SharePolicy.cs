namespace HaulersDream.Core
{
    /// <summary>
    /// Pure gate for whether a carrier's tagged inventory stack should count as a sharable
    /// material/ingredient for a worker (construction delivery or a crafting bill). The load-bearing rule
    /// is the SELF bypass: a worker's OWN scooped stock is already in hand at the bench/site, so it skips
    /// the reachability + ingredient-radius gates that only bound FETCHING from a remote carrier. Everyone
    /// still has to pass the reservation opt-out (a stack the worker has already committed to another job)
    /// and the recipe/material filter. Game-independent so it's unit-testable headlessly.
    /// </summary>
    public static class SharePolicy
    {
        /// <param name="isSelf">The carrier IS the worker (its own inventory).</param>
        /// <param name="reachable">The worker can path to the carrier (ignored for self).</param>
        /// <param name="canReserve">The worker can reserve this exact stack (false = the carrier is actively using it).</param>
        /// <param name="isUsable">The stack matches the recipe/material the worker needs.</param>
        /// <param name="withinRadius">The carrier is inside the bill's ingredient radius (ignored for self).</param>
        public static bool ShouldIncludeStack(bool isSelf, bool reachable, bool canReserve, bool isUsable, bool withinRadius)
        {
            // Everyone: must be reservable (not already claimed for another job) and the right material.
            if (!canReserve || !isUsable)
                return false;
            // Self: already at the bench/site — bypass the fetch-only reach + radius gates.
            if (isSelf)
                return true;
            // Others: only if the worker can actually reach them and they're inside the search radius.
            return reachable && withinRadius;
        }
    }
}
