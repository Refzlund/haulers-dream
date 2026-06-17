using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure "how many units of a packable food stack the pawn keeps" math — the THIRD tmpItemsToKeep source in
    /// vanilla <c>Pawn_InventoryTracker.FirstUnloadableThing</c>: a colonist keeps packable food up to its food
    /// need's <c>MaxLevel</c> of nutrition, so the unload must not strip a packed lunch a harvested yield merged
    /// into. The runtime wrapper (<c>InventorySurplus.FoodKeepCountOf</c>) gathers the live nutrition numbers;
    /// this leaf does the arithmetic, so it is unit-testable headlessly and benchable 0-alloc.
    /// </summary>
    public static class FoodKeepMath
    {
        /// <summary>
        /// Units of this stack to KEEP as packable food: <c>stackCount − k</c>, where <c>k</c> is the fewest units
        /// whose removal brings the pawn's total packable nutrition within <paramref name="maxLevel"/>; 0 when the
        /// whole stack is surplus (even removing all of it still leaves the pawn over its cap).
        ///
        /// Closed form of the old incremental loop
        /// (<c>k = 0; while (total − perUnit·k &gt; maxLevel) k++; return stackCount − k;</c>):
        /// the loop finds the smallest non-negative integer <c>k</c> with <c>total − perUnit·k ≤ maxLevel</c>,
        /// i.e. <c>k = ceil((total − maxLevel) / perUnit)</c>, clamped to <c>[0, stackCount]</c>. The two early
        /// returns are folded in: <c>perUnit ≤ 0</c> (no nutrition per unit) keeps nothing, and "over cap even
        /// without the whole stack" (<c>total − perUnit·stackCount &gt; maxLevel</c> ⇒ <c>k ≥ stackCount</c>)
        /// yields keep 0. Behaviour is identical to the loop for every input (proven by an oracle test over a
        /// randomized grid), at O(1) instead of O(stackCount).
        /// </summary>
        /// <param name="totalPackableNutrition">The pawn's CURRENT total packable-food nutrition (incl. this stack).</param>
        /// <param name="maxLevel">The food need's MaxLevel (the nutrition cap the pawn packs up to).</param>
        /// <param name="perUnitNutrition">Per-unit nutrition of this stack's def.</param>
        /// <param name="stackCount">Units in this stack.</param>
        /// <returns>Units to keep, in <c>[0, stackCount]</c>.</returns>
        public static int KeepCount(float totalPackableNutrition, float maxLevel, float perUnitNutrition, int stackCount)
        {
            if (perUnitNutrition <= 0f || stackCount <= 0)
                return 0;

            // Over cap even with the WHOLE stack removed -> every unit is surplus (matches the loop's first early-out).
            if (totalPackableNutrition - perUnitNutrition * stackCount > maxLevel)
                return 0;

            float over = totalPackableNutrition - maxLevel;
            if (over <= 0f)
                return stackCount; // already within cap -> keep the whole stack (loop exits at k = 0)

            // k = ceil(over / perUnit), clamped to [0, stackCount]; keep = stackCount − k.
            int k = (int)Math.Ceiling(over / perUnitNutrition);
            if (k < 0)
                k = 0;
            if (k > stackCount)
                k = stackCount;
            return stackCount - k;
        }
    }
}
