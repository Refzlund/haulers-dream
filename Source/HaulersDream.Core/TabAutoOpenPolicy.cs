namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for the auto-open-inspect-tab conveniences (no game types, so it is unit-tested
    /// headlessly). The game layer <c>Patch_Selector_Select</c> reads the live Selector / Pawn primitives and
    /// delegates both decisions here.
    /// </summary>
    public static class TabAutoOpenPolicy
    {
        /// <summary>
        /// Whether the selected pawn is an audience for the Gear auto-open: one of the PLAYER's non-humanlike
        /// carriers (pack animal, colony mech, vehicle) that is actually holding something.
        ///
        /// <para>Colonists are excluded on purpose (#224). The vanilla inspect pane already KEEPS the player's
        /// last-opened tab open as they click between pawns, so force-opening Gear for every colonist that happens
        /// to be carrying anything only ever takes the pane AWAY from whatever the player was looking at — which is
        /// exactly what was reported. Non-humanlike is the "pack animal or other carrier" the setting advertises
        /// and covers animals, colony mechs and Vehicle Framework vehicles alike; player-faction keeps a visiting
        /// trader's loaded muffalo from hijacking the tab.</para>
        /// </summary>
        /// <param name="isHumanlike">True for a humanlike pawn (colonist, guest, raider). Excluded: the pane
        /// already remembers the player's tab across humanlike selections.</param>
        /// <param name="isPlayerFaction">True when the pawn belongs to the player's faction; a neutral trader's
        /// or an enemy's loaded carrier must never take the pane over.</param>
        /// <param name="hasInventoryContents">True when the pawn's inventory currently holds at least one thing —
        /// with an empty carrier there is nothing for the Gear tab to show, so the pane is left alone.</param>
        /// <returns>True if selecting this pawn should force its Gear tab open.</returns>
        public static bool CarrierGearApplies(bool isHumanlike, bool isPlayerFaction, bool hasInventoryContents)
            => !isHumanlike && isPlayerFaction && hasInventoryContents;

        /// <summary>
        /// Whether this <c>Selector.Select</c> is a genuinely NEW selection rather than a re-select of the thing
        /// that is already selected. Two ways to be new:
        /// <list type="number">
        /// <item>an unconsumed <c>ClearSelection</c> from an EARLIER frame — the player emptied the selection (a
        ///       click on bare ground) and is now selecting again, so even the same thing is a fresh selection;</item>
        /// <item>otherwise, a different thing id than the last single selection.</item>
        /// </list>
        /// A gap recorded in the CURRENT frame is the <c>ClearSelection(); Select(obj)</c> pair that vanilla runs on
        /// every plain click (<c>Selector.SelectUnderMouse</c>) — it is part of THIS click and must not count as a
        /// gap, or the memo would never suppress anything.
        /// </summary>
        /// <param name="selectedId">Identity of the thing being selected now (its <c>thingIDNumber</c>).</param>
        /// <param name="lastSelectedId">Identity of the previous single selection, or a sentinel (-1) when there
        /// was none — a sentinel never matches a real id, so the first selection always counts as new.</param>
        /// <param name="gapPending">True when a <c>ClearSelection</c> has been recorded and not yet consumed by a
        /// selection.</param>
        /// <param name="gapFrame">The frame that pending gap was recorded on; only meaningful when
        /// <paramref name="gapPending"/> is true.</param>
        /// <param name="frame">The current frame, compared against <paramref name="gapFrame"/> to tell an EARLIER
        /// deselect apart from the clear-then-select pair of this very click.</param>
        /// <returns>True if the auto-open should fire for this selection.</returns>
        public static bool IsNewSelection(int selectedId, int lastSelectedId, bool gapPending, int gapFrame, int frame)
            => (gapPending && gapFrame != frame) || selectedId != lastSelectedId;
    }
}
