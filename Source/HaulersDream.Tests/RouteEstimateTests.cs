using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class RouteEstimateTests
    {
        [Test]
        public void NoLimit_KeepsAllStops()
        {
            var legs = new[] { 5f, 10f, 20f, 40f };
            Assert.That(RouteEstimate.StopsWithinBudget(legs, 0f), Is.EqualTo(4));
            Assert.That(RouteEstimate.StopsWithinBudget(legs, -1f), Is.EqualTo(4));
            Assert.That(RouteEstimate.StopsWithinBudget(legs, float.PositiveInfinity), Is.EqualTo(4));
        }

        [Test]
        public void Budget_TrimsToCumulativeCeiling()
        {
            var legs = new[] { 5f, 10f, 20f, 40f }; // cumulative 5,15,35,75
            Assert.That(RouteEstimate.StopsWithinBudget(legs, 35f), Is.EqualTo(3));  // exactly fits 3
            Assert.That(RouteEstimate.StopsWithinBudget(legs, 34f), Is.EqualTo(2));  // 3rd would exceed
            Assert.That(RouteEstimate.StopsWithinBudget(legs, 100f), Is.EqualTo(4)); // all fit
        }

        [Test]
        public void StopsWithinBudget_IsMonotonicWhenExtendingLegs()
        {
            // RoutePlanner feeds a prefix-stable (nearest-first) leg order, so extending the leg list with more
            // (farther) stops must NEVER reduce how many fit a fixed budget. This is the pure property that
            // keeps a planned route from shrinking when the player drags the "stops" slider up.
            var legs8 = new[] { 10f, 12f, 8f, 15f, 9f, 11f, 7f, 13f };
            var legs11 = new[] { 10f, 12f, 8f, 15f, 9f, 11f, 7f, 13f, 20f, 18f, 25f }; // identical prefix + 3 more
            for (float budget = 0f; budget <= 200f; budget += 1f)
                Assert.That(RouteEstimate.StopsWithinBudget(legs11, budget),
                    Is.GreaterThanOrEqualTo(RouteEstimate.StopsWithinBudget(legs8, budget)),
                    $"budget {budget}: extending the leg list reduced the kept count");
        }

        [Test]
        public void FirstLegOverBudget_KeepsNone()
        {
            var legs = new[] { 50f, 10f };
            Assert.That(RouteEstimate.StopsWithinBudget(legs, 30f), Is.EqualTo(0));
        }

        [Test]
        public void EmptyOrNull_KeepsNone()
        {
            Assert.That(RouteEstimate.StopsWithinBudget(new float[0], 100f), Is.EqualTo(0));
            Assert.That(RouteEstimate.StopsWithinBudget(null, 100f), Is.EqualTo(0));
        }

        [Test]
        public void NegativeLegTreatedAsZero()
        {
            var legs = new[] { -5f, 10f, 10f }; // treat -5 as 0 → cumulative 0,10,20
            Assert.That(RouteEstimate.StopsWithinBudget(legs, 15f), Is.EqualTo(2));
        }

        [Test]
        public void TotalTicks_SumsWalkWorkAndReturn()
        {
            var walk = new[] { 100f, 200f, 300f };
            var work = new[] { 50f, 60f, 70f };
            // keep 2: walk 100+200=300, work 50+60=110, return 25 → 435
            Assert.That(RouteEstimate.TotalTicks(walk, work, keptStops: 2, returnLegTicks: 25f), Is.EqualTo(435f));
        }

        [Test]
        public void TotalTicks_ClampsKeptToArrayLength_AndIgnoresNegatives()
        {
            var walk = new[] { 100f, -5f };
            var work = new[] { 50f };
            // keep 5 (clamped): walk 100 + 0(neg) , work 50, no return → 150
            Assert.That(RouteEstimate.TotalTicks(walk, work, keptStops: 5, returnLegTicks: -3f), Is.EqualTo(150f));
        }

        [Test]
        public void HoursFromTicks()
        {
            Assert.That(RouteEstimate.HoursFromTicks(2500f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(RouteEstimate.HoursFromTicks(1250f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(RouteEstimate.HoursFromTicks(0f), Is.EqualTo(0f));
            Assert.That(RouteEstimate.HoursFromTicks(-100f), Is.EqualTo(0f));
        }

        [Test]
        public void PlantWorkTicks_FullyGrownIsSlowestCase()
        {
            // growth=1 → rate = speed·1 → ticks = harvestWork/speed. Default harvestWork 10, speed 1 → 10 ticks.
            Assert.That(RouteEstimate.PlantWorkTicks(10f, 1f, 1f), Is.EqualTo(10f).Within(1e-3f));
            // growth=0 → rate = speed·3.3 → 3.3× faster.
            Assert.That(RouteEstimate.PlantWorkTicks(10f, 1f, 0f), Is.EqualTo(10f / 3.3f).Within(1e-3f));
            // immature plant is faster than mature.
            Assert.That(RouteEstimate.PlantWorkTicks(10f, 1f, 0.2f),
                Is.LessThan(RouteEstimate.PlantWorkTicks(10f, 1f, 1f)));
        }

        [Test]
        public void PlantWorkTicks_DegradesOnZeroInputs()
        {
            Assert.That(RouteEstimate.PlantWorkTicks(10f, 0f, 1f), Is.EqualTo(0f));   // no speed
            Assert.That(RouteEstimate.PlantWorkTicks(0f, 1f, 1f), Is.EqualTo(0f));    // no work
            Assert.That(RouteEstimate.PlantWorkTicks(10f, 1f, 5f), Is.EqualTo(10f).Within(1e-3f)); // growth clamps to 1
        }

        [Test]
        public void MineWorkTicks_NaturalRockVsOther()
        {
            // natural rock: 100 HP, 80 dmg/hit, speed 1 → 100*100/(80*1) = 125 ticks.
            Assert.That(RouteEstimate.MineWorkTicks(100, true, 1f), Is.EqualTo(125f).Within(1e-3f));
            // other mineable: 40 dmg/hit → 100*100/(40*1) = 250 ticks (slower per HP).
            Assert.That(RouteEstimate.MineWorkTicks(100, false, 1f), Is.EqualTo(250f).Within(1e-3f));
            // faster mining speed → fewer ticks.
            Assert.That(RouteEstimate.MineWorkTicks(100, true, 2f), Is.EqualTo(62.5f).Within(1e-3f));
        }

        [Test]
        public void MineWorkTicks_DegradesOnZeroInputs()
        {
            Assert.That(RouteEstimate.MineWorkTicks(100, true, 0f), Is.EqualTo(0f));
            Assert.That(RouteEstimate.MineWorkTicks(0, true, 1f), Is.EqualTo(0f));
        }
    }
}
