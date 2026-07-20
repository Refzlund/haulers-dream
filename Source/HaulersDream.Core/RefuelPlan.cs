using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure planning math for the bulk-refuel sweep (the game-coupled planner lives in
    /// <c>HaulersDream.BulkRefuel</c>). A refuel courier sweeps nearby fuel stacks into its inventory to fill
    /// a <c>CompRefuelable</c> in one trip, so every pull is bounded by TWO limits at once: what the refuelable
    /// still needs (its remaining fuel deficit) and what the pawn may carry (the smart-overload carry-weight
    /// ceiling every other into-inventory path already respects, via <see cref="BulkHaulPolicy.CeilingKg"/>).
    /// Strict carry weight and the "Off" slider collapse that ceiling to 100% of the pawn's carry weight, so the
    /// swept total can never pass it; the "no slowdown" slider leaves it unbounded (carrying more is free). This
    /// is the analogue of <see cref="TransportLoadPlan"/> for the refuel path.
    /// </summary>
    public static class RefuelPlan
    {
        /// <summary>
        /// How many units of the next fuel stack to sweep into inventory: the smaller of what the refuelable
        /// still needs and what fits under the carry-weight ceiling (via
        /// <see cref="BulkHaulPolicy.CountWithinCeiling"/>). Massless fuel is bounded only by the deficit and the
        /// stack (weight never binds); a pawn already at the ceiling takes none of a mass-bearing stack, and a
        /// later trip resumes once it has unloaded.
        /// </summary>
        /// <param name="deficitRemaining">Fuel units the refuelable still needs after what earlier stacks in this plan already cover. Non-positive returns 0 (nothing left to load).</param>
        /// <param name="ceilingKg">The carry-weight ceiling from <see cref="BulkHaulPolicy.CeilingKg"/>: the base cap under strict/Off, a break-even multiple of it at the Fair slider, or +infinity at "no slowdown".</param>
        /// <param name="runningMassKg">The pawn's gear+inventory mass plus what earlier stacks in this plan already added.</param>
        /// <param name="unitMassKg">Per-unit mass of this fuel stack. Non-positive means weight never binds it.</param>
        /// <param name="stackCount">Units available in this stack. Non-positive returns 0.</param>
        /// <returns>Units to take: <c>min(deficitRemaining, stackCount, unitsFittingUnderTheCeiling)</c>, never negative.</returns>
        public static int TakeFromStack(int deficitRemaining, float ceilingKg, float runningMassKg, float unitMassKg, int stackCount)
        {
            if (deficitRemaining <= 0 || stackCount <= 0)
                return 0;
            int wanted = Math.Min(stackCount, deficitRemaining);
            return BulkHaulPolicy.CountWithinCeiling(ceilingKg, runningMassKg, unitMassKg, wanted);
        }
    }
}
