using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for loading a pawn's tagged inventory onto a pack animal (no game types — unit-tested
    /// headlessly): the auto-divert trigger gate, and the per-stack deposit clamp. The game-layer
    /// <c>PackAnimalLoad</c> gathers the primitives (carrier, surplus, free space) and acts on the result.
    /// </summary>
    public static class PackAnimalLoadPolicy
    {
        /// <summary>
        /// Should an over-encumbered pawn on a non-home map automatically divert to load the nearest pack
        /// animal? Only when the feature is enabled, the mod is active on non-home maps at all, we ARE on a
        /// non-home/temporary map (a caravan/encounter site — at home, vanilla storage handles unloading), a
        /// usable carrier exists, the pawn actually has surplus to offload, and it isn't already loading.
        /// </summary>
        public static bool ShouldAutoDivert(bool autoDivertEnabled, bool modActiveOnNonHomeMaps,
            bool isPlayerHome, bool hasCarrier, bool hasSurplus, bool alreadyLoading)
            => autoDivertEnabled && modActiveOnNonHomeMaps && !isPlayerHome
               && hasCarrier && hasSurplus && !alreadyLoading;

        /// <summary>
        /// How many units of a stack a pack animal can accept before going over-encumbered: fits within the
        /// animal's remaining free carry mass, never more than the offered count. Massless items are accepted
        /// in full. 0 when the animal has no room (the caller then tries another carrier, or keeps the load).
        /// </summary>
        public static int DepositCountWithinFreeSpace(float freeSpaceKg, float unitMassKg, int offeredCount)
        {
            if (offeredCount <= 0)
                return 0;
            if (unitMassKg <= 0f)
                return offeredCount;
            if (freeSpaceKg <= 0f)
                return 0;
            int fits = (int)Math.Floor(freeSpaceKg / unitMassKg);
            return Math.Min(fits, offeredCount);
        }
    }
}
