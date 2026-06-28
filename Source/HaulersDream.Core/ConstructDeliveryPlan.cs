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
        /// Whether the inventory-construction driver should make a stockpile LOAD trip before delivering to
        /// the IMMEDIATE needer: only when the carried stock of this material can't already cover what this
        /// one frame still needs.
        ///
        /// This is the fix for the "tops off at the stockpile after every wall" route bug. A haul+build route
        /// stamps the WHOLE remaining route's demand into each stop's load target, so the driver's fill loop
        /// kept walking back to the stockpile after every single deposit (carried stock was always below the
        /// whole-route demand, and the mass headroom reopened each time a frame was filled). Gating the TRIP
        /// decision on the immediate frame's need instead means the pawn delivers from its carried surplus
        /// across the intervening frames and only re-loads when it genuinely runs short — once per
        /// ceiling-worth of material, not once per frame. (When it does trip, the fill loop still loads toward
        /// the whole-route ceiling, so the "few trips" benefit is preserved.)
        /// </summary>
        /// <param name="inventoryUnits">Units of the material the pawn already carries.</param>
        /// <param name="immediateNeedUnits">Units this one frame still needs (space remaining, enroute-aware).</param>
        public static bool ShouldLoadBeforeDeliver(int inventoryUnits, int immediateNeedUnits)
            => immediateNeedUnits > 0 && inventoryUnits < immediateNeedUnits;

        /// <summary>
        /// Whether to convert a vanilla SAME-MATERIAL construction CLUSTER (the primary needer plus the
        /// ≤8-tile needers vanilla batched into targetQueueB) into ONE inventory trip that loads the
        /// cluster's combined demand and delivers to every site. Only worthwhile when the cluster has at
        /// least two distinct sites still needing material AND the combined demand exceeds a single hand-
        /// load — a one-site cluster, or a cluster that fits in one armful, is left to the existing single-
        /// needer / vanilla hand-batch logic.
        /// </summary>
        /// <param name="clusterNeederCount">Distinct cluster sites still needing this material (need &gt; 0).</param>
        /// <param name="clusterNeedUnits">Combined units the whole cluster still needs (enroute-aware sum).</param>
        /// <param name="handStackCap">Units that fit in the hands in one carry (<c>MaxStackSpaceEver</c>).</param>
        public static bool MultiSiteWorthIt(int clusterNeederCount, int clusterNeedUnits, int handStackCap)
            => clusterNeederCount >= 2 && handStackCap > 0 && clusterNeedUnits > handStackCap;

        /// <summary>
        /// Whether HD may query a construction needer's remaining space at all: only when the needer is still
        /// SPAWNED and we know which material we're delivering. This pins the guard that prevents the issue #88
        /// NullReferenceException — vanilla's enroute space query dereferences the needer's <c>Map</c>, which is
        /// null once another pawn has finished or despawned the needer mid-job, and the older guard
        /// (<c>!DestroyedOrNull()</c>) let a plain-despawned needer through. The runtime wrapper
        /// (<c>EnrouteSafety.SpaceRemainingSafe</c>) reports 0 space when this is false, so the delivery job
        /// aborts cleanly instead of throwing. Pure, so a refactor that re-weakens the guard is caught by a test
        /// rather than by players hitting the crash again.
        /// </summary>
        public static bool ShouldQueryNeederSpace(bool neederSpawned, bool hasResourceDef)
            => neederSpawned && hasResourceDef;

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
