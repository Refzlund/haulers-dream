using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Decides how many units of a resource a pawn should load into its inventory on THIS trip,
    /// allowing it to overload past 100% capacity when doing so saves enough trips to be worth the
    /// resulting slowdown. The pawn is "smart": it only overloads when there is demand beyond a single
    /// capacity-load (current work + queued/future plans), and only up to the break-even point where
    /// the slowdown cost overtakes the trip it saves.
    ///
    /// The trip-vs-slowdown tradeoff is distance- and speed-independent: a saved trip and the extra
    /// slowdown both scale with the haul distance, so they cancel, leaving the optimal load a function
    /// of mass, demand and the slider only (see <see cref="OverloadTuning"/>). That is why this needs
    /// no distances — and is fully unit-testable.
    /// </summary>
    public static class OverloadPolicy
    {
        /// <summary>
        /// Units to pick up now.
        /// </summary>
        /// <param name="overloadLevel">The slider level (0..<see cref="OverloadTuning.MaxLevel"/>).</param>
        /// <param name="maxCapacityKg">The pawn's TRUE max carry capacity (100%); overload extends past this.</param>
        /// <param name="baseCapKg">The configured carry-limit mass (fraction × capacity) — the no-overload cap.</param>
        /// <param name="currentMassKg">The pawn's current gear+inventory mass.</param>
        /// <param name="unitMassKg">Per-unit mass of the resource.</param>
        /// <param name="demandUnits">Total units usefully needed (this job + future build/craft plans).</param>
        /// <param name="availableUnits">Units actually pickable right now.</param>
        public static int UnitsToCarry(
            int overloadLevel,
            float maxCapacityKg,
            float baseCapKg,
            float currentMassKg,
            float unitMassKg,
            int demandUnits,
            int availableUnits)
        {
            int cap = Math.Min(Math.Max(demandUnits, 0), Math.Max(availableUnits, 0));
            if (cap <= 0)
                return 0;

            // Baseline: how many fit under the (fractional) carry limit, never overloading.
            int baseUnits = CarryMath.CountToPickUp(baseCapKg, currentMassKg, unitMassKg, cap);

            // Massless items, no capacity, or overload disabled -> just the baseline (capped by demand).
            if (unitMassKg <= 0f || maxCapacityKg <= 0f || OverloadTuning.IsOff(overloadLevel))
                return Math.Min(baseUnits, cap);

            // No point overloading unless more than one capacity-load is actually wanted.
            if (cap <= baseUnits)
                return Math.Min(baseUnits, cap);

            float ratio = OverloadTuning.MaxOverloadRatio(overloadLevel);
            if (float.IsPositiveInfinity(ratio))
                return cap; // "no slowdown": carry everything we can use (an explicit player choice)

            // Load up to ratio × the CONFIGURED base cap (total mass ceiling), then cap by demand/availability.
            // Scaling off baseCap (not raw capacity) means a player-reduced carry-limit fraction also scales the
            // overload ceiling — otherwise any overload level silently nullifies the configured limit. At the
            // default fraction (1.0, base == max) the two are identical.
            float room = ratio * baseCapKg - currentMassKg;
            if (room <= 0f)
                return Math.Min(baseUnits, cap);

            int overloadUnits = (int)Math.Floor(room / unitMassKg);
            int target = Math.Max(baseUnits, overloadUnits);
            return Math.Min(target, cap);
        }
    }
}
