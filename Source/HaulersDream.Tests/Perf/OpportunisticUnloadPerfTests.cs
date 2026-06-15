using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guard for HD-OPPUNLOAD's cheap pre-gate <see cref="OpportunisticUnloadPolicy.ShouldAttemptDivert"/>.
    /// This runs per-pawn on every work-found scan BEFORE the expensive Verse-side storage search
    /// (<c>TryFindBestBetterStoreCellFor</c>), so it must be branch-only — a gate that allocated would trade the
    /// spatial-search saving for GC jitter. It is pure primitives in / bool out, so it is 0 B/op.
    ///
    /// The fixture also pins the load-bearing safety property: the pre-gate may only SHORT-CIRCUIT a divert the
    /// full <see cref="OpportunisticUnloadPolicy.ShouldUnloadOnWay"/> / <see cref="OpportunisticUnloadPolicy.ShouldUnloadOnRunEnd"/>
    /// math would already reject — it must never admit one those reject. (The runtime still runs the full math
    /// after the gate passes; the gate just lets the storage search be deferred.)
    /// </summary>
    [TestFixture, Category("Perf")]
    public class OpportunisticUnloadPerfTests
    {
        private const float LoadFraction = 0.3f;        // above MinLoadFraction (0.15)
        private const bool CooldownElapsed = true;
        private const int TrackedCount = 4;
        private const bool CapPositive = true;

        [Test]
        public void ShouldAttemptDivert_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => OpportunisticUnloadPolicy.ShouldAttemptDivert(
                    LoadFraction, CooldownElapsed, TrackedCount, CapPositive),
                "the opportunistic-unload pre-gate must stay branch-only (it runs before the storage search)");

        // --- Behaviour: the pre-gate's individual short-circuits ---

        [Test]
        public void NoCapacity_DoesNotAttempt() =>
            Assert.That(OpportunisticUnloadPolicy.ShouldAttemptDivert(
                LoadFraction, CooldownElapsed, TrackedCount, capPositive: false), Is.False);

        [Test]
        public void NothingTracked_DoesNotAttempt() =>
            Assert.That(OpportunisticUnloadPolicy.ShouldAttemptDivert(
                LoadFraction, CooldownElapsed, trackedCount: 0, CapPositive), Is.False);

        [Test]
        public void CooldownNotElapsed_DoesNotAttempt() =>
            Assert.That(OpportunisticUnloadPolicy.ShouldAttemptDivert(
                LoadFraction, cooldownElapsed: false, TrackedCount, CapPositive), Is.False);

        [Test]
        public void SubThresholdLoad_DoesNotAttempt() =>
            Assert.That(OpportunisticUnloadPolicy.ShouldAttemptDivert(
                loadFraction: 0.05f, CooldownElapsed, TrackedCount, CapPositive), Is.False);

        [Test]
        public void WorthwhileLoadOffCooldown_Attempts() =>
            Assert.That(OpportunisticUnloadPolicy.ShouldAttemptDivert(
                LoadFraction, CooldownElapsed, TrackedCount, CapPositive), Is.True);

        // --- Safety property: a load fraction the full decision rejects (below the minimum) must never pass the
        // gate, so the gate can only suppress diverts the full math would reject. We sweep load fractions and
        // assert: when ShouldAttemptDivert returns false purely on the load fraction, BOTH full decisions also
        // reject (for every geometry), and when the full decision admits a divert the gate did not block it. ---

        [Test]
        public void Gate_OnlyShortCircuits_WhatFullMathRejects()
        {
            // Geometries chosen so the full math admits a divert for a worthwhile load (storage on the way,
            // a real trip) — so any false from the gate at a worthwhile load would be an over-suppression.
            int[] toTarget = { 8, 16, 40, 60, 100 };
            for (float load = 0f; load <= 1f; load += 0.05f)
            {
                bool gate = OpportunisticUnloadPolicy.ShouldAttemptDivert(load, true, 3, true);
                if (load < OpportunisticUnloadPolicy.MinLoadFraction)
                {
                    // Below the minimum: the gate rejects, AND so must the full math for every geometry.
                    Assert.That(gate, Is.False, $"gate must reject sub-threshold load {load}");
                    foreach (int t in toTarget)
                    {
                        int onWayStorage = t / 2, onWayRest = t - onWayStorage; // storage exactly on the line
                        Assert.That(OpportunisticUnloadPolicy.ShouldUnloadOnWay(t, onWayStorage, onWayRest, load),
                            Is.False, "full on-way decision must also reject a sub-threshold load");
                        Assert.That(OpportunisticUnloadPolicy.ShouldUnloadOnRunEnd(t, onWayStorage, onWayRest, load),
                            Is.False, "full run-end decision must also reject a sub-threshold load");
                    }
                }
                else
                {
                    // At/above the minimum (off cooldown, with capacity + tracked items) the gate must NOT block —
                    // it defers to the full math, which is what actually decides.
                    Assert.That(gate, Is.True, $"gate must not block a worthwhile load {load}");
                }
            }
        }
    }
}
