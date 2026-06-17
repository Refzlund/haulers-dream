using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the en-route ↔ opportunistic-unload mutex (plan G3) BOTH directions: a sub-threshold load lets
    /// en-route proceed; an at/over-threshold load (with somewhere to unload, off cooldown) stands en-route
    /// down. Also pins the inert cases (feature off, cooling down, nowhere to unload) where the mutex must
    /// never force en-route to yield. Threshold parity with <see cref="OpportunisticUnloadPolicy"/> is
    /// asserted so the two features hand off at the same point.
    /// </summary>
    [TestFixture]
    public class EnRouteMutexPolicyTests
    {
        const float Above = OpportunisticUnloadPolicy.MinLoadFraction + 0.05f; // clearly over the divert point
        const float Below = OpportunisticUnloadPolicy.MinLoadFraction - 0.05f; // clearly under

        static bool StandDown(bool enabled = true, float load = Above, bool hasCell = true, bool cooldownElapsed = true)
            => EnRouteMutexPolicy.MustStandDown(enabled, load, hasCell, cooldownElapsed);

        // --- Direction 1: at/over the unload-divert point -> en-route STANDS DOWN ----------------------

        [Test]
        public void AtOrOverLoad_WithStorageOffCooldown_StandsDown()
        {
            Assert.That(StandDown(load: Above), Is.True);
        }

        [Test]
        public void ExactlyAtThreshold_StandsDown()
        {
            // The threshold is inclusive (>=) on the unload side (ShouldAttemptDivert uses >=), so en-route
            // yields exactly at the boundary too.
            Assert.That(StandDown(load: OpportunisticUnloadPolicy.MinLoadFraction), Is.True);
        }

        // --- Direction 2: below the unload-divert point -> en-route MAY PROCEED ------------------------

        [Test]
        public void SubThresholdLoad_AllowsEnRoute()
        {
            Assert.That(StandDown(load: Below), Is.False);
        }

        [Test]
        public void JustUnderThreshold_AllowsEnRoute()
        {
            // One epsilon under the inclusive threshold -> the unload divert rejects -> en-route is free.
            Assert.That(StandDown(load: OpportunisticUnloadPolicy.MinLoadFraction - 0.0001f), Is.False);
        }

        [Test]
        public void EmptyLoad_AllowsEnRoute()
        {
            Assert.That(StandDown(load: 0f), Is.False);
        }

        // --- Inert cases: the mutex never forces a stand-down -------------------------------------------

        [Test]
        public void FeatureOff_NeverStandsDown()
        {
            // Even a heavy load with storage off cooldown does not yield when opportunistic unload is off.
            Assert.That(StandDown(enabled: false, load: 0.9f), Is.False);
        }

        [Test]
        public void NoStorableCell_AllowsEnRoute()
        {
            // Nowhere to drop the load -> no unload divert can happen -> en-route may grab.
            Assert.That(StandDown(load: Above, hasCell: false), Is.False);
        }

        [Test]
        public void CooldownActive_AllowsEnRoute()
        {
            // A recent (possibly failed) divert is cooling down -> the unload feature won't fire -> en-route
            // is not forced to yield on that basis.
            Assert.That(StandDown(load: Above, cooldownElapsed: false), Is.False);
        }

        // --- Threshold handoff parity with the unload policy --------------------------------------------

        [Test]
        public void HandsOffAtTheSameThresholdAsTheUnloadDivert()
        {
            // For every load fraction (with storage available, off cooldown, feature on), the mutex stands
            // en-route down EXACTLY when the unload feature's necessary load-fraction precondition is met —
            // i.e. they agree on the boundary, so the two features are exclusive with no gap or overlap.
            for (float load = 0f; load <= 1f; load += 0.025f)
            {
                bool standDown = EnRouteMutexPolicy.MustStandDown(true, load, true, true);
                bool unloadPossible = OpportunisticUnloadPolicy.ShouldAttemptDivert(load, true, 1, true);
                Assert.That(standDown, Is.EqualTo(unloadPossible),
                    $"mutex and unload-divert must agree on the load-fraction boundary at {load}");
            }
        }
    }
}
