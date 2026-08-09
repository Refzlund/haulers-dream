namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for "haul to stack" (unit-tested headlessly): haulers prefer topping up an
    /// EXISTING partial stack over starting a new one — but only WITHIN the storage area vanilla already
    /// chose (which room/stockpile wins stays a priority/distance decision; this never changes priorities).
    /// </summary>
    public static class HaulToStackPolicy
    {
        /// <summary>Scan radius (cells) around the vanilla-chosen cell when the destination is outside —
        /// the open outdoors is one giant "room", so same-room scoping would sweep the whole map.</summary>
        public const float OutsideScanRadius = 20f;

        public const float OutsideScanRadiusSquared = OutsideScanRadius * OutsideScanRadius;

        /// <summary>Cells-examined budget per refinement — a huge stockpile falls back to vanilla's pick
        /// rather than stalling the work scan.</summary>
        public const int MaxCellsScanned = 600;

        /// <summary>
        /// Whether a def can ever share a cell with more of itself, and therefore whether topping up an
        /// existing partial stack is a meaningful thing to search for.
        ///
        /// <para>The ONE definition of "unstackable" in the haul-to-stack feature. It used to be spelled out
        /// by hand at four sites — the cell-refinement scope, the vanilla reservation strip, and both unload
        /// branches — and those copies drifted apart once already (issue #162). The three reservation sites
        /// are now decided by the storage commitment ledger instead, which needs no such test at all: one
        /// corpse claims one unit of one cell and the next hauler's gate simply finds no room.</para>
        /// </summary>
        /// <param name="stackLimit">The def's stack limit.</param>
        /// <returns>True when two of this def can occupy one cell.</returns>
        public static bool CanTopUp(int stackLimit) => stackLimit > 1;

        /// <summary>
        /// Whether a candidate cell beats the best-so-far: a cell with an existing partial stack always
        /// beats one without; among equals, nearer (squared distance) wins.
        /// </summary>
        public static bool IsBetter(bool candidateIsStack, float candidateDistSq, bool bestIsStack, float bestDistSq)
        {
            if (candidateIsStack != bestIsStack)
                return candidateIsStack;
            return candidateDistSq < bestDistSq;
        }

        /// <summary>
        /// Whether to scope the stack search by RADIUS instead of by room: the destination has no room at
        /// all, or its room touches the map edge (the unbounded outdoors). A walled courtyard — outdoors
        /// psychologically but enclosed — still scopes by room.
        /// </summary>
        public static bool UseRadiusScan(bool hasRoom, bool roomTouchesMapEdge)
            => !hasRoom || roomTouchesMapEdge;
    }
}
