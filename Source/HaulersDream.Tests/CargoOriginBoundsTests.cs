using System;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CargoOriginBoundsTests
    {
        [Test]
        public void EverySmallPhysicalPartition_HasExactlyTheFeasibleMarginalBounds()
        {
            // The oracle enumerates every physically possible origin allocation. It does not
            // assume FIFO/LIFO provenance for fungible units or reuse the interval formula.
            for (int total = 0; total <= 10; total++)
            for (int low = 0; low <= total; low++)
            for (int high = low; high <= total; high++)
            for (int moved = 0; moved <= total; moved++)
            {
                int takeMin = int.MaxValue, takeMax = -1, leftMin = int.MaxValue, leftMax = -1;
                for (int origin = low; origin <= high; origin++)
                for (int takenOrigin = 0; takenOrigin <= origin; takenOrigin++)
                {
                    int takenOther = moved - takenOrigin;
                    if (takenOther < 0 || takenOther > total - origin) continue;
                    takeMin = Math.Min(takeMin, takenOrigin);
                    takeMax = Math.Max(takeMax, takenOrigin);
                    leftMin = Math.Min(leftMin, origin - takenOrigin);
                    leftMax = Math.Max(leftMax, origin - takenOrigin);
                }
                new CargoOriginBounds(total, low, high).Partition(moved, out var taken, out var remaining);
                Assert.That(new[] { taken.Total, taken.Minimum, taken.Maximum, remaining.Total, remaining.Minimum, remaining.Maximum },
                    Is.EqualTo(new long[] { moved, takeMin, takeMax, total - moved, leftMin, leftMax }),
                    $"total={total}, origin=[{low},{high}], moved={moved}");
            }
        }

        [Test]
        public void MixedPartialReturn_CannotProveThatEveryOriginalUnitReturned()
        {
            var originalAndUnrelated = new CargoOriginBounds(10, 10, 10)
                .MergeDisjoint(new CargoOriginBounds(5, 0, 0));
            originalAndUnrelated.Partition(10, out var returned, out var held);
            Assert.That(new[] { returned.Minimum, returned.Maximum, held.Minimum, held.Maximum },
                Is.EqualTo(new long[] { 5, 10, 0, 5 }));
            Assert.That(returned.IsExact, Is.False);
        }

        [Test]
        public void ClosedMixtureReturn_RecoversItsExactAggregateWithoutInventingUnitLabels()
        {
            var mixture = new CargoOriginBounds(20, 10, 10);
            mixture.Partition(10, out var first, out var second);
            var marginals = first.MergeDisjoint(second);
            Assert.That(new[] { marginals.Minimum, marginals.Maximum }, Is.EqualTo(new long[] { 0, 20 }));
            var closed = marginals.ConstrainByOriginTotal(10, 0, 0);
            Assert.That(new[] { closed.Minimum, closed.Maximum }, Is.EqualTo(new long[] { 10, 10 }));
        }

        [Test]
        public void PartialConsumption_RetainsTheSinkInsteadOfAssumingEveryOriginUnitSurvived()
        {
            new CargoOriginBounds(15, 10, 10).Partition(5, out var consumed, out var live);
            var constrained = live.ConstrainByOriginTotal(10, consumed.Minimum, consumed.Maximum);
            Assert.That(new[] { constrained.Minimum, constrained.Maximum }, Is.EqualTo(new long[] { 5, 10 }));
            // Claiming that the ten live units all have the original origin would lose a feasible
            // history in which five original units were consumed and five unrelated units survived.
            Assert.That(constrained.IsExact, Is.False);
        }

        [Test]
        public void EverySmallConservationConstraint_MatchesFeasibleIntegerAssignments()
        {
            for (int low = 0; low <= 5; low++)
            for (int high = low; high <= 5; high++)
            for (int otherLow = 0; otherLow <= 5; otherLow++)
            for (int otherHigh = otherLow; otherHigh <= 5; otherHigh++)
            for (int totalOrigin = 0; totalOrigin <= 10; totalOrigin++)
            {
                int expectedMin = int.MaxValue, expectedMax = -1;
                for (int here = low; here <= high; here++)
                for (int elsewhere = otherLow; elsewhere <= otherHigh; elsewhere++)
                    if (here + elsewhere == totalOrigin)
                    {
                        expectedMin = Math.Min(expectedMin, here);
                        expectedMax = Math.Max(expectedMax, here);
                    }
                var input = new CargoOriginBounds(5, low, high);
                if (expectedMax < 0)
                    Assert.Throws<ArgumentException>(() => input.ConstrainByOriginTotal(totalOrigin, otherLow, otherHigh));
                else
                {
                    var constrained = input.ConstrainByOriginTotal(totalOrigin, otherLow, otherHigh);
                    Assert.That(new[] { constrained.Minimum, constrained.Maximum },
                        Is.EqualTo(new long[] { expectedMin, expectedMax }));
                }
            }
        }

        [Test]
        public void InvalidPhysicalCountsAndContradictoryEvidenceAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CargoOriginBounds(-1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CargoOriginBounds(5, -1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CargoOriginBounds(5, 4, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CargoOriginBounds(5, 0, 6));
            var input = new CargoOriginBounds(5, 2, 4);
            Assert.Throws<ArgumentOutOfRangeException>(() => input.Partition(-1, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => input.Partition(6, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => input.ConstrainByOriginTotal(-1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => input.ConstrainByOriginTotal(2, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => input.ConstrainByOriginTotal(2, 3, 2));
            Assert.Throws<ArgumentException>(() => input.ConstrainByOriginTotal(1, 0, 0));
            Assert.Throws<ArgumentException>(() => input.ConstrainByOriginTotal(8, 0, 3));
        }

        [Test]
        public void ExtremeQuantities_DoNotWrapIntoAFalseOriginClaim()
        {
            var input = new CargoOriginBounds(long.MaxValue, long.MaxValue - 1, long.MaxValue);
            input.Partition(long.MaxValue - 1, out var taken, out var remaining);
            Assert.That(new[] { taken.Minimum, taken.Maximum, remaining.Minimum, remaining.Maximum },
                Is.EqualTo(new[] { long.MaxValue - 2, long.MaxValue - 1, 0L, 1L }));
            Assert.Throws<OverflowException>(() => input.MergeDisjoint(new CargoOriginBounds(1, 0, 1)));
            Assert.Throws<ArgumentException>(() => input.ConstrainByOriginTotal(0, long.MaxValue, long.MaxValue));
        }

        [Test]
        public void DefaultValueIsAnExactEmptyFragment()
        {
            var empty = default(CargoOriginBounds);
            empty.Partition(0, out var taken, out var remaining);
            Assert.That(new[] { taken.Total, taken.Minimum, taken.Maximum, remaining.Total }, Is.EqualTo(new long[4]));
            Assert.That(empty.IsExact, Is.True);
        }
    }
}
