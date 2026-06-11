using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure, game-independent carry/encumbrance math. All inputs are primitives so this can be
    /// unit-tested headlessly (no loaded RimWorld game required). The game-coupled WorkGiver reads
    /// the live numbers from <c>MassUtility</c> and feeds them here.
    ///
    /// "Carry limit" = a fraction of the pawn's true maximum carry capacity
    /// (<c>MassUtility.Capacity(pawn)</c>). Hauler's Dream defaults that fraction to 1.0, i.e. the
    /// pawn's full maximum — the deliberate change from the original mod's fixed/percentage cap.
    /// </summary>
    public static class CarryMath
    {
        /// <summary>Smallest fraction we allow, to avoid a 0 capacity making pawns never haul.</summary>
        public const float MinFraction = 0.05f;

        /// <summary>Largest fraction we allow: 1.0 = the pawn's full maximum carrying capacity (100%).</summary>
        public const float MaxFraction = 1.0f;

        /// <summary>
        /// Effective carry-limit mass in kg: <paramref name="maxCapacityKg"/> scaled by
        /// <paramref name="carryLimitFraction"/>. A non-positive fraction is treated as the full
        /// maximum (1.0) so a misconfigured setting never disables hauling outright.
        /// </summary>
        public static float EffectiveCapacity(float maxCapacityKg, float carryLimitFraction)
        {
            if (maxCapacityKg <= 0f)
                return 0f;
            if (carryLimitFraction <= 0f)
                return maxCapacityKg;
            float f = Clamp(carryLimitFraction, MinFraction, MaxFraction);
            return maxCapacityKg * f;
        }

        /// <summary>
        /// How many units of a stack a pawn may add to its inventory before reaching the carry
        /// limit. Massless items (unit mass &lt;= 0) are taken in full; a pawn already at/over the
        /// limit takes none.
        /// </summary>
        /// <param name="capacityKg">The effective carry-limit mass (see <see cref="EffectiveCapacity"/>).</param>
        /// <param name="currentMassKg">The pawn's current gear+inventory mass.</param>
        /// <param name="unitMassKg">Per-unit mass of the thing being picked up.</param>
        /// <param name="stackCount">Units available to pick up.</param>
        public static int CountToPickUp(float capacityKg, float currentMassKg, float unitMassKg, int stackCount)
        {
            if (stackCount <= 0)
                return 0;
            if (unitMassKg <= 0f)
                return stackCount; // massless: capacity is irrelevant
            float remaining = capacityKg - currentMassKg;
            if (remaining <= 0f)
                return 0;
            int fits = (int)Math.Floor(remaining / unitMassKg);
            if (fits <= 0)
                return 0;
            return Math.Min(fits, stackCount);
        }

        /// <summary>Current encumbrance as a fraction of the pawn's true maximum (0..1+).</summary>
        public static float EncumbranceFraction(float currentMassKg, float maxCapacityKg)
            => maxCapacityKg <= 0f ? 0f : currentMassKg / maxCapacityKg;

        /// <summary>
        /// True once the pawn's current mass has reached the effective carry limit — the signal to
        /// stop scooping and run the single unload pass.
        /// </summary>
        public static bool ReachedCarryLimit(float capacityKg, float currentMassKg, float epsilon = 0.0001f)
            => currentMassKg >= capacityKg - epsilon;

        private static float Clamp(float v, float lo, float hi)
            => v < lo ? lo : (v > hi ? hi : v);
    }
}
