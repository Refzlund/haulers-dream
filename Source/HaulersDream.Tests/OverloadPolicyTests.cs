using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class OverloadPolicyTests
    {
        // Reference pawn: 35 kg capacity, 5 kg of worn gear, 1 kg/unit resource, full carry limit.
        const float Cap = 35f;
        const float Base = 35f;   // carry-limit mass = fraction 1.0 × capacity
        const float Gear = 5f;
        const float Unit = 1f;

        static int Carry(int level, int demand, int available, float current = Gear, float unit = Unit, float baseCap = Base)
            => OverloadPolicy.UnitsToCarry(level, Cap, baseCap, current, unit, demand, available);

        // baseUnits under the carry limit = floor((35-5)/1) = 30.

        [Test]
        public void Off_NeverOverloads()
        {
            Assert.That(Carry(OverloadTuning.OffLevel, demand: 200, available: 200), Is.EqualTo(30));
        }

        [Test]
        public void NoSlowdownLevel_CarriesEverythingDemanded()
        {
            Assert.That(Carry(0, demand: 200, available: 200), Is.EqualTo(200));
        }

        [Test]
        public void NoSlowdownLevel_StillCappedByAvailability()
        {
            Assert.That(Carry(0, demand: 200, available: 47), Is.EqualTo(47));
        }

        [Test]
        public void Fair_OverloadsToTheBreakEvenCeiling()
        {
            // Fair break-even ratio ≈ 2.75 → mass cap ≈ 96.25 kg → room 91.25 → 91 units of 1 kg.
            Assert.That(Carry(5, demand: 200, available: 200), Is.EqualTo(91));
        }

        [Test]
        public void Fair_CappedByDemand()
        {
            // Only 45 units are actually wanted (this job + future plans) → carry 45, not the 91 it could.
            Assert.That(Carry(5, demand: 45, available: 200), Is.EqualTo(45));
        }

        [Test]
        public void Fair_CappedByAvailability()
        {
            Assert.That(Carry(5, demand: 200, available: 40), Is.EqualTo(40));
        }

        [Test]
        public void DemandWithinCarryLimit_NoOverload()
        {
            // Demand (20) is below the no-overload baseline (30) → just take the 20, no overload.
            Assert.That(Carry(5, demand: 20, available: 200), Is.EqualTo(20));
        }

        [Test]
        public void DemandEqualsBaseline_NoOverload()
        {
            Assert.That(Carry(5, demand: 30, available: 200), Is.EqualTo(30));
        }

        [Test]
        public void HigherLevel_CarriesNoMoreThanLowerLevel()
        {
            int l1 = Carry(1, demand: 500, available: 500);
            int l5 = Carry(5, demand: 500, available: 500);
            int l9 = Carry(9, demand: 500, available: 500);
            Assert.That(l1, Is.GreaterThanOrEqualTo(l5));
            Assert.That(l5, Is.GreaterThanOrEqualTo(l9));
            Assert.That(l9, Is.GreaterThanOrEqualTo(30)); // never below the no-overload baseline
        }

        [Test]
        public void AlreadyOverloaded_NoRoom_ReturnsBaseline()
        {
            // Pawn already at 100 kg — past the Fair overload ceiling (~96.25 kg = 2.75 × 35) → take none
            // extra beyond the (zero) baseline room. baseUnits here is 0 (already over the carry limit).
            Assert.That(Carry(5, demand: 200, available: 200, current: 100f), Is.EqualTo(0));
        }

        [Test]
        public void Massless_TakenInFullUpToDemand()
        {
            Assert.That(Carry(5, demand: 123, available: 500, unit: 0f), Is.EqualTo(123));
        }

        [Test]
        public void ZeroDemandOrAvailable_TakesNothing()
        {
            Assert.That(Carry(5, demand: 0, available: 200), Is.EqualTo(0));
            Assert.That(Carry(5, demand: 200, available: 0), Is.EqualTo(0));
        }

        [Test]
        public void LowerCarryLimitFraction_ScalesTheOverloadCeilingToo()
        {
            // Carry limit set to 50% (baseCap 17.5): the overload ceiling scales off the CONFIGURED base cap
            // (~2.75 × 17.5 ≈ 48.1 kg; room from 5 kg current ≈ 43.1 → 43 units), NOT the true capacity — a
            // player-reduced carry limit must not be silently nullified by the overload feature.
            // (Supersedes the old "...StillOverloadsFromTrueCapacity" spec, which encoded exactly that bug.)
            int units = Carry(5, demand: 200, available: 200, baseCap: 17.5f);
            Assert.That(units, Is.EqualTo(43));
            // At the default fraction (base == max) the ceiling matches the headline Fair break-even.
            Assert.That(Carry(5, demand: 200, available: 200), Is.EqualTo(91));
        }

        [Test]
        public void HeavyUnits_FewerUnitsButSameMassCeiling()
        {
            // 10 kg/unit: mass cap ≈ 96.25, room from 5 kg = 91.25 → floor(91.25/10) = 9 units.
            Assert.That(Carry(5, demand: 200, available: 200, unit: 10f), Is.EqualTo(9));
        }

        [Test]
        public void ZeroCapacityPawn_NoOverloadNoCrash()
        {
            // Babies / subhumans / non-tool-user non-pack-animals: MassUtility.Capacity returns 0,
            // which OverloadGate passes as maxCapacityKg. The maxCapacityKg<=0 guard must fall back to
            // the (zero) baseline rather than attempting overload (no divide, no negative room).
            int units = OverloadPolicy.UnitsToCarry(
                OverloadTuning.FairLevel,
                maxCapacityKg: 0f, baseCapKg: 0f, currentMassKg: 0f,
                unitMassKg: 1f, demandUnits: 200, availableUnits: 200);
            Assert.That(units, Is.EqualTo(0));
        }

        [Test]
        public void ZeroCapacityPawn_WithGearMass_StillNoOverload()
        {
            int units = OverloadPolicy.UnitsToCarry(
                OverloadTuning.FairLevel,
                maxCapacityKg: 0f, baseCapKg: 0f, currentMassKg: 3f,
                unitMassKg: 1f, demandUnits: 200, availableUnits: 200);
            Assert.That(units, Is.EqualTo(0));
        }
    }
}
