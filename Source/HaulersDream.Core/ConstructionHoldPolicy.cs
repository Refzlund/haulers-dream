namespace HaulersDream.Core
{
    /// <summary>
    /// Should a pawn HOLD construction material it is carrying instead of making a storage trip with it? Pure; the
    /// Verse adapter (<c>HaulersDream.ConstructionMaterialHold</c>) answers "does construction this pawn is about to
    /// do still want one of the tagged stacks it holds?" and calls in here.
    ///
    /// <para>Motivation (reported, mod-disabling, and it reproduces at stock defaults): a construction pawn walked
    /// back to the stockpile after every single wall tile. The leftover material from one frame's delivery is
    /// exactly what the NEXT frame is about to eat, so shipping it to storage between frames costs two walks and
    /// buys nothing. This mirrors the existing "don't unload stock a bill is about to consume" guard
    /// (<c>PawnUnloadChecker.HoldsStockForActiveDoBill</c>) — same shape, construction instead of crafting.</para>
    ///
    /// <para>The hold is a HOLD, never a drop: nothing is dropped, destroyed or hidden. The material stays tagged in
    /// the pawn's pack and unloads on the next trigger the moment any signal goes false (the build finishes or is
    /// cancelled, the pawn walks away, the queue clears, the pawn is drafted, or the player presses "Unload now").</para>
    /// </summary>
    public static class ConstructionHoldPolicy
    {
        /// <summary>
        /// The never-strand escape: the longest a hold may last without the pawn picking anything up, in ticks
        /// (2 in-game hours). This is a BACKSTOP, not the normal release — a builder actually working refreshes its
        /// last-intake stamp on every delivery, so the ceiling only bites once the pawn has genuinely stopped
        /// working while still standing near a site that wants its material. Deliberately a fixed constant rather
        /// than the player's settle-window setting: the settle window is a tuning knob for when a run counts as
        /// over, whereas this is the guarantee that the guard can never hold material forever.
        /// </summary>
        public const int MaxHoldTicks = 5000;

        /// <summary>
        /// CHEAP half of <see cref="ShouldHoldMaterial"/> — the part that needs no map scan. Exposed so the Verse
        /// adapter can short-circuit BEFORE the (expensive) "does a nearby site still want this def?" search, in
        /// the same way <see cref="OpportunisticUnloadPolicy.ShouldAttemptDivert"/> pre-gates the storage search.
        /// It can only ever short-circuit a hold that the full decision would also refuse, never admit one.
        /// </summary>
        /// <param name="forced">The unload is a deliberate override — the "Unload now" gizmo, a bulk-haul finish
        /// flush, the mech shed-before-charge. A forced unload NEVER holds; the player asked for it.</param>
        /// <param name="ticksSinceLastIntake">Ticks since the pawn last took anything into its pack (the mod's
        /// last-yield stamp). A working builder refreshes this on every delivery.</param>
        /// <param name="maxHoldTicks">The never-strand ceiling; see <see cref="MaxHoldTicks"/>.</param>
        public static bool MayHoldAtAll(bool forced, int ticksSinceLastIntake, int maxHoldTicks = MaxHoldTicks)
            => !forced && ticksSinceLastIntake < maxHoldTicks;

        /// <summary>
        /// The whole decision: hold the material only when this is an AUTOMATIC unload, the pawn is still within the
        /// never-strand window, and construction the pawn is about to do genuinely still wants what it is carrying.
        /// </summary>
        /// <param name="forced">See <see cref="MayHoldAtAll"/> — a forced unload never holds.</param>
        /// <param name="constructionWantsMaterial">A tagged stack still in the pack is wanted by construction this
        /// pawn is about to do (its current job, a queued job, or a reachable site near it that still needs the
        /// def). This is the signal that goes false when the build finishes or the pawn moves on.</param>
        /// <param name="ticksSinceLastIntake">See <see cref="MayHoldAtAll"/>.</param>
        /// <param name="maxHoldTicks">The never-strand ceiling; see <see cref="MaxHoldTicks"/>.</param>
        public static bool ShouldHoldMaterial(bool forced, bool constructionWantsMaterial, int ticksSinceLastIntake,
            int maxHoldTicks = MaxHoldTicks)
            => MayHoldAtAll(forced, ticksSinceLastIntake, maxHoldTicks) && constructionWantsMaterial;
    }
}
