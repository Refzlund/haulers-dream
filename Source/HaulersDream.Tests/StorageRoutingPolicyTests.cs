using HaulersDream.Core;
using NUnit.Framework;
using static HaulersDream.Core.StorageRoutingPolicy;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the storage-routing relocation core (<see cref="StorageRoutingPolicy"/>) at WYU's exact gates:
    /// the slot-group priority-eligibility rule (own-group never; strictly-higher always; equal only on the
    /// before-carry path with the toggle on; lower never), the "is the relocation worth it" distance gate,
    /// and the distance/midway ranking. Verified against While You're Up's
    /// <c>StoreUtility.TryFindBestBetterStoreCellFor_MidwayToTarget</c> (StoreUtility.cs:214-266) and
    /// <c>BeforeCarryDetour_Job</c> (BeforeCarryDetour.cs:100-104).
    /// </summary>
    [TestFixture]
    public class StorageRoutingPolicyTests
    {
        // Integer priorities standing in for the StoragePriority enum (the Verse layer casts to int):
        // Unstored=0 < Low=1 < Normal=2 < Preferred=3 < Important=4 < Critical=5. Higher int = higher.
        const int Low = 1, Normal = 2, Preferred = 3;

        // --- PriorityEligibleForRoute: own group ---------------------------------------------------------

        [Test]
        public void OwnGroup_NeverEligible_RegardlessOfPriorityOrFlags()
        {
            // StoreUtility.cs:233 — never relocate within the SAME slot group. Wins over every priority case.
            // Higher-priority own group:
            Assert.That(PriorityEligibleForRoute(Preferred, Normal, beforeCarryActive: true,
                allowEqualPriority: true, isOwnGroup: true), Is.False);
            // Equal-priority own group (before-carry + toggle on — the case that WOULD pass if not own group):
            Assert.That(PriorityEligibleForRoute(Normal, Normal, beforeCarryActive: true,
                allowEqualPriority: true, isOwnGroup: true), Is.False);
            // Opportunity own group:
            Assert.That(PriorityEligibleForRoute(Preferred, Normal, beforeCarryActive: false,
                allowEqualPriority: false, isOwnGroup: true), Is.False);
        }

        // --- PriorityEligibleForRoute: strictly higher --------------------------------------------------

        [Test]
        public void StrictlyHigher_AlwaysEligible_OnBothPaths()
        {
            // A strictly-higher priority destination trips none of WYU's breaks (StoreUtility.cs:218 only
            // breaks on LOWER), so it's eligible in every (path, toggle) combination.
            Assert.That(PriorityEligibleForRoute(Preferred, Normal, beforeCarryActive: false,
                allowEqualPriority: false, isOwnGroup: false), Is.True);
            Assert.That(PriorityEligibleForRoute(Preferred, Normal, beforeCarryActive: false,
                allowEqualPriority: true, isOwnGroup: false), Is.True);
            Assert.That(PriorityEligibleForRoute(Preferred, Normal, beforeCarryActive: true,
                allowEqualPriority: false, isOwnGroup: false), Is.True);
            Assert.That(PriorityEligibleForRoute(Preferred, Normal, beforeCarryActive: true,
                allowEqualPriority: true, isOwnGroup: false), Is.True);
            // Higher by more than one step is likewise fine.
            Assert.That(PriorityEligibleForRoute(Preferred, Low, beforeCarryActive: false,
                allowEqualPriority: false, isOwnGroup: false), Is.True);
        }

        // --- PriorityEligibleForRoute: equal priority ---------------------------------------------------

        [Test]
        public void EqualPriority_BeforeCarryWithToggle_Eligible()
        {
            // StoreUtility.cs:232 — before-carry allows equal priority only when HaulBeforeCarry_ToEqualPriority
            // (HD routeToEqualPriority) is on.
            Assert.That(PriorityEligibleForRoute(Normal, Normal, beforeCarryActive: true,
                allowEqualPriority: true, isOwnGroup: false), Is.True);
        }

        [Test]
        public void EqualPriority_BeforeCarryWithoutToggle_NotEligible()
        {
            // StoreUtility.cs:232 — toggle off -> equal priority breaks out on the before-carry path.
            Assert.That(PriorityEligibleForRoute(Normal, Normal, beforeCarryActive: true,
                allowEqualPriority: false, isOwnGroup: false), Is.False);
        }

        [Test]
        public void EqualPriority_Opportunity_NeverEligible_EvenWithToggle()
        {
            // StoreUtility.cs:220 — an OPPORTUNITY relocation (no before-carry target) ALWAYS breaks out of
            // equal priority; the equal-priority toggle is a before-carry-only allowance, so it does not
            // re-enable the opportunity path.
            Assert.That(PriorityEligibleForRoute(Normal, Normal, beforeCarryActive: false,
                allowEqualPriority: false, isOwnGroup: false), Is.False);
            Assert.That(PriorityEligibleForRoute(Normal, Normal, beforeCarryActive: false,
                allowEqualPriority: true, isOwnGroup: false), Is.False);
        }

        // --- PriorityEligibleForRoute: strictly lower ---------------------------------------------------

        [Test]
        public void StrictlyLower_NeverEligible_OnAnyPathOrToggle()
        {
            // StoreUtility.cs:218 — a strictly-lower priority is broken out unconditionally.
            Assert.That(PriorityEligibleForRoute(Low, Normal, beforeCarryActive: false,
                allowEqualPriority: false, isOwnGroup: false), Is.False);
            Assert.That(PriorityEligibleForRoute(Low, Normal, beforeCarryActive: true,
                allowEqualPriority: true, isOwnGroup: false), Is.False);
            Assert.That(PriorityEligibleForRoute(Normal, Preferred, beforeCarryActive: true,
                allowEqualPriority: true, isOwnGroup: false), Is.False);
        }

        // --- PriorityEligibleForRoute: boundaries -------------------------------------------------------

        [Test]
        public void Boundaries_AdjacentSteps()
        {
            // One step above current is "strictly higher" -> eligible everywhere.
            Assert.That(PriorityEligibleForRoute(Normal, Low, beforeCarryActive: false,
                allowEqualPriority: false, isOwnGroup: false), Is.True);
            // One step below current is "strictly lower" -> never.
            Assert.That(PriorityEligibleForRoute(Low, Normal, beforeCarryActive: true,
                allowEqualPriority: true, isOwnGroup: false), Is.False);
            // Exactly equal pivots on (path, toggle) only.
            Assert.That(PriorityEligibleForRoute(Normal, Normal, beforeCarryActive: true,
                allowEqualPriority: true, isOwnGroup: false), Is.True);
            Assert.That(PriorityEligibleForRoute(Normal, Normal, beforeCarryActive: true,
                allowEqualPriority: false, isOwnGroup: false), Is.False);
        }

        // --- WorthRelocating ----------------------------------------------------------------------------

        [Test]
        public void WorthRelocating_StrictlyCloserStore_Yes()
        {
            // BeforeCarryDetour.cs:104 — relocate only when the store is strictly closer to the target than
            // the current position (fromStoreSquared < fromHereSquared).
            Assert.That(WorthRelocating(currentToTargetDistSquared: 100, storeToTargetDistSquared: 49), Is.True);
        }

        [Test]
        public void WorthRelocating_EqualOrFarther_No()
        {
            // Strict <: equal distance buys nothing (and avoids churn between equidistant stores).
            Assert.That(WorthRelocating(currentToTargetDistSquared: 100, storeToTargetDistSquared: 100), Is.False);
            // Farther store: never.
            Assert.That(WorthRelocating(currentToTargetDistSquared: 49, storeToTargetDistSquared: 100), Is.False);
        }

        [Test]
        public void WorthRelocating_OneSquaredTileCloser_Boundary()
        {
            Assert.That(WorthRelocating(100, 99), Is.True);  // strictly closer by one squared tile
            Assert.That(WorthRelocating(100, 101), Is.False); // strictly farther
        }

        // --- Distance / midway ranking ------------------------------------------------------------------

        [Test]
        public void CompareByDestinationDistance_StrictNearerWins()
        {
            Assert.That(CompareByDestinationDistance(distSqA: 10, distSqB: 20), Is.True);
            Assert.That(CompareByDestinationDistance(distSqA: 20, distSqB: 10), Is.False);
            // Equal -> NOT "A before B" (strict order: first-among-equals stays, stable).
            Assert.That(CompareByDestinationDistance(distSqA: 15, distSqB: 15), Is.False);
        }

        [Test]
        public void MidwayBetter_DelegatesToSharedEnRouteHelper()
        {
            // Strictly closer to the midway replaces the best; equal does not (stable, first-among-equals).
            Assert.That(MidwayBetter(candidateMidwayDistSquared: 4, bestMidwayDistSquared: 9), Is.True);
            Assert.That(MidwayBetter(candidateMidwayDistSquared: 9, bestMidwayDistSquared: 9), Is.False);
            Assert.That(MidwayBetter(candidateMidwayDistSquared: 16, bestMidwayDistSquared: 9), Is.False);
            // It IS the shared en-route midway comparison (one ranking for both features), proven by
            // identity over a range of inputs.
            for (int c = 0; c < 30; c++)
                for (int b = 0; b < 30; b++)
                    Assert.That(MidwayBetter(c, b),
                        Is.EqualTo(EnRoutePickupPolicy.MidwayBetter(c, b)),
                        $"storage-routing midway ranking must equal the shared en-route ranking ({c},{b})");
        }

        [Test]
        public void MidwayRanking_UsesSharedMidwayMath_NoDivergentDuplicate()
        {
            // Sanity: the candidate cell's midway distance the Verse layer feeds is the SAME midway the
            // en-route policy computes (StoreUtility.cs:249 == EnRoutePickupPolicy.Midway). thing (2,4),
            // job (8,12) -> midway (5, 8). A cell exactly on the midway ranks 0 (best possible).
            EnRoutePickupPolicy.Midway(2, 0, 4, 8, 0, 12, out int mx, out _, out int mz);
            Assert.That(mx, Is.EqualTo(5));
            Assert.That(mz, Is.EqualTo(8));
            int onMidway = EnRoutePickupPolicy.MidwayDistanceSquared(5, 8, mx, mz);
            int offMidway = EnRoutePickupPolicy.MidwayDistanceSquared(7, 8, mx, mz);
            Assert.That(MidwayBetter(onMidway, offMidway), Is.True);
        }
    }
}
