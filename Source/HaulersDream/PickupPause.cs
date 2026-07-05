using HaulersDream.Core;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The vanilla-like pickup pause (issue #121): a factory for the ONE toil every HD "pocket a ground stack"
    /// driver yields between its arrive-at-the-stack toil and its take toil, so a pawn visibly works at each
    /// stack (progress bar, facing the item) instead of hoovering it up instantly.
    ///
    /// Faithful to vanilla by construction: <c>JobDriver_TakeInventory.MakeNewToils</c> (decompiled 1.6) shows
    /// its pickup delay as <c>Toils_General.Wait(job.takeInventoryDelay)</c> + <c>WithProgressBarToilDelay</c>
    /// while facing the item. <c>Toils_General.Wait(ticks, face)</c> bakes the same StopDead + Delay + facing
    /// (its face branch is byte-equivalent to the tickIntervalAction vanilla writes by hand), so
    /// <c>Wait(ticks, ind).WithProgressBarToilDelay(ind)</c> reproduces the vanilla block exactly.
    ///
    /// Loop-safe: HD's pickup toils sit inside decide-&gt;goto-&gt;take jump loops, so the SAME toil object is
    /// re-entered once per stack. That works because the JobDriver resets <c>ticksLeftThisToil</c> from
    /// <c>defaultDuration</c> on every toil (re)start, the facing + progress-bar target resolve per tick via
    /// <c>CurJob.GetTarget(ind)</c> (which the decide toil re-points at the current stack each pass), and
    /// <c>WithProgressBar</c>'s finish action (run by <c>Toil.Cleanup</c> on EVERY transition off the toil,
    /// jumps included) cleans up and NULLS its effecter so the bar re-spawns fresh for the next stack.
    ///
    /// Toil-count stability: when the delay is 0 this returns an instant no-op label instead of yielding
    /// nothing, so the driver's toil COUNT does not depend on the setting value. A mid-job save re-enters via
    /// the scribed <c>curToilIndex</c>, so a setting changed between save and load must not shift the indices
    /// (same pattern as <c>JobDriver_UnloadCarrierInBulk</c>'s visualUnloadDelay wait).
    ///
    /// GOTCHA (why the duration is resolved HERE, at MakeNewToils time, not per stack in an initAction): the
    /// JobDriver reads <c>defaultDuration</c> when the toil starts, before initAction runs, so a per-iteration
    /// duration write would be one stack late. A flat per-job duration is also exactly vanilla's shape (the
    /// delay is a flat constant, never scaled per stack) and keeps multiplayer deterministic: the value comes
    /// from host-authoritative settings and the wait itself is a plain tick countdown (no Rand, no wall clock).
    /// </summary>
    public static class PickupPause
    {
        /// <summary>
        /// Build the per-stack pickup pause toil for the current <c>pickupDelayTicks</c> setting: a
        /// Wait-with-progress-bar facing <paramref name="stackInd"/> when the delay is positive, an instant
        /// no-op label when it is 0 (instant pickups, the pre-#121 behavior).
        ///
        /// Deliberately carries NO fail conditions: in the multi-stack sweep loops a stack that despawns or is
        /// sniped mid-pause must be SKIPPED by the following take toil's re-validation, never fail the whole
        /// job. Single-target callers that want prompt termination chain their own <c>FailOn...</c> on the
        /// returned toil (see <c>JobDriver_KeepInInventory</c>).
        /// </summary>
        /// <param name="stackInd">The job target slot the driver keeps pointed at the stack currently being
        /// picked up. Anchors the progress bar and the facing; resolved per tick, so a loop driver re-pointing
        /// the slot each pass retargets both for free. An unspawned target (an item inside a container) renders
        /// the bar on the pawn instead, which is the correct visual for standing at the container.</param>
        /// <returns>The toil to yield between the goto toil and the take toil. Never null.</returns>
        public static Toil MakeToil(TargetIndex stackInd)
        {
            // Null settings mirrors the sibling wait's fallback (JobDriver_UnloadCarrierInBulk: `?? 0`):
            // degrade to the old instant behavior. Unreachable in practice; jobs only run with live settings.
            int ticks = PickupDelayPolicy.TicksPerStack(HaulersDreamMod.Settings?.pickupDelayTicks ?? 0);
            if (ticks <= 0)
                return Toils_General.Label();
            Toil wait = Toils_General.Wait(ticks, stackInd);
            wait.WithProgressBarToilDelay(stackInd);
            return wait;
        }
    }
}
