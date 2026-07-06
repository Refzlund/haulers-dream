using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class OpportunisticUnloadPolicyTests
    {
        // Default load fraction comfortably above the minimum but below the HEAVY threshold (0.5),
        // so these tests pin the strict light-load gates; heavy-load relaxation has its own tests.
        const float Load = 0.3f;

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

        // --- heavy-load relaxation: at >= HeavyLoadFraction (0.5) the trip/detour bars loosen,
        // so a pawn lugging half its capacity sheds the load far sooner. ---

        [Test]
        public void HeavyLoad_ShortLocalTrip_Diverts()
        {
            // Same geometry LocalWork_ShortTrip_DoesNotDivert pins as blocked for a light load:
            // trip 10 >= HeavyMinTripTiles (8), detour 1 -> a heavy pawn takes the side trip.
            Assert.That(Should(toTarget: 10, toStorage: 2, storageToTarget: 9, load: 0.5f), Is.True);
        }

        [Test]
        public void HeavyLoad_AcceptsBiggerDetour()
        {
            // 40-tile trip, detour 16: light bar = max(10, 0.25*40) = 10 -> blocked;
            // heavy bar = max(10, 0.5*40) = 20 -> allowed.
            Assert.That(Should(toTarget: 40, toStorage: 28, storageToTarget: 28, load: 0.3f), Is.False);
            Assert.That(Should(toTarget: 40, toStorage: 28, storageToTarget: 28, load: 0.5f), Is.True);
        }

        [Test]
        public void HeavyLoad_TinyTrip_StillBlocked()
        {
            // Even fully overloaded, a hop below HeavyMinTripTiles (8) never diverts.
            Assert.That(Should(toTarget: 6, toStorage: 1, storageToTarget: 6, load: 0.9f), Is.False);
        }

        [Test]
        public void JustBelowHeavy_KeepsStrictGates()
        {
            // 0.49 load is still "light": the 16-tile minimum trip applies.
            Assert.That(Should(toTarget: 12, toStorage: 6, storageToTarget: 6, load: 0.49f), Is.False);
        }

        // --- run-END relaxation: once the yield run is over (pawn picked unrelated work), there is no
        // minimum-trip floor — a worthwhile load is shed at nearby storage even on a short local hop. ---

        static bool RunEnd(int toTarget, int toStorage, int storageToTarget, float load = Load)
            => OpportunisticUnloadPolicy.ShouldUnloadOnRunEnd(toTarget, toStorage, storageToTarget, load);

        [Test]
        public void RunEnd_ShortLocalTripNearStorage_Diverts()
        {
            // The screenshot case: pawn finished deconstructing, picks LOCAL cleaning (trip 2) right next to
            // storage. ShouldUnloadOnWay blocks this (below MinTripTiles=16); the run-end variant diverts.
            Assert.That(Should(toTarget: 2, toStorage: 3, storageToTarget: 4), Is.False); // strict: blocked
            Assert.That(RunEnd(toTarget: 2, toStorage: 3, storageToTarget: 4), Is.True);  // run-end: diverts
        }

        [Test]
        public void RunEnd_LocalWorkFarFromStorage_DoesNotDivert()
        {
            // Cleaning locally (trip 2) but storage is across the map: detour ~2*40 = 78 > bar(20) -> no
            // cross-map round-trip just to unload; it'll unload later via the interval/idle backstop.
            Assert.That(RunEnd(toTarget: 2, toStorage: 40, storageToTarget: 40), Is.False);
        }

        [Test]
        public void RunEnd_StorageOnWayToFarTarget_Diverts()
        {
            // Next job is far (trip 40) with storage on the way (detour 0) -> divert (bar = max(20, 40)).
            Assert.That(RunEnd(toTarget: 40, toStorage: 20, storageToTarget: 20), Is.True);
        }

        [Test]
        public void RunEnd_TrivialLoad_DoesNotDivert()
        {
            // Even at run-end, a trivial load isn't worth a trip.
            Assert.That(RunEnd(toTarget: 2, toStorage: 3, storageToTarget: 4, load: 0.05f), Is.False);
        }

        [Test]
        public void RunEnd_DetourAtFloor_Diverts()
        {
            // Local trip (2), detour exactly at the 20-tile floor -> allowed; one past -> blocked.
            Assert.That(RunEnd(toTarget: 2, toStorage: 11, storageToTarget: 11), Is.True);  // detour 20 == bar
            Assert.That(RunEnd(toTarget: 2, toStorage: 12, storageToTarget: 11), Is.False); // detour 21 > bar
        }

        // --- downtime-swap severity gates (issue #122): the unload-before-eating/sleep swap must stand
        // down at the critical need category, so a starving pawn eats NOW and an exhausted pawn sleeps
        // NOW. Vanilla enum values pinned as ints (HungerCategory / RestCategory: 0..3). ---

        [Test]
        public void FoodSwap_AllowedBelowStarving()
        {
            // Fed(0) / Hungry(1) / UrgentlyHungry(2): the swap may replace the food job (the divert
            // cooldown then guarantees the very next determination keeps the vanilla food job).
            Assert.That(OpportunisticUnloadPolicy.MaySwapFoodJobForUnload(0), Is.True);
            Assert.That(OpportunisticUnloadPolicy.MaySwapFoodJobForUnload(1), Is.True);
            Assert.That(OpportunisticUnloadPolicy.MaySwapFoodJobForUnload(2), Is.True);
        }

        [Test]
        public void FoodSwap_StandsDownAtStarving()
        {
            // Starving(3): the pawn is taking malnutrition damage, so the vanilla food job must run
            // unchanged. Anything at or beyond Starving (a hypothetical modded category) also stands down.
            Assert.That(OpportunisticUnloadPolicy.MaySwapFoodJobForUnload(3), Is.False);
            Assert.That(OpportunisticUnloadPolicy.MaySwapFoodJobForUnload(4), Is.False);
        }

        [Test]
        public void RestSwap_AllowedBelowExhausted()
        {
            Assert.That(OpportunisticUnloadPolicy.MaySwapRestJobForUnload(0), Is.True);
            Assert.That(OpportunisticUnloadPolicy.MaySwapRestJobForUnload(1), Is.True);
            Assert.That(OpportunisticUnloadPolicy.MaySwapRestJobForUnload(2), Is.True);
        }

        [Test]
        public void RestSwap_StandsDownAtExhausted()
        {
            Assert.That(OpportunisticUnloadPolicy.MaySwapRestJobForUnload(3), Is.False);
            Assert.That(OpportunisticUnloadPolicy.MaySwapRestJobForUnload(4), Is.False);
        }
    }
}
