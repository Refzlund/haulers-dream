using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class TransporterUnloadProgressTests
    {
        [Test]
        public void QueuedOrFailedDeliveryCannotEarnAnotherTrip()
        {
            var trip = new TransporterUnloadProgress();
            trip.RecordPull(37);
            Assert.That(trip.RecordDelivery(0, 37), Is.Zero, "No removal from actual hands.");
            Assert.That(trip.RecordDelivery(37, 0), Is.Zero, "Removal without accepted storage is not delivery.");
            Assert.That(trip.BeginNextTrip(), Is.False);
            Assert.That(trip.Pulled, Is.EqualTo(37));
        }

        [Test]
        public void EveryPulledUnitMustBeStoredAcrossPartialDeliveriesBeforeNextTrip()
        {
            var trip = new TransporterUnloadProgress();
            trip.RecordPull(20); trip.RecordPull(17);
            Assert.That(trip.RecordDelivery(20, 20), Is.EqualTo(20));
            Assert.That(trip.BeginNextTrip(), Is.False);
            Assert.That(trip.RecordDelivery(10, 10), Is.EqualTo(10));
            var restored = new TransporterUnloadProgress(trip.Pulled, trip.Delivered);
            Assert.That(restored.Complete, Is.False);
            Assert.That(restored.RecordDelivery(7, 7), Is.EqualTo(7));
            Assert.That(restored.Complete, Is.True);
            Assert.That(restored.BeginNextTrip(), Is.True);
            Assert.That(restored.Pulled, Is.Zero);
            Assert.That(restored.Delivered, Is.Zero);
            Assert.That(restored.BeginNextTrip(), Is.False, "An empty visit cannot chain.");
        }

        [Test]
        public void ExcessDestinationGainDoesNotCreditUnrelatedCargo()
        {
            var trip = new TransporterUnloadProgress();
            trip.RecordPull(10);
            Assert.That(trip.RecordDelivery(4, 30), Is.EqualTo(4));
            Assert.That(trip.RecordDelivery(30, 2), Is.EqualTo(2));
            Assert.That(trip.RecordDelivery(-1, 10), Is.Zero);
            Assert.That(trip.RecordDelivery(10, -1), Is.Zero);
            Assert.That(trip.RecordDelivery(20, 20), Is.EqualTo(4));
            Assert.That(trip.Delivered, Is.EqualTo(10));
            Assert.That(trip.RecordDelivery(1, 1), Is.Zero);
        }
    }
}
