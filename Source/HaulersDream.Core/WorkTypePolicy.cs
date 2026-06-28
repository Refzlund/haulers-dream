namespace HaulersDream.Core
{
    /// <summary>Maps a yield's source work-type to the per-category <see cref="YieldBehavior"/> the player chose.</summary>
    public static class WorkTypePolicy
    {
        public static YieldBehavior BehaviorFor(HaulSourceType type,
            YieldBehavior harvest, YieldBehavior logging, YieldBehavior mining, YieldBehavior chunks,
            YieldBehavior deepDrill, YieldBehavior deconstruct, YieldBehavior animal, YieldBehavior strip,
            YieldBehavior uninstall, YieldBehavior fishing)
        {
            switch (type)
            {
                case HaulSourceType.Harvest:     return harvest;
                case HaulSourceType.Logging:     return logging;
                case HaulSourceType.Mining:      return mining;
                case HaulSourceType.Chunks:      return chunks;
                case HaulSourceType.DeepDrill:   return deepDrill;
                case HaulSourceType.Deconstruct: return deconstruct;
                case HaulSourceType.Animal:      return animal;
                case HaulSourceType.Strip:       return strip;
                case HaulSourceType.Uninstall:   return uninstall;
                case HaulSourceType.Fishing:     return fishing;
                default:                         return YieldBehavior.Disabled; // unknown -> do nothing (safe)
            }
        }

        /// <summary>
        /// Pure one-time legacy migration mapping: turns an old per-work boolean toggle (<paramref name="enabled"/>)
        /// + the old GLOBAL <see cref="PickupMode"/> into the new per-category <see cref="YieldBehavior"/>.
        /// <para>
        /// A disabled toggle -> <see cref="YieldBehavior.Disabled"/>; otherwise the old global pickup mode decides
        /// between <see cref="YieldBehavior.DropThenHaul"/> and <see cref="YieldBehavior.DirectToInventory"/>.
        /// <paramref name="forceDropOnly"/> models the Strip special case (Strip was ALWAYS drop-then-haul, never
        /// direct), forcing an enabled category to <see cref="YieldBehavior.DropThenHaul"/> regardless of the global mode.
        /// </para>
        /// Extracted to Core so both the settings migration and the unit tests exercise the exact same mapping.
        /// </summary>
        public static YieldBehavior MapLegacyYield(bool enabled, PickupMode legacyPickupMode, bool forceDropOnly)
        {
            if (!enabled)
                return YieldBehavior.Disabled;
            if (forceDropOnly)
                return YieldBehavior.DropThenHaul;
            return legacyPickupMode == PickupMode.DirectToInventory
                ? YieldBehavior.DirectToInventory
                : YieldBehavior.DropThenHaul;
        }
    }
}
