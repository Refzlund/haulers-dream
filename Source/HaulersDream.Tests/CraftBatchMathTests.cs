using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CraftBatchMathTests
    {
        // Reference pawn: 35 kg capacity, carrying 5 kg of gear.
        const float Cap = 35f;
        const float Base = 35f;
        const float Gear = 5f;

        [Test]
        public void RepsByAvailability_FloorsDivision()
        {
            Assert.That(CraftBatchMath.RepsByAvailability(perRepUnits: 10, availableUnits: 35), Is.EqualTo(3)); // 3×10=30 ≤ 35
            Assert.That(CraftBatchMath.RepsByAvailability(10, 30), Is.EqualTo(3));
            Assert.That(CraftBatchMath.RepsByAvailability(10, 9), Is.EqualTo(0));  // not even one rep
        }

        [Test]
        public void RepsByAvailability_NoIngredientNeed_IsUnbounded()
        {
            Assert.That(CraftBatchMath.RepsByAvailability(perRepUnits: 0, availableUnits: 5), Is.EqualTo(int.MaxValue));
            Assert.That(CraftBatchMath.RepsByAvailability(-3, 5), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void RepsByMass_MasslessIngredients_NeverLimit()
        {
            Assert.That(CraftBatchMath.RepsByMass(OverloadTuning.OffLevel, Cap, Base, Gear, massPerRepKg: 0f, wantReps: 7),
                Is.EqualTo(7));
        }

        [Test]
        public void RepsByMass_OffLevel_FitsUnderBaseCap()
        {
            // room = 35 - 5 = 30 kg; 10 kg per rep → 3 reps fit (no overload at OffLevel).
            Assert.That(CraftBatchMath.RepsByMass(OverloadTuning.OffLevel, Cap, Base, Gear, massPerRepKg: 10f, wantReps: 10),
                Is.EqualTo(3));
        }

        [Test]
        public void RepsByMass_NeverExceedsWanted()
        {
            // plenty of room (light ingredients) but only 2 reps wanted → 2.
            Assert.That(CraftBatchMath.RepsByMass(OverloadTuning.OffLevel, Cap, Base, Gear, massPerRepKg: 0.5f, wantReps: 2),
                Is.EqualTo(2));
        }

        [Test]
        public void RepsByMass_Overload_CarriesMoreThanBaseCap()
        {
            // At a generous overload level, room extends past 100% capacity, so more reps fit than the base cap allows.
            int off = CraftBatchMath.RepsByMass(OverloadTuning.OffLevel, Cap, Base, Gear, massPerRepKg: 5f, wantReps: 100);
            int on = CraftBatchMath.RepsByMass(OverloadTuning.MaxLevel, Cap, Base, Gear, massPerRepKg: 5f, wantReps: 100);
            Assert.That(off, Is.EqualTo(6));                 // (35-5)/5 = 6
            Assert.That(on, Is.GreaterThanOrEqualTo(off));   // overload never carries fewer
        }

        [Test]
        public void RepsByTimeout_NoTimeout_IsUnbounded()
        {
            Assert.That(CraftBatchMath.RepsByTimeout(ticksPerRep: 500, timeoutTicks: 0), Is.EqualTo(int.MaxValue));
            Assert.That(CraftBatchMath.RepsByTimeout(500, -1), Is.EqualTo(int.MaxValue));
            Assert.That(CraftBatchMath.RepsByTimeout(ticksPerRep: 0, timeoutTicks: 60000), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void RepsByTimeout_FloorsButKeepsAtLeastOne()
        {
            // 2-hour timeout (1 hour = 2500 ticks → 5000), 1200-tick recipe → 4 reps.
            Assert.That(CraftBatchMath.RepsByTimeout(ticksPerRep: 1200, timeoutTicks: 5000), Is.EqualTo(4));
            // recipe longer than the whole timeout → still allow 1 (don't make the batch impossible).
            Assert.That(CraftBatchMath.RepsByTimeout(ticksPerRep: 9000, timeoutTicks: 5000), Is.EqualTo(1));
        }

        [Test]
        public void Resolve_IsTheSmallestCap_FlooredAtZero()
        {
            Assert.That(CraftBatchMath.Resolve(requested: 10, byAvailability: 3, byMass: 7, byTimeout: int.MaxValue),
                Is.EqualTo(3));
            Assert.That(CraftBatchMath.Resolve(10, int.MaxValue, int.MaxValue, int.MaxValue), Is.EqualTo(10));
            Assert.That(CraftBatchMath.Resolve(10, 0, 5, 5), Is.EqualTo(0)); // an ingredient with none → batch impossible
            Assert.That(CraftBatchMath.Resolve(-2, 5, 5, 5), Is.EqualTo(0)); // negative request floored
        }

        [Test]
        public void ScarcestDefReps_SingleDefPerSlot_MatchesPerSlotFloor()
        {
            // slot0: def 0 needs 10, 100 available → 10 reps.
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0 }, new[] { 10 }, new[] { 100 }), Is.EqualTo(10));
            // two distinct defs → min of each slot's floor.
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0, 1 }, new[] { 10, 5 }, new[] { 100, 40 }), Is.EqualTo(8));   // min(10, 8)
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0, 1 }, new[] { 10, 5 }, new[] { 100, 12 }), Is.EqualTo(2));   // min(10, 2)
        }

        [Test]
        public void ScarcestDefReps_SharedDefAcrossSlots_SumsDemand()
        {
            // Two slots BOTH source def 0 (10 each = 20/rep) against one pool of 100 → 5 reps, NOT 10.
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0, 0 }, new[] { 10, 10 }, new[] { 100 }), Is.EqualTo(5));
            // Three slots on def 0 (5+5+5=15/rep) + one on def 1 (10/rep); pools 90 & 100 → min(6, 10) = 6.
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0, 0, 0, 1 }, new[] { 5, 5, 5, 10 }, new[] { 90, 100 }), Is.EqualTo(6));
        }

        [Test]
        public void ScarcestDefReps_Empty_IsUnbounded()
        {
            Assert.That(CraftBatchMath.ScarcestDefReps(new int[0], new int[0], new int[0]), Is.EqualTo(int.MaxValue));
            // A def with zero demand imposes no limit (skipped).
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0 }, new[] { 0 }, new[] { 50 }), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void Resolve_TheUserScenario_CookFourSimpleMeals_TimesThree()
        {
            // "Cook simple meal" needs 10 raw food/rep. Player wants 12 reps (4 meals × 3 batches = 120 raw food).
            // Stockpile has 200 raw food; raw food ~0.5 kg; a strong pawn (75 kg cap). Timeout 2 in-game hours.
            const int perRep = 10;
            int byAvail = CraftBatchMath.RepsByAvailability(perRep, availableUnits: 200);            // 20
            int byMass = CraftBatchMath.RepsByMass(OverloadTuning.MaxLevel, 75f, 75f, 5f,
                massPerRepKg: perRep * 0.5f, wantReps: 12);                                          // 12 (5 kg/rep fits)
            int byTimeout = CraftBatchMath.RepsByTimeout(ticksPerRep: 1300, timeoutTicks: 5000);    // 3
            Assert.That(CraftBatchMath.Resolve(12, byAvail, byMass, byTimeout), Is.EqualTo(3));      // timeout is the binding cap
        }
    }
}
