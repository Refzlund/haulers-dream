using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the bulk-refuel carry-weight clamp (<see cref="RefuelPlan.TakeFromStack"/>): a refuel courier sweeps
    /// fuel bounded by BOTH the refuelable's deficit AND the smart-overload carry-weight ceiling, so strict carry
    /// weight and the "Off" slider can never load it past 100% of its carry weight. Before the fix the sweep sized
    /// purely by the deficit, the lone into-inventory path that ignored the overload ceiling.
    /// </summary>
    [TestFixture]
    public class RefuelPlanTests
    {
        // A worked scenario: base carry cap 35 kg, pawn starts carrying 5 kg of gear, fuel is 2 kg / unit.
        const float Base = 35f;
        const float Gear = 5f;
        const float Unit = 2f;

        [Test]
        public void Take_ClampsToCeiling_UnderStrict()
        {
            // Strict -> ceiling = baseCap = 35. Room = 35 - 5 = 30 kg, so 15 units of 2 kg fit, even though the
            // refuelable needs 100 and the stack holds 40. This is the reported invariant: never past 100% weight.
            float ceiling = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, strictCarryWeight: true, Base);
            int take = RefuelPlan.TakeFromStack(deficitRemaining: 100, ceiling, runningMassKg: Gear, unitMassKg: Unit, stackCount: 40);
            Assert.That(take, Is.EqualTo(15));
            Assert.That(Gear + take * Unit, Is.LessThanOrEqualTo(ceiling + 1e-4f));
        }

        [Test]
        public void Take_ClampsToDeficit_WhenDeficitBindsFirst()
        {
            // The refuelable needs only 7, and 7 units (14 kg) fit within the 30 kg of room, so the deficit binds.
            float ceiling = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, strictCarryWeight: true, Base);
            Assert.That(RefuelPlan.TakeFromStack(deficitRemaining: 7, ceiling, Gear, Unit, stackCount: 40), Is.EqualTo(7));
        }

        [Test]
        public void Take_ClampsToStack_WhenStackBindsFirst()
        {
            // Deficit 100 and ample carry room, but the stack only holds 4 units.
            float ceiling = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, strictCarryWeight: true, Base);
            Assert.That(RefuelPlan.TakeFromStack(deficitRemaining: 100, ceiling, Gear, Unit, stackCount: 4), Is.EqualTo(4));
        }

        [Test]
        public void Take_NonStrict_AllowsOverloadPastCarryLimit()
        {
            // Fair slider, NOT strict -> the courier may overload to the break-even ceiling (well past 35), so it
            // carries MORE than the strict 15. This is why the fix does not regress non-strict users: the same
            // CeilingKg every other path uses keeps their configured overload, only strict/Off caps at 100%.
            float strict = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, strictCarryWeight: true, Base);
            float fair = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, strictCarryWeight: false, Base);
            int strictTake = RefuelPlan.TakeFromStack(deficitRemaining: 100, strict, Gear, Unit, stackCount: 200);
            int fairTake = RefuelPlan.TakeFromStack(deficitRemaining: 100, fair, Gear, Unit, stackCount: 200);
            Assert.That(fairTake, Is.GreaterThan(strictTake));
        }

        [Test]
        public void Take_NoSlowdownLevel_IsUnbounded_LoadsWholeDeficit()
        {
            // At the "no slowdown" slider stop the ceiling is infinite (carrying more is free), so behaviour is the
            // old deficit-only sizing: take min(deficit, stack) with weight never binding. Keeps that stop's users
            // byte-identical to before the clamp.
            float ceiling = BulkHaulPolicy.CeilingKg(0, strictCarryWeight: false, Base);
            Assert.That(RefuelPlan.TakeFromStack(deficitRemaining: 100, ceiling, runningMassKg: 9999f, unitMassKg: Unit, stackCount: 80), Is.EqualTo(80));
        }

        [Test]
        public void Take_MasslessFuel_BoundedOnlyByDeficitAndStack()
        {
            // A weightless modded fuel: the carry-weight ceiling never binds, so the take is min(deficit, stack)
            // even for a pawn already over the ceiling.
            float ceiling = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, strictCarryWeight: true, Base);
            Assert.That(RefuelPlan.TakeFromStack(deficitRemaining: 8, ceiling, runningMassKg: 999f, unitMassKg: 0f, stackCount: 40), Is.EqualTo(8));
        }

        [Test]
        public void Take_PawnAtCeiling_TakesNothingOfMassBearingFuel()
        {
            // Already at the strict ceiling -> no room for any mass-bearing fuel; a later trip resumes after unload.
            float ceiling = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, strictCarryWeight: true, Base);
            Assert.That(RefuelPlan.TakeFromStack(deficitRemaining: 100, ceiling, runningMassKg: Base, unitMassKg: Unit, stackCount: 40), Is.EqualTo(0));
        }

        [Test]
        public void Take_ZeroDeficitOrEmptyStack_TakesNothing()
        {
            float ceiling = BulkHaulPolicy.CeilingKg(OverloadTuning.OffLevel, false, Base);
            Assert.That(RefuelPlan.TakeFromStack(deficitRemaining: 0, ceiling, Gear, Unit, stackCount: 40), Is.EqualTo(0));
            Assert.That(RefuelPlan.TakeFromStack(deficitRemaining: 100, ceiling, Gear, Unit, stackCount: 0), Is.EqualTo(0));
        }

        [Test]
        public void EndToEndSweep_NeverExceedsCeilingOrDeficit_UnderStrict()
        {
            // Oracle mirroring BulkRefuel.TryGiveBulkRefuelJob's loop: a big deficit and many heavy fuel stacks
            // under strict carry weight. The cumulative swept mass must never pass the ceiling, and the total swept
            // units must never pass the deficit; a later trip tops up the rest.
            float ceiling = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, strictCarryWeight: true, Base);
            const int deficit = 200;
            int[] stacks = { 40, 40, 40, 40, 40, 40 }; // 240 units available on the ground, deficit 200
            float runningMass = Gear;
            int running = 0;
            int queued = 0;
            for (int i = 0; i < stacks.Length && running < deficit; i++)
            {
                int take = RefuelPlan.TakeFromStack(deficit - running, ceiling, runningMass, Unit, stacks[i]);
                if (take <= 0)
                    continue;
                queued++;
                running += take;
                runningMass += take * Unit;
            }
            Assert.That(runningMass, Is.LessThanOrEqualTo(ceiling + 1e-4f), "never past the carry-weight ceiling");
            Assert.That(running, Is.LessThanOrEqualTo(deficit), "never past the refuelable's deficit");
            // Room is 30 kg / 2 kg = 15 units, so the whole strict sweep is one 15-unit stack. The planner's
            // worth-it gate (needs 2+ queued stacks) then declines and defers to vanilla's single-stack refuel:
            // the correct outcome, since at 15 units the courier saves no trips over vanilla under strict.
            Assert.That(running, Is.EqualTo(15));
            Assert.That(queued, Is.EqualTo(1));
        }

        [Test]
        public void EndToEndSweep_NonStrictFair_SweepsMultipleStacksInOneTrip()
        {
            // The same field under the default Fair slider (not strict): the break-even ceiling (~2.75x) lets the
            // courier carry ~45 units across 2 stacks in one trip, so bulk-refuel still adds its trip-saving value.
            float ceiling = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, strictCarryWeight: false, Base);
            const int deficit = 200;
            int[] stacks = { 40, 40, 40, 40, 40, 40 };
            float runningMass = Gear;
            int running = 0;
            int queued = 0;
            for (int i = 0; i < stacks.Length && running < deficit; i++)
            {
                int take = RefuelPlan.TakeFromStack(deficit - running, ceiling, runningMass, Unit, stacks[i]);
                if (take <= 0)
                    continue;
                queued++;
                running += take;
                runningMass += take * Unit;
            }
            Assert.That(runningMass, Is.LessThanOrEqualTo(ceiling + 1e-4f));
            Assert.That(queued, Is.GreaterThanOrEqualTo(2), "the Fair overload still bulk-loads multiple stacks per trip");
        }
    }
}
