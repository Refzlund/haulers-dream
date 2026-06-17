using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The single "is Hauler's Dream active on THIS map?" gate. HD's autonomous behaviors stand down on a
    /// non-home (caravan / raid / temporary) map when the player has turned off
    /// <see cref="HaulersDreamSettings.enableOnNonHomeMaps"/> — because there is no player storage there, so
    /// scooped loot would just strand in inventory until the pawn gets home. This was inlined ~10× as
    /// <c>!s.enableOnNonHomeMaps &amp;&amp; !map.IsPlayerHome</c> (with an INCONSISTENT null-guard: some sites
    /// pre-checked <c>map != null</c>, some relied on the caller, one folded a redundant null-check inline).
    /// Centralizing it removes the drift risk and fixes the inconsistent null-handling in one place.
    ///
    /// <para>NULL-MAP HANDLING (decided once, here): a null map is treated as ACTIVE (returns true), so the
    /// gate never silently stands HD down on a null map — downstream code then hits its own null checks. This
    /// matches the only call site that could genuinely see a null map and deliberately did NOT bail
    /// (<see cref="YieldRouter.IsCandidate"/>'s <c>p.Map != null &amp;&amp; ...</c>). Sites that pre-guarantee a
    /// non-null map (the bulk-haul builders, the float-menu providers, the corpse strippers) never pass null,
    /// so the choice is irrelevant for them and behaviour-preserving.</para>
    ///
    /// <para>Settings read live (mirrors <see cref="MasterEnable"/>): when settings aren't loaded yet, HD is
    /// considered active (the stand-down can't fire without a loaded <c>enableOnNonHomeMaps</c>).</para>
    /// </summary>
    public static class MapGate
    {
        /// <summary>True if HD's map-gated behaviors may run on <paramref name="map"/>: always on a player-home
        /// map, on a non-home map only when <see cref="HaulersDreamSettings.enableOnNonHomeMaps"/> is on, and
        /// (by the null-handling above) on a null map. The inverse of the old inline
        /// <c>!s.enableOnNonHomeMaps &amp;&amp; !map.IsPlayerHome</c> stand-down.</summary>
        public static bool HdActiveOnMap(Map map)
        {
            if (map == null)
                return true; // null map -> never silently stand down here; let downstream null checks handle it
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return true; // settings not loaded yet -> the stand-down can't fire without enableOnNonHomeMaps
            return s.enableOnNonHomeMaps || map.IsPlayerHome;
        }
    }
}
