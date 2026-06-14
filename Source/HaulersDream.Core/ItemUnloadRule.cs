namespace HaulersDream.Core
{
    /// <summary>
    /// How Hauler's Dream treats a specific item def in a pawn's inventory, configured per-item in
    /// mod options → "Individual Item Unload Settings". A def with NO rule uses the normal behaviour
    /// (HD's auto-detected keep-mods + the global "unload surplus" toggle). An explicit rule OVERRIDES
    /// both of those for that def.
    /// </summary>
    public enum ItemUnloadMode
    {
        /// <summary>Never unload this def out of inventory — keep the whole stack as personal kit.
        /// Value 0 so a legacy bare "keep" entry (pre-1.1.x) decodes to this.</summary>
        KeepAll = 0,

        /// <summary>Keep at most N units of this def across the pawn's inventory; unload the excess.</summary>
        KeepAtMost = 1,

        /// <summary>Always unload this def — even units another mod (Simple Sidearms, Smart Medicine,
        /// Dub's Bad Hygiene, Combat Extended) or vanilla addiction would otherwise keep.</summary>
        UnloadAlways = 2,
    }

    /// <summary>A per-item unload rule: the <see cref="ItemUnloadMode"/> and, for <see cref="ItemUnloadMode.KeepAtMost"/>,
    /// the count to keep. A value type stored by defName so it is fully fallback-safe across mod removal.</summary>
    public struct ItemUnloadRule
    {
        public ItemUnloadMode mode;
        public int amount; // units to keep (KeepAtMost only); ignored for the other modes

        public ItemUnloadRule(ItemUnloadMode mode, int amount = 0)
        {
            this.mode = mode;
            this.amount = amount;
        }
    }
}
