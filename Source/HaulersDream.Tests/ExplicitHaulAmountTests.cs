using System;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    public class ExplicitHaulAmountTests
    {
        [Test]
        public void RequestedTotalSpansBoundedTripsAndDoesNotGrowWithSource()
        {
            int delivered = 0, source = 150;
            foreach (int expected in new[] { 35, 35, 30 })
            {
                int trip = ExplicitHaulAmount.Trip(100 - delivered, source, 35, 150 - delivered);
                Assert.That(trip, Is.EqualTo(expected));
                delivered = ExplicitHaulAmount.Credit(100, delivered, trip, 0, trip);
                source -= trip;
            }
            Assert.Multiple(() => { Assert.That(delivered, Is.EqualTo(100)); Assert.That(source, Is.EqualTo(50)); });
        }
        [Test]
        public void SevenOfFifteenLeavesEightAndStopsAtTotal()
        {
            int trip = ExplicitHaulAmount.Trip(7, 15, 75, 75);
            Assert.That(15-trip, Is.EqualTo(8));
            Assert.That(ExplicitHaulAmount.Credit(7, 0, trip, 0, 7), Is.EqualTo(7));
            Assert.That(ExplicitHaulAmount.Trip(0, 8, 75, 68), Is.Zero);
        }
        [Test]
        public void PartialFalseOrThrowingPlacementCreditsPhysicalThreeAndPreservesFour()
        {
            int delivered = ExplicitHaulAmount.Credit(7, 0, 7, 4, 3);
            Assert.That(delivered, Is.EqualTo(3));
            Assert.That(ExplicitHaulAmount.Credit(7, delivered, 4, 0, 4), Is.EqualTo(7));
        }
        [TestCase(0, 0)] // missing parcel cannot be called delivered without a receiver
        [TestCase(4, 4)] // inconsistent receiver delta must not double-credit
        [TestCase(8, 0)] // foreign stock cannot enlarge ownership
        [TestCase(-1, 8)]
        public void AmbiguousCustodyRefusesCredit(int retained, int observed)
            => Assert.Throws<InvalidOperationException>(() => ExplicitHaulAmount.Credit(7, 0, 7, retained, observed));
        [Test]
        public void AlreadyCreditedParcelCannotBeCreditedAgain()
            => Assert.Throws<InvalidOperationException>(() => ExplicitHaulAmount.Credit(7, 3, 7, 4, 3));
        [TestCase(0,75)]
        [TestCase(75,0)]
        public void NoAvailableResourceMeansNoPickup(int hands,int destination)
            => Assert.That(ExplicitHaulAmount.Trip(100,100,hands,destination),Is.Zero);
        [Test]
        public void NativeHandsAndLiveDestinationEachBoundIntake()
        {
            Assert.That(ExplicitHaulAmount.Trip(100,100,35,3),Is.EqualTo(3));
            Assert.That(ExplicitHaulAmount.Trip(100,100,2,3),Is.EqualTo(2));
        }
    }
}
