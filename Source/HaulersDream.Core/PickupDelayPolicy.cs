namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for the vanilla-like PICKUP PAUSE (issue #121): how many ticks a pawn stands at a
    /// stack, showing a progress bar, before the stack enters its inventory. One knob (ticks per stack), three
    /// shapes: 0 = instant (the pre-feature behavior), <see cref="VanillaDelayTicks"/> = exactly vanilla's
    /// player "Pick up" order, anything else = custom. No game types, unit-tested headlessly; the game-layer
    /// <c>PickupPause</c> turns the result into a Wait toil with vanilla's progress bar.
    ///
    /// Vanilla evidence (decompiled 1.6 Assembly-CSharp): <c>JobDriver_TakeInventory.MakeNewToils</c> gates its
    /// wait on <c>job.takeInventoryDelay &gt; 0</c> and shows it via <c>Toils_General.Wait(takeInventoryDelay)</c>
    /// plus <c>WithProgressBarToilDelay(TargetIndex.A)</c> while facing the item; the only vanilla writer of that
    /// field is the player "Pick up one/all/some" float menu (<c>FloatMenuOptionProvider_PickUpItem</c>; 1.6
    /// refactored the float menu into providers), which hard-codes <c>job.takeInventoryDelay = 120</c> in all
    /// three options. Vanilla never scales the delay by stack count or mass (no such scaling exists anywhere in
    /// the decompile), so this policy is a flat per-stack constant too, not a formula over stack size.
    /// </summary>
    public static class PickupDelayPolicy
    {
        /// <summary>
        /// Vanilla's own pickup-into-inventory delay: the flat 120 ticks (about 2 in-game seconds) the player
        /// "Pick up" float-menu order writes into <c>Job.takeInventoryDelay</c> (all three pick-up options in
        /// <c>FloatMenuOptionProvider_PickUpItem</c>). The mod's default, so HD pickups pace exactly like a
        /// vanilla ordered pickup.
        /// </summary>
        public const int VanillaDelayTicks = 120;

        /// <summary>
        /// Upper bound the settings slider offers (twice vanilla). Values beyond it are reachable only by editing
        /// the config XML by hand, and even those are capped at <see cref="MaxDelayTicks"/>.
        /// </summary>
        public const int SliderMaxTicks = 240;

        /// <summary>
        /// Hard ceiling on the effective delay (10 in-game seconds per stack): a hand-edited config value above
        /// this clamps down, so a typo (an extra zero) degrades to "slow" instead of stalling every pickup job
        /// for minutes per stack.
        /// </summary>
        public const int MaxDelayTicks = 600;

        /// <summary>
        /// The effective per-stack wait a pickup toil should pause for, given the raw configured setting.
        /// Zero and negative values (the latter only via a hand-edited config) mean instant: the pause toil is
        /// skipped entirely, no progress bar, matching vanilla's own <c>takeInventoryDelay &gt; 0</c> gate.
        /// </summary>
        /// <param name="configuredTicks">The raw <c>pickupDelayTicks</c> setting value, unclamped and untrusted
        /// (a config file can hold anything).</param>
        /// <returns>The clamped wait in ticks, in [0, <see cref="MaxDelayTicks"/>]; 0 = instant.</returns>
        public static int TicksPerStack(int configuredTicks)
        {
            if (configuredTicks <= 0)
                return 0;
            return configuredTicks > MaxDelayTicks ? MaxDelayTicks : configuredTicks;
        }
    }
}
