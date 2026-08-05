namespace HaulersDream.Core
{
    /// <summary>
    /// Decision boundary for CE's direct inventory drop. The selected excess stack deliberately has no
    /// "is HD-tagged" input: CE category ceilings are shared across matching defs, so HD-tagged Beer can make CE
    /// select an untagged Wake-Up stack. Any genuine pending HD cargo therefore defers the whole CE excess pass.
    /// </summary>
    public static class CeCargoDropPolicy
    {
        public static bool ShouldVetoExcessDrop(
            bool ceSelectedExcess,
            bool unloadEverything,
            bool selectedStackStillInInventory,
            bool hasAnyUnloadableHdCargo)
            => ceSelectedExcess
               && !unloadEverything
               && selectedStackStillInInventory
               && hasAnyUnloadableHdCargo;
    }
}
