namespace HaulersDream.Core
{
    /// <summary>
    /// A Verse-free, unit-pinned model of vanilla <c>JobGiver_DropUnusedInventory</c>'s RAW-FOOD drop loop — the
    /// loop that has dumped HD-scooped harvest yields at a pawn's feet across multiple RimWorld updates
    /// (issues #62 and #87). The runtime guard depends on the exact shape of this loop, and that shape has
    /// silently broken before; pinning it here, with an oracle test over the whole <c>FoodPreferability</c>
    /// range, makes any drift a failing build instead of a player report.
    ///
    /// <para>Vanilla (1.6) runs this once per think pass for a non-drafted player colonist standing in the Home
    /// area:</para>
    /// <code>
    /// if (TicksGame > pawn.mindState.lastInventoryRawFoodUseTick + RawFoodDropDelay)   // 150000 ticks
    ///     for each inventory thing:
    ///         if (thing.def.IsIngestible &amp;&amp; !thing.def.IsDrug &amp;&amp; (int)thing.def.ingestible.preferability &lt;= 5)
    ///             Drop(pawn, thing);
    /// </code>
    /// <para>HD scoops raw crops / milk / eggs into the pawn's inventory (tagged via
    /// <c>CompHauledToInventory</c>) to haul them to storage on its own unload trip; left alone, this loop dumps
    /// them first. HD suppresses the loop the moment the pawn is carrying a tagged stack the loop would drop, by
    /// re-arming <c>lastInventoryRawFoodUseTick</c> to "now" — which makes the gate
    /// <c>TicksGame &gt; now + RawFoodDropDelay</c> false, so the whole loop is skipped while HD cargo is aboard
    /// and resumes normally once the trip empties it.</para>
    /// </summary>
    public static class DropUnusedFoodPolicy
    {
        /// <summary>Vanilla <c>JobGiver_DropUnusedInventory.RawFoodDropDelay</c>: ticks of "no raw-food use"
        /// before the loop drops raw food (~2.5 in-game days). This gate is exactly why the drop surfaces only
        /// on established saves, never on a fresh start.</summary>
        public const int RawFoodDropDelay = 150000;

        /// <summary>The threshold the vanilla loop compares <c>(int)ingestible.preferability</c> against
        /// (<c>&lt;=</c> this is dropped). 5 is <c>FoodPreferability.RawTasty</c>, so raw foods (Undefined..RawTasty,
        /// 0..5) are dropped while cooked meals (MealAwful = 6 and up) are kept.</summary>
        public const int MaxDroppedPreferability = 5;

        /// <summary>
        /// EXACTLY vanilla's per-item food-loop predicate: would the raw-food drop loop drop a stack with these
        /// def properties? <c>isIngestible &amp;&amp; !isDrug &amp;&amp; preferabilityInt &lt;= 5</c>. Pinning it here means a
        /// refactor that weakens HD's suppression — or a vanilla change to the dropped category — trips the oracle
        /// test, instead of surfacing months later as players reporting dropped crops again.
        /// </summary>
        public static bool IsRawFoodDropCandidate(bool isIngestible, bool isDrug, int preferabilityInt)
            => isIngestible && !isDrug && preferabilityInt <= MaxDroppedPreferability;

        /// <summary>
        /// Vanilla's food-loop GATE: would the loop run this think pass? <c>ticksGame &gt; lastInventoryRawFoodUseTick
        /// + RawFoodDropDelay</c>. HD's suppression works by setting <c>lastInventoryRawFoodUseTick = ticksGame</c>,
        /// after which this returns false (the loop is skipped). Pinned so a test proves the re-arm closes the gate
        /// at the exact boundary, and so a change to the delay constant or the comparison can't pass unnoticed.
        /// </summary>
        public static bool FoodLoopWouldRun(int ticksGame, int lastInventoryRawFoodUseTick)
            => ticksGame > lastInventoryRawFoodUseTick + RawFoodDropDelay;
    }
}
