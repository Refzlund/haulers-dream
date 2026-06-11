using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CarriedInterceptPolicyTests
    {
        static bool Intercept(int workerToHauler, int haulerToStorage, int haulerToNeeder, int storageToNeeder,
            float frac = 1f)
            => CarriedInterceptPolicy.ShouldIntercept(workerToHauler, haulerToStorage, haulerToNeeder, storageToNeeder, frac);

        [Test]
        public void Worthwhile_WhenInterceptBeatsBaseline()
        {
            // baseline = 30 (toStorage) + 25 (storage->needer) = 55; intercept = 10 (toHauler) + 12 (->needer) = 22.
            Assert.That(Intercept(workerToHauler: 10, haulerToStorage: 30, haulerToNeeder: 12, storageToNeeder: 25), Is.True);
        }

        [Test]
        public void NotWorthwhile_WhenBaselineCheaper()
        {
            // intercept (40+30=70) >= baseline (30+20=50) -> let it go to storage.
            Assert.That(Intercept(workerToHauler: 40, haulerToStorage: 30, haulerToNeeder: 30, storageToNeeder: 20), Is.False);
        }

        [Test]
        public void HaulerNearlyAtStorage_DoesNotIntercept()
        {
            // haulerToStorage 8 < 16 -> let it finish, claim from storage.
            Assert.That(Intercept(workerToHauler: 2, haulerToStorage: 8, haulerToNeeder: 40, storageToNeeder: 40), Is.False);
        }

        [Test]
        public void ChaseTooFar_DoesNotIntercept()
        {
            // workerToHauler 30 > 24 -> don't chase.
            Assert.That(Intercept(workerToHauler: 30, haulerToStorage: 50, haulerToNeeder: 5, storageToNeeder: 50), Is.False);
        }

        [Test]
        public void TokenLoad_DoesNotIntercept()
        {
            // carriedFractionOfNeed 0.3 < 0.5 -> not worth interrupting.
            Assert.That(Intercept(workerToHauler: 5, haulerToStorage: 40, haulerToNeeder: 5, storageToNeeder: 40, frac: 0.3f), Is.False);
        }

        [Test]
        public void HalfLoad_AtThreshold_CanIntercept()
        {
            Assert.That(Intercept(workerToHauler: 5, haulerToStorage: 40, haulerToNeeder: 5, storageToNeeder: 40, frac: 0.5f), Is.True);
        }

        [Test]
        public void ChaseAtThreshold_StillAllowed()
        {
            // workerToHauler == 24 (not > 24); baseline 40+40=80 > intercept 24+5=29.
            Assert.That(Intercept(workerToHauler: 24, haulerToStorage: 40, haulerToNeeder: 5, storageToNeeder: 40), Is.True);
        }

        [Test]
        public void StorageLeftAtThreshold_StillAllowed()
        {
            // haulerToStorage == 16 (not < 16); baseline 16+40=56 > intercept 5+5=10.
            Assert.That(Intercept(workerToHauler: 5, haulerToStorage: 16, haulerToNeeder: 5, storageToNeeder: 40), Is.True);
        }

        [Test]
        public void EqualCostIsNotWorthwhile()
        {
            // intercept == baseline -> require STRICT saving -> false.
            Assert.That(Intercept(workerToHauler: 20, haulerToStorage: 20, haulerToNeeder: 20, storageToNeeder: 20), Is.False);
        }
    }
}
