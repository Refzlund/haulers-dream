namespace HaulersDream.Core
{
    /// <summary>Where a single unload step puts one stack.</summary>
    public enum UnloadPlacement
    {
        /// <summary>Real storage (stockpile / shelf / container) was found — carry it there.</summary>
        Deliver,
        /// <summary>Non-home map: the load rides home with the caravan, never dumped.</summary>
        KeepInInventory,
        /// <summary>No storage, but a good home-area floor cell is within reach — carry and place.</summary>
        PlaceOnNearbyHomeCell,
        /// <summary>Nowhere at all — put it down where the pawn stands, preferring the home area.</summary>
        DropAtFeet
    }

    /// <summary>
    /// Pure placement decision for one unload step (issue #231). The runtime
    /// (<c>JobDriver_UnloadHauledInventory</c>) gathers the three facts and acts.
    ///
    /// DELIBERATELY there is NO "haul it somewhere outside the home area" outcome. Vanilla
    /// <c>StoreUtility.TryFindStoreCellNearColonyDesperate</c>'s third leg
    /// (<c>RCellFinder.TryFindRandomSpotJustOutsideColony</c>) picks a random cell in a large OUTDOOR
    /// district that TOUCHES THE MAP EDGE, with no home-area test whatsoever. Vanilla reaches it only
    /// behind the rare, event-driven <c>UnloadEverything</c> flag and only once per job; Hauler's Dream
    /// ran it per tagged stack, in a loop, for every hauling pawn — re-rolling a new random cell from each
    /// new position — which is precisely the reported "items scattered completely outside the Home area".
    /// </summary>
    public static class UnloadFallbackPolicy
    {
        /// <summary>Radial cells tried around the carrier for the home-area fallback cell. Vanilla
        /// leg-2 parity (<c>StoreUtility.TryFindStoreCellNearColonyDesperate</c>, RimWorld 1.6:
        /// <c>for (int i = -4; i &lt; 20; i++)</c>) — radius ≈ 2.5.</summary>
        public const int RadialCellsToTry = 20;

        /// <summary>Leading iterations that pick a RANDOM index in [0, RandomLeadTries] instead of a
        /// sequential one, so two pawns unloading in the same spot don't always target the same cell.
        /// Vanilla leg-2 parity (the four <c>i &lt; 0</c> iterations).</summary>
        public const int RandomLeadTries = 4;

        /// <summary>First match wins; mirrors the driver's branch order exactly.</summary>
        /// <param name="hasStorageDestination">True when a real stockpile / shelf / container accepted the
        /// stack — the only outcome that moves it to actual storage.</param>
        /// <param name="onPlayerHomeMap">True on the player's settled map. False on a caravan camp / temporary
        /// map, where dropping the load abandons it when the caravan leaves.</param>
        /// <param name="hasNearbyHomeCell">True when the home-area radial scan around the carrier found a
        /// reachable, non-storage floor cell that would accept the stack.</param>
        /// <returns>The placement the driver should perform for this stack.</returns>
        public static UnloadPlacement Choose(bool hasStorageDestination, bool onPlayerHomeMap, bool hasNearbyHomeCell)
        {
            if (hasStorageDestination)
                return UnloadPlacement.Deliver;
            if (!onPlayerHomeMap)
                return UnloadPlacement.KeepInInventory;
            if (hasNearbyHomeCell)
                return UnloadPlacement.PlaceOnNearbyHomeCell;
            return UnloadPlacement.DropAtFeet;
        }

        /// <summary>
        /// How many units the UNCONSTRAINED second placement pass may still drop, after a home-area-only first
        /// pass that reported failure but may have placed part of the stack anyway.
        ///
        /// BOTH terms of the clamp are load-bearing, and each alone is a real bug:
        /// <list type="bullet">
        /// <item>Subtracting what already landed (<c>requested - placed</c>) is what stops the second pass
        /// silently OVER-DROPPING. Clamping to the stack alone (<c>Min(requested, stackCountAfter)</c>) re-drops
        /// the full request even though part of it is already on the ground, so more units leave the pawn than
        /// were ever asked for — quietly shaving personal keep-stock off the top of the stack.</item>
        /// <item>Clamping to what physically remains (<c>stackCountAfter</c>) is what stops the red
        /// "Tried to drop N of X while only having M" error vanilla's <c>ThingOwner.TryDrop</c> logs. Using
        /// <c>requested - placed</c> alone re-introduces it whenever the caller's requested count exceeds the
        /// stack (a surplus figure computed before the stack shrank, or a foreign mod mutating it mid-drop).</item>
        /// </list>
        /// </summary>
        /// <param name="requested">Units the caller originally asked to drop across both passes. The result can
        /// never exceed this, so the two passes together never place more than was asked for.</param>
        /// <param name="stackCountBefore">The stack's count immediately BEFORE the first pass ran.</param>
        /// <param name="stackCountAfter">The stack's count now. Ignored when <paramref name="stackGone"/> is true
        /// (the value is meaningless once the stack has left the container).</param>
        /// <param name="stackGone">True when the stack was destroyed or is no longer in the container — the first
        /// pass placed or absorbed all of it, so there is nothing left for a second pass to do.</param>
        /// <returns>Units for the second pass, always in <c>[0, requested]</c> and never above
        /// <paramref name="stackCountAfter"/>. 0 means "don't run a second pass".</returns>
        public static int RemainingToDrop(int requested, int stackCountBefore, int stackCountAfter, bool stackGone)
        {
            if (stackGone)
                return 0;
            int placed = stackCountBefore - stackCountAfter;
            int left = requested - placed;
            // A stack that GREW during the first pass (a foreign patch on GenPlace.TryPlaceThing, or a placedAction
            // topping the inventory stack up) makes `placed` negative, which would otherwise WIDEN the request past
            // what the caller asked for and drop the difference out of the pawn's keep-stock. Vanilla alone cannot
            // produce this, but a heavy mod list is this mod's design point, so clamp both directions.
            if (left > requested)
                left = requested;
            if (left > stackCountAfter)
                left = stackCountAfter;
            return left < 0 ? 0 : left;
        }

        /// <summary>Should an at-feet drop first try a home-area-only placement before falling back to an
        /// unconstrained one? Only where "home area" is meaningful: the player's home map, with a home
        /// area actually painted. Off the home map (a caravan camp) the home grid is empty, so gating on
        /// it would reject every cell and waste a placement pass.</summary>
        /// <param name="onPlayerHomeMap">True on the player's settled map (see <see cref="Choose"/>).</param>
        /// <param name="homeAreaHasAnyCells">True when the map's Home area actually has at least one painted
        /// cell — an empty grid would reject every candidate, so the constrained pass is skipped.</param>
        /// <returns>True to run the home-area-constrained placement pass first.</returns>
        public static bool PreferHomeAreaDrop(bool onPlayerHomeMap, bool homeAreaHasAnyCells)
            => onPlayerHomeMap && homeAreaHasAnyCells;
    }
}
