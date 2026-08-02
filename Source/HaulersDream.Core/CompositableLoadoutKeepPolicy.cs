namespace HaulersDream.Core
{
    /// <summary>
    /// Pure "how much does HD keep in inventory on behalf of a Compositable Loadouts loadout" arithmetic. The
    /// runtime wrapper (<c>CompositableLoadoutsCompat.KeepCount</c>) reads CL's live per-pawn loadout reflectively
    /// and the pawn's equipped weapons from vanilla; this leaf does the integer decisions, so they are unit-testable
    /// headlessly and multiplayer-deterministic (no game types, no ordering, no side effects).
    ///
    /// THE MODEL, stated once: HD's keep exists for exactly one reason — to stop the #200 unload↔re-fetch loop —
    /// so it must equal the units the keeper mod would RE-FETCH, no more. Keeping a unit the keeper would never
    /// re-fetch buys nothing and costs a permanent strand: HD's own hauled cargo is pinned in the pack forever,
    /// because the pin is HD's and only HD would have released it. Two rules follow.
    ///
    /// <list type="number">
    /// <item><see cref="ShieldsDef"/> — CL never re-fetches APPAREL into inventory (it sends the colonist to WEAR
    /// it off the map instead), so a loadout apparel entry must contribute no keep at all. Issue #233: three
    /// duster copies HD bulk-hauled into a pack were never unloaded, because the loadout's duster entry was
    /// charged in full against the pack while the WORN duster satisfied it invisibly.</item>
    /// <item><see cref="ContributedKeep"/> — a loadout entry the pawn already satisfies from its EQUIPMENT slots
    /// is discharged by that equipment, so it must not also pin an inventory copy. Same shape, same reason, as
    /// <see cref="SidearmKeepMath.KeepForPair"/> subtracting the equipped primary from the Simple Sidearms keep.</item>
    /// </list>
    ///
    /// EVIDENCE GRADE, and it applies to BOTH rules above: every statement here about what CL itself does ("never
    /// re-fetches apparel", "counts gear as inventory PLUS equipment") is SOURCE-READ from
    /// github.com/simplyWiri/Loadout-Compositing and is NOT decompile-verified — CL is not installed on the machine
    /// this was written on. The specific CL members those claims rest on, the reasoning for why the design stays safe
    /// even if a read is wrong, and the known residual are all in <c>CompositableLoadoutsCompat.KeepCount</c>'s doc;
    /// read it before changing either rule. This file is the policy of record and the only part with tests, so it
    /// must not read as if the claims were established fact.
    ///
    /// WHAT THIS POLICY DOES NOT OWN: the final <c>held - keep</c> subtraction. That stays in
    /// <c>InventorySurplus.SurplusOf</c>, because the keep it subtracts is a SUM of five contributions (vanilla
    /// drug policy, vanilla inventoryStock, CE loadout, Item Policy, CL) and CL owns only one term. Re-deriving
    /// the subtraction here would misrepresent the shipped math as if CL's term were the whole keep.
    /// </summary>
    public static class CompositableLoadoutKeepPolicy
    {
        /// <summary>
        /// Whether a CL loadout entry for a def of this kind may contribute ANY keep — true for everything except
        /// apparel. CL satisfies an apparel entry by making the colonist WEAR a garment lying on the map, never by
        /// stocking a spare in inventory, so an apparel entry can pin nothing without stranding HD's cargo (#233).
        /// </summary>
        /// <param name="defIsApparel">The def's vanilla <c>ThingDef.IsApparel</c>. Passed as a plain bool, not a
        /// def, deliberately: the call site reads it from VANILLA before touching CL's reflected API, so the
        /// apparel decision can never be blamed on — or disabled by — a CL API fault.</param>
        /// <returns>True when a loadout entry for the def may be shielded; false for apparel (keep nothing).</returns>
        public static bool ShieldsDef(bool defIsApparel) => !defIsApparel;

        /// <summary>
        /// The units ONE loadout entry asks for, floored at 0. A stray negative desired-count must never SUBTRACT
        /// from a sibling entry's units when a caller sums entries for the same def — a def can legitimately appear
        /// in several entries at once (CL's <c>Loadout.Items</c> is a <c>SelectMany</c> over the pawn's active tags,
        /// so two tags naming the same def sum), and one bad entry silently cancelling another would leak surplus
        /// into the unload. Mirrors the same clamp in <c>ItemPolicyCompat.KeepCount</c>.
        /// </summary>
        /// <param name="quantity">The entry's desired count as read from CL (its <c>Item.Quantity</c>).</param>
        /// <returns><paramref name="quantity"/> when non-negative, otherwise 0.</returns>
        public static int EntryUnits(int quantity) => quantity < 0 ? 0 : quantity;

        /// <summary>
        /// The keep CL's loadout actually contributes for one def: what the loadout WANTS minus what the pawn
        /// already carries in its EQUIPMENT slots. CL counts a pawn's gear as inventory PLUS equipment, so a
        /// wielded loadout weapon discharges its entry; HD's surplus math counts inventory only, so without this
        /// subtraction the whole entry is charged to the pack and a hauled spare of the same def is pinned forever.
        /// Floored at 0 at both ends, so an over-satisfied entry (more equipped than wanted) keeps nothing rather
        /// than lending a negative keep to another contribution in <c>InventorySurplus.KeepCountOf</c>'s sum.
        /// </summary>
        /// <param name="wantedUnits">Units of the def the loadout asks for, summed across its entries (each via
        /// <see cref="EntryUnits"/>). Never negative in practice; a negative is absorbed by the return's floor.</param>
        /// <param name="equippedUnits">Units of the same def in the pawn's equipment slots. Negative is treated as
        /// 0 (a corrupt count must not INFLATE the keep, which is the strand-causing direction).</param>
        /// <returns>The keep to contribute, in <c>[0, wantedUnits]</c>.</returns>
        public static int ContributedKeep(int wantedUnits, int equippedUnits)
        {
            if (equippedUnits < 0)
                equippedUnits = 0;
            int keep = wantedUnits - equippedUnits;
            return keep < 0 ? 0 : keep;
        }
    }
}
