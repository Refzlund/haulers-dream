namespace HaulersDream.Core
{
    /// <summary>
    /// Pure predicates used while inferring which pawn produced a yield and what to do with it. The
    /// game-coupled YieldRouter extracts coordinates/flags from Verse types and calls these so the
    /// branching logic is unit-testable.
    /// </summary>
    public static class InferencePolicy
    {
        /// <summary>
        /// True if this placement is a candidate for routing into inventory: not a re-entrant call,
        /// placed in "Near" mode, and the thing is an item. (Null/destroyed checks stay game-side.)
        /// </summary>
        public static bool IsRoutablePlacement(bool reentrant, bool nearMode, bool isItem)
            => !reentrant && nearMode && isItem;

        /// <summary>
        /// True if the candidate pawn is the actual producer of a yield placed at the center cell —
        /// either standing on the cell (plants / animals / deep-drill), or whose current job target
        /// IS the cell (mining). Used to prefer the real producer over any other tracked worker that
        /// merely happens to be within the 3×3.
        /// </summary>
        public static bool IsTrueProducer(int pawnX, int pawnZ, bool hasJobTarget, int targetX, int targetZ, int centerX, int centerZ)
        {
            if (pawnX == centerX && pawnZ == centerZ)
                return true;
            return hasJobTarget && targetX == centerX && targetZ == centerZ;
        }

        /// <summary>
        /// Whether a thing in the deconstruction leavings rect should be scooped: it's an item, it
        /// newly appeared (wasn't there before the deconstruct), it isn't forbidden, and it isn't
        /// already in valid storage (storage protection).
        /// </summary>
        public static bool ShouldScoopLeaving(bool isItem, bool wasPresentBefore, bool forbidden, bool inValidStorage)
            => isItem && !wasPresentBefore && !forbidden && !inValidStorage;
    }
}
