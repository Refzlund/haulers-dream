using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The single "is Hauler's Dream active on THIS map?" gate. HD is ALWAYS active on a player-home colony map.
    /// Off the home colony its behaviour is scoped by two settings:
    /// <list type="bullet">
    /// <item><see cref="HaulersDreamSettings.enableOnNonHomeMaps"/> (the master off-home toggle, default on):
    ///       with it off, HD is fully inert on every non-home (caravan / raid / temporary) map, because such a
    ///       map has no player storage, so scooped loot would just strand in inventory until the pawn gets
    ///       home;</item>
    /// <item><see cref="HaulersDreamSettings.nonHomeMapsPlayerControlledOnly"/> (default off): for a nomad /
    ///       no-home playstyle, restricts off-home activity to maps the player actually controls (see
    ///       <see cref="IsPlayerControlled"/>: a settled or temporary camp, a player-faction site, any map with
    ///       player storage), standing HD down on transient ambush / enemy / pocket maps. Default off preserves
    ///       the shipped "work on every non-home map" behaviour for existing saves.</item>
    /// </list>
    /// The core stand-down was inlined ~10x as <c>!s.enableOnNonHomeMaps &amp;&amp; !map.IsPlayerHome</c> (with an
    /// INCONSISTENT null-guard: some sites pre-checked <c>map != null</c>, some relied on the caller, one folded
    /// a redundant null-check inline). Centralizing it here removed the drift risk, and the player-controlled
    /// scope now layers on in the same place. The pure scope ladder lives in
    /// <see cref="Core.MapEligibilityPolicy.HdActiveOnMap"/>.
    ///
    /// <para>NULL-MAP HANDLING (decided once, here): a null map is treated as ACTIVE (returns true), so the
    /// gate never silently stands HD down on a null map; downstream code then hits its own null checks. This
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
        /// <summary>True if HD's map-gated behaviors may run on <paramref name="map"/>. Always active on a
        /// player-home map, and (by the null-handling above) on a null map or before settings load. On a
        /// non-home map: active only when <see cref="HaulersDreamSettings.enableOnNonHomeMaps"/> is on, and when
        /// <see cref="HaulersDreamSettings.nonHomeMapsPlayerControlledOnly"/> is on, only if the map is
        /// <see cref="IsPlayerControlled"/> (so a nomad can keep HD off transient ambush / enemy maps). The
        /// scope decision itself is the pure <see cref="Core.MapEligibilityPolicy.HdActiveOnMap"/>.</summary>
        public static bool HdActiveOnMap(Map map)
        {
            if (map == null)
                return true; // null map -> never silently stand down here; let downstream null checks handle it
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return true; // settings not loaded yet -> the stand-down can't fire without enableOnNonHomeMaps
            // Only run IsPlayerControlled (a HasPlayerStorage slot-group scan) when the scope toggle is on: this
            // is a hot initiation gate the repo tracks for microstutter, and the policy discards the controlled
            // arg unless playerControlledOnly is on, so the short-circuit is behaviour-identical.
            bool controlled = s.nonHomeMapsPlayerControlledOnly && IsPlayerControlled(map);
            return MapEligibilityPolicy.HdActiveOnMap(map.IsPlayerHome, controlled,
                s.enableOnNonHomeMaps, s.nonHomeMapsPlayerControlledOnly);
        }

        /// <summary>True if <paramref name="map"/> is one the PLAYER controls, for HD's off-home scope: a
        /// player-home map, a settled nomad camp / player-faction site (<see cref="Map.ParentFaction"/> is the
        /// player), or any map with player storage (<see cref="HasPlayerStorage"/>, which catches a settled camp
        /// the player gave shelves/zones or a Vehicle Framework RV interior). A bare ambush / enemy / pocket map
        /// with none of these does not count. <see cref="Map.ParentFaction"/> can be null; the null-safe storage
        /// fallback and the <c>map != null</c> guard keep this total.</summary>
        public static bool IsPlayerControlled(Map map)
            => map != null && (map.IsPlayerHome || map.ParentFaction == Faction.OfPlayer || HasPlayerStorage(map));

        /// <summary>
        /// True if a carrying pawn on <paramref name="map"/> should unload to MAP STORAGE (HD's storage-unload
        /// driver) rather than onto a pack animal. The old code treated every map as a strict binary —
        /// <c>IsPlayerHome</c> (storage) vs <c>!IsPlayerHome</c> (pack-animal safeguard); a first fix narrowed the
        /// non-home storage case to <c>IsPocketMap &amp;&amp; HasPlayerStorage</c> to catch a Vehicle Framework RV
        /// interior — but that was WRONG: a VF RV interior is NOT a pocket map (<see cref="Map.IsPocketMap"/> is a
        /// hard <c>info.isPocketMap</c> flag VF never sets), so the <c>IsPocketMap</c> conjunction excluded the
        /// exact case it was meant to fix and the pick-up/drop loop persisted. The real discriminator is simply
        /// "does this map have PLAYER storage?", not "is it a pocket map". So:
        /// <list type="bullet">
        /// <item>player-home map → always true (unchanged);</item>
        /// <item>any OTHER map WITH player storage (a VF RV interior, a settled-in away camp the player gave
        ///       shelves/zones) → true — deliver to that storage (the FIX);</item>
        /// <item>a storage-less map (a transient caravan/raid camp, a hostile pocketmap like an undercave) → false
        ///       — the loot still goes onto a pack animal / rides home in inventory, exactly as before.</item>
        /// </list>
        /// The per-item "does THIS stack fit / where" decision stays the unload driver's
        /// <c>TryFindBestBetterStorageFor</c>; this only decides which ROUTE the autonomous triggers take. When a
        /// storage-having map is momentarily full the driver keeps the load tagged in inventory (no drop, no
        /// loop), so the cheap "any storage at all" test below is sufficient — it need not prove free space.
        /// </summary>
        public static bool ShouldUnloadToStorage(Map map)
        {
            if (map == null)
                return false;
            if (map.IsPlayerHome)
                return true;
            // Player storage present (anywhere, not just a pocket map) -> deliver there; otherwise the loot rides a
            // pack animal / stays in inventory. A VF RV interior is NOT a pocket map, so the old IsPocketMap gate
            // excluded it and the pawn looped pick-up -> drop with no reachable carrier; keying purely on storage
            // presence fixes that while leaving genuine storage-less caravan/raid maps on the pack-animal path.
            return HasPlayerStorage(map);
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
