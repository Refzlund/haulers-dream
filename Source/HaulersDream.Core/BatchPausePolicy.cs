namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision mirroring RimWorld's <c>Bill_Production.ShouldDoNow()</c> "Do until you have X" (TargetCount)
    /// stop condition, for HD's batch crafting.
    ///
    /// WHY this exists: HD's batch driver banks freshly-made products in the crafter's INVENTORY (to deliver a whole
    /// batch in one trip). Vanilla's <c>RecipeWorkerCounter.CountProducts</c> counts world/storage/in-hands but NOT
    /// pawn inventory, so it cannot see those banked products. Vanilla's own pause/satisfied gate therefore reads a
    /// stale count mid-batch and never stops or pauses — pawns overproduce. The caller supplies an EFFECTIVE count
    /// (vanilla's CountProducts PLUS the HD-banked in-flight products), and this decides whether another unit may be
    /// crafted, exactly as vanilla would if it could see them.
    ///
    /// Pause UI state is intentionally NOT decided here: once the banked products reach storage, vanilla's own
    /// <c>ShouldDoNow()</c> re-derives <c>paused</c> from the (now-correct) world count and latches it with the
    /// unpause-at hysteresis. This policy only prevents the overproduction in the meantime, so the bill pauses on
    /// delivery exactly like a normal (one-at-a-time) bill does.
    /// </summary>
    public static class BatchPausePolicy
    {
        /// <summary>
        /// May the batch craft another unit of a "Do until you have X" bill?
        /// <paramref name="effectiveCount"/> = vanilla CountProducts + HD-banked in-flight products of the counted def.
        /// <paramref name="paused"/> = the bill's current latched pause state (vanilla owns it; respected here so a
        /// pause-when-satisfied bill that vanilla has already latched stays stopped). Returns false at/over target.
        /// Mirrors vanilla's TargetCount branch: stop once <c>num &gt;= targetCount</c> (for both pause-when-satisfied
        /// and not), and stop while paused.
        /// </summary>
        public static bool MayCraftMore(int effectiveCount, int targetCount, bool paused)
        {
            if (paused)
                return false;
            return effectiveCount < targetCount;
        }
    }
}
