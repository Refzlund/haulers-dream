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

        /// <summary>The three outcomes of a player's haul order, once the trigger and the oversized-stack
        /// carve-out are resolved (see <see cref="DecideOrderedHaul"/>).</summary>
        public enum OrderedHaulPlan
        {
            /// <summary>Keep vanilla's single hand-haul of just the clicked stack (no HD bulk job).</summary>
            VanillaSingle,

            /// <summary>Route the one oversized clicked stack through inventory for a single trip, WITHOUT
            /// sweeping any neighbors (issue #223).</summary>
            InventorySingleStack,

            /// <summary>Sweep the clicked stack plus the nearby haulables into one bulk trip.</summary>
            SweepNeighbors,
        }

        /// <summary>
        /// What a haul order should actually do, once the trigger and the oversized-stack carve-out are
        /// resolved. This replaces the old two-way "sweep or not" gate with three outcomes so an oversized
        /// single order can ride inventory for one trip WITHOUT dragging in the neighborhood.
        ///
        /// Issue #223: the oversized carve-out used to short-circuit the "return null" gate and fall straight
        /// into the sweep, so with stack-size mods (almost every clicked stack is oversized) every forced haul
        /// behaved like "Haul everything nearby" and the <see cref="BulkHaulTrigger.SecondTasked"/> option never
        /// took effect. Splitting the carve-out into its own plan preserves the one-trip delivery while keeping
        /// a single order to just its one stack.
        ///
        /// Precedence, highest first: an explicit sweep (<paramref name="forceSweep"/>) and every automatic
        /// (non-forced) haul always sweep; a forced order sweeps under <see cref="BulkHaulTrigger.Always"/> or
        /// when a second nearby haul is already tasked; otherwise a forced order of an OVERSIZED stack
        /// (<paramref name="oversizedRidesInventory"/>) delivers that one stack via inventory; anything left is a
        /// plain vanilla single hand-haul.
        /// </summary>
        /// <param name="trigger">The bulk-haul trigger setting.</param>
        /// <param name="forced">A player-ordered (forced) haul, not an automatic work-scan haul.</param>
        /// <param name="forceSweep">The explicit "Haul everything nearby" order: always sweeps.</param>
        /// <param name="secondNearbyTasked">Another haul order is already queued on a nearby item (the
        /// SecondTasked "clean up this area" signal).</param>
        /// <param name="oversizedRidesInventory">A forced order whose clicked stack is too big for one armful and
        /// the oversized-into-inventory option is on: worth one inventory trip even with no second order tasked.</param>
        public static OrderedHaulPlan DecideOrderedHaul(BulkHaulTrigger trigger, bool forced, bool forceSweep,
            bool secondNearbyTasked, bool oversizedRidesInventory)
        {
            if (forceSweep)
                return OrderedHaulPlan.SweepNeighbors;                    // explicit "haul everything nearby"
            if (!forced)
                return OrderedHaulPlan.SweepNeighbors;                    // automatic hauls always sweep
            if (trigger == BulkHaulTrigger.Always || secondNearbyTasked)
                return OrderedHaulPlan.SweepNeighbors;
            if (oversizedRidesInventory)
                return OrderedHaulPlan.InventorySingleStack;             // one big stack, one trip, NO sweep
            return OrderedHaulPlan.VanillaSingle;
        }

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
        /// Is a single hauled stack worth routing through INVENTORY instead of carrying it in hands? Yes only
        /// when it's too big to carry in one armful (<paramref name="stackCount"/> &gt; <paramref name="handCap"/>,
        /// the pawn's per-stack carry cap) AND the reachable storage can accept MORE than one hand-trip would
        /// deliver (<paramref name="deliverable"/> &gt; handCap). Otherwise hands are at least as good and we keep
        /// the vanilla single haul — and the caller never carries more than <paramref name="deliverable"/>, so
        /// nothing strands. Pure so the boundary is unit-pinned.
        /// </summary>
        public static bool OversizedStackWorthInventory(int stackCount, int handCap, int deliverable)
            => stackCount > handCap && deliverable > handCap;

        /// <summary>
        /// #115: should a haul be DECLINED for the into-inventory sweep because carrying it in inventory would move
        /// FEWER units of the hauled stack per trip than a plain hand-haul? Under Combat Extended a very BULKY stack
        /// (e.g. a heavy cannon shell) fits fewer units in inventory (CE caps inventory by weight AND bulk) than a
        /// hands-carry armful, which is stack/volume-limited only (CE does not bulk-limit the carry tracker). Routing
        /// it through inventory then delivers a trickle where hands would carry a full armful (the "one round at a
        /// time into the shelf" report). True = keep vanilla's single hand-haul instead of converting.
        ///
        /// #124 (previously overlooked): <paramref name="inventoryFit"/> is clamped by the stack's live count, so it
        /// must be compared against what hands would move OF THAT SAME STACK, min(<paramref name="handCap"/>,
        /// <paramref name="stackCount"/>), never the def-level armful alone. A lone rock chunk under a chunk-stacking
        /// mod (stackLimit raised above 1, each field chunk still a 1-count stack) otherwise compares fit 1 against
        /// armful N and wrongly declines EVERY automatic chunk haul: hands cannot move more than the whole stack
        /// either, and the sweep takes the neighbors on top. When the whole stack fits in inventory the inventory
        /// route is never worse, so the decline only fires for a genuine partial fit of a bigger stack.
        ///
        /// Only meaningful with CE active (<paramref name="ceActive"/>): without CE, inventory and hands share the one
        /// mass/volume limit, so a small <paramref name="inventoryFit"/> would arise only for an already-loaded pawn,
        /// where declining would wrongly abort a legitimate accumulation, so it stays false.
        /// A <paramref name="forceSweep"/> (the explicit "haul everything nearby" order) is never declined.
        /// </summary>
        /// <param name="ceActive">Combat Extended present (the bulk dimension exists).</param>
        /// <param name="forceSweep">An explicit player sweep order: always convert, never decline.</param>
        /// <param name="inventoryFit">Units of this stack that fit in inventory (mass + CE bulk clamped, and never
        /// more than the stack holds).</param>
        /// <param name="handCap">Units a hands-carry could take of this DEF per armful (vanilla MaxStackSpaceEver,
        /// which is min(stackLimit, CarryingCapacity / VolumePerUnit) and ignores the live stack's count).</param>
        /// <param name="stackCount">The hauled stack's live count. Clamps the hands side: a hand-haul of this stack
        /// moves at most this many units, whatever the def-level armful says.</param>
        public static bool InventoryHaulWorseThanHands(bool ceActive, bool forceSweep, int inventoryFit, int handCap, int stackCount)
            => ceActive && !forceSweep && inventoryFit < Math.Min(handCap, stackCount);

        /// <summary>
        /// Def-level overload kept for source/binary compatibility (pre #124 shape): compares against the raw
        /// def armful with no stack clamp, i.e. behaves as if the stack were at least an armful. Prefer the
        /// <c>stackCount</c> overload; this one wrongly declines a stack smaller than one armful (issue #124).
        /// </summary>
        /// <param name="ceActive">Combat Extended present (the bulk dimension exists).</param>
        /// <param name="forceSweep">An explicit player sweep order: always convert, never decline.</param>
        /// <param name="inventoryFit">Units of this stack that fit in inventory (mass + CE bulk clamped).</param>
        /// <param name="handArmful">Units a hands-carry would take for this def (vanilla MaxStackSpaceEver).</param>
        public static bool InventoryHaulWorseThanHands(bool ceActive, bool forceSweep, int inventoryFit, int handArmful)
            => InventoryHaulWorseThanHands(ceActive, forceSweep, inventoryFit, handArmful, int.MaxValue);

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
