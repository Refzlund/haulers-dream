using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The opt-in corpse-carry loosening (Steam: "colonists and mechs haul only one corpse at a time").
    ///
    /// <para>The load-bearing property is the FIRST fixture below: at the shipped defaults every function
    /// here is an exact no-op, so an existing colony's hauling is untouched until the player asks for
    /// something else. Everything after that pins one switch at a time, plus the arithmetic behind the
    /// user-visible claim that an allowance of 2.0 makes two bodies fit where one did.</para>
    /// </summary>
    [TestFixture]
    public class CorpseHaulPolicyTests
    {
        // A default colonist: MassUtility.Capacity = BodySize 1.0 × 35 kg, extended by the "Fair" overload
        // break-even (≈2.75) to ≈96.25 kg. Derived from OverloadTuning rather than hardcoded so the tests
        // keep measuring the REAL ceiling if the slider is ever re-tuned.
        private static float DefaultColonistCeilingKg
            => 35f * OverloadTuning.MaxOverloadRatio(OverloadTuning.FairLevel);

        // 60 × InnerPawn.BodySize (1.0) × notMissingPartsCoverage (1.0), naked. The number in the report.
        private const float HumanlikeCorpseKg = 60f;

        // ---- defaults are a no-op: the single most important property in this file ----

        [Test]
        public void Defaults_EveryHaulerKindStillSweeps()
        {
            // Shipped default is every switch on, so nobody who never opens the settings loses a hauler.
            foreach (bool isMech in new[] { true, false })
                foreach (bool isHumanlike in new[] { true, false })
                    Assert.That(CorpseHaulPolicy.HaulerMaySweepCorpses(isMech, isHumanlike,
                            corpseHaulByColonists: true, corpseHaulByMechs: true, corpseHaulByAnimals: true),
                        Is.True, $"isMech={isMech} isHumanlike={isHumanlike}");
        }

        [Test]
        public void Defaults_EveryCorpseStillQualifies()
        {
            // corpseHaulHumanlike on + the 0 "no limit" sentinel = the pre-feature world, where the only
            // thing that ever refused a body was the carry arithmetic itself.
            foreach (bool humanlikeCorpse in new[] { true, false })
                foreach (float massKg in new[] { 0f, 12f, HumanlikeCorpseKg, 100_000f })
                    Assert.That(CorpseHaulPolicy.CorpseMayBeSwept(humanlikeCorpse,
                            corpseHaulHumanlike: true, massKg, corpseMaxHaulMassKg: 0f),
                        Is.True, $"humanlike={humanlikeCorpse} mass={massKg}");
        }

        [Test]
        public void Defaults_BudgetIsBitwiseIdentity()
        {
            // Asserted with NO tolerance: at allowance 1.0 the planner must see the very same float it saw
            // before this feature existed. A near-miss here (a divide by 1.0000001, say) would re-plan every
            // haul in the game by a hair, which is precisely the silent regression the opt-in exists to avoid.
            foreach (float massKg in new[] { 0f, 12f, HumanlikeCorpseKg, 100_000f })
            {
                Assert.That(CorpseHaulPolicy.BudgetMassKg(massKg, isCorpse: true, corpseCarryAllowance: 1f),
                    Is.EqualTo(massKg), $"corpse mass={massKg}");
                Assert.That(CorpseHaulPolicy.BudgetMassKg(massKg, isCorpse: false, corpseCarryAllowance: 1f),
                    Is.EqualTo(massKg), $"item mass={massKg}");
            }
        }

        // ---- hauler kinds: one switch each, and the branch ORDER ----

        [Test]
        public void EachHaulerSwitch_GatesOnlyItsOwnRace()
        {
            // Turn exactly one switch off at a time; exactly one race may notice.
            Assert.That(CorpseHaulPolicy.HaulerMaySweepCorpses(false, true, false, true, true), Is.False,
                "colonist switch must stop colonists");
            Assert.That(CorpseHaulPolicy.HaulerMaySweepCorpses(true, false, false, true, true), Is.True,
                "the colonist switch must not touch mechs");
            Assert.That(CorpseHaulPolicy.HaulerMaySweepCorpses(false, false, false, true, true), Is.True,
                "the colonist switch must not touch animals");

            Assert.That(CorpseHaulPolicy.HaulerMaySweepCorpses(true, false, true, false, true), Is.False,
                "mech switch must stop mechs");
            Assert.That(CorpseHaulPolicy.HaulerMaySweepCorpses(false, true, true, false, true), Is.True,
                "the mech switch must not touch colonists");

            Assert.That(CorpseHaulPolicy.HaulerMaySweepCorpses(false, false, true, true, false), Is.False,
                "animal switch must stop animals");
            Assert.That(CorpseHaulPolicy.HaulerMaySweepCorpses(false, true, true, true, false), Is.True,
                "the animal switch must not touch colonists");
        }

        [Test]
        public void MechanoidBranchWins_EvenWhenTheRaceAlsoClaimsHumanlike()
        {
            // A modded race can declare both. The mech branch is first, so the mech switch governs it —
            // and the point is not which switch wins but that eligibility resolves the same tie the same
            // way, so the two predicates never file one pawn under two different settings.
            Assert.That(CorpseHaulPolicy.HaulerMaySweepCorpses(isMechanoid: true, isHumanlike: true,
                corpseHaulByColonists: true, corpseHaulByMechs: false, corpseHaulByAnimals: true), Is.False);
            Assert.That(CorpseHaulPolicy.HaulerMaySweepCorpses(isMechanoid: true, isHumanlike: true,
                corpseHaulByColonists: false, corpseHaulByMechs: true, corpseHaulByAnimals: false), Is.True);
        }

        /// <summary>
        /// The reason the branch order is copied rather than invented: over every race/switch combination
        /// this filter must route a pawn to the same arm <see cref="EligibilityPolicy.IsEligible"/> routes it
        /// to. Fed the same per-race toggles (and eligibility's colonist-only draft/incapable conditions held
        /// neutral), the two must agree everywhere. Branching on <c>RaceProps.Animal</c> instead would break
        /// this for anomaly entities and tool-users, which are neither humanlike, mech, nor animal.
        /// </summary>
        [Test]
        public void RaceRouting_AgreesWithEligibilityPolicy()
        {
            foreach (bool isMech in new[] { true, false })
                foreach (bool isHumanlike in new[] { true, false })
                    foreach (bool allowMechs in new[] { true, false })
                        foreach (bool allowAnimals in new[] { true, false })
                        {
                            bool eligible = EligibilityPolicy.IsEligible(isMech, isHumanlike,
                                isDrafted: false, incapableOfHauling: false,
                                allowMechanoids: allowMechs, pauseWhileDrafted: false, allowIncapable: true,
                                allowAnimals: allowAnimals);
                            bool mayHaulCorpses = CorpseHaulPolicy.HaulerMaySweepCorpses(isMech, isHumanlike,
                                corpseHaulByColonists: true, corpseHaulByMechs: allowMechs,
                                corpseHaulByAnimals: allowAnimals);
                            Assert.That(mayHaulCorpses, Is.EqualTo(eligible),
                                $"isMech={isMech} isHumanlike={isHumanlike} " +
                                $"allowMechs={allowMechs} allowAnimals={allowAnimals}");
                        }
        }

        // ---- which bodies qualify ----

        [Test]
        public void HumanlikeOptOut_BlocksOnlyHumanlikeCorpses()
        {
            // The whole point of splitting the switch by race: a player can refuse to have colonists and
            // raiders swept up while still letting the sweep clear a field of dead squirrels.
            Assert.That(CorpseHaulPolicy.CorpseMayBeSwept(isHumanlikeCorpse: true,
                corpseHaulHumanlike: false, HumanlikeCorpseKg, 0f), Is.False);
            Assert.That(CorpseHaulPolicy.CorpseMayBeSwept(isHumanlikeCorpse: false,
                corpseHaulHumanlike: false, 12f, 0f), Is.True);
        }

        [Test]
        public void MassLimit_IsInclusive()
        {
            // A player who types 60 for a 60 kg body means "these, and nothing heavier" — an exclusive
            // comparison would reject the exact thing they dialled the slider to.
            Assert.That(CorpseHaulPolicy.CorpseMayBeSwept(true, true, HumanlikeCorpseKg,
                corpseMaxHaulMassKg: HumanlikeCorpseKg), Is.True, "a limit set to the body's own weight admits it");
            Assert.That(CorpseHaulPolicy.CorpseMayBeSwept(true, true, HumanlikeCorpseKg,
                corpseMaxHaulMassKg: 59.9f), Is.False);
            Assert.That(CorpseHaulPolicy.CorpseMayBeSwept(false, true, 12f,
                corpseMaxHaulMassKg: 20f), Is.True, "a hare well under the limit still rides along");
        }

        [Test]
        public void MassLimit_ZeroAndNegative_BothMeanNoLimit()
        {
            // 0 is the shipped default and matches the carryMassCapKg convention elsewhere in the settings.
            // Negative is unreachable from the slider but must not read as "nothing may ever be swept".
            foreach (float noLimit in new[] { 0f, -1f, -100f })
                Assert.That(CorpseHaulPolicy.CorpseMayBeSwept(true, true, 100_000f, noLimit),
                    Is.True, $"limit={noLimit}");
        }

        [Test]
        public void MassLimit_MeasuresTheRealBody_NotTheDiscountedBudget()
        {
            // The two settings must not fight: a limit expressed in kilograms means kilograms, so raising
            // the allowance can never smuggle a body past a limit the player set to exclude it. Pinned
            // structurally — CorpseMayBeSwept has no allowance parameter, and this is why.
            float budgeted = CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, isCorpse: true, 2f);
            Assert.That(budgeted, Is.EqualTo(30f).Within(0.0001f));
            Assert.That(CorpseHaulPolicy.CorpseMayBeSwept(true, true, HumanlikeCorpseKg,
                corpseMaxHaulMassKg: 40f), Is.False,
                "the 60 kg body is over a 40 kg limit even though it would budget at 30");
        }

        // ---- the budget discount ----

        [Test]
        public void Allowance_AtOrBelowOne_IsIdentity()
        {
            // The allowance may only ever loosen. Tightening has its own knob (corpseMaxHaulMassKg), and a
            // sub-1.0 value silently INFLATING a body's cost would be the surprising direction.
            foreach (float allowance in new[] { 1f, 0.5f, 0f, -2f })
                Assert.That(CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, allowance),
                    Is.EqualTo(HumanlikeCorpseKg), $"allowance={allowance}");
        }

        [Test]
        public void Allowance_Divides_TheCorpsesBudgetedCost()
        {
            Assert.That(CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, 2f),
                Is.EqualTo(30f).Within(0.0001f));
            Assert.That(CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, 4f),
                Is.EqualTo(15f).Within(0.0001f));
            Assert.That(CorpseHaulPolicy.BudgetMassKg(12f, true, 3f),
                Is.EqualTo(4f).Within(0.0001f));
        }

        /// <summary>
        /// THE user-visible claim, written out with the real numbers so a future reader can see exactly what
        /// was promised: a default colonist has a ≈96.25 kg bulk ceiling (35 kg capacity × the "Fair"
        /// break-even) and a humanlike corpse is 60 kg, so today the second body wants 120 kg and is refused.
        /// At an allowance of 2.0 the pair budgets at 30 + 30 = 60 kg and both fit — while the pawn still
        /// physically carries 120 kg and is slowed for all of it.
        /// </summary>
        [Test]
        public void AtAllowanceTwo_TwoHumanlikeBodiesFitWhereOneDidBefore()
        {
            float ceiling = DefaultColonistCeilingKg;
            Assert.That(ceiling, Is.GreaterThan(HumanlikeCorpseKg).And.LessThan(2f * HumanlikeCorpseKg),
                "the premise: one body fits under the default ceiling and two do not");

            float atDefault = 2f * CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, 1f);
            Assert.That(atDefault, Is.GreaterThan(ceiling), "today: the second body is refused");

            float atTwo = 2f * CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, 2f);
            Assert.That(atTwo, Is.EqualTo(HumanlikeCorpseKg).Within(0.0001f));
            Assert.That(atTwo, Is.LessThanOrEqualTo(ceiling), "opted in: both bodies fit");
        }

        // ---- the budget CURRENCY: plan and execution must keep score the same way ----

        /// <summary>
        /// The arithmetic <c>BulkHaul.BudgetedCarriedMassKg</c> performs, reproduced here in pure form: start
        /// from what <c>MassUtility.GearAndInventoryMass</c> would report (every real kilogram, corpses
        /// included) and subtract only each carried body's DISCOUNT. Written in that direction on purpose —
        /// the production helper cannot simply sum budget masses, because it has to leave every non-corpse
        /// item in the inventory charged at its real weight.
        /// </summary>
        /// <param name="gearKg">Everything the pawn carries that is not a corpse: gear, apparel, loot.</param>
        /// <param name="corpsesCarried">Bodies already in the pawn's inventory.</param>
        /// <param name="corpseRealKg">What one of those bodies actually weighs.</param>
        /// <param name="allowance">The live corpse carry allowance.</param>
        private static float BudgetedCarried(float gearKg, int corpsesCarried, float corpseRealKg, float allowance)
        {
            float realTotal = gearKg + corpsesCarried * corpseRealKg;
            float discountPerBody = corpseRealKg - CorpseHaulPolicy.BudgetMassKg(corpseRealKg, true, allowance);
            return realTotal - corpsesCarried * discountPerBody;
        }

        [Test]
        public void Defaults_CarriedMassIsUndiscounted_HoweverManyBodies()
        {
            // At the shipped allowance the running total a plan keeps is the pawn's real mass, unchanged and
            // exact — the same guarantee Defaults_BudgetIsBitwiseIdentity makes for a single item, extended to
            // the total the ceiling is actually compared against.
            for (int carried = 0; carried <= 3; carried++)
                Assert.That(BudgetedCarried(3f, carried, HumanlikeCorpseKg, 1f),
                    Is.EqualTo(3f + carried * HumanlikeCorpseKg), $"carrying {carried}");
        }

        [Test]
        public void TheSeedAndTheIncrements_AgreeInBudgetCurrency()
        {
            // The property that makes ONE currency actually one: re-pricing what is already carried
            // (real total minus each body's discount) has to land on the same number as adding up what the
            // planner charged for those bodies as it took them (a budget unit each). If these two ever
            // disagreed, a plan and the driver replaying it would drift apart body by body.
            foreach (float allowance in new[] { 1f, 1.5f, 2f, 3f })
                for (int carried = 0; carried <= 3; carried++)
                {
                    float bySubtraction = BudgetedCarried(3f, carried, HumanlikeCorpseKg, allowance);
                    float byAccumulation = 3f + carried * CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, allowance);
                    Assert.That(bySubtraction, Is.EqualTo(byAccumulation).Within(0.001f),
                        $"allowance={allowance} carrying={carried}");
                }
        }

        /// <summary>
        /// THE regression this fixture exists for, in the numbers it actually failed with. The planner admitted
        /// two bodies at 30 kg apiece; the driver then re-clamped the second one against the 60 REAL kilograms
        /// the first had added, got zero, and dropped it — so an opted-in player watched a colonist walk the
        /// whole planned route and come home with one body, exactly as before. Both currencies are asserted
        /// side by side so the failing one stays legible rather than being remembered as "an off-by-one".
        /// </summary>
        [Test]
        public void AfterTheFirstBodyIsCarried_TheSecondStillFits_OnlyInBudgetCurrency()
        {
            float ceiling = DefaultColonistCeilingKg;
            const float gearKg = 3f;
            float unit = CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, 2f);
            Assert.That(unit, Is.EqualTo(30f).Within(0.0001f));

            // What the driver measures now: one body carried, re-priced to 30, so 33 of the ceiling is spent.
            float budgetedRunning = BudgetedCarried(gearKg, 1, HumanlikeCorpseKg, 2f);
            Assert.That(budgetedRunning, Is.EqualTo(33f).Within(0.0001f));
            Assert.That(BulkHaulPolicy.CountWithinCeiling(ceiling, budgetedRunning, unit, 1), Is.EqualTo(1),
                "the second body must survive the driver's live re-clamp");

            // What it measured before: the body's full 60 kg charged against 63 real kilograms already carried.
            float realRunning = gearKg + HumanlikeCorpseKg;
            Assert.That(BulkHaulPolicy.CountWithinCeiling(ceiling, realRunning, HumanlikeCorpseKg, 1), Is.EqualTo(0),
                "the mixed-currency clamp is what silently cancelled the feature");
        }

        [Test]
        public void WalkingTheChain_CarriesMoreThanOneBody_OnlyWhenOptedIn()
        {
            // The user-visible outcome, end to end. Deliberately NOT pinned to an exact count: at allowance 2.0
            // a 96.25 kg ceiling admits three 30 kg bodies, not two, and writing "2" here would pin an incidental
            // consequence of the current overload tuning rather than the property that was broken.
            Assert.That(BodiesTakenWalkingTheChain(2f), Is.GreaterThan(1),
                "opted in, the chain must bring home more than the single body it always did");
            Assert.That(BodiesTakenWalkingTheChain(1f), Is.EqualTo(1),
                "the default is untouched: one body per trip, as it has always been");
        }

        [Test]
        public void ThePlannerAndTheDriver_TakeTheSameNumberOfBodies()
        {
            // The invariant the whole fix rests on, and the one whose absence WAS the bug. The planner's way of
            // keeping score (charge each accepted body its budget unit) and the driver's (re-measure the budgeted
            // load that body actually added, then re-clamp) are two different routes to the same total, and they
            // have to reach the same answer. When the driver measured real kilograms instead, it stopped one body
            // in and the plan's remaining entries were skipped one by one.
            foreach (float allowance in new[] { 1f, 1.5f, 2f, 3f })
                Assert.That(BodiesTakenWalkingTheChain(allowance), Is.EqualTo(BodiesPlanned(allowance)),
                    $"plan and execution disagree at allowance={allowance}");
        }

        /// <summary>
        /// Replays the bulk driver's pickup loop in pure arithmetic: before each pickup, re-measure the budgeted
        /// carried mass (as <c>BudgetedCarriedMassKg</c> does over a pawn holding that many bodies) and ask
        /// <see cref="BulkHaulPolicy.CountWithinCeiling"/> whether one more fits, stopping at the first refusal.
        /// </summary>
        /// <param name="allowance">The corpse carry allowance under test.</param>
        /// <returns>How many bodies the chain actually takes.</returns>
        private static int BodiesTakenWalkingTheChain(float allowance)
        {
            float unit = CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, allowance);
            int taken = 0;
            while (taken < MaxBodiesConsidered)
            {
                float running = BudgetedCarried(3f, taken, HumanlikeCorpseKg, allowance);
                if (BulkHaulPolicy.CountWithinCeiling(DefaultColonistCeilingKg, running, unit, 1) <= 0)
                    break;
                taken++;
            }
            return taken;
        }

        /// <summary>
        /// Replays the planner's snowball instead: keep a running budget total and charge each accepted body the
        /// same unit the fit test priced it at, which is what <c>BuildBulkJob</c> does with the mass reported
        /// back through <c>unitMassKg</c>.
        /// </summary>
        /// <param name="allowance">The corpse carry allowance under test.</param>
        /// <returns>How many bodies the plan admits.</returns>
        private static int BodiesPlanned(float allowance)
        {
            float unit = CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, allowance);
            float running = 3f;
            int planned = 0;
            while (planned < MaxBodiesConsidered)
            {
                int fits = BulkHaulPolicy.CountWithinCeiling(DefaultColonistCeilingKg, running, unit, 1);
                if (fits <= 0)
                    break;
                planned += fits;
                running += fits * unit;
            }
            return planned;
        }

        // Enough bodies for any allowance the slider offers (it stops at 3.0) to run out of ceiling first, so
        // both simulations above always terminate on the arithmetic rather than on this bound.
        private const int MaxBodiesConsidered = 12;

        [Test]
        public void ALifterMech_AlreadyFitsTwoBodies_AtTheDefaultAllowance()
        {
            // Why the grave-overshoot clamp must not be gated on the allowance: the hauler most likely to be
            // carrying bodies overshoots a one-body grave at STOCK settings, so a clamp that only switches on
            // with the allowance is switched off in the case it exists to prevent.
            float lifterCeiling = 52.5f * OverloadTuning.MaxOverloadRatio(OverloadTuning.FairLevel);
            Assert.That(lifterCeiling, Is.GreaterThan(2f * HumanlikeCorpseKg),
                "a Lifter's ceiling takes two 60 kg bodies with no setting changed");
            Assert.That(BulkHaulPolicy.CountWithinCeiling(lifterCeiling, HumanlikeCorpseKg,
                    CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, 1f), 1), Is.EqualTo(1),
                "and the second one clears the undiscounted clamp too");
        }

        [Test]
        public void NonCorpses_AreNeverDiscounted()
        {
            // A stack of steel needs no discount — it simply splits at the ceiling and the rest is fetched
            // next trip. Only an indivisible one-per-victim load has the problem this setting solves.
            foreach (float allowance in new[] { 1f, 2f, 10f, 0.5f, float.NaN, float.PositiveInfinity })
                foreach (float massKg in new[] { 0f, 12f, HumanlikeCorpseKg, 100_000f })
                    Assert.That(CorpseHaulPolicy.BudgetMassKg(massKg, isCorpse: false, allowance),
                        Is.EqualTo(massKg), $"allowance={allowance} mass={massKg}");
        }

        [Test]
        public void NonFiniteAllowance_ReturnsTheInputUnchanged()
        {
            // +∞ is the dangerous one: it passes a naive `> 1f` test and would divide a body down to a
            // weightless 0 kg, letting one hauler pocket an unbounded pile of them.
            Assert.That(CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, float.PositiveInfinity),
                Is.EqualTo(HumanlikeCorpseKg));
            Assert.That(CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, float.NegativeInfinity),
                Is.EqualTo(HumanlikeCorpseKg));
            Assert.That(CorpseHaulPolicy.BudgetMassKg(HumanlikeCorpseKg, true, float.NaN),
                Is.EqualTo(HumanlikeCorpseKg));
        }

        [Test]
        public void ADiscountedBudget_IsNeverMoreThanTheRealMass()
        {
            // The planner may under-charge a corpse, never over-charge one, and never produce a negative or
            // non-finite budget that downstream carry arithmetic would have to defend against.
            foreach (float allowance in new[] { -1f, 0f, 1f, 1.5f, 2f, 8f, float.NaN, float.PositiveInfinity })
                foreach (float massKg in new[] { 0f, 12f, HumanlikeCorpseKg, 100_000f })
                {
                    float budget = CorpseHaulPolicy.BudgetMassKg(massKg, isCorpse: true, allowance);
                    Assert.That(budget, Is.InRange(0f, massKg), $"allowance={allowance} mass={massKg}");
                }
        }
    }
}
