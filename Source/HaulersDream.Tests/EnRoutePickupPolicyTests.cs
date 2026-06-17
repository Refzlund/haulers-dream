using HaulersDream.Core;
using NUnit.Framework;
using static HaulersDream.Core.EnRoutePickupPolicy;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the en-route pickup cascade (<see cref="EnRoutePickupPolicy"/>) at the WYU default thresholds,
    /// including the squared-range / leg-ratio boundaries, the two-phase short-circuit ordering, the
    /// path-cost leg bounds, and the midway store-cell ranking. Ported math is checked against the verbatim
    /// WYU constants (OpportunityDetour.cs / Settings.cs).
    /// </summary>
    [TestFixture]
    public class EnRoutePickupPolicyTests
    {
        // A fresh band at the WYU defaults: StartToThing 30, StartToThingPct 0.5, StoreToJob 50,
        // StoreToJobPct 0.6.
        static MaxRanges Defaults()
        {
            var r = new MaxRanges();
            r.Reset();
            return r;
        }

        // --- WYU default thresholds are exactly the extracted values ------------------------------------

        [Test]
        public void Defaults_MatchWyuVerbatim()
        {
            Assert.That(DefaultMaxStartToThing, Is.EqualTo(30f));
            Assert.That(DefaultMaxStartToThingPctOrigTrip, Is.EqualTo(0.5f));
            Assert.That(DefaultMaxStoreToJob, Is.EqualTo(50f));
            Assert.That(DefaultMaxStoreToJobPctOrigTrip, Is.EqualTo(0.6f));
            Assert.That(DefaultMaxTotalTripPctOrigTrip, Is.EqualTo(1.7f));
            Assert.That(DefaultMaxNewLegsPctOrigTrip, Is.EqualTo(1.0f));
            Assert.That(DefaultMaxStartToThingRegionLookCount, Is.EqualTo(25));
            Assert.That(DefaultMaxStoreToJobRegionLookCount, Is.EqualTo(25));
            Assert.That(DefaultPathChecker, Is.EqualTo(EnRoutePathChecker.Default));
            Assert.That(MaxRanges.HeuristicRangeExpandFactor, Is.EqualTo(2f));
        }

        [Test]
        public void Reset_SeedsTheBandFromSettings()
        {
            var r = Defaults();
            Assert.That(r.ExpandCount, Is.EqualTo(0));
            Assert.That(r.StartToThing, Is.EqualTo(30f));
            Assert.That(r.StartToThingPctOrigTrip, Is.EqualTo(0.5f));
            Assert.That(r.StoreToJob, Is.EqualTo(50f));
            Assert.That(r.StoreToJobPctOrigTrip, Is.EqualTo(0.6f));
        }

        [Test]
        public void Expand_MultipliesEveryRangeAndBumpsCount()
        {
            var r = Defaults();
            r.Expand(); // *2
            Assert.That(r.ExpandCount, Is.EqualTo(1));
            Assert.That(r.StartToThing, Is.EqualTo(60f));
            Assert.That(r.StartToThingPctOrigTrip, Is.EqualTo(1.0f));
            Assert.That(r.StoreToJob, Is.EqualTo(100f));
            Assert.That(r.StoreToJobPctOrigTrip, Is.EqualTo(1.2f));
            r.Expand(3f); // *3 on top
            Assert.That(r.ExpandCount, Is.EqualTo(2));
            Assert.That(r.StartToThing, Is.EqualTo(180f));
        }

        // --- Phase 1 (CheckBeforeStore) ----------------------------------------------------------------

        [Test]
        public void BeforeStore_AcceptsAnOnPathCandidate()
        {
            // pawn->thing 10, pawn->job 40, thing->job 35: 10 <= 30 (range), 10 <= 40*0.5=20 (pct ok),
            // 10+35=45 <= 40*1.7=68 (total-trip pre-bound ok) -> Continue.
            var r = Defaults();
            Assert.That(CheckBeforeStore(10f, 40f, 35f, r), Is.EqualTo(EnRouteResult.Continue));
        }

        [Test]
        public void BeforeStore_StartToThingRange_Boundary()
        {
            var r = Defaults(); // StartToThing cap = 30
            // Use a very long trip so the %-of-trip gate (0.5) can't bind first.
            // 30 exactly: 30^2 (900) > 30^2 (900)? No -> not a range fail on the absolute cap.
            Assert.That(CheckBeforeStore(30f, 200f, 1f, r), Is.Not.EqualTo(EnRouteResult.RangeFail));
            // 30.01: just over -> RangeFail.
            Assert.That(CheckBeforeStore(30.01f, 200f, 1f, r), Is.EqualTo(EnRouteResult.RangeFail));
        }

        [Test]
        public void BeforeStore_StartToThingPctOfTrip_Boundary()
        {
            var r = Defaults(); // pct cap = 0.5
            // pawn->job 40 -> pct allowance = 20 tiles. Keep absolute cap (30) from binding by staying <=20.
            // 20 exactly: 20^2 (400) > 40^2*0.5^2 = 1600*0.25 = 400? No -> ok.
            Assert.That(CheckBeforeStore(20f, 40f, 1f, r), Is.Not.EqualTo(EnRouteResult.RangeFail));
            // 20.01: just over -> RangeFail (the squared %-of-trip gate).
            Assert.That(CheckBeforeStore(20.01f, 40f, 1f, r), Is.EqualTo(EnRouteResult.RangeFail));
        }

        [Test]
        public void BeforeStore_TotalTripPreBound_HardFailBoundary()
        {
            var r = Defaults();
            // pawn->job 20 -> total-trip budget = 20*1.7 = 34. Keep start->thing within range (<=10 here).
            // start->thing + thing->job = 10 + 24 = 34 exactly: 34 > 34? No -> Continue.
            Assert.That(CheckBeforeStore(10f, 20f, 24f, r), Is.EqualTo(EnRouteResult.Continue));
            // 10 + 24.01 = 34.01 > 34 -> HardFail.
            Assert.That(CheckBeforeStore(10f, 20f, 24.01f, r), Is.EqualTo(EnRouteResult.HardFail));
        }

        [Test]
        public void BeforeStore_RangeFailTakesPrecedenceOverHardFail()
        {
            var r = Defaults();
            // start->thing 35 is over the 30 cap (RangeFail) AND the total trip is huge (would HardFail) —
            // WYU evaluates the range gate first, so RangeFail wins.
            Assert.That(CheckBeforeStore(35f, 10f, 100f, r), Is.EqualTo(EnRouteResult.RangeFail));
        }

        // --- Phase 2 (CheckAfterStore) -----------------------------------------------------------------

        [Test]
        public void AfterStore_AcceptsAnOnPathCandidate()
        {
            // pawn->thing 10, pawn->job 40, thing->store 8, store->job 25.
            // store->job 25 <= 50 (abs) and 25^2=625 <= 40^2*0.6^2 = 1600*0.36=576? 625>576 -> RangeFail!
            // Pick store->job 20 instead: 400 <= 576 ok; new legs 10+20=30 <= 40 (40*1.0) ok;
            // total 10+8+20=38 <= 68 ok -> Continue.
            var r = Defaults();
            Assert.That(CheckAfterStore(10f, 40f, 8f, 20f, r), Is.EqualTo(EnRouteResult.Continue));
        }

        [Test]
        public void AfterStore_StoreToJobRange_Boundary()
        {
            var r = Defaults(); // StoreToJob cap = 50
            // Long trip so the 0.6 pct gate can't bind: pawn->job 1000 -> pct allowance huge.
            // 50 exactly: 2500 > 2500? No -> ok.
            Assert.That(CheckAfterStore(1f, 1000f, 1f, 50f, r), Is.Not.EqualTo(EnRouteResult.RangeFail));
            // 50.01: just over -> RangeFail.
            Assert.That(CheckAfterStore(1f, 1000f, 1f, 50.01f, r), Is.EqualTo(EnRouteResult.RangeFail));
        }

        [Test]
        public void AfterStore_StoreToJobPctOfTrip_Boundary()
        {
            var r = Defaults(); // pct cap = 0.6
            // pawn->job 50 -> pct allowance = 30. Keep absolute cap (50) from binding by staying <=30.
            // 30 exactly: 900 > 50^2*0.6^2 = 2500*0.36 = 900? No -> ok.
            Assert.That(CheckAfterStore(1f, 50f, 1f, 30f, r), Is.Not.EqualTo(EnRouteResult.RangeFail));
            // 30.01: just over -> RangeFail.
            Assert.That(CheckAfterStore(1f, 50f, 1f, 30.01f, r), Is.EqualTo(EnRouteResult.RangeFail));
        }

        [Test]
        public void AfterStore_MaxNewLegs_HardFailBoundary()
        {
            var r = Defaults(); // MaxNewLegs pct = 1.0
            // pawn->job 40 -> new-legs budget = 40. new legs = pawn->thing + store->job.
            // Keep ranges from binding: store->job 20 (range ok). pawn->thing 20 -> 20+20=40 == 40 -> ok.
            Assert.That(CheckAfterStore(20f, 40f, 1f, 20f, r), Is.EqualTo(EnRouteResult.Continue));
            // pawn->thing 20.01 -> 40.01 > 40 -> HardFail.
            Assert.That(CheckAfterStore(20.01f, 40f, 1f, 20f, r), Is.EqualTo(EnRouteResult.HardFail));
        }

        [Test]
        public void AfterStore_MaxTotalTrip_HardFailBoundary()
        {
            var r = Defaults(); // MaxTotalTrip pct = 1.7
            // pawn->job 40 -> total budget = 68. Keep new-legs (pawn->thing + store->job) within 40 and
            // ranges within caps. pawn->thing 10, store->job 20 (new legs 30 <= 40 ok, ranges ok).
            // total = 10 + thing->store + 20. thing->store 38 -> total = 68 == 68 -> ok.
            Assert.That(CheckAfterStore(10f, 40f, 38f, 20f, r), Is.EqualTo(EnRouteResult.Continue));
            // thing->store 38.01 -> total 68.01 > 68 -> HardFail.
            Assert.That(CheckAfterStore(10f, 40f, 38.01f, 20f, r), Is.EqualTo(EnRouteResult.HardFail));
        }

        [Test]
        public void AfterStore_RangeFailTakesPrecedenceOverHardFail()
        {
            var r = Defaults();
            // store->job 60 is over the 50 cap (RangeFail) and would also blow MaxNewLegs/MaxTotalTrip —
            // the range gate runs first, so RangeFail wins.
            Assert.That(CheckAfterStore(40f, 10f, 40f, 60f, r), Is.EqualTo(EnRouteResult.RangeFail));
        }

        [Test]
        public void ExpandedBand_AdmitsAFartherCandidate()
        {
            // pawn->thing 45 is past the 30 cap at band 0 (RangeFail) but within 60 after one expand.
            var r = Defaults();
            Assert.That(CheckBeforeStore(45f, 1000f, 1f, r), Is.EqualTo(EnRouteResult.RangeFail));
            r.Expand(); // StartToThing -> 60
            Assert.That(CheckBeforeStore(45f, 1000f, 1f, r), Is.Not.EqualTo(EnRouteResult.RangeFail));
        }

        // --- Path-cost leg bounds (Default / Pathfinding stage) ----------------------------------------

        [Test]
        public void WithinPathLegBounds_AcceptsAnOnPathRoute()
        {
            // pawn->job cost 100. new legs (pawn->thing + store->job) = 30+60 = 90 <= 100 (1.0) ok.
            // total = 30+20+60 = 110 <= 170 (1.7) ok.
            Assert.That(WithinPathLegBounds(30f, 20f, 60f, 100f), Is.True);
        }

        [Test]
        public void WithinPathLegBounds_NewLegsBoundary()
        {
            // pawn->job 100 -> new-legs budget 100. new legs = pawn->thing + store->job.
            Assert.That(WithinPathLegBounds(40f, 5f, 60f, 100f), Is.True);    // 100 == 100 -> ok
            Assert.That(WithinPathLegBounds(40.01f, 5f, 60f, 100f), Is.False); // 100.01 > 100 -> fail
        }

        [Test]
        public void WithinPathLegBounds_TotalTripBoundary()
        {
            // pawn->job 100 -> total budget 170, new-legs budget 100.
            // pawn->thing 30, store->job 60 (new legs 90 <= 100 ok). total = 30+thing->store+60.
            Assert.That(WithinPathLegBounds(30f, 80f, 60f, 100f), Is.True);    // total 170 == 170 -> ok
            Assert.That(WithinPathLegBounds(30f, 80.01f, 60f, 100f), Is.False); // total 170.01 > 170 -> fail
        }

        // --- Midway store-cell ranking -----------------------------------------------------------------

        [Test]
        public void Midway_FloorsLikeWyu()
        {
            // WYU: ((jobX+thingX)/2, jobY, (jobZ+thingZ)/2) with integer division.
            Midway(thingX: 2, thingY: 0, thingZ: 4, jobX: 9, jobY: 7, jobZ: 11, out int mx, out int my, out int mz);
            Assert.That(mx, Is.EqualTo((9 + 2) / 2)); // 5
            Assert.That(my, Is.EqualTo(7));           // job's y
            Assert.That(mz, Is.EqualTo((11 + 4) / 2)); // 7
        }

        [Test]
        public void MidwayDistanceSquared_IsHorizontalSquared()
        {
            // dx=3, dz=4 -> 9+16 = 25 (X/Z only, no Y).
            Assert.That(MidwayDistanceSquared(cellX: 8, cellZ: 9, midX: 5, midZ: 5), Is.EqualTo(25));
        }

        [Test]
        public void MidwayBetter_IsStrictlyCloser()
        {
            Assert.That(MidwayBetter(10, 20), Is.True);  // closer -> better
            Assert.That(MidwayBetter(20, 20), Is.False); // equal -> NOT better (first-seen wins, stable)
            Assert.That(MidwayBetter(30, 20), Is.False); // farther -> not better
        }

        [Test]
        public void MidwayRanking_PicksCellNearestTheMidpoint()
        {
            // thing at (0,0), job at (20,0) -> midway (10,0). Three candidate cells; nearest the midway wins.
            Midway(0, 0, 0, 20, 0, 0, out int mx, out _, out int mz);
            (int x, int z)[] cells = { (3, 0), (11, 0), (18, 0) };
            int bestDist = int.MaxValue, bestIdx = -1;
            for (int i = 0; i < cells.Length; i++)
            {
                int d = MidwayDistanceSquared(cells[i].x, cells[i].z, mx, mz);
                if (MidwayBetter(d, bestDist)) { bestDist = d; bestIdx = i; }
            }
            Assert.That(bestIdx, Is.EqualTo(1)); // (11,0) is 1 tile from the midway (10,0)
        }
    }
}
