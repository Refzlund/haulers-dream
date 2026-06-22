namespace HaulersDream.Core
{
    /// <summary>
    /// Picks the BASE carrying capacity Hauler's Dream uses for a pawn (issue #1).
    ///
    /// <para>A hauler MECHANOID's haul ceiling should track the "carrying capacity" shown on its UI panel
    /// (RimWorld's <c>CarryingCapacity</c> stat — e.g. 52 for a vanilla lifter, 158 for a modded loader), NOT
    /// vanilla <c>MassUtility.Capacity</c> (<c>BodySize × 35 ≈ 24.5</c> for a lifter). The old code used
    /// <c>MassUtility.Capacity</c> for every pawn, so a mech's load ignored its actual hauling stat and every
    /// mech hauled the same tiny amount regardless of how capable it was (a flat per-mech multiplier — the
    /// previous fix — still scaled the wrong base). Reading the <c>CarryingCapacity</c> stat makes the mech's HD
    /// load match the number the player sees on the mech.</para>
    ///
    /// <para>Humanlikes and animals KEEP the inventory-mass model (<c>MassUtility.Capacity</c>) that the overload
    /// break-even math (<see cref="OverloadPolicy"/>) and the move-speed StatPart are tuned around — switching
    /// them would silently change every colonist's haul size and the encumbrance curve, a balance change well
    /// beyond this bug. Under Combat Extended, <c>MassUtility.Capacity</c> IS CE's CarryWeight model, so always
    /// defer to it (mixing in <c>CarryingCapacity</c> would desync from CE's weight + bulk penalty).</para>
    ///
    /// <para>DELIBERATE dimensional note: <c>CarryingCapacity</c> is, in vanilla terms, a hand-haul item-COUNT stat
    /// (base 75), whereas HD's capacity number is consumed as a KG ceiling. We use the stat value directly as the
    /// ceiling anyway, because the request is precisely "let a mech's load equal the carrying-capacity number on its
    /// panel" (e.g. 52 / 158). For a vanilla mech this is ≈2.14× the old mass-model capacity (both stats scale with
    /// BodySize off different bases), so it reads as a sane "haul more" bump rather than a count; the overload curve
    /// keeps an overfilled mech slow, and <c>mechHaulMultiplier</c> dials it. A mod that gives a mech a very large
    /// <c>CarryingCapacity</c> will get a correspondingly large kg ceiling — intended (that mech is built to haul a
    /// lot), and a player can lower the multiplier.</para>
    ///
    /// <para>Pure, so it is unit-tested headlessly; the game-coupled stat reads live in
    /// <c>HaulersDream.CarryCapacity</c>, which feeds this the resolved inputs.</para>
    /// </summary>
    public static class CarryCapacityPolicy
    {
        /// <param name="isPlayerMech">The pawn is a player-faction mechanoid.</param>
        /// <param name="ceActive">Combat Extended is loaded (owns the capacity model).</param>
        /// <param name="statCarryingCapacity"><c>pawn.GetStatValue(StatDefOf.CarryingCapacity)</c> — the UI value.</param>
        /// <param name="massUtilityCapacity"><c>MassUtility.Capacity(pawn)</c> — vanilla <c>BodySize × 35</c> (CE CarryWeight under CE).</param>
        /// <param name="mechMultiplier">The optional per-mech multiplier (<c>HaulersDreamSettings.mechHaulMultiplier</c>); ≤ 0 is treated as 1.</param>
        public static float BaseCapacity(bool isPlayerMech, bool ceActive,
            float statCarryingCapacity, float massUtilityCapacity, float mechMultiplier)
        {
            // CE owns the capacity model end to end — never mix CarryingCapacity into it.
            if (ceActive)
                return massUtilityCapacity;
            if (isPlayerMech)
            {
                float mult = mechMultiplier > 0f ? mechMultiplier : 1f;
                return statCarryingCapacity * mult;
            }
            return massUtilityCapacity;
        }
    }
}
