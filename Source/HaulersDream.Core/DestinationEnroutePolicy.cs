namespace HaulersDream.Core
{
    /// <summary>
    /// The CROSS-PAWN half of the over-haul fix (issue #114): how much of a destination's remaining space is
    /// genuinely still free for the pawn being planned RIGHT NOW, once the loads other pawns are already
    /// bringing to that same destination are taken off the top.
    ///
    /// <para>WHY: a free-space number read straight off the destination is the SAME number every hauler sees,
    /// and nothing lands between their plans, so it is truthfully "empty" for all of them at once. Each pawn
    /// therefore pockets a whole stack, drops the two or three that fit, and carries the remainder back to
    /// where it came from — the high-priority stockpile fills a trickle at a time while N round trips are
    /// burned. What is already IN FLIGHT is the only number that can tell those N plans apart.</para>
    ///
    /// <para>Deliberately an ESTIMATE, not a reservation. No cell or group is locked, so a load that never
    /// arrives (job interrupted, stack stolen, pawn drafted) cannot hold a destination hostage for everyone
    /// else; the price is that a momentarily stale estimate can still allow one extra trip. A stuck stockpile
    /// would be the worse failure.</para>
    ///
    /// <para>Pure (no game types) so the arithmetic is unit-tested headlessly; the caller does the colony scan
    /// and feeds it the two counts.</para>
    /// </summary>
    public static class DestinationEnroutePolicy
    {
        /// <summary>
        /// Units of a def the pawn being planned may still send to a destination, after the loads already
        /// heading there are subtracted. Total and saturating: no input combination can produce a negative
        /// result or overflow.
        /// </summary>
        /// <param name="spaceLeft">Units the destination can still take, ignoring in-flight loads.
        /// <see cref="int.MaxValue"/> means "not known to be limited" (an unbounded or unpriced destination) and
        /// passes through UNCHANGED — an unknown space must stay unknown, or a large-but-unmeasured stockpile
        /// would be mistaken for a full one and no pawn would haul to it at all. Zero or negative reads as
        /// "nothing left".</param>
        /// <param name="unitsEnroute">Units of the same def other pawns are already carrying to, or committed
        /// to carry to, that destination. Negative reads as zero.</param>
        /// <returns>0 when the in-flight loads already cover the space — the pawn should stand down rather than
        /// haul a stack it would only have to carry back — otherwise the remainder.</returns>
        public static int FreeAfterEnroute(int spaceLeft, int unitsEnroute)
        {
            if (spaceLeft == int.MaxValue)
                return int.MaxValue;
            if (spaceLeft <= 0)
                return 0;
            if (unitsEnroute <= 0)
                return spaceLeft;
            // Both sides are known positive here, so the difference can neither overflow nor go negative.
            return unitsEnroute >= spaceLeft ? 0 : spaceLeft - unitsEnroute;
        }
    }
}
