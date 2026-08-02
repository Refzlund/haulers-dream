namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for the OPT-IN corpse-carry loosening (unit-tested headlessly): which haulers may
    /// sweep bodies, which bodies qualify, and how heavily a body counts against the hauler's carry ceiling.
    ///
    /// <para>WHAT THE STEAM REPORT ACTUALLY HIT. A humanlike corpse weighs exactly 60 kg
    /// (<c>60 × InnerPawn.BodySize × notMissingPartsCoverage</c>, plus whatever it is still wearing), while a
    /// default colonist's bulk-haul ceiling is 96.25 kg (<c>MassUtility.Capacity</c> = BodySize 1.0 × 35 kg,
    /// times the "Fair" overload break-even of ≈2.75). Two bodies want 120 kg, so the second is refused and
    /// colonists fetch corpses one per trip. Nothing is malfunctioning there — that is the carry model
    /// answering correctly — but a vanilla Lifter mech, whose 52.5 kg capacity buys a 144.4 kg ceiling,
    /// already fits two, so from inside the game the rule looks arbitrary rather than physical.</para>
    ///
    /// <para>THE DISCOUNT, AND WHY IT STAYS HONEST. <see cref="BudgetMassKg"/> divides what a corpse COSTS
    /// THE PLANNER, never what the pawn actually carries. The 60 kg body is still 60 real kilograms in that
    /// pawn's inventory afterwards, so <c>StatPart_Overload</c> — which reads the live
    /// <c>GearAndInventoryMass</c>, not this budget — charges the full move-speed penalty for every one of
    /// them. The overload slider's "carry more, move slower" bargain therefore survives intact: nobody is
    /// handed free capacity, they are permitted to OVERSHOOT the break-even point that slider aims at. The
    /// overshoot is a real cost knowingly accepted — one slow trip instead of two fast ones — and that trade
    /// is exactly why this must be opt-in rather than a default someone discovers by accident.</para>
    ///
    /// <para>EVERY SWITCH HERE DEFAULTS TO TODAY'S BEHAVIOUR: all three hauler kinds allowed, humanlike
    /// corpses allowed, no mass limit (0), allowance 1.0. Under that configuration all three functions are
    /// provable no-ops, and <c>CorpseHaulPolicyTests</c> pins that as its first and most important property —
    /// a settings default that quietly re-planned a shipped colony's hauling would be a regression wearing a
    /// feature's clothes.</para>
    /// </summary>
    public static class CorpseHaulPolicy
    {
        /// <summary>
        /// May THIS hauler take part in corpse hauling at all? A NARROWING filter layered on top of
        /// <see cref="EligibilityPolicy.IsEligible"/>: it can only subtract from the set of pawns that
        /// already passed eligibility, never add to it, so a race the mod does not haul with in the first
        /// place cannot be switched on here by accident.
        ///
        /// <para>WHY THE BRANCH ORDER IS COPIED RATHER THAN REINVENTED. This walks the races in exactly the
        /// order <see cref="EligibilityPolicy.IsEligible"/> walks them — mech, then not-humanlike, then the
        /// fall-through — because the two predicates have to classify any given pawn the SAME way. If they
        /// ever disagreed, a player's "mechs may haul corpses" switch would end up governing a pawn the
        /// eligibility pass had already filed as a colonist. The tempting shorthand is what breaks it:
        /// RimWorld's <c>RaceProps.Animal</c> is NOT the complement of humanlike-plus-mech — anomaly entities
        /// and tool-user races are none of the three — so branching on <c>Animal</c> here would route those
        /// races down a different arm than eligibility does. Order first, flags second; note that the last
        /// branch is a fall-through, not a humanlike test.</para>
        /// </summary>
        /// <param name="isMechanoid">The pawn's mechanoid flag. Tested FIRST so that a modded race declaring
        /// itself both mechanoid and humanlike is governed by the mech switch, matching how eligibility
        /// resolves the same tie — the point is that the two agree, not which one wins.</param>
        /// <param name="isHumanlike">The pawn's humanlike flag, consulted only once the mech branch declines.
        /// Its job is to separate colonists from everything that is neither mech nor humanlike, which is a
        /// wider set than "animals" (see the note above).</param>
        /// <param name="corpseHaulByColonists">Player switch for the fall-through (colonist) branch. On by
        /// default: colonists haul corpses today and must keep doing so untouched.</param>
        /// <param name="corpseHaulByMechs">Player switch for mechanoid haulers — the group most likely to be
        /// turned OFF, since a Lifter's larger ceiling makes it the one that hauls bodies in numbers.</param>
        /// <param name="corpseHaulByAnimals">Player switch for the not-humanlike branch. Narrowing only: an
        /// animal still has to have cleared <see cref="EligibilityPolicy.IsEligible"/>'s own
        /// <c>allowAnimals</c> opt-in (off by default) before this is ever asked.</param>
        public static bool HaulerMaySweepCorpses(bool isMechanoid, bool isHumanlike,
            bool corpseHaulByColonists, bool corpseHaulByMechs, bool corpseHaulByAnimals)
        {
            if (isMechanoid)
                return corpseHaulByMechs;
            if (!isHumanlike)
                return corpseHaulByAnimals;
            return corpseHaulByColonists;
        }

        /// <summary>
        /// May THIS body be swept? Two independent gates that fail for different reasons: an opt-out for
        /// humanlike corpses (a player who wants colonist and raider bodies handled deliberately, by hand,
        /// can still let the sweep clear a field of dead squirrels), and an absolute per-body mass limit for
        /// a player who wants the sweep to stay off the heavy things entirely.
        ///
        /// <para>Note this is the TIGHTENING half of the feature and reads the body's REAL mass, deliberately
        /// not the discounted budget from <see cref="BudgetMassKg"/>. A limit expressed in kilograms should
        /// mean kilograms; if the allowance were applied first, raising the allowance would silently raise
        /// the limit too and the two settings would fight.</para>
        /// </summary>
        /// <param name="isHumanlikeCorpse">True when the dead pawn was humanlike — the distinction players
        /// actually care about (a body they might bury, resurrect or strip, versus butchery and cleanup).
        /// The corresponding switch does not exist for animal corpses because nobody has asked for one.</param>
        /// <param name="corpseHaulHumanlike">The humanlike opt-out. On by default (today's behaviour); off
        /// leaves humanlike bodies to vanilla's own single-body haul and touches nothing else.</param>
        /// <param name="corpseMassKg">The body's real mass in kilograms, gear included — the same number the
        /// carry arithmetic will charge, so the limit means what it says on the slider.</param>
        /// <param name="corpseMaxHaulMassKg">Per-body ceiling in kilograms, or the "no limit" sentinel. Any
        /// value at or below 0 disables the test, matching the <c>carryMassCapKg = 0</c> convention already
        /// used for the carry-weight cap, so a player reading two mass sliders reads them the same way. The
        /// comparison is INCLUSIVE (<c>&lt;=</c>): a limit dialled to exactly a body's weight admits it,
        /// because a player who types 60 for a 60 kg body means "these, and nothing heavier".</param>
        public static bool CorpseMayBeSwept(bool isHumanlikeCorpse, bool corpseHaulHumanlike,
            float corpseMassKg, float corpseMaxHaulMassKg)
            => (!isHumanlikeCorpse || corpseHaulHumanlike)
               && (corpseMaxHaulMassKg <= 0f || corpseMassKg <= corpseMaxHaulMassKg);

        /// <summary>
        /// What a thing COSTS against the hauler's carry ceiling, which for a corpse may be less than it
        /// weighs. This is the loosening knob the Steam report asked for: at an allowance of 2.0 a 60 kg body
        /// is budgeted at 30 kg, so two of them fit under the 96.25 kg ceiling that previously admitted one.
        ///
        /// <para>THE PAWN STILL CARRIES THE REAL MASS. Only the planner's budget is discounted, so the
        /// move-speed penalty (which reads the live gear-and-inventory mass, not this) charges full price for
        /// both bodies. See the type remarks: the player is buying an overshoot past the overload slider's
        /// break-even, not free capacity, and that is the whole reason the default is exact identity.</para>
        ///
        /// <para>Every non-corpse and every non-loosening allowance returns <paramref name="realMassKg"/>
        /// unchanged and bit-for-bit, so callers can pipe EVERY candidate through this one function rather
        /// than each deciding for itself when the discount applies — which is how a discount ends up applied
        /// in one place and forgotten in another.</para>
        /// </summary>
        /// <param name="realMassKg">The thing's true mass in kilograms: what the pawn will actually be
        /// carrying, and what the slowdown will actually charge for.</param>
        /// <param name="isCorpse">True only for a corpse. Ordinary items are never discounted — the entire
        /// justification is that a body is an indivisible, unusually heavy, one-per-victim load, which no
        /// stack of steel is (a stack simply gets split at the ceiling and the rest fetched next trip).</param>
        /// <param name="corpseCarryAllowance">How many bodies' worth of budget one body may occupy. 1.0 (the
        /// default) means a corpse costs exactly what it weighs; 2.0 halves the price. At or below 1.0 this
        /// is identity — the allowance can only ever loosen, never tighten, because tightening already has
        /// its own knob in <see cref="CorpseMayBeSwept"/>.</param>
        public static float BudgetMassKg(float realMassKg, bool isCorpse, float corpseCarryAllowance)
        {
            if (!isCorpse)
                return realMassKg;
            // The naive `allowance > 1f ? mass / allowance : mass` is a trap at the edges. +∞ passes the
            // `> 1f` test and divides a body down to a weightless 0 kg, which would let one hauler pocket an
            // unbounded pile of them. NaN happens to fail the test already (every IEEE comparison against NaN
            // is false), but is rejected explicitly so the guard reads as "only a real, loosening number gets
            // to divide" instead of leaning on comparison trivia a later reader has to re-derive.
            // (net48 has no float.IsFinite, hence the two-part check.)
            if (corpseCarryAllowance <= 1f
                || float.IsNaN(corpseCarryAllowance)
                || float.IsInfinity(corpseCarryAllowance))
                return realMassKg;
            return realMassKg / corpseCarryAllowance;
        }
    }
}
