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
        /// Should a non-home pawn OPPORTUNISTICALLY offload its scooped loot onto a pack animal now — the
        /// caravan counterpart to the home storage unload, fired by the settle/schedule triggers (end-of-run,
        /// before-downtime, interval, idle backstop) rather than the over-encumbered ceiling. Same terms as
        /// <see cref="ShouldAutoDivert"/> PLUS the two standing gates the AUTOMATIC home unloads also apply but
        /// the ceiling/manual pack paths deliberately do not: the pawn must be HD-eligible and not drafted (an
        /// ineligible / hauling-incapable / disallowed-mech / drafted pawn must never auto-walk off to an
        /// animal). The trigger TIMING (grace / settle / downtime) is decided by each caller, NOT here — this
        /// is purely the destination-availability + standing gate, so it can't diverge from the home settle math.
        /// </summary>
        public static bool ShouldOffloadOpportunistically(bool autoDivertEnabled, bool modActiveOnNonHomeMaps,
            bool isPlayerHome, bool hasCarrier, bool hasSurplus, bool alreadyLoading, bool eligible, bool drafted)
            => ShouldAutoDivert(autoDivertEnabled, modActiveOnNonHomeMaps, isPlayerHome, hasCarrier, hasSurplus, alreadyLoading)
               && eligible && !drafted;

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
