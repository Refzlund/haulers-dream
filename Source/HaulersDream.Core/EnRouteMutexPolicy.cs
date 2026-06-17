namespace HaulersDream.Core
{
    /// <summary>
    /// The en-route-pickup ↔ opportunistic-unload MUTEX (plan conflict guard G3). En-route pickup
    /// (<see cref="EnRoutePickupPolicy"/>) makes a pawn GRAB more on the way to a job; opportunistic unload
    /// (<c>OpportunisticUnload.ShouldDivert</c>) makes a pawn DROP its load on the way to a job. These are
    /// directly opposed, so they must be mutually exclusive: a pawn whose load is already at/over the
    /// unload-divert point must NOT also scoop more — it should shed, not accumulate.
    ///
    /// <para>This is the pure boundary. It returns TRUE when en-route pickup must STAND DOWN because the
    /// pawn meets the NECESSARY conditions to instead divert to unload. It deliberately mirrors only the
    /// <em>necessary</em> (not sufficient) conditions of <c>OpportunisticUnload.ShouldDivert</c> — load
    /// fraction at/over the minimum, a storable representative storage cell exists, and the divert cooldown
    /// has elapsed — because en-route should yield whenever an unload divert is even POSSIBLE, not only
    /// when the unload's full on-the-way geometry also happens to pass. Standing down on the necessary
    /// conditions errs on the side of "don't pile more onto a pawn that's plausibly about to unload," which
    /// is the safe direction for the mutex. (The unload feature still applies its full geometry test
    /// separately; this just keeps en-route from fighting it.)</para>
    ///
    /// <para>Necessary divert conditions reproduced (from <c>OpportunisticUnload.ShouldDivert</c> /
    /// <c>OpportunisticUnloadPolicy.ShouldAttemptDivert</c>):</para>
    /// <list type="bullet">
    ///   <item><b>load fraction</b> ≥ <see cref="OpportunisticUnloadPolicy.MinLoadFraction"/> — below the
    ///   minimum, <c>ShouldUnloadOnWay</c>/<c>ShouldUnloadOnRunEnd</c> both reject outright, so no unload
    ///   divert is possible and en-route is free to grab.</item>
    ///   <item><b>a storable representative cell exists</b> — <c>ShouldDivert</c> returns false when no
    ///   tracked stack has a valid store cell (nothing to divert toward), so without one no unload divert
    ///   can happen and en-route may proceed.</item>
    ///   <item><b>cooldown elapsed</b> — a recent (possibly failed) divert is still cooling down;
    ///   <c>ShouldDivert</c> bails until <c>DivertCooldownTicks</c> has passed. While cooling down, the
    ///   unload feature will not fire, so the mutex does not force en-route to stand down on that basis.</item>
    /// </list>
    ///
    /// <para>Pure, allocation-free (primitives in / bool out). The Verse layer supplies the live numbers
    /// (the tracked-goods load fraction, whether a storable store cell was found for any tracked stack, and
    /// whether the per-pawn unload cooldown has elapsed). The mutex is only meaningful when the opportunistic
    /// unload feature is ENABLED — when it is off the Verse layer passes
    /// <paramref name="opportunisticUnloadEnabled"/> = false and the mutex never stands en-route down.</para>
    /// </summary>
    public static class EnRouteMutexPolicy
    {
        /// <summary>
        /// True when en-route pickup must STAND DOWN (yield to a possible opportunistic-unload divert).
        /// </summary>
        /// <param name="opportunisticUnloadEnabled">Whether the opportunistic-unload feature is on. When
        /// false the two features can never both fire, so the mutex is inert (returns false).</param>
        /// <param name="loadFraction">The pawn's scooped-goods mass as a fraction of its carry capacity
        /// (same quantity the unload divert reads). When the pawn carries nothing this is 0.</param>
        /// <param name="hasStorableCell">Whether the Verse layer found a valid storage cell for at least
        /// one tracked stack (the unload divert's "somewhere to drop it" precondition).</param>
        /// <param name="cooldownElapsed">Whether the per-pawn unload divert cooldown has elapsed.</param>
        /// <param name="minLoadFraction">The load-fraction floor (default
        /// <see cref="OpportunisticUnloadPolicy.MinLoadFraction"/>) — the same constant the unload divert
        /// uses, so the two features hand off at exactly the same threshold.</param>
        /// <returns>True ⇒ en-route pickup stands down (the pawn is at/over the unload-divert point).
        /// False ⇒ en-route pickup may proceed (a sub-threshold load, no place to unload, the feature is
        /// off, or the cooldown is active).</returns>
        public static bool MustStandDown(
            bool opportunisticUnloadEnabled,
            float loadFraction,
            bool hasStorableCell,
            bool cooldownElapsed,
            float minLoadFraction = OpportunisticUnloadPolicy.MinLoadFraction)
        {
            // Feature off -> no conflict possible.
            if (!opportunisticUnloadEnabled)
                return false;
            // A recent (possibly failed) divert is cooling down -> the unload feature won't fire now, so
            // en-route is not forced to yield on that basis.
            if (!cooldownElapsed)
                return false;
            // Nowhere to drop the load -> no unload divert can happen -> en-route may grab.
            if (!hasStorableCell)
                return false;
            // Sub-threshold load -> the unload divert rejects outright -> en-route may grab.
            // At/over the threshold -> the pawn is at the unload-divert point -> en-route stands down.
            return loadFraction >= minLoadFraction;
        }
    }
}
