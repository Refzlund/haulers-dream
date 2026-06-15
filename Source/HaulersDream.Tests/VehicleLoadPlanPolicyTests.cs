using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class VehicleLoadPlanPolicyTests
    {
        [Test]
        public void Deposit_SurplusWins()
            => Assert.That(VehicleLoadPlanPolicy.DepositUnits(5, 100), Is.EqualTo(5));

        [Test]
        public void Deposit_DemandWins()
            => Assert.That(VehicleLoadPlanPolicy.DepositUnits(100, 7), Is.EqualTo(7));

        [Test]
        public void Deposit_Zero()
            => Assert.That(VehicleLoadPlanPolicy.DepositUnits(0, 9), Is.EqualTo(0));

        [Test]
        public void Deposit_NegativeDemandClampsToZero()
            => Assert.That(VehicleLoadPlanPolicy.DepositUnits(10, -3), Is.EqualTo(0));

        [Test]
        public void Deposit_BothZero()
            => Assert.That(VehicleLoadPlanPolicy.DepositUnits(0, 0), Is.EqualTo(0));
    }
}
