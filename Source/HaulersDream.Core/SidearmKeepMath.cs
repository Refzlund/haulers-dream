namespace HaulersDream.Core
{
    /// <summary>
    /// Pure Simple Sidearms per (def, stuff) "how many of this weapon pair does the pawn keep, and how many are
    /// surplus" arithmetic. The runtime wrapper (<c>InventorySurplus.SurplusOf</c> / <c>SimpleSidearmsCompat</c>)
    /// reads the live SS remembered-weapon count and the equipped primary; this leaf does the integer math, so it
    /// is unit-testable headlessly and multiplayer-deterministic (no game types, no ordering, no side effects).
    ///
    /// The model: SS records one <c>rememberedWeapons</c> entry per equipped primary/sidearm of a pair, and the
    /// pawn wants to keep that many of the pair IN INVENTORY. But the equipped PRIMARY physically lives in
    /// <c>equipment.Primary</c>, NOT <c>inventory.innerContainer</c>, so when the primary is this pair its
    /// remembered entry must not pin an inventory copy (else a hauled duplicate of the primary's pair reads
    /// have(1) minus keep(1) = 0 and never unloads: the "won't put away / re-stows" bug). Sidearms, which DO live
    /// in inventory, are unaffected. Every EXTRA inventory copy above the keep is surplus the unload should put
    /// away, so a looted duplicate of a kept sidearm is unloaded while the one wanted copy stays (#222).
    /// </summary>
    public static class SidearmKeepMath
    {
        /// <summary>
        /// Units of a (def, stuff) weapon pair the pawn keeps IN INVENTORY: the SS remembered count MINUS the
        /// equipped primary when the primary is this pair (the primary satisfies one remembered entry from the
        /// equipment slot, not from inventory). Floored at 0, so a remembered count of 0 (or a primary-only pair
        /// with no sidearm entries) keeps nothing. Mirrors <c>SimpleSidearmsCompat.InventoryKeepCount</c> exactly.
        /// </summary>
        /// <param name="rememberedCount">SS rememberedWeapons entries matching this exact (def, stuff). Never
        /// negative in practice (it is a count); a negative input is floored to 0 by the return.</param>
        /// <param name="primaryMatchesPair">True when the pawn's equipped primary is this exact (def, stuff), so
        /// one remembered entry is satisfied by equipment and must not pin an inventory copy.</param>
        /// <returns>Inventory keep count for the pair, in <c>[0, rememberedCount]</c>.</returns>
        public static int KeepForPair(int rememberedCount, bool primaryMatchesPair)
        {
            int keep = rememberedCount - (primaryMatchesPair ? 1 : 0);
            return keep < 0 ? 0 : keep;
        }

        /// <summary>
        /// Units of ONE inventory stack of a (def, stuff) weapon pair that are surplus the unload should move:
        /// everything the pawn holds of the pair above its inventory keep (<see cref="KeepForPair"/>), attributed
        /// to this stack up to its own size. Weapons are stackLimit 1, so <paramref name="stackCount"/> is normally
        /// 1 and each Thing contributes 0 or 1; the clamp keeps the math correct for any stack size. Mirrors the
        /// old inline <c>InventorySurplus.SurplusOf</c> SS branch (<c>over = pairHave - pairKeep;
        /// over &lt;= 0 ? 0 : Min(stackCount, over)</c>) byte-for-byte.
        /// </summary>
        /// <param name="rememberedCount">SS rememberedWeapons entries matching this (def, stuff).</param>
        /// <param name="primaryMatchesPair">True when the equipped primary is this (def, stuff).</param>
        /// <param name="pairHave">Total units of this (def, stuff) the pawn holds in inventory (all its stacks).</param>
        /// <param name="stackCount">This stack's unit count (the cap on what this call can contribute).</param>
        /// <returns>Surplus units of this stack, in <c>[0, stackCount]</c> (0 when the pawn holds at most the keep).</returns>
        public static int SurplusForPair(int rememberedCount, bool primaryMatchesPair, int pairHave, int stackCount)
        {
            int keep = KeepForPair(rememberedCount, primaryMatchesPair);
            int over = pairHave - keep;
            if (over <= 0)
                return 0;
            return over < stackCount ? over : stackCount;
        }
    }
}
