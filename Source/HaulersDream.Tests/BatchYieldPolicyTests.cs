using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class BatchYieldPolicyTests
    {
        // Category ints mirror the vanilla enum ordinals the driver passes:
        //   HungerCategory: Fed=0, Hungry=1, UrgentlyHungry=2, Starving=3
        //   RestCategory:   Rested=0, Tired=1, VeryTired=2, Exhausted=3
        // "Urgent" thresholds are 2 (UrgentlyHungry / VeryTired), per BatchYieldPolicy.{Food,Rest}UrgentLevel.

        private const int Fed = 0, Hungry = 1, UrgentlyHungry = 2, Starving = 3;
        private const int Rested = 0, Tired = 1, VeryTired = 2, Exhausted = 3;

        // ── nothing wrong: keep crafting ────────────────────────────────────────────────
        [Test]
        public void NoUrgency_DoesNotYield()
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, Fed, Rested, false, false, false), Is.False);
        }

        // ── minor needs do NOT yield (the whole point — don't shred a batch) ─────────────
        [Test]
        public void MerelyHungry_DoesNotYield()
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, Hungry, Rested, false, false, false), Is.False);
        }

        [Test]
        public void MerelyTired_DoesNotYield()
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, Fed, Tired, false, false, false), Is.False);
        }

        // ── urgent survival needs DO yield ──────────────────────────────────────────────
        [TestCase(UrgentlyHungry, Rested)]
        [TestCase(Starving, Rested)]
        public void UrgentHunger_Yields(int food, int rest)
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, food, rest, false, false, false), Is.True);
        }

        [TestCase(Fed, VeryTired)]
        [TestCase(Fed, Exhausted)]
        public void UrgentRest_Yields(int food, int rest)
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, food, rest, false, false, false), Is.True);
        }

        // ── mental break imminent / drafted / danger DO yield ───────────────────────────
        [Test]
        public void MentalBreakImminent_Yields()
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, Fed, Rested, mentalBreakImminent: true, drafted: false, inDanger: false), Is.True);
        }

        [Test]
        public void Drafted_Yields()
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, Fed, Rested, false, drafted: true, inDanger: false), Is.True);
        }

        [Test]
        public void InDanger_Yields()
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, Fed, Rested, false, false, inDanger: true), Is.True);
        }

        // ── playerForced NEVER yields, no matter how dire ───────────────────────────────
        [Test]
        public void PlayerForced_NeverYields_EvenWhenStarvingExhaustedBreakingDraftedDowned()
        {
            Assert.That(BatchYieldPolicy.ShouldYield(playerForced: true, Starving, Exhausted,
                mentalBreakImminent: true, drafted: true, inDanger: true), Is.False);
        }

        // ── a missing need (negative category) never triggers ───────────────────────────
        [Test]
        public void MissingNeed_NegativeCategory_DoesNotYield()
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, -1, -1, false, false, false), Is.False);
        }

        // ── threshold boundary: exactly at the urgent level yields; one below does not ───
        [Test]
        public void FoodThresholdBoundary()
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, BatchYieldPolicy.FoodUrgentLevel - 1, Rested, false, false, false), Is.False);
            Assert.That(BatchYieldPolicy.ShouldYield(false, BatchYieldPolicy.FoodUrgentLevel, Rested, false, false, false), Is.True);
        }

        [Test]
        public void RestThresholdBoundary()
        {
            Assert.That(BatchYieldPolicy.ShouldYield(false, Fed, BatchYieldPolicy.RestUrgentLevel - 1, false, false, false), Is.False);
            Assert.That(BatchYieldPolicy.ShouldYield(false, Fed, BatchYieldPolicy.RestUrgentLevel, false, false, false), Is.True);
        }
    }
}
