using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class PackAnimalLoadPolicyTests
    {
        private static bool Divert(
            bool autoDivertEnabled = true, bool modActiveOnNonHomeMaps = true, bool isPlayerHome = false,
            bool hasCarrier = true, bool hasSurplus = true, bool alreadyLoading = false)
            => PackAnimalLoadPolicy.ShouldAutoDivert(autoDivertEnabled, modActiveOnNonHomeMaps, isPlayerHome,
                hasCarrier, hasSurplus, alreadyLoading);

        [Test]
        public void HeavyPawnOnCaravanWithCarrierAndSurplus_Diverts()
        {
            Assert.That(Divert(), Is.True);
        }

        [Test]
        public void FeatureOff_DoesNotDivert()
        {
            Assert.That(Divert(autoDivertEnabled: false), Is.False);
        }

        [Test]
        public void ModInertOnNonHomeMaps_DoesNotDivert()
        {
            // enableOnNonHomeMaps off = the mod is fully inert on caravans; never auto-load there.
            Assert.That(Divert(modActiveOnNonHomeMaps: false), Is.False);
        }

        [Test]
        public void AtHome_DoesNotDivert()
        {
            // At home, vanilla storage + the normal unload pass handle it; never divert to an animal.
            Assert.That(Divert(isPlayerHome: true), Is.False);
        }

        [Test]
        public void NoCarrier_DoesNotDivert()
        {
            Assert.That(Divert(hasCarrier: false), Is.False);
        }

        [Test]
        public void NoSurplus_DoesNotDivert()
        {
            // Nothing above the pawn's personal kit to offload (only keep-stock food/drugs) -> stay put.
            Assert.That(Divert(hasSurplus: false), Is.False);
        }

        [Test]
        public void AlreadyLoading_DoesNotDivert()
        {
            // Dedup: a load job already running/queued must not double-queue another.
            Assert.That(Divert(alreadyLoading: true), Is.False);
        }

        // --- DepositCountWithinFreeSpace ---

        [Test]
        public void Deposit_FitsWithinFreeSpace()
        {
            // 10 kg free, 1 kg/unit, 100 offered -> 10 fit.
            Assert.That(PackAnimalLoadPolicy.DepositCountWithinFreeSpace(10f, 1f, 100), Is.EqualTo(10));
        }

        [Test]
        public void Deposit_ClampsToOfferedCount()
        {
            // Plenty of room, but only 5 offered.
            Assert.That(PackAnimalLoadPolicy.DepositCountWithinFreeSpace(1000f, 1f, 5), Is.EqualTo(5));
        }

        [Test]
        public void Deposit_MasslessTakenInFull()
        {
            Assert.That(PackAnimalLoadPolicy.DepositCountWithinFreeSpace(0f, 0f, 7), Is.EqualTo(7));
        }

        [Test]
        public void Deposit_NoFreeSpace_TakesNone()
        {
            Assert.That(PackAnimalLoadPolicy.DepositCountWithinFreeSpace(0f, 1f, 50), Is.EqualTo(0));
            Assert.That(PackAnimalLoadPolicy.DepositCountWithinFreeSpace(-3f, 1f, 50), Is.EqualTo(0));
        }

        [Test]
        public void Deposit_PartialUnitRoundsDown()
        {
            // 2.9 kg free, 1 kg/unit -> 2 fit (never over-encumber by rounding up).
            Assert.That(PackAnimalLoadPolicy.DepositCountWithinFreeSpace(2.9f, 1f, 50), Is.EqualTo(2));
        }

        [Test]
        public void Deposit_ZeroOffered_TakesNone()
        {
            Assert.That(PackAnimalLoadPolicy.DepositCountWithinFreeSpace(100f, 1f, 0), Is.EqualTo(0));
        }
    }
}
