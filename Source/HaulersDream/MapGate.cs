using RimWorld;
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

        /// <summary>
        /// True if a carrying pawn on <paramref name="map"/> should unload to MAP STORAGE (HD's storage-unload
        /// driver) rather than onto a pack animal. The old code treated every map as a strict binary —
        /// <c>IsPlayerHome</c> (storage) vs <c>!IsPlayerHome</c> (pack-animal safeguard) — but a Vehicle Framework
        /// RV interior is the unhandled THIRD kind: a PERSISTENT pocket sub-map that is <c>!IsPlayerHome</c> yet
        /// full of player shelves/zones. Routing such a pawn straight to the pack-animal path dead-ended (no
        /// carrier reachable inside the RV → nothing unloaded → it kept scooping and looping). So:
        /// <list type="bullet">
        /// <item>player-home map → always true (unchanged);</item>
        /// <item>non-home POCKET map WITH player storage (an RV interior) → true — the FIX: deliver to its shelves;</item>
        /// <item>everything else (transient caravan/raid camp, a storage-less hostile pocketmap like an undercave)
        ///       → false — the loot still goes onto a pack animal / rides home in inventory, exactly as before.</item>
        /// </list>
        /// The per-item "does THIS stack fit / where" decision stays the unload driver's
        /// <c>TryFindBestBetterStorageFor</c>; this only decides which ROUTE the autonomous triggers take. When a
        /// storage-having pocketmap is momentarily full the driver keeps the load tagged in inventory (no drop, no
        /// loop), so the cheap "any storage at all" test below is sufficient — it need not prove free space.
        /// </summary>
        public static bool ShouldUnloadToStorage(Map map)
        {
            if (map == null)
                return false;
            if (map.IsPlayerHome)
                return true;
            return map.IsPocketMap && HasPlayerStorage(map);
        }

        /// <summary>True if <paramref name="map"/> has at least one PLAYER storage destination — a stockpile/storage
        /// zone (player-made by definition) or a player-faction storage building (a shelf). Deliberately ignores a
        /// hostile pocketmap's own enemy shelves so loot is never routed into an enemy base's storage. Cheap:
        /// returns on the first qualifying slot group.</summary>
        public static bool HasPlayerStorage(Map map)
        {
            var mgr = map?.haulDestinationManager;
            var groups = mgr?.AllGroupsListForReading;
            if (groups == null)
                return false;
            for (int i = 0; i < groups.Count; i++)
            {
                var parent = groups[i]?.parent;
                if (parent == null)
                    continue;
                if (parent is Zone)
                    return true; // a stockpile/storage zone is player-owned by construction
                if (parent is Thing th && th.Faction == Faction.OfPlayer)
                    return true; // a player-faction storage building (shelf)
            }
            return false;
        }
    }
}
