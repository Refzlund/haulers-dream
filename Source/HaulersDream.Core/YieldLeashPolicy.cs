namespace HaulersDream.Core
{
    /// <summary>
    /// Deep-drill "yield leash" (#187b): a deep drill is a <c>ToilCompleteMode.Never</c> job
    /// (<c>JobDriver_OperateDeepDrill</c>) that never ends, so the producer's front-queued self-pickup can never
    /// run and its dropped portions pile up on the ground beside the drill indefinitely. A periodic backstop
    /// briefly interrupts the drill so the queued self-pickup fires (collecting the pile ~1 tile away), then the
    /// work scan re-issues the drill (portion progress lives on the drill's comp, so nothing is lost). This
    /// decides WHEN that interrupt is worth doing. Pure; the GameComponent supplies the live counts/ticks and
    /// performs the interrupt.
    /// </summary>
    public static class YieldLeashPolicy
    {
        /// <summary>Should the drill be interrupted NOW so the pawn collects its accumulated pile?</summary>
        /// <param name="pendingCount">How many drops the pawn currently has queued for self-pickup.</param>
        /// <param name="threshold">The minimum pile size that justifies an interrupt (a small positive constant);
        /// below it, let the drill keep running and the pile keep growing so collections stay batched.</param>
        /// <param name="ticksSinceLastInterrupt">Ticks since this pawn's last drill interrupt (a large value when
        /// it has never been interrupted).</param>
        /// <param name="cooldownTicks">The minimum spacing between one pawn's interrupts, so the drill isn't
        /// churned every scan — the pawn must get back to drilling for a while between collections.</param>
        /// <returns>True only when the pile has reached <paramref name="threshold"/> AND the per-pawn cooldown
        /// (<paramref name="cooldownTicks"/>) has elapsed since the last interrupt. Both bounds are inclusive.</returns>
        public static bool ShouldCollectNow(int pendingCount, int threshold, int ticksSinceLastInterrupt, int cooldownTicks)
        {
            if (pendingCount < threshold)
                return false;
            return ticksSinceLastInterrupt >= cooldownTicks;
        }
    }
}
