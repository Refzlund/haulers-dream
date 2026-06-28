using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Oracle tests for the recurring "pawns drop scooped crops" bug (issues #62 / #87). These pin the EXACT
    /// vanilla <c>JobGiver_DropUnusedInventory</c> raw-food loop that HD's runtime guard depends on, across the
    /// whole <c>FoodPreferability</c> range and at the clock-gate boundary. If a future RimWorld (or a careless
    /// refactor of HD's own policy) changes the dropped category or the gate, one of these fails at build time —
    /// which is the whole point: the bug must never silently come back.
    /// </summary>
    [TestFixture]
    public class DropUnusedFoodPolicyTests
    {
        // FoodPreferability enum values as ints (vanilla 1.6). The loop drops everything <= RawTasty (5);
        // cooked meals (MealAwful = 6 and up) and the special NutrientPaste tiers are kept.
        private const int Undefined = 0;
        private const int NeverForNutrition = 1;
        private const int DesperateOnly = 2;
        private const int DesperateOnlyForHumanlikes = 3;
        private const int RawBad = 4;   // e.g. raw rice / corn / potatoes
        private const int RawTasty = 5; // e.g. raw berries / agave
        private const int MealAwful = 6;
        private const int MealSimple = 7;
        private const int MealFine = 8;
        private const int MealLavish = 9;

        [Test]
        public void RawFood_IsDropped()
        {
            // Every raw food (ingestible, not a drug, preferability 0..5) is a drop candidate — exactly the
            // crops / milk / eggs HD scoops. This is the category the bug dumps.
            foreach (int pref in new[] { Undefined, NeverForNutrition, DesperateOnly,
                DesperateOnlyForHumanlikes, RawBad, RawTasty })
            {
                Assert.That(DropUnusedFoodPolicy.IsRawFoodDropCandidate(isIngestible: true, isDrug: false, pref),
                    Is.True, $"preferability {pref} should be a raw-food drop candidate");
            }
        }

        [Test]
        public void CookedMeals_AreNotDropped()
        {
            // Meals (preferability >= 6) are above the threshold — vanilla keeps them, so HD must not treat
            // them as scooped raw-food cargo. Pins the boundary at exactly 5 (RawTasty), not 6.
            foreach (int pref in new[] { MealAwful, MealSimple, MealFine, MealLavish })
            {
                Assert.That(DropUnusedFoodPolicy.IsRawFoodDropCandidate(isIngestible: true, isDrug: false, pref),
                    Is.False, $"preferability {pref} (a meal) must not be a raw-food drop candidate");
            }
        }

        [Test]
        public void BoundaryAtFive()
        {
            // The exact comparison the vanilla loop uses is `<= 5`. Pin both sides of the edge.
            Assert.That(DropUnusedFoodPolicy.IsRawFoodDropCandidate(true, false, 5), Is.True);
            Assert.That(DropUnusedFoodPolicy.IsRawFoodDropCandidate(true, false, 6), Is.False);
            Assert.That(DropUnusedFoodPolicy.MaxDroppedPreferability, Is.EqualTo(5));
        }

        [Test]
        public void Drugs_AreNeverRawFoodCandidates()
        {
            // The food loop explicitly excludes drugs (the drug loop / #81 handle those). A drug-classified
            // ingestible, even a low-preferability one, must never be caught by the food predicate.
            Assert.That(DropUnusedFoodPolicy.IsRawFoodDropCandidate(isIngestible: true, isDrug: true, RawBad), Is.False);
            Assert.That(DropUnusedFoodPolicy.IsRawFoodDropCandidate(isIngestible: true, isDrug: true, Undefined), Is.False);
        }

        [Test]
        public void NonIngestible_IsNeverACandidate()
        {
            // Wool / leather / steel etc. are not ingestible, so neither vanilla loop ever drops them — and HD
            // must not re-arm the food clock on their account.
            Assert.That(DropUnusedFoodPolicy.IsRawFoodDropCandidate(isIngestible: false, isDrug: false, Undefined), Is.False);
            Assert.That(DropUnusedFoodPolicy.IsRawFoodDropCandidate(isIngestible: false, isDrug: false, RawTasty), Is.False);
        }

        // --- the clock gate: re-arming lastInventoryRawFoodUseTick to "now" must close the loop ---

        [Test]
        public void Rearmed_ClockClosesTheLoop()
        {
            // HD's suppression sets lastInventoryRawFoodUseTick = ticksGame. After that, the gate
            // (ticksGame > last + 150000) must be false at any tick, including negative / fresh-game ticks.
            foreach (int now in new[] { 0, 1, 150000, 1_000_000, -10 })
                Assert.That(DropUnusedFoodPolicy.FoodLoopWouldRun(ticksGame: now, lastInventoryRawFoodUseTick: now),
                    Is.False, $"re-armed at tick {now} must suppress the loop");
        }

        [Test]
        public void StaleClock_RunsTheLoop()
        {
            // The unsuppressed case the bug needs: the pawn hasn't used raw food in > 150000 ticks, so the loop
            // runs and (without HD) would drop the scooped yields.
            int now = 2_000_000;
            Assert.That(DropUnusedFoodPolicy.FoodLoopWouldRun(now, now - DropUnusedFoodPolicy.RawFoodDropDelay - 1), Is.True);
        }

        [Test]
        public void GateBoundary_IsStrictlyGreater()
        {
            // Vanilla uses `>` (not `>=`): exactly at last + delay the loop does NOT run; one tick past, it does.
            int last = 500_000;
            int delay = DropUnusedFoodPolicy.RawFoodDropDelay;
            Assert.That(DropUnusedFoodPolicy.FoodLoopWouldRun(last + delay, last), Is.False);
            Assert.That(DropUnusedFoodPolicy.FoodLoopWouldRun(last + delay + 1, last), Is.True);
        }

        [Test]
        public void DelayConstant_MatchesVanilla()
        {
            // A tripwire on the constant itself: vanilla's RawFoodDropDelay is 150000. If a future version changes
            // it, this fails so we re-confirm the suppression math rather than shipping a silently wrong gate.
            Assert.That(DropUnusedFoodPolicy.RawFoodDropDelay, Is.EqualTo(150000));
        }
    }
}
