namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for the per-pawn, per-def "keep an amount in inventory" feature (issue #197): the
    /// player pins a colonist to hold up to N units of a def (e.g. 50 silver), and Hauler's Dream keeps that many
    /// and treats only the excess as surplus to unload. Replaces the old whole-stack "keep this exact Thing" model
    /// (a <c>HashSet&lt;Thing&gt;</c>) with an amount the player can dial with a slider and see/edit on the Gear tab.
    ///
    /// This holds ONLY the surplus arithmetic (the risky, MP-deterministic part); the game layer
    /// (<c>CompHauledToInventory</c> / <c>InventorySurplus</c>) owns the def→count map, the prune-when-empty rule,
    /// and the slider/Gear-tab UI. No game types, unit-tested headlessly.
    ///
    /// Semantics mirror the existing per-def <c>KeepAtMost</c> unload rule (carry at most N of a def, unload the
    /// rest), so the two compose predictably: a per-pawn keep-count is just a <c>KeepAtMost</c> that lives on the
    /// pawn instead of in the global rule table, and it takes precedence (the player kept THIS pawn's stock
    /// deliberately).
    /// </summary>
    public static class KeepCountPolicy
    {
        /// <summary>
        /// The surplus units to unload from ONE stack of a kept def, given how many of that def the pawn holds in
        /// total and how many the player pinned to keep. Mirrors the <c>KeepAtMost</c> rule: unload
        /// <c>held - keptCount</c> across the def, attributed to this stack up to its own size, so summing over
        /// every stack of the def unloads exactly the excess (a merged single stack — the common case — unloads
        /// <c>held - keptCount</c> directly).
        /// </summary>
        /// <param name="keptCount">Units of the def to hold (the player's pin). Negative is treated as 0.</param>
        /// <param name="heldOfDef">Total units of the def currently in the pawn's inventory (all its stacks).</param>
        /// <param name="stackCount">This stack's unit count (the cap on what this call can contribute).</param>
        /// <returns>Units of this stack that are surplus (0 when the pawn holds at most the kept count).</returns>
        public static int SurplusForKeptDef(int keptCount, int heldOfDef, int stackCount)
        {
            if (keptCount < 0)
                keptCount = 0;
            int over = heldOfDef - keptCount;
            if (over <= 0)
                return 0;
            return over < stackCount ? over : stackCount;
        }

        /// <summary>
        /// Clamp a requested keep amount into the valid range for a def the pawn could hold. Used by the slider
        /// (order and Gear tab) so a stored count never goes negative or exceeds a sane ceiling. A count of 0 means
        /// "not kept" (the caller removes the entry).
        /// </summary>
        /// <param name="requested">The raw amount the player asked to keep.</param>
        /// <param name="max">The upper bound offered (e.g. the clicked stack size, or a def's stack limit).</param>
        /// <returns>The requested amount clamped to <c>[0, max]</c> (and <c>max</c> floored at 0).</returns>
        public static int ClampKeepAmount(int requested, int max)
        {
            if (max < 0)
                max = 0;
            if (requested < 0)
                return 0;
            return requested > max ? max : requested;
        }
    }
}
