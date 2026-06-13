using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// When does a haul trip pick up EVERYTHING around the hauled item (bulk hauling into inventory)?
    /// </summary>
    public enum BulkHaulTrigger
    {
        /// <summary>Every haul — automatic or player-ordered — sweeps the nearby haulables too.</summary>
        Always,

        /// <summary>Automatic hauls always sweep; a PLAYER-ORDERED haul sweeps only when a second nearby
        /// item has also been tasked (another haul order queued on the same pawn). Ordering a single haul
        /// then truly hauls just that one thing — the finer-control default.</summary>
        SecondTasked,
    }

    /// <summary>
    /// Pure decision logic for bulk hauling (no game types — unit-tested headlessly).
    ///
    /// The "how much is worth carrying" question reuses the smart-overload model: carrying more saves
    /// round-trips but slows the pawn past 100% capacity, and <see cref="OverloadTuning.MaxOverloadRatio"/>
    /// is the break-even encumbrance where the slowdown starts costing more than the trip it saves
    /// (distance and base speed cancel out of that tradeoff — see OverloadTuning). So the bulk-haul mass
    /// ceiling is simply ratio × the configured carry limit: at "no slowdown" it's unbounded, at "off" or
    /// strict it's exactly the carry limit (never overload), in between it's the economically-correct load.
    /// </summary>
    public static class BulkHaulPolicy
    {
        /// <summary>
        /// The total gear+inventory mass a bulk-hauling pawn may load up to. PositiveInfinity = no mass
        /// ceiling (the "no slowdown" slider stop — carrying more is free, so more is always worth it).
        /// </summary>
        /// <param name="overloadLevel">The smart-overload slider level.</param>
        /// <param name="strictCarryWeight">Strict mode: never overload regardless of the slider.</param>
        /// <param name="baseCapKg">The configured carry-limit mass (fraction × true capacity).</param>
        public static float CeilingKg(int overloadLevel, bool strictCarryWeight, float baseCapKg)
        {
            if (baseCapKg <= 0f)
                return 0f;
            int level = strictCarryWeight ? OverloadTuning.OffLevel : overloadLevel;
            float ratio = OverloadTuning.MaxOverloadRatio(level);
            return float.IsPositiveInfinity(ratio) ? float.PositiveInfinity : ratio * baseCapKg;
        }

        /// <summary>
        /// Whether this haul should sweep the surroundings at all. Automatic (non-forced) hauls always
        /// sweep — every nearby haulable is already tasked by the hauling work itself. A player-ordered
        /// (forced) haul sweeps under <see cref="BulkHaulTrigger.Always"/>, or under
        /// <see cref="BulkHaulTrigger.SecondTasked"/> only when another haul order is queued nearby.
        /// </summary>
        public static bool TriggerSatisfied(BulkHaulTrigger trigger, bool forced, bool secondNearbyTasked)
            => !forced || trigger == BulkHaulTrigger.Always || secondNearbyTasked;

        /// <summary>What a player-ordered bulk haul arriving at the job tracker should do to an in-progress
        /// surgical first haul (see <see cref="DecideTakeover"/>).</summary>
        public enum BulkTakeoverAction
        {
            /// <summary>Let vanilla handle the order (no in-progress haul to fold in).</summary>
            PassThrough,
            /// <summary>A sweep is already running — fold the newly-ordered item into it (one trip).</summary>
            AppendToActiveBulk,
            /// <summary>The pawn is still hauling the surgical first item solo — interrupt it and start this
            /// (already-first-item-inclusive) bulk sweep NOW, instead of finishing the solo haul then coming back.</summary>
            TakeOverSoloHaul,
        }

        /// <summary>
        /// Under <see cref="BulkHaulTrigger.SecondTasked"/>, ordering a SECOND nearby haul is the player's
        /// "clean up this area" signal — the bulk sweep should take over IMMEDIATELY, not finish the first
        /// solo haul and come back for the rest. The incoming order is already a bulk job at this point (the
        /// JobOnThing postfix converted it because a nearby first order existed), so the decision is only:
        /// fold it into an already-running sweep, take over the still-solo first haul, or pass through.
        /// Pure so the gate is unit-pinned. (Under <see cref="BulkHaulTrigger.Always"/> the first order is
        /// itself already a sweep, so there is never a solo haul to take over — pass through, unchanged.)
        /// </summary>
        /// <param name="incomingIsBulk">The arriving player order is a bulk-haul job (not a single haul / container).</param>
        /// <param name="curIsLoadingBulk">The pawn's CURRENT job is a bulk haul still in its load phase (can absorb more).</param>
        /// <param name="curIsSoloHaulInSweep">The pawn's CURRENT job is the surgical first single haul, and its
        /// target is part of the incoming bulk's planned sweep (so taking over loses nothing).</param>
        public static BulkTakeoverAction DecideTakeover(BulkHaulTrigger trigger, bool incomingIsBulk,
            bool curIsLoadingBulk, bool curIsSoloHaulInSweep)
        {
            if (trigger != BulkHaulTrigger.SecondTasked || !incomingIsBulk)
                return BulkTakeoverAction.PassThrough;
            if (curIsLoadingBulk)
                return BulkTakeoverAction.AppendToActiveBulk; // 3rd/4th order coalesces into the running sweep
            if (curIsSoloHaulInSweep)
                return BulkTakeoverAction.TakeOverSoloHaul;    // 2nd order: absorb the solo first haul, start now
            return BulkTakeoverAction.PassThrough;             // lone order stays surgical / current job unrelated
        }

        /// <summary>
        /// How many units of a candidate stack to take, given the running load: fits under the ceiling,
        /// never more than the stack holds. Massless items are taken in full.
        /// </summary>
        public static int CountWithinCeiling(float ceilingKg, float currentMassKg, float unitMassKg, int stackCount)
        {
            if (stackCount <= 0)
                return 0;
            if (unitMassKg <= 0f)
                return stackCount;
            if (float.IsPositiveInfinity(ceilingKg))
                return stackCount;
            return CarryMath.CountToPickUp(ceilingKg, currentMassKg, unitMassKg, stackCount);
        }
    }
}
