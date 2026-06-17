using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class VehicleCarrierEligibilityPolicyTests
    {
        [Test]
        public void Eligible_BaseTrue_NotInVehicle()
            => Assert.That(VehicleCarrierEligibilityPolicy.IsEligibleVehicleAwareHolder(true, false), Is.True);

        [Test]
        public void Eligible_BaseTrue_InVehicle()
            => Assert.That(VehicleCarrierEligibilityPolicy.IsEligibleVehicleAwareHolder(true, true), Is.False);

        [Test]
        public void Eligible_BaseFalse_RegardlessOfInVehicle()
        {
            Assert.That(VehicleCarrierEligibilityPolicy.IsEligibleVehicleAwareHolder(false, false), Is.False);
            Assert.That(VehicleCarrierEligibilityPolicy.IsEligibleVehicleAwareHolder(false, true), Is.False);
        }
    }
}
