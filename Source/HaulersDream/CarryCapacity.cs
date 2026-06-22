using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The single source of "the carrying capacity Hauler's Dream uses for this pawn".
    ///
    /// <para>For a player-faction MECHANOID this is the mech's <see cref="StatDefOf.CarryingCapacity"/> stat — the
    /// "carrying capacity" shown on the mech's UI panel (e.g. 52 for a vanilla lifter, 158 for a modded loader) —
    /// times HD's optional per-mech multiplier (<see cref="HaulersDreamSettings.mechHaulMultiplier"/>, default 1.0).
    /// Issue #1: the old code used <see cref="MassUtility.Capacity"/> (<c>BodySize × 35 ≈ 24.5</c> for a lifter) for
    /// every pawn, so a mech's load ignored its actual hauling stat and every mech hauled the same tiny amount; the
    /// previous multiplier-only fix just scaled that wrong base. For humanlikes/animals (and everyone under Combat
    /// Extended) this stays <see cref="MassUtility.Capacity"/> — the inventory-mass model the overload break-even
    /// math and the move-speed StatPart are tuned around. The selection rule is the pure
    /// <see cref="Core.CarryCapacityPolicy"/>.</para>
    ///
    /// <para>Every HD carry/overload decision reads capacity through here (the live ceilings in
    /// <c>BulkHaul</c>/<c>OverloadGate</c>/the bulk-load drivers, AND the per-(pawn,tick) memo in
    /// <see cref="PawnMassCache"/> that <see cref="StatPart_Overload"/> consumes), so the amount a mech is
    /// allowed to load and the move-speed slowdown it then pays are computed from the SAME number — the overload
    /// "carry more / move slower" bargain stays in lockstep. Routing every site through one helper is what keeps
    /// that invariant true.</para>
    /// </summary>
    internal static class CarryCapacity
    {
        /// <summary>
        /// HD's carry capacity for <paramref name="pawn"/> (see the type remarks). NOT memoized — callers needing
        /// the per-(pawn,tick) memo go through <see cref="PawnMassCache"/>, which fills from here so the cached and
        /// live values agree. 0 for a null pawn.
        /// </summary>
        internal static float Of(Pawn pawn)
        {
            if (pawn == null)
                return 0f;
            float massCap = MassUtility.Capacity(pawn);
            var race = pawn.RaceProps;
            bool playerMech = race != null && race.IsMechanoid
                              && pawn.Faction != null && pawn.Faction.IsPlayer;
            // Common path (humanlikes/animals, or anyone under CE): vanilla MassUtility.Capacity, unchanged — and
            // we skip the (costlier) CarryingCapacity stat read entirely. The policy returns the same value here.
            if (!playerMech || CECompat.IsActive)
                return massCap;
            // Player mech, no CE: base the haul ceiling on the mech's UI CarryingCapacity stat, × the mech multiplier.
            var s = HaulersDreamMod.Settings;
            float statCap = pawn.GetStatValue(StatDefOf.CarryingCapacity);
            float mult = s != null ? s.mechHaulMultiplier : 1f;
            return Core.CarryCapacityPolicy.BaseCapacity(isPlayerMech: true, ceActive: false,
                statCarryingCapacity: statCap, massUtilityCapacity: massCap, mechMultiplier: mult);
        }
    }
}
