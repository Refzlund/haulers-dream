namespace HaulersDream.Core
{
    /// <summary>
    /// "Keep working when full" (opt-in) decision math. When a pawn doing a WORK job (mining, harvesting,
    /// deconstructing — carrying is incidental, not the task) reaches its carry ceiling, the default mode
    /// breaks off to unload; this feature instead keeps it working and lets overflow yields drop on the
    /// ground for normal hauling. The only time a still-full pawn should unload is when it pays off — right
    /// before a LONG relocation to its next work target.
    ///
    /// WEIGHTED RULE (pure): carrying the load onward to the next work target costs
    /// <c>distToNextWork · drag</c> (the overload slowdown applies the whole way there); detouring to
    /// storage first costs <c>distToStorage · drag</c> and then the pawn travels onward at FULL speed
    /// (it's empty). So shedding the load first wins exactly when the next task is FARTHER than the
    /// dropoff — i.e. <c>distToNextWork &gt; distToStorage + margin</c>. The margin is hysteresis so a pawn
    /// doesn't dither when the two distances are nearly equal. It only applies while the pawn is actually
    /// overloaded (paying the drag): a pawn at/under capacity has <c>speedFactor == 1</c> and never detours
    /// — carrying costs it nothing, so it just keeps the load and works.
    ///
    /// Verse-free and fully unit-tested.
    /// </summary>
    public static class KeepWorkingPolicy
    {
        /// <summary>Default hysteresis margin (tiles) between "carry onward" and "unload first".</summary>
        public const float DefaultMarginCells = 5f;

        /// <summary>
        /// True when a full, overloaded pawn should detour to unload at storage BEFORE walking to its next
        /// work target.
        /// </summary>
        /// <param name="speedFactor">The pawn's current overload move-speed factor (1 = not overloaded /
        /// no drag; &lt; 1 = overloaded and paying the slowdown). The detour only pays off while the pawn is
        /// actually dragged, so a value &gt;= 1 always returns false.</param>
        /// <param name="distToNextWork">Tiles from the pawn to its next work target.</param>
        /// <param name="distToStorage">Tiles from the pawn to the storage it would unload to.</param>
        /// <param name="marginCells">Hysteresis: the next target must be FARTHER than the dropoff by at
        /// least this many tiles before the detour is taken (clamped to &gt;= 0).</param>
        public static bool ShouldUnloadBeforeNext(
            float speedFactor, float distToNextWork, float distToStorage,
            float marginCells = DefaultMarginCells)
        {
            // Not overloaded -> carrying costs nothing -> never detour (keep working with the load).
            if (speedFactor >= 1f)
                return false;
            if (marginCells < 0f)
                marginCells = 0f;
            // Unload first only when the next task is farther than the dropoff (beyond the hysteresis margin).
            return distToNextWork > distToStorage + marginCells;
        }
    }
}
