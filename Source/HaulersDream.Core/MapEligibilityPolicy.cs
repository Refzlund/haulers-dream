namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for whether Hauler's Dream's map-gated behaviours may run on a given map (no game
    /// types, so it is unit-tested headlessly and evaluates identically on every multiplayer client). The
    /// game-layer <c>MapGate</c> reads the live map/settings primitives and delegates the actual gate here.
    /// </summary>
    public static class MapEligibilityPolicy
    {
        /// <summary>
        /// Whether HD's map-gated behaviours may run on a map, applied as a short-circuiting ladder:
        /// <list type="number">
        /// <item>a player-home map is ALWAYS active (settings never stand HD down at the home colony);</item>
        /// <item>otherwise, the master off-home toggle gates everything: with it off, HD is fully inert on any
        ///       non-home map;</item>
        /// <item>otherwise, the player-controlled-only scope (for nomad/no-home playstyles) restricts off-home
        ///       activity to maps the player actually controls, standing HD down on transient ambush / enemy /
        ///       pocket maps;</item>
        /// <item>otherwise HD runs (the default, unscoped "work on every non-home map" behaviour).</item>
        /// </list>
        /// </summary>
        /// <param name="isPlayerHome">True if this is a player-home colony map; when true it short-circuits to
        /// active and the remaining parameters are irrelevant.</param>
        /// <param name="isPlayerControlled">True if the player controls this off-home map (a settled/temporary
        /// camp, a player-faction map, or any map with player storage); only consulted when
        /// <paramref name="playerControlledOnly"/> is on. A bare ambush / enemy / pocket map is not controlled.</param>
        /// <param name="enableOnNonHomeMaps">The master "work off the home colony at all" toggle; when off, HD
        /// is inert on every non-home map regardless of the scope below.</param>
        /// <param name="playerControlledOnly">When on, narrows off-home activity to player-controlled maps only;
        /// when off (the default), HD works on every non-home map.</param>
        /// <returns>True if HD's map-gated behaviours are permitted to run on the map.</returns>
        public static bool HdActiveOnMap(bool isPlayerHome, bool isPlayerControlled, bool enableOnNonHomeMaps,
            bool playerControlledOnly)
        {
            if (isPlayerHome)
                return true;
            if (!enableOnNonHomeMaps)
                return false;
            if (playerControlledOnly && !isPlayerControlled)
                return false;
            return true;
        }
    }
}
