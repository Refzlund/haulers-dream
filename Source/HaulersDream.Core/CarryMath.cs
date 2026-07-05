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
        /// How many units fit in a pawn's remaining BULK room, Combat Extended's second carry dimension
        /// (weight is the first; vanilla has no bulk). The runtime feeds CE's live numbers here so every
        /// bulk clamp shares ONE tested division instead of re-inlining the two float gotchas: an INFINITE
        /// room (CE absent, or CE's read failed open) must mean "never binds" (a raw <c>(int)(infinity / x)</c>
        /// cast is int.MinValue and would silently kill every plan), and a nonpositive per-unit bulk means
        /// the dimension does not apply to this item at all (also "never binds", NOT "fits zero").
        /// Previously overlooked (issue #125): the construction planner had NO bulk term, so it offered
        /// inventory loads the driver's bulk-clamped take could never perform, an infinite re-offer loop.
        /// </summary>
        /// <param name="bulkRoomAvailable">Remaining bulk capacity (CE's availableBulk). May be negative
        /// (an over-capacity pawn), or positive infinity when the dimension is absent.</param>
        /// <param name="bulkPerUnit">Bulk of ONE unit of the item; &lt;= 0 means bulk never binds for it.</param>
        /// <returns>Whole units that fit; 0 when the room is used up; int.MaxValue when bulk never binds.</returns>
        public static int UnitsThatFitBulk(float bulkRoomAvailable, float bulkPerUnit)
        {
            if (bulkPerUnit <= 0f || float.IsPositiveInfinity(bulkRoomAvailable))
                return int.MaxValue;
            if (bulkRoomAvailable <= 0f)
                return 0;
            double units = Math.Floor(bulkRoomAvailable / (double)bulkPerUnit);
            return units >= int.MaxValue ? int.MaxValue : (int)units;
        }

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
