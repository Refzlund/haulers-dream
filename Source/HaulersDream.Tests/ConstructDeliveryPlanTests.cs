using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class ConstructDeliveryPlanTests
    {
        // Reference: a colonist with 35 kg capacity carrying 5 kg of gear, delivering 0.5 kg/unit steel,
        // full carry-limit fraction. Hands hold a 75-steel stack (MaxStackSpaceEver). These mirror the
        // real geothermal-generator case (needs 340 steel).
        const float Cap = 35f;
        const float Base = 35f;
        const float Gear = 5f;
        const float Steel = 0.5f;
        const int Hand = 75;

        static int Plan(int level, int frameNeed, int handCap, int available,
            float maxCap = Cap, float baseCap = Base, float cur = Gear, float unit = Steel)
            => ConstructDeliveryPlan.PlanLoad(level, maxCap, baseCap, cur, unit, frameNeed, handCap, available);

        [Test]
        public void Geothermal_Fair_LoadsPastHandStack()
        {
            // Fair overload ceiling ≈ 2.75×35 = 96.25 kg → room 91.25 → 182 steel. Far more than a 75 hand-stack.
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 340, handCap: Hand, available: 300), Is.EqualTo(182));
        }

        [Test]
        public void SmallNeed_FallsBackToHands()
        {
            // One hand-trip (≤75) already satisfies the needer -> don't intervene.
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 50, handCap: Hand, available: 300), Is.EqualTo(0));
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 75, handCap: Hand, available: 300), Is.EqualTo(0));
        }

        [Test]
        public void ScarceMaterial_FallsBackToHands()
        {
            // Only ~one hand-load of material exists nearby -> hands are already optimal.
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 340, handCap: Hand, available: 75), Is.EqualTo(0));
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 340, handCap: Hand, available: 50), Is.EqualTo(0));
        }

        [Test]
        public void MaterialJustAboveHandStack_LoadsThatMuch()
        {
            // 80 available, need 340: load the 80 (one inventory trip beats two hand trips of 75 + 5).
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 340, handCap: Hand, available: 80), Is.EqualTo(80));
        }

        [Test]
        public void OverloadOff_StrongPawn_StillLoadsToCapacity()
        {
            // Overload disabled, but a 70 kg-capacity pawn fits 130 steel under 100% — beats a 75 hand-stack,
            // with NO slowdown. Inventory delivery still wins.
            Assert.That(Plan(OverloadTuning.OffLevel, frameNeed: 340, handCap: Hand, available: 300, maxCap: 70f, baseCap: 70f),
                Is.EqualTo(130));
        }

        [Test]
        public void OverloadOff_WeakPawn_FallsBackToHands()
        {
            // Overload off + a 35 kg pawn fits only 60 steel at 100% < a 75 hand-stack -> no benefit, use hands.
            Assert.That(Plan(OverloadTuning.OffLevel, frameNeed: 340, handCap: Hand, available: 300), Is.EqualTo(0));
        }

        [Test]
        public void AlreadyNearCeiling_FallsBackToHands()
        {
            // Pawn already at 95 kg (near the Fair ~96.25 kg ceiling) can add ~2 steel -> nowhere near beating hands.
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 340, handCap: Hand, available: 300, cur: 95f), Is.EqualTo(0));
        }

        [Test]
        public void HeavyMaterial_CeilingBelowHandStack_FallsBackToHands()
        {
            // 8 kg/unit material, hands hold 15: Fair ceiling is ~11 units (< 15) -> hands already optimal.
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 200, handCap: 15, available: 200, unit: 8f), Is.EqualTo(0));
        }

        [Test]
        public void DegenerateInputs_ReturnZero()
        {
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 340, handCap: 0, available: 300), Is.EqualTo(0));
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 340, handCap: Hand, available: 300, unit: 0f), Is.EqualTo(0));
            Assert.That(Plan(OverloadTuning.FairLevel, frameNeed: 340, handCap: Hand, available: 300, maxCap: 0f), Is.EqualTo(0));
        }

        [Test]
        public void GatherCeiling_BoundsTheLoadByMassAndNeed()
        {
            // Fair: mass ceiling 182, but never gather beyond the needer's own need.
            Assert.That(ConstructDeliveryPlan.GatherCeiling(OverloadTuning.FairLevel, Cap, Base, Gear, Steel, frameNeedUnits: 340),
                Is.EqualTo(182));
            Assert.That(ConstructDeliveryPlan.GatherCeiling(OverloadTuning.FairLevel, Cap, Base, Gear, Steel, frameNeedUnits: 90),
                Is.EqualTo(90));
        }

        [Test]
        public void GatherCeiling_OffWeakPawn_IsBelowHandStack()
        {
            // The game layer rejects inventory delivery when this ceiling ≤ hand cap; prove it's 60 (< 75) here.
            Assert.That(ConstructDeliveryPlan.GatherCeiling(OverloadTuning.OffLevel, Cap, Base, Gear, Steel, frameNeedUnits: 340),
                Is.EqualTo(60));
        }

        [Test]
        public void PlanNeverExceedsNeedOrAvailability()
        {
            // Whatever the slider, the load is bounded by the needer's need and the material on hand.
            for (int lv = 0; lv <= OverloadTuning.MaxLevel; lv++)
            {
                int load = Plan(lv, frameNeed: 120, handCap: Hand, available: 500);
                Assert.That(load, Is.LessThanOrEqualTo(120), $"level {lv} exceeded need");
                int load2 = Plan(lv, frameNeed: 500, handCap: Hand, available: 110);
                Assert.That(load2, Is.LessThanOrEqualTo(110), $"level {lv} exceeded availability");
            }
        }

        // ---- ShouldLoadBeforeDeliver: the route "top off after every wall" fix ----
        // The driver makes a stockpile LOAD trip only when carried stock can't cover the IMMEDIATE frame.

        [Test]
        public void ShouldLoad_EmptyInventory_NeedsToLoad()
        {
            // First stop (or after running dry): carries nothing, frame needs 5 -> must trip and load.
            Assert.That(ConstructDeliveryPlan.ShouldLoadBeforeDeliver(inventoryUnits: 0, immediateNeedUnits: 5), Is.True);
        }

        [Test]
        public void ShouldLoad_CarriesEnough_DeliversFromInventory()
        {
            // The fix: mid-route the pawn carries a big batch (63) and the frame needs 5 -> NO stockpile trip.
            Assert.That(ConstructDeliveryPlan.ShouldLoadBeforeDeliver(inventoryUnits: 63, immediateNeedUnits: 5), Is.False);
        }

        [Test]
        public void ShouldLoad_BoundaryExactlyEnough_DeliversFromInventory()
        {
            // Carries exactly the frame's need -> deliver from inventory, don't trip.
            Assert.That(ConstructDeliveryPlan.ShouldLoadBeforeDeliver(inventoryUnits: 5, immediateNeedUnits: 5), Is.False);
        }

        [Test]
        public void ShouldLoad_BoundaryOneShort_NeedsToLoad()
        {
            // One unit short of the frame's need -> trip (and the fill loop then refills to the ceiling).
            Assert.That(ConstructDeliveryPlan.ShouldLoadBeforeDeliver(inventoryUnits: 4, immediateNeedUnits: 5), Is.True);
        }

        [Test]
        public void ShouldLoad_NothingNeeded_DoesNotLoad()
        {
            // Frame already satisfied (enroute-covered) -> nothing to load even with an empty inventory.
            Assert.That(ConstructDeliveryPlan.ShouldLoadBeforeDeliver(inventoryUnits: 0, immediateNeedUnits: 0), Is.False);
            Assert.That(ConstructDeliveryPlan.ShouldLoadBeforeDeliver(inventoryUnits: 99, immediateNeedUnits: 0), Is.False);
        }

        [Test]
        public void RouteCadence_OneTripPerCeiling_NotPerFrame()
        {
            // End-to-end contract: simulate a wall line where the WHOLE route demand far exceeds the carry
            // ceiling, driving the trip decision with ShouldLoadBeforeDeliver and refilling to the ceiling
            // on each trip. The fix's promise is "one stockpile trip per ceiling-worth," NOT one per wall.
            const int walls = 30;       // 30 walls
            const int perWall = 5;      // 5 wood each -> 150 total, far above the ceiling
            const int ceiling = 91;     // ~Fair ceiling for a 35 kg colonist, 1 kg wood (2.75×35 − ~5 gear)

            int inventory = 0;
            int trips = 0;
            for (int wall = 0; wall < walls; wall++)
            {
                if (ConstructDeliveryPlan.ShouldLoadBeforeDeliver(inventory, perWall))
                {
                    trips++;
                    inventory = ceiling; // the fill loop tops up to the ceiling when it does trip
                }
                inventory -= perWall;    // deliver this wall from the carried stock
            }

            // Old behaviour topped off on essentially every wall (~30 trips); the fix is ceil(total/ceiling).
            int total = walls * perWall;
            int expected = (total + ceiling - 1) / ceiling; // ceil(150/91) = 2
            Assert.That(trips, Is.EqualTo(expected), "should reload once per ceiling-worth, not per wall");
            Assert.That(trips, Is.LessThan(walls), "must be far fewer trips than walls");
        }
    }
}
