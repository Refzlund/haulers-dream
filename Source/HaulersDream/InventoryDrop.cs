using System;
using HaulersDream.Core;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The ONE way the UNLOAD puts an inventory stack down on the ground (issue #231).
    ///
    /// Every unload path that has to set a load down — a failed destination reservation, a pawn that cannot
    /// manipulate, or genuinely nowhere to store it — goes through <see cref="TryDropPreferHome"/> instead of
    /// calling <c>ThingOwner.TryDrop</c> directly, so the drop PREFERS the Home area everywhere rather than in
    /// one branch. It never carries the load off to the wilderness: that was vanilla's
    /// <c>RCellFinder.TryFindRandomSpotJustOutsideColony</c> leg, which this mod no longer uses (see
    /// <see cref="InventorySurplus.TryFindDesperateHomeAreaCell"/>).
    ///
    /// → NOTE: this is deliberately NOT the only <c>TryDrop</c> in the mod. The softlock breaker
    ///   (<c>HaulersDreamGameComponent.Softlock</c>'s <c>DropTrackedSnapshot</c>, shared with the mech-shed
    ///   pre-charge hook) drops tagged cargo raw, at the pawn's feet, on purpose: it is an emergency unstick
    ///   whose whole job is to empty the pack cheaply RIGHT HERE and abort on the first failure. Sending it
    ///   through a radius-12.9 home-preferring search — which can legitimately pick a cell in another room —
    ///   would make an unstick slower and likelier to fail, for no #231 benefit: dropping at the feet is the
    ///   SAFE direction already, since #231 is about a pawn CARRYING a load far outside the Home area. Any NEW
    ///   drop site in the unload path belongs here; that one is a considered exception.
    ///
    /// → KEY: the preference is expressed through vanilla's OWN <c>nearPlaceValidator</c>, threaded
    ///   <c>ThingOwner.TryDrop</c> → <c>GenDrop.TryDropSpawn</c> → <c>GenPlace.TryPlaceThing</c> →
    ///   <c>TryFindPlaceSpotNear</c> → <c>PlaceSpotQualityAt</c>. That search reaches
    ///   <c>PlaceNearMaxRadialCells</c> (radius 12.9) and ranks a cell already holding a stackable same-def
    ///   stack as <c>Perfect</c> — so repeated no-storage drops MERGE INTO ONE GROWING PILE inside the Home
    ///   area instead of speckling the floor.
    /// → KEY: the home-only pass is ALWAYS followed by an unconstrained one. The preference must never be able
    ///   to strand or lose an item; if the Home area genuinely cannot take it, the load is set down exactly
    ///   where it would have been without this mod.
    /// → GOTCHA: a failed <c>TryDrop</c> can still have placed PART of the stack (vanilla's Near loop spawns one
    ///   stack-limit chunk at a time and returns false for the remainder) and reports no count of what it
    ///   managed. The second pass therefore re-derives its amount from a before/after stack snapshot through
    ///   <see cref="UnloadFallbackPolicy.RemainingToDrop"/>, which both subtracts what already landed (else the
    ///   two passes together drop MORE than was requested) and clamps to what physically remains (else
    ///   <c>ThingOwner.TryDrop</c> posts a red "Tried to drop N of X while only having M" error).
    /// </summary>
    internal static class InventoryDrop
    {
        /// <summary>
        /// Put <paramref name="count"/> units of <paramref name="thing"/> down at the pawn's position,
        /// preferring a cell inside the Home area and falling back to an unconstrained placement so the load is
        /// never stranded.
        /// </summary>
        /// <param name="pawn">The carrying pawn; its inventory is the source and its position the drop centre.</param>
        /// <param name="thing">The inventory stack to set down. Must be in the pawn's inventory.</param>
        /// <param name="count">Units to drop. Re-clamped internally if a partial placement already consumed some.</param>
        /// <param name="site">Short call-site tag for the debug trail (e.g. "nowhere", "reserve-failed-storage") —
        /// so an issue report shows WHICH unload branch put a load on the floor.</param>
        /// <param name="result">The resulting world Thing (it may be a pre-existing ground stack the load merged
        /// into), or null when nothing was placed.</param>
        /// <returns>True when the units were placed in the world; false leaves them in the inventory (the caller
        /// must then keep the item tagged, or it becomes an untracked black hole).</returns>
        internal static bool TryDropPreferHome(Pawn pawn, Thing thing, int count, string site, out Thing result)
        {
            result = null;
            var inner = pawn?.inventory?.innerContainer;
            var map = pawn?.Map;
            if (inner == null || map == null || thing == null || thing.Destroyed)
                return false;

            var homeArea = map.areaManager?.Home;
            bool preferHome = UnloadFallbackPolicy.PreferHomeAreaDrop(map.IsPlayerHome,
                homeArea != null && homeArea.TrueCount > 0);

            // Snapshot BEFORE the first pass: comparing it against the live count afterwards is the only way to
            // learn how much a "failed" pass actually placed (vanilla reports no partial count of its own).
            int countBefore = thing.stackCount;

            bool dropped = false;
            if (preferHome)
            {
                // Defensive: Area's indexer is an UNCHECKED grid index (Area.this[IntVec3] →
                // innerGrid[CellToIndex(c)]), and we do not rely on the caller's ordering to keep it in range.
                // (On the vanilla path GenPlace.PlaceSpotQualityAt calls GenSpawn.CanSpawnAt first, which rejects
                // out-of-bounds cells before extraValidator ever runs — but a foreign patch on that path is one
                // edit away from removing that ordering, and an out-of-range read here is silent corruption.)
                Predicate<IntVec3> homeOnly = c => c.InBounds(map) && homeArea[c];
                dropped = TryDropMarked(inner, thing, count, out result, homeOnly);
            }

            bool homePassWon = dropped;
            if (!dropped)
            {
                // Re-clamp before retrying (see the GOTCHA on the class). Both terms of the clamp are load-bearing
                // and the arithmetic is the unit-tested Core policy: subtracting what the home pass already placed
                // stops the second pass over-dropping (dropping `count` again would put MORE units on the ground
                // than were ever requested, shaving the pawn's keep-stock), and clamping to what physically
                // remains stops vanilla's red "Tried to drop N of X while only having M" error.
                //
                // Every read here is safe on a stack the pass may have destroyed: `thing` is a local reference C#
                // cannot null out, Thing.Destroyed reads a plain sbyte field, Thing.stackCount is a public field,
                // and ThingOwner.Contains is `item.holdingOwner == this` (false, not a throw, for a dead thing).
                bool stackGone = thing.Destroyed || !inner.Contains(thing);
                int left = UnloadFallbackPolicy.RemainingToDrop(count, countBefore, thing.stackCount, stackGone);
                if (left > 0)
                {
                    // Keep whatever the home pass managed to place if this pass reports nothing: vanilla nulls the
                    // out param on failure, and the caller must still see that something landed.
                    var placedByHomePass = result;
                    dropped = TryDropMarked(inner, thing, left, out result, null);
                    if (result == null)
                        result = placedByHomePass;
                }
                else
                {
                    // Nothing left for a second pass — either the stack left the container entirely, or the home
                    // pass already placed the whole requested amount despite reporting failure. Report success
                    // only on PROOF that something reached the world (a non-null resulting Thing), never on the
                    // absence of the stack alone. This can only ever UNDER-report: if a foreign mod moved the
                    // stack elsewhere without placing it, `result` is null, we return false, and the caller keeps
                    // the item tagged — which the unload driver's relink path already handles. Over-reporting is
                    // the direction that would strand an item untracked, and it is unreachable here.
                    dropped = result != null;
                }
            }

            var landedAt = result?.Position ?? IntVec3.Invalid;
            HDLog.Dbg($"drop[{site}] {pawn.LabelShort}: {thing.LabelShort} x{count} -> "
                      + $"{(dropped ? landedAt.ToString() : "FAILED")} "
                      + $"(pawnAt={pawn.Position}, pawnInHome={IsInHome(map, pawn.Position)}, "
                      + $"preferHome={preferHome}, homePassWon={homePassWon}, "
                      + $"landedInHome={(dropped ? IsInHome(map, landedAt).ToString() : "n/a")})");
            return dropped;
        }

        /// <summary>
        /// One <c>ThingOwner.TryDrop</c> pass, run inside Hauler's Dream's placement-ancestor scope (issue #236).
        ///
        /// <para>Everything this call reaches — <c>GenDrop.TryDropSpawn</c> → <c>GenPlace.TryPlaceThing</c>, which
        /// HD patches — has HD as its CALLER, and neither channel the universal exception breadcrumb reads can
        /// see that: the Harmony registry has no HD entry for a callee HD does not patch, and a captured stack
        /// spans the throw site down to the observing method, never up to who called it. So the breadcrumb has to
        /// be TOLD, or a foreign fault during this drop is reported as "nothing points at Hauler's Dream" while
        /// HD's own unload driver is the caller. Both passes funnel through here so the marker cannot be added to
        /// one and forgotten on the other.</para>
        ///
        /// <para>try/finally, not a bare pair: the placement CAN throw, and a depth that did not unwind would make
        /// every later exception on this thread falsely claim an HD ancestor.</para>
        /// </summary>
        /// <param name="inner">The pawn's inventory container to drop from.</param>
        /// <param name="thing">The stack to set down.</param>
        /// <param name="count">Units to drop on this pass (already clamped by the caller).</param>
        /// <param name="result">The resulting world Thing, or null when nothing was placed.</param>
        /// <param name="nearPlaceValidator">Vanilla's per-cell filter — the Home-area predicate on the first
        /// pass, null on the unconstrained fallback.</param>
        /// <returns>Vanilla's own result: true when the requested units reached the world.</returns>
        private static bool TryDropMarked(ThingOwner inner, Thing thing, int count, out Thing result,
            Predicate<IntVec3> nearPlaceValidator)
        {
            HaulChurnGuard.EnterPlacementWrapper();
            try
            {
                return inner.TryDrop(thing, ThingPlaceMode.Near, count, out result, null, nearPlaceValidator);
            }
            finally
            {
                HaulChurnGuard.ExitPlacementWrapper();
            }
        }

        /// <summary>Is this cell painted Home? False for a missing map / area manager or an out-of-bounds cell —
        /// the <see cref="Area"/> indexer is an UNCHECKED grid index, so the bounds test is required, not
        /// defensive noise. The one place the mod asks that question for its logs, so a diagnostic can never be
        /// the thing that throws.</summary>
        /// <param name="map">The map the cell belongs to; null → false.</param>
        /// <param name="cell">The cell to test; invalid or out of bounds → false.</param>
        internal static bool IsInHome(Map map, IntVec3 cell)
        {
            var homeArea = map?.areaManager?.Home;
            return homeArea != null && cell.IsValid && cell.InBounds(map) && homeArea[cell];
        }
    }
}
