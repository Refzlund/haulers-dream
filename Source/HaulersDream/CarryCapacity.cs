using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The single source of "the carrying capacity Hauler's Dream uses for this pawn": vanilla
    /// <see cref="MassUtility.Capacity"/> with HD's optional per-player-mechanoid multiplier
    /// (<see cref="HaulersDreamSettings.mechHaulMultiplier"/>) applied.
    ///
    /// <para>Every HD carry/overload decision reads capacity through here (the live ceilings in
    /// <c>BulkHaul</c>/<c>OverloadGate</c>/the bulk-load drivers, AND the per-(pawn,tick) memo in
    /// <see cref="PawnMassCache"/> that <see cref="StatPart_Overload"/> consumes), so the amount a mech is
    /// allowed to load and the move-speed slowdown it then pays are computed from the SAME number — the overload
    /// "carry more / move slower" bargain stays in lockstep. Routing every site through one helper is what keeps
    /// that invariant true when the multiplier is non-default.</para>
    ///
    /// <para>Default 1.0 ⇒ returns <c>MassUtility.Capacity(pawn)</c> unchanged (byte-identical to before this
    /// setting existed). The multiplier applies ONLY to player-faction mechanoids, and stands down entirely under
    /// Combat Extended — CE replaces the vanilla encumbrance/slowdown model with its own (weight + bulk), so
    /// scaling the ceiling there would desync from CE's penalty.</para>
    /// </summary>
    internal static class CarryCapacity
    {
        /// <summary>
        /// Live <c>MassUtility.Capacity(pawn)</c> (CE's CarryWeight under CE) with HD's mech multiplier applied.
        /// NOT memoized — callers needing the per-(pawn,tick) memo go through <see cref="PawnMassCache"/>, which
        /// fills from here so the cached and live values agree. 0 for a null / non-carrying pawn (matches
        /// <c>MassUtility</c>).
        /// </summary>
        internal static float Of(Pawn pawn)
        {
            float cap = MassUtility.Capacity(pawn);
            var s = HaulersDreamMod.Settings;
            // Fast path: default multiplier, no settings, or CE active → vanilla value unchanged.
            if (s == null || s.mechHaulMultiplier == 1f || CECompat.IsActive)
                return cap;
            var race = pawn?.RaceProps;
            if (race != null && race.IsMechanoid && pawn.Faction != null && pawn.Faction.IsPlayer)
                cap *= s.mechHaulMultiplier;
            return cap;
        }
    }
}
