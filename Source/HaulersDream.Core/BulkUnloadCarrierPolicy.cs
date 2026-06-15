using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for pack-animal BULK UNLOAD — the INVERSE of <see cref="PackAnimalLoadPolicy"/>: a
    /// hauler empties many stacks OUT of a flagged carrier's inventory into its OWN backpack in one visit, with
    /// the last/overflow stack going to its hands. No game types — unit-tested headlessly; the game-layer
    /// <c>JobDriver_UnloadCarrierInBulk</c> feeds it live <see cref="MassUtility"/> numbers and acts on the result.
    ///
    /// The selection ladder (<see cref="PlanNextPull"/>):
    ///   1. BACKPACK-FIRST — pull as many units as fit under the hauler's free carry mass into its inventory
    ///      (tagged in HD's comp → ships to storage via the normal unload). Always preferred while room remains.
    ///   2. LAST-STACK-TO-HANDS — when only ONE stack is left in the carrier and it won't fit the backpack, take
    ///      it to the carry tracker (it ships directly via a HaulToStorage job; the carrier is then empty).
    ///   3. FALLBACK-ONE-TO-HANDS — the backpack is near full but several stacks remain: take one to hands so the
    ///      visit still makes progress (deliberately exceeds the soft carry ceiling → the JobDef carries NO
    ///      checkEncumbrance).
    /// </summary>
    public static class BulkUnloadCarrierPolicy
    {
        /// <summary>
        /// Does the hauler have enough free backpack room to be worth starting a bulk unload? True when its
        /// current encumbrance leaves at least <paramref name="minFreeSpacePct"/> of capacity free, i.e.
        /// <c>encumbrancePct &lt;= 1 − minFreeSpacePct</c>. (At <c>minFreeSpacePct = 0.5</c> a pawn already at or
        /// above 50% encumbrance is rejected, so the visit can scoop a meaningful amount into the backpack rather
        /// than immediately overflowing everything to hands.)
        /// </summary>
        public static bool HasEnoughBackpackRoom(float encumbrancePct, float minFreeSpacePct)
            => encumbrancePct <= 1f - minFreeSpacePct;

        /// <summary>
        /// How many units of a stack fit in the hauler's remaining free carry mass — the backpack-room clamp,
        /// the same shape as <see cref="PackAnimalLoadPolicy.DepositCountWithinFreeSpace"/> (this direction pulls
        /// INTO the hauler instead of onto the animal, but the mass arithmetic is identical). Massless items are
        /// taken in full; 0 when there is no room for even one unit (the ladder then routes to hands).
        /// </summary>
        public static int PullCountWithinFreeSpace(float freeSpaceKg, float unitMassKg, int stackCount)
        {
            if (stackCount <= 0)
                return 0;
            if (unitMassKg <= 0f)
                return stackCount;
            if (freeSpaceKg <= 0f)
                return 0;
            int fits = (int)Math.Floor(freeSpaceKg / unitMassKg);
            return Math.Min(fits, stackCount);
        }

        /// <summary>One carrier-inventory stack as the planner sees it: its index in the carrier's container, its
        /// per-unit mass, and its current count.</summary>
        public struct CarrierStack
        {
            public int Index;
            public float UnitMassKg;
            public int StackCount;

            public CarrierStack(int index, float unitMassKg, int stackCount)
            {
                Index = index;
                UnitMassKg = unitMassKg;
                StackCount = stackCount;
            }
        }

        /// <summary>The planner's verdict: which carrier stack to pull next, how many units, and whether it goes
        /// to the hauler's HANDS (carry tracker, ships directly) or BACKPACK (inventory, tagged → normal unload).
        /// <c>ChosenIndex &lt; 0</c> means nothing can be pulled (empty carrier / all stacks empty) — the loop ends.</summary>
        public struct PullPlan
        {
            public int ChosenIndex;
            public int Count;
            public bool ToHands;

            public static readonly PullPlan None = new PullPlan { ChosenIndex = -1, Count = 0, ToHands = false };

            public PullPlan(int chosenIndex, int count, bool toHands)
            {
                ChosenIndex = chosenIndex;
                Count = count;
                ToHands = toHands;
            }
        }

        /// <summary>
        /// Decide the next pull. The ladder, in order:
        ///   1. BACKPACK-FIRST: the first stack (carrier order) that fits ≥1 unit under <paramref name="freeSpaceKg"/>
        ///      is pulled into the backpack (the count clamped by <see cref="PullCountWithinFreeSpace"/>).
        ///   2. LAST-STACK-TO-HANDS: nothing fits the backpack AND exactly one non-empty stack remains → take that
        ///      whole stack to hands (the carrier ends empty; it ships directly).
        ///   3. FALLBACK-ONE-TO-HANDS: nothing fits the backpack and several stacks remain → take one whole stack
        ///      (the first non-empty) to hands so the visit still makes progress.
        /// Returns <see cref="PullPlan.None"/> only when no non-empty stack exists.
        /// </summary>
        public static PullPlan PlanNextPull(IReadOnlyList<CarrierStack> stacks, float freeSpaceKg)
        {
            if (stacks == null || stacks.Count == 0)
                return PullPlan.None;

            // 1. Backpack-first: first stack that fits at least one unit under the free carry mass.
            for (int i = 0; i < stacks.Count; i++)
            {
                var s = stacks[i];
                if (s.StackCount <= 0)
                    continue;
                int fit = PullCountWithinFreeSpace(freeSpaceKg, s.UnitMassKg, s.StackCount);
                if (fit > 0)
                    return new PullPlan(s.Index, fit, toHands: false);
            }

            // Nothing fits the backpack. Find non-empty stacks for the to-hands ladder.
            int firstNonEmpty = -1;
            int nonEmptyCount = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].StackCount > 0)
                {
                    nonEmptyCount++;
                    if (firstNonEmpty < 0)
                        firstNonEmpty = i;
                }
            }
            if (firstNonEmpty < 0)
                return PullPlan.None; // no non-empty stack -> done

            // 2. Last-stack-to-hands, or 3. fallback-one-to-hands — both take ONE whole stack to the carry tracker;
            // the distinction is purely informational (the carrier is emptied either way, the count is the same).
            var chosen = stacks[firstNonEmpty];
            return new PullPlan(chosen.Index, chosen.StackCount, toHands: true);
        }
    }
}
