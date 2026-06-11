namespace HaulersDream.Core
{
    /// <summary>
    /// Decides whether a construction-material delivery to a SINGLE big needer (a geothermal generator,
    /// a large sculpture, a turret — anything needing more than one hand-stack of one material) is worth
    /// carrying in the pawn's INVENTORY instead of its hands, and if so how many units to load.
    ///
    /// Vanilla always carries construction material in the hands, which are stack-limited (e.g. 75 steel),
    /// so a 340-steel generator costs ~5 round trips. The inventory is mass-limited, not stack-limited, so
    /// a pawn can carry a full capacity-load (and, with smart overload on, more) in one trip — fewer trips.
    /// The benefit only exists when the load would exceed a single hand-stack, so that is the gate.
    ///
    /// This reuses <see cref="OverloadPolicy.UnitsToCarry"/> for the ceiling, so the amount loaded is
    /// exactly consistent with the rest of the mod: at/under 100% capacity there is no slowdown (the win
    /// is free); past it the same break-even economics apply. Pure + headlessly unit-testable.
    /// </summary>
    public static class ConstructDeliveryPlan
    {
        /// <summary>
        /// Units to load into inventory for a single-needer construction delivery, or 0 to fall back to
        /// the vanilla hand-carry (used whenever inventory wouldn't carry strictly more than one hand-load).
        /// </summary>
        /// <param name="overloadLevel">Effective slider level (callers pass <c>OffLevel</c> for strict carry weight).</param>
        /// <param name="maxCapacityKg">The pawn's true max carry capacity (100%).</param>
        /// <param name="baseCapKg">The configured carry-limit mass (fraction × capacity) — the no-overload cap.</param>
        /// <param name="currentMassKg">The pawn's current gear+inventory mass.</param>
        /// <param name="unitMassKg">Per-unit mass of the construction material.</param>
        /// <param name="frameNeedUnits">Units the needer still wants (space remaining, enroute-aware).</param>
        /// <param name="handStackCap">Units that fit in the hands in one carry (<c>MaxStackSpaceEver</c>).</param>
        /// <param name="availableUnits">Units of the material actually gatherable nearby right now.</param>
        public static int PlanLoad(
            int overloadLevel,
            float maxCapacityKg,
            float baseCapKg,
            float currentMassKg,
            float unitMassKg,
            int frameNeedUnits,
            int handStackCap,
            int availableUnits)
        {
            if (unitMassKg <= 0f || maxCapacityKg <= 0f || handStackCap <= 0)
                return 0;
            // One hand-trip already satisfies the needer, or there isn't enough material to beat one
            // hand-trip — vanilla's hand carry is already optimal, so don't intervene.
            if (frameNeedUnits <= handStackCap || availableUnits <= handStackCap)
                return 0;

            int target = OverloadPolicy.UnitsToCarry(
                overloadLevel, maxCapacityKg, baseCapKg, currentMassKg, unitMassKg,
                demandUnits: frameNeedUnits, availableUnits: availableUnits);

            // Only worthwhile if a single inventory trip carries strictly MORE than a single hand-load.
            return target > handStackCap ? target : 0;
        }

        /// <summary>
        /// The mass-and-demand-capped ceiling for how many units the pawn could usefully load for this
        /// needer, ignoring how much is currently on the floor — used to bound the nearby-resource gather
        /// before the real <see cref="PlanLoad"/> (which takes the gathered availability into account).
        /// </summary>
        public static int GatherCeiling(
            int overloadLevel,
            float maxCapacityKg,
            float baseCapKg,
            float currentMassKg,
            float unitMassKg,
            int frameNeedUnits)
        {
            if (unitMassKg <= 0f || maxCapacityKg <= 0f || frameNeedUnits <= 0)
                return 0;
            return OverloadPolicy.UnitsToCarry(
                overloadLevel, maxCapacityKg, baseCapKg, currentMassKg, unitMassKg,
                demandUnits: frameNeedUnits, availableUnits: int.MaxValue);
        }
    }
}
