using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure per-def deliverable cap + per-trip mass budget the transporter/portal bulk-load sweep uses to bound each
    /// pull. No game types — unit-tested headlessly; the runtime feeds it live <c>stackCount</c> / manifest /
    /// ledger / <c>OverloadGate</c> / <c>MassUtility</c> numbers.
    /// </summary>
    public static class TransportLoadPlan
    {
        /// <summary>
        /// How many units of one stack to pull this step — the tightest of every limit:
        /// the stack in hand, what the manifest still wants, what the ledger lets this pawn claim, and what the
        /// pawn can carry (the smart-overload count). Never &lt; 0.
        /// </summary>
        public static int DeliverableUnits(int stackInHand, int manifestRemaining, int ledgerAvailable, int carryAffordable)
        {
            int min = stackInHand;
            if (manifestRemaining < min) min = manifestRemaining;
            if (ledgerAvailable < min) min = ledgerAvailable;
            if (carryAffordable < min) min = carryAffordable;
            return min < 0 ? 0 : min;
        }

        /// <summary>
        /// The mass headroom for this trip — the destination-remaining-mass cap HD's plain bulk-haul lacks (the one
        /// genuinely new term over <c>OverloadGate.CountToPickUp</c>):
        ///   • transporters (<paramref name="hasMassCap"/> true): <c>min(pawnFreeSpace, groupMassCap − groupMassUsage)</c>
        ///     — never load more into the pod group than it can still hold;
        ///   • portals (<paramref name="hasMassCap"/> false): <c>pawnFreeSpace</c> only (a portal is uncapped, the
        ///     group term is ignored).
        /// A negative group headroom clamps the whole budget to 0 (the pod group is already full). Never &lt; 0.
        /// </summary>
        public static float TripMassBudget(float pawnFreeSpace, float groupMassCap, float groupMassUsage, bool hasMassCap)
        {
            float budget = pawnFreeSpace;
            if (hasMassCap)
            {
                float groupHeadroom = groupMassCap - groupMassUsage;
                if (groupHeadroom < budget)
                    budget = groupHeadroom;
            }
            return budget < 0f ? 0f : budget;
        }

        /// <summary>
        /// How many units of a stack fit in <paramref name="massBudgetKg"/> at <paramref name="unitMassKg"/> per
        /// unit, never more than <paramref name="offeredCount"/>. Massless items take in full; 0 with no budget.
        /// (Same mass clamp as <c>PackAnimalLoadPolicy.DepositCountWithinFreeSpace</c>; surfaced here so the
        /// transporter sweep can apply the trip budget without reaching into the pack-animal policy.)
        /// </summary>
        public static int UnitsWithinMassBudget(float massBudgetKg, float unitMassKg, int offeredCount)
        {
            if (offeredCount <= 0)
                return 0;
            if (unitMassKg <= 0f)
                return offeredCount;
            if (massBudgetKg <= 0f)
                return 0;
            float ratio = massBudgetKg / unitMassKg;
            // Short-circuit BEFORE the int cast when the whole offer fits. Previously overlooked: a huge or infinite
            // budget (float.MaxValue trip mass at overload level 0 on an uncapped portal, or the fair-share no-clamp
            // sentinel) made the float division overflow to infinity, and net48's (int) cast of an out-of-range
            // double yields int.MinValue, so the clamp answered 0 for EVERY stack and the sweep silently built
            // nothing. ratio < offeredCount also proves the cast below is in int range (offeredCount is an int).
            if (ratio >= offeredCount)
                return offeredCount;
            return (int)Math.Floor(ratio);
        }
    }
}
