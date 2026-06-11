namespace HaulersDream.Core
{
    /// <summary>
    /// Decides whether it's worth a worker intercepting a stack a colonist is hand-hauling TO STORAGE,
    /// instead of letting it reach storage and fetching it from there. Pure straight-line-distance math
    /// (like <see cref="OpportunisticUnloadPolicy"/>), so it's headlessly unit-testable. The game layer
    /// (<c>CarriedHaulShare</c>) supplies the live tile distances.
    ///
    /// Baseline (no intercept): the material travels hauler → storage, then storage → needer.
    /// Intercept: the worker walks to the hauler, takes the stack, and carries it hauler → needer.
    /// Intercept only when it strictly beats the baseline AND clears guard rails (don't chase across the
    /// map, don't interrupt a hauler that's basically at storage, don't bother for a token amount).
    /// </summary>
    public static class CarriedInterceptPolicy
    {
        public static bool ShouldIntercept(
            int workerToHauler,
            int haulerToStorage,
            int haulerToNeeder,
            int storageToNeeder,
            float carriedFractionOfNeed,
            int maxChaseTiles = 24,
            int minStorageLeftTiles = 16,
            float minLoadFraction = 0.5f)
        {
            if (carriedFractionOfNeed < minLoadFraction)
                return false; // the stack covers too little of the need to be worth interrupting a worker
            if (haulerToStorage < minStorageLeftTiles)
                return false; // hauler is nearly at storage -> let it finish, claim from storage instead
            if (workerToHauler > maxChaseTiles)
                return false; // don't send a worker on a long chase across the colony

            int baseline = haulerToStorage + storageToNeeder; // material's path to the needer via storage
            int intercept = workerToHauler + haulerToNeeder;   // meet the hauler in transit, then deliver
            return intercept < baseline;                       // require a positive trip saving
        }
    }
}
