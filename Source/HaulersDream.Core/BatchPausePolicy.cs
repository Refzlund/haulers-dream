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

        /// <summary>
        /// Overshoot-aware overload of <see cref="MayCraftMore(int,int,bool)"/> for the "overshoot by Y" feature
        /// (issue #3): once a "Do until you have X" batch has STARTED (vanilla only starts it while the world count
        /// is below X), keep crafting up to X+Y so the pawn finishes a useful round number "while it's already there",
        /// instead of stopping the instant the count crosses X. <paramref name="overshoot"/> (Y) is the EXTRA amount
        /// past the target; it is clamped to a floor of 0 so a stray negative value is treated as "no overshoot" and
        /// the result is then byte-identical to the plain overload (overshoot 0 ⇒ stop at X, exactly as before).
        /// X stays the vanilla START threshold (the route/start guards still only begin a batch when below X); this
        /// only widens the per-rep CONTINUE gate to X+Y.
        ///
        /// <para>The <paramref name="paused"/> latch is vanilla's "Pause when satisfied" auto-pause, which
        /// <c>Bill_Production.ShouldDoNow()</c> sets the instant the (banked-inclusive) count reaches X. WITHIN an
        /// active overshoot window (<c>overshoot &gt; 0</c>) we deliberately IGNORE that latch: it latches at X, but
        /// the player explicitly asked to finish to X+Y, so honouring it would defeat the overshoot the moment the
        /// count crossed X (and a concurrent work scan can latch it mid-batch). The manual STOP is the separate
        /// <c>Bill.suspended</c> field, which the driver checks independently — so a player who pauses the bill by
        /// hand still stops. With no overshoot (Y = 0) the latch is honoured exactly as the plain overload, so a
        /// normal pause-when-satisfied bill is unchanged.</para>
        /// </summary>
        public static bool MayCraftMore(int effectiveCount, int targetCount, int overshoot, bool paused)
        {
            if (overshoot < 0)
                overshoot = 0;
            // Honour the pause-at-X latch only when there's no overshoot to finish; inside the X..X+Y window the
            // overshoot intentionally overrides it (the player asked to make Y more). repsTarget + the X+Y ceiling
            // below still bound the batch, so this can never run away.
            if (paused && overshoot == 0)
                return false;
            return effectiveCount < targetCount + overshoot;
        }
    }
}
