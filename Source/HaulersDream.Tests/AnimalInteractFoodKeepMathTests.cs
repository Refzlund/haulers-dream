using System;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Oracle tests for the animal-interaction food keep — the keep that stops a hauler shedding the kibble it is
    /// carrying to tame or train an animal (reported twice on Steam).
    ///
    /// <para>The load-bearing fact these pin is a DISJOINTNESS: vanilla will hand an animal only food with
    /// <c>(int)preferability &lt;= 5</c>, while HD's pre-existing packable-food keep only ever covered
    /// <c>&gt;= 7</c>. No value satisfies both, so that keep was structurally incapable of protecting interaction
    /// food — which is why a separate keep had to exist at all. If a future RimWorld moves either boundary so the
    /// ranges meet, one of these fails at build time and the duplication can be revisited deliberately instead of
    /// silently double-keeping.</para>
    /// </summary>
    [TestFixture]
    public class AnimalInteractFoodKeepMathTests
    {
        // The full vanilla 1.6 FoodPreferability range as ints, decompile-verified against Assembly-CSharp:
        // Undefined 0, NeverForNutrition 1, DesperateOnly 2, DesperateOnlyForHumanlikes 3, RawBad 4 (kibble),
        // RawTasty 5, MealTerrible 6, MealAwful 7, MealSimple 8, MealFine 9, MealLavish 10.
        private const int MinPreferability = 0;
        private const int MaxPreferability = 10;
        private const int Kibble = 4; // RawBad — the def in both player reports

        // ---- the def predicate ---------------------------------------------------------------------------

        [Test]
        public void InteractFood_AcceptsExactlyPreferabilityFiveAndBelow()
        {
            // The boundary IS the fix: vanilla's WorkGiver_InteractAnimal.HasFoodToInteractAnimal skips a stack
            // when (int)preferability > 5, so 5 is inclusive and 6 is out.
            for (int pref = MinPreferability; pref <= MaxPreferability; pref++)
            {
                bool expected = pref <= AnimalInteractFoodKeepMath.MaxInteractPreferability;
                Assert.That(AnimalInteractFoodKeepMath.IsInteractFood(isIngestible: true, isDrug: false, pref),
                    Is.EqualTo(expected), $"preferability {pref}");
            }
        }

        [Test]
        public void InteractFood_KibbleQualifies()
        {
            // The def from both reports. If this ever goes false the whole fix is inert.
            Assert.That(AnimalInteractFoodKeepMath.IsInteractFood(isIngestible: true, isDrug: false, Kibble),
                Is.True);
        }

        [Test]
        public void InteractFood_RejectsDrugsAndNonIngestiblesAcrossTheWholeRange()
        {
            // Vanilla excludes drugs outright (thing.def.IsDrug), and a non-ingestible has no ingestible block to
            // read a preferability from at all. Neither can ever be interaction food, at any preferability.
            for (int pref = MinPreferability; pref <= MaxPreferability; pref++)
            {
                Assert.That(AnimalInteractFoodKeepMath.IsInteractFood(isIngestible: true, isDrug: true, pref),
                    Is.False, $"drug at preferability {pref}");
                Assert.That(AnimalInteractFoodKeepMath.IsInteractFood(isIngestible: false, isDrug: false, pref),
                    Is.False, $"non-ingestible at preferability {pref}");
            }
        }

        [Test]
        public void InteractRange_IsDisjointFromThePackableFoodRange()
        {
            // THE reason this keep exists. HD's packable-food keep (FoodKeepMath, gated on
            // JobGiver_PackFood.IsGoodPackableFoodFor) requires preferability >= 7; interaction food requires <= 5.
            // Nothing is in both, so the packed-lunch keep could never have protected a training stack.
            Assert.That(AnimalInteractFoodKeepMath.MaxInteractPreferability,
                Is.LessThan(AnimalInteractFoodKeepMath.MinPackablePreferability));

            for (int pref = MinPreferability; pref <= MaxPreferability; pref++)
            {
                bool interact = AnimalInteractFoodKeepMath.IsInteractFood(true, false, pref);
                bool packable = pref >= AnimalInteractFoodKeepMath.MinPackablePreferability;
                Assert.That(interact && packable, Is.False,
                    $"preferability {pref} must not be BOTH interaction food and packable food");
            }
        }

        [Test]
        public void PreferabilitySix_IsInNeitherRange()
        {
            // MealTerrible sits in the gap between the two ranges — pinned so the disjointness above is a real gap
            // rather than two ranges that merely happen to touch.
            Assert.That(AnimalInteractFoodKeepMath.IsInteractFood(true, false, 6), Is.False);
            Assert.That(6 >= AnimalInteractFoodKeepMath.MinPackablePreferability, Is.False);
        }

        // ---- the reserve size ----------------------------------------------------------------------------

        [Test]
        public void RequiredNutritionPerFeed_IsFifteenPercentOfMaxLevel_CappedAtThreeTenths()
        {
            // Vanilla JobDriver_InteractAnimal.RequiredNutritionPerFeed = Min(MaxLevel * 0.15f, 0.3f).
            Assert.That(AnimalInteractFoodKeepMath.RequiredNutritionPerFeed(1f),
                Is.EqualTo(0.15f).Within(1e-5f));
            // A small animal is well under the cap.
            Assert.That(AnimalInteractFoodKeepMath.RequiredNutritionPerFeed(0.4f),
                Is.EqualTo(0.06f).Within(1e-5f));
            // The cap binds from MaxLevel 2 upward, and never rises again however large the animal.
            Assert.That(AnimalInteractFoodKeepMath.RequiredNutritionPerFeed(2f),
                Is.EqualTo(AnimalInteractFoodKeepMath.MaxNutritionPerFeed).Within(1e-5f));
            Assert.That(AnimalInteractFoodKeepMath.RequiredNutritionPerFeed(100f),
                Is.EqualTo(AnimalInteractFoodKeepMath.MaxNutritionPerFeed).Within(1e-5f));
        }

        [Test]
        public void RequiredNutritionPerFeed_NoFoodNeed_IsZero()
        {
            // Vanilla returns 0 when animal.needs.food == null; the runtime expresses that as a 0 max level. An
            // animal that is never fed reserves nothing — this is what keeps the keep from firing on a dryad or a
            // mech-like animal.
            Assert.That(AnimalInteractFoodKeepMath.RequiredNutritionPerFeed(0f), Is.EqualTo(0f));
            Assert.That(AnimalInteractFoodKeepMath.RequiredNutritionPerFeed(-1f), Is.EqualTo(0f));
        }

        [Test]
        public void ReserveNutrition_IsEightFeeds_AndNeverExceedsTheCeiling()
        {
            // WorkGiver_InteractAnimal asks for RequiredNutritionPerFeed * 2f * 4f.
            Assert.That(AnimalInteractFoodKeepMath.FeedsFetchedPerTrip, Is.EqualTo(8f));
            Assert.That(AnimalInteractFoodKeepMath.ReserveNutrition(1f),
                Is.EqualTo(1.2f).Within(1e-5f));
            Assert.That(AnimalInteractFoodKeepMath.MaxReserveNutrition,
                Is.EqualTo(2.4f).Within(1e-5f));

            // The ceiling is what bounds the keep. Sweep a wide range of animal sizes: none may exceed it.
            for (float maxLevel = 0f; maxLevel <= 50f; maxLevel += 0.25f)
                Assert.That(AnimalInteractFoodKeepMath.ReserveNutrition(maxLevel),
                    Is.LessThanOrEqualTo(AnimalInteractFoodKeepMath.MaxReserveNutrition + 1e-5f),
                    $"maxLevel {maxLevel}");
        }

        // ---- the keep-count clamp -----------------------------------------------------------------------

        [Test]
        public void KeepCount_ExactFit_KeepsExactlyTheUnitsNeeded()
        {
            // Binary-clean numbers so the expectation is exact: 1.0 nutrition of 0.25-per-unit food = 4 units.
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(1f, 0.25f, 100), Is.EqualTo(4));
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(2f, 0.5f, 100), Is.EqualTo(4));
        }

        [Test]
        public void KeepCount_PartialUnit_RoundsUp()
        {
            // Ceiling, not round: vanilla's two fetch formulas are CeilToInt (taming) and Max(RoundToInt, 1)
            // (training), and ceiling is >= both — the keep must never fall short of what vanilla fetched.
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(1.125f, 0.5f, 100), Is.EqualTo(3));
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(0.125f, 0.5f, 100), Is.EqualTo(1));
        }

        [Test]
        public void KeepCount_ReserveExceedsStack_KeepsTheWholeStackOnly()
        {
            // The clamp to stackCount: a reserve larger than what the pawn holds keeps everything it holds, never
            // a number above it (a keep above the stack would read as negative surplus at the call site).
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(2.4f, 0.05f, 3), Is.EqualTo(3));
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(100f, 0.5f, 7), Is.EqualTo(7));
        }

        [Test]
        public void KeepCount_ReleasesWhenNoInteractionJobRemains()
        {
            // THE self-release property. The runtime passes reserve == 0 the moment no current or queued
            // interaction job is left, and the keep must then be 0 so the ordinary unload ships the food.
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(0f, 0.05f, 200), Is.EqualTo(0));
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(-1f, 0.05f, 200), Is.EqualTo(0));
            // Same thing end to end: a no-food animal yields a zero reserve, which yields a zero keep.
            float reserve = AnimalInteractFoodKeepMath.ReserveNutrition(0f);
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(reserve, 0.05f, 200), Is.EqualTo(0));
        }

        [Test]
        public void KeepCount_EmptyStack_KeepsNothing()
        {
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(2.4f, 0.05f, 0), Is.EqualTo(0));
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(2.4f, 0.05f, -5), Is.EqualTo(0));
        }

        [Test]
        public void KeepCount_NonPositivePerUnitNutrition_KeepsNothing()
        {
            // Same discipline as FoodKeepMath.KeepCount: rather than divide by zero and pin the whole stack
            // (an unbounded keep is a black hole), a zero-nutrition "food" keeps nothing. Vanilla would not have
            // fetched it either — StackCountForNutrition is only ever fed a real nutrition value.
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(2.4f, 0f, 200), Is.EqualTo(0));
            Assert.That(AnimalInteractFoodKeepMath.KeepCount(2.4f, -0.5f, 200), Is.EqualTo(0));
        }

        [Test]
        public void KeepCount_BoundedByTheCeiling_NeverPinsALargeHaulStack()
        {
            // The reason the nutrition ceiling is load-bearing: without it, a pawn that swept a 200-unit kibble
            // stack for hauling would pin all 200 the moment it also had a training job. At kibble's 0.05
            // nutrition the largest possible keep is ~48 units, so the rest still unloads.
            int keep = AnimalInteractFoodKeepMath.KeepCount(
                AnimalInteractFoodKeepMath.MaxReserveNutrition, 0.05f, 200);
            Assert.That(keep, Is.GreaterThan(0));
            Assert.That(keep, Is.LessThanOrEqualTo(49)); // 2.4 / 0.05 = 48, +1 for float ceiling slack
            Assert.That(200 - keep, Is.GreaterThan(150), "the bulk of a swept haul stack must stay surplus");
        }

        // ---- oracle --------------------------------------------------------------------------------------

        [Test]
        public void KeepCount_Oracle_IsTheMinimalCoveringCountClampedToTheStack()
        {
            // Sweep the whole plausible input space and assert the DEFINING properties rather than hand-computed
            // numbers, so no assertion depends on a brittle float division:
            //   1. the result is always in [0, stackCount];
            //   2. it covers the reserve — unless the stack is too small, in which case it takes the whole stack;
            //   3. it is MINIMAL — one unit fewer would not have covered the reserve.
            var reserves = new[] { 0.06f, 0.12f, 0.25f, 0.5f, 1f, 1.2f, 2f, AnimalInteractFoodKeepMath.MaxReserveNutrition };
            var perUnits = new[] { 0.05f, 0.1f, 0.25f, 0.3f, 0.5f, 0.9f, 1f, 2f };
            var stacks = new[] { 1, 2, 5, 10, 48, 75, 200, 500 };

            foreach (float reserve in reserves)
                foreach (float perUnit in perUnits)
                    foreach (int stack in stacks)
                    {
                        int keep = AnimalInteractFoodKeepMath.KeepCount(reserve, perUnit, stack);
                        string ctx = $"reserve={reserve} perUnit={perUnit} stack={stack} -> keep={keep}";

                        Assert.That(keep, Is.InRange(0, stack), ctx);

                        // Covers the reserve, or the pawn simply does not hold enough to cover it.
                        bool covers = keep * (double)perUnit >= reserve - 1e-4;
                        Assert.That(covers || keep == stack, Is.True, "must cover the reserve or take all: " + ctx);

                        // Minimal: one fewer unit would leave the reserve uncovered (float slack allows the
                        // ceiling to land one unit high, so allow exactly that much).
                        if (keep > 0)
                            Assert.That((keep - 2) * (double)perUnit, Is.LessThan(reserve),
                                "must not over-keep by more than a unit of ceiling slack: " + ctx);
                    }
        }

        [Test]
        public void KeepCount_Oracle_MatchesCeilingOfTheQuotientForCleanBinaryInputs()
        {
            // A direct oracle against the formula, restricted to per-unit values that are exact in binary so the
            // reference computation cannot itself drift: keep == clamp(ceil(reserve / perUnit), 0, stackCount).
            var perUnits = new[] { 0.5f, 0.25f, 0.125f, 1f, 2f };
            foreach (float perUnit in perUnits)
                for (int r = 1; r <= 40; r++)
                    foreach (int stack in new[] { 1, 3, 16, 64, 250 })
                    {
                        float reserve = r * 0.125f;
                        int expected = (int)Math.Ceiling(reserve / perUnit);
                        if (expected > stack)
                            expected = stack;
                        Assert.That(AnimalInteractFoodKeepMath.KeepCount(reserve, perUnit, stack),
                            Is.EqualTo(expected), $"reserve={reserve} perUnit={perUnit} stack={stack}");
                    }
        }
    }
}
