using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class OpportunisticUnloadPolicyTests
    {
        // Default load fraction comfortably above the minimum unless a test overrides it.
        const float Load = 0.5f;

        static bool Should(int toTarget, int toStorage, int storageToTarget, float load = Load)
            => OpportunisticUnloadPolicy.ShouldUnloadOnWay(toTarget, toStorage, storageToTarget, load);

        [Test]
        public void StorageDirectlyOnTheWay_Diverts()
        {
            // pawn ---- storage ---- target (storage on the straight line) -> detour 0.
            Assert.That(Should(toTarget: 60, toStorage: 30, storageToTarget: 30), Is.True);
        }

        [Test]
        public void StorageWayOffToTheSide_DoesNotDivert()
        {
            // Big detour relative to the trip.
            Assert.That(Should(toTarget: 50, toStorage: 45, storageToTarget: 45), Is.False);
        }

        [Test]
        public void LocalWork_ShortTrip_DoesNotDivert()
        {
            // Trip below MinTripTiles — don't interrupt local work even if storage is right there.
            Assert.That(Should(toTarget: 10, toStorage: 2, storageToTarget: 9), Is.False);
        }

        [Test]
        public void TrivialLoad_DoesNotDivert()
        {
            Assert.That(Should(toTarget: 60, toStorage: 30, storageToTarget: 30, load: 0.05f), Is.False);
        }

        [Test]
        public void LongHaul_SlightSideStorage_Diverts()
        {
            // 100-tile trip; storage a little off the line (detour 10) -> within the fractional bar.
            Assert.That(Should(toTarget: 100, toStorage: 55, storageToTarget: 55), Is.True);
        }

        [Test]
        public void StorageNearPawnBeforeLongTrip_Diverts()
        {
            // Storage right next to the pawn, target far the same direction: detour ~2*near ~ small.
            Assert.That(Should(toTarget: 80, toStorage: 4, storageToTarget: 82), Is.True);
        }

        [Test]
        public void ShortTripStorageOnWay_StillBlockedByMinTrip()
        {
            // Even with zero detour, a 12-tile trip is below MinTripTiles (16).
            Assert.That(Should(toTarget: 12, toStorage: 6, storageToTarget: 6), Is.False);
        }

        [Test]
        public void DetourExactlyAtFloor_Diverts()
        {
            // 40-tile trip: fractional bar = 0.25*40 = 10 = MinDetourTiles. Detour 10 -> allowed.
            Assert.That(Should(toTarget: 40, toStorage: 25, storageToTarget: 25), Is.True);
        }
    }
}
