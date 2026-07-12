namespace HaulersDream.Core
{
    /// <summary>Which kind of "pocket a stack into inventory" a pickup pause is being built for, so the delay
    /// can match VANILLA's boundary: vanilla delays ONLY the deliberate player "Pick up" / carry order, and
    /// sweeps every hauled/loaded stack instantly (issue: floor removal debris took as long as a manual pickup).
    /// The scope toggles opt the two automatic families back into the delay.</summary>
    public enum PickupDelayContext
    {
        /// <summary>A DELIBERATE player order to hold/pocket a specific stack ("Keep X in inventory", "Pick up
        /// X"): vanilla's own delayed pickup. Always paced by the delay (governed only by the magnitude slider).</summary>
        ManualCarry,

        /// <summary>AUTOMATIC hauling or sweeping loose stacks into inventory (the bulk-haul sweep, scooping up
        /// one's own work yields, bulk refuel). Vanilla hauls these instantly, so the pause is opt-in.</summary>
        AutoHaul,

        /// <summary>Loading stacks into transporters / portals / pack animals. Vanilla loads these instantly, so
        /// the pause is opt-in (independently of the hauling toggle).</summary>
        Loading,

        /// <summary>An ISOLATED plant harvest collected immediately (not held for a nearby cluster): the yield
        /// dropped visibly and the same pawn scoops it up on the spot. Paused only when BOTH the hauling opt-in
        /// AND the dedicated direct-harvest opt-in are on — so a one-off ordered harvest is collected snappily by
        /// default even while automatic hauling is set to pace.</summary>
        DirectHarvest
    }

    /// <summary>
    /// Pure decision logic for the vanilla-like PICKUP PAUSE (issue #121): how many ticks a pawn stands at a
    /// stack, showing a progress bar, before the stack enters its inventory. One knob (ticks per stack), three
    /// shapes: 0 = instant (the pre-feature behavior), <see cref="VanillaDelayTicks"/> = exactly vanilla's
    /// player "Pick up" order, anything else = custom. A second axis, <see cref="ShouldPause"/>, scopes WHICH
    /// pickups the delay applies to (default: only the deliberate carry order, matching vanilla). No game types,
    /// unit-tested headlessly; the game-layer <c>PickupPause</c> turns the result into a Wait toil with vanilla's
    /// progress bar.
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

        /// <summary>
        /// Whether a pickup in the given <paramref name="context"/> should show the pause, given the two opt-in
        /// scope toggles. <see cref="PickupDelayContext.ManualCarry"/> ALWAYS pauses (it is vanilla's own delayed
        /// pickup order, so pacing it is the vanilla-faithful default); the two AUTOMATIC families pause only when
        /// the player opts in, because vanilla sweeps and loads those instantly. Orthogonal to the magnitude:
        /// <see cref="TicksPerStack"/> still gates everything (a magnitude of 0 skips every pause regardless).
        /// </summary>
        /// <param name="context">What kind of pickup this pause is for.</param>
        /// <param name="delayOnHauling">The <c>pickupDelayOnHauling</c> opt-in: pace automatic hauling / sweeps /
        /// refuel like a manual pickup.</param>
        /// <param name="delayOnLoading">The <c>pickupDelayOnLoading</c> opt-in: pace transporter / pack-animal
        /// loading like a manual pickup.</param>
        /// <param name="delayOnDirectHarvest">The <c>pickupDelayOnDirectHarvest</c> opt-in (default off): also
        /// pace an ISOLATED harvest collected on the spot. It is an ADDITIONAL gate on top of
        /// <paramref name="delayOnHauling"/> (a direct harvest is a kind of auto-haul pickup), so a direct harvest
        /// paces only when BOTH are on — keeping one-off ordered harvests snappy by default even with hauling
        /// pacing enabled.</param>
        /// <returns>True if the pause toil should be built for this context.</returns>
        public static bool ShouldPause(PickupDelayContext context, bool delayOnHauling, bool delayOnLoading,
            bool delayOnDirectHarvest = false)
        {
            switch (context)
            {
                case PickupDelayContext.ManualCarry: return true;
                case PickupDelayContext.AutoHaul: return delayOnHauling;
                case PickupDelayContext.Loading: return delayOnLoading;
                case PickupDelayContext.DirectHarvest: return delayOnHauling && delayOnDirectHarvest;
                default: return false;
            }
        }
    }
}
