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
        /// The SINGLE source of truth for the overload "lockstep" invariant: the shared race/feature
        /// off-matrix that BOTH the capacity gate (<c>OverloadGate.NoOverloadFor</c> — who may load past
        /// 100%) and the move-speed penalty (<c>StatPart_Overload.TryGetFactor</c> — who gets slowed)
        /// derive from. Because the overload deal is "extra capacity FOR a speed penalty", the set of
        /// pawns that MAY overload must equal the set that PAYS the slowdown — so both sites compute
        /// this one predicate and can never silently diverge on the shared conditions.
        ///
        /// True when the overload feature is active for THIS pawn's race/feature settings, i.e. ALL of:
        /// <list type="bullet">
        /// <item>not strict carry weight (the player hasn't pinned hauling to the carry limit),</item>
        /// <item>not Combat Extended (CE's encumbrance simulation is the sole slowdown truth there),</item>
        /// <item>the slider is not at "Off" (<see cref="OverloadTuning.IsOff"/>),</item>
        /// <item>the race may participate: a humanlike OR a mechanoid (animals never overload, so they
        ///       must never get the capacity — they'd never pay the matching slowdown).</item>
        /// </list>
        ///
        /// The conditions that are LEGITIMATELY different between the two sites are deliberately NOT part of
        /// this predicate (so the shared truth stays the shared truth):
        /// <list type="bullet">
        /// <item><b>Faction / drafted</b> — the capacity gate is faction- and draft-AGNOSTIC (a drafted or
        ///   non-player overloaded pawn keeps the extra capacity it already loaded), while the speed penalty
        ///   applies only to player-faction, undrafted pawns. The penalty site layers those on top via
        ///   <see cref="AppliesSpeedPenalty"/>.</item>
        /// <item><b>Level 0 ("Free")</b> — both agree by construction: the gate lets a level-0 pawn carry
        ///   freely and <see cref="OverloadTuning.SpeedFactor"/> at level 0 is 1.0 (the α-0 curve never
        ///   slows), so "free capacity, no slowdown" is itself a consistent bargain — no special-casing
        ///   needed here.</item>
        /// <item>The penalty site's live <c>currentMass/capacity &gt; 1</c> check is a per-instant state
        ///   test, not part of the who-MAY-overload set, so it also stays at that site.</item>
        /// </list>
        /// </summary>
        public static bool ParticipatesInOverload(
            bool strictCarryWeight, int overloadLevel, bool ceActive, bool isHumanlike, bool isMechanoid)
            => !strictCarryWeight
               && !ceActive
               && !OverloadTuning.IsOff(overloadLevel)
               && (isHumanlike || isMechanoid);

        /// <summary>
        /// Whether the move-speed penalty applies to a pawn (the WHO-set only, before the live
        /// over-capacity ratio test). This is <see cref="ParticipatesInOverload"/> — the SAME shared
        /// off-matrix the capacity gate uses — PLUS the two documented penalty-only asymmetries:
        /// the pawn must be player-faction and not drafted. Keeping this as a thin layer over the shared
        /// predicate (rather than re-deriving strict/CE/level/race) is what guarantees the gate and the
        /// StatPart can never disagree on the shared conditions. The caller still applies the live
        /// <c>ratio &gt; 1</c> test and the <see cref="OverloadTuning.SpeedFactor"/> result afterwards.
        /// </summary>
        public static bool AppliesSpeedPenalty(
            bool strictCarryWeight, int overloadLevel, bool ceActive,
            bool isHumanlike, bool isMechanoid, bool isPlayerFaction, bool isDrafted)
            => ParticipatesInOverload(strictCarryWeight, overloadLevel, ceActive, isHumanlike, isMechanoid)
               && isPlayerFaction
               && !isDrafted;

        /// <summary>
        /// Units to pick up now, bounded by the smart-overload ceiling and, when set, an absolute
        /// carry-weight cap.
        /// </summary>
        /// <param name="overloadLevel">The slider level (0..<see cref="OverloadTuning.MaxLevel"/>).</param>
        /// <param name="maxCapacityKg">The pawn's TRUE max carry capacity (100%); overload extends past this.</param>
        /// <param name="baseCapKg">The configured carry-limit mass (fraction × capacity), the no-overload cap.</param>
        /// <param name="currentMassKg">The pawn's current gear+inventory mass.</param>
        /// <param name="unitMassKg">Per-unit mass of the resource.</param>
        /// <param name="demandUnits">Total units usefully needed (this job + future build/craft plans).</param>
        /// <param name="availableUnits">Units actually pickable right now.</param>
        /// <param name="maxCeilingKg">Absolute cap on the pawn's total carried mass in kg (the "Max carry
        /// weight" setting), or <see cref="float.PositiveInfinity"/> when unset. Applied on TOP of the
        /// fractional limit and overload so the load never passes it; massless items are unbounded by it.</param>
        public static int UnitsToCarry(
            int overloadLevel,
            float maxCapacityKg,
            float baseCapKg,
            float currentMassKg,
            float unitMassKg,
            int demandUnits,
            int availableUnits,
            float maxCeilingKg = float.PositiveInfinity)
        {
            int units = UnitsToCarryUncapped(
                overloadLevel, maxCapacityKg, baseCapKg, currentMassKg, unitMassKg, demandUnits, availableUnits);
            // Absolute carry-weight cap: a hard ceiling the fractional/overload ceiling can never exceed.
            // Massless items (unit <= 0) carry no weight, so a weight cap cannot bound them; leave them as-is.
            if (float.IsPositiveInfinity(maxCeilingKg) || unitMassKg <= 0f)
                return units;
            return CarryMath.CountToPickUp(maxCeilingKg, currentMassKg, unitMassKg, units);
        }

        /// <summary>
        /// The overload load-size decision WITHOUT the absolute carry-weight cap: the original smart-overload
        /// economics. Kept as its own method so the cap in <see cref="UnitsToCarry"/> stays a clean post-filter
        /// and this math is exactly the tested logic.
        /// </summary>
        private static int UnitsToCarryUncapped(
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
            // overload ceiling; otherwise any overload level silently nullifies the configured limit. At the
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
