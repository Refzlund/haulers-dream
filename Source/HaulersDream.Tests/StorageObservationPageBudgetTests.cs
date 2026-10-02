using System;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    public class StorageObservationPageBudgetTests
    {
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(24)]
        public void BoundedFreshPageLetsHeldCargoMakeProgressWithoutCertifyingTheWholeStockpile(int parcels)
        {
            var budget = new StorageObservationPageBudget(8192, parcels, 1);
            int cells = 0;
            while (cells < 200 && budget.TryInclude(0, cells + 1)) cells++;
            Assert.That(cells, Is.InRange(1, 199));

            var coordinates = Enumerable.Range(0, cells).Select(i => "cell-" + i).ToArray();
            var physical = coordinates.Select(c => new StorageAllocationCell(c, 1)).ToArray();
            var requests = Enumerable.Range(0, parcels).Select(i => new StorageAllocationRequest(
                new object(), new object(), new object(), 1, 1, coordinates)).ToArray();
            var allocation = StorageResourceAllocator.Allocate(physical, StorageAllocationState.Empty,
                requests, (a, b) => false, s => true,
                new StorageAllocationOptions(observationComplete: false, maximumRequests: 512));
            Assert.That(allocation.CanPublish, Is.True);
            Assert.That(allocation.AdmittedUnits(requests[0]), Is.EqualTo(1),
                "Existing held delivery can drain its deterministic physical slice.");
            Assert.That(allocation.State.Slices.Sum(s => s.Units), Is.EqualTo(Math.Min(cells, parcels)));
            Assert.That(allocation.State.Slices.Select(s => s.CellKey).Distinct().Count(),
                Is.EqualTo(allocation.State.Slices.Count));
            // Actual native positive-path cost fits both initial and publication checks;
            // choosing this page does not raise the original hard predicate limit.
            Assert.That(5L * parcels + 2 + cells * (16L + 10L * parcels) + cells,
                Is.LessThanOrEqualTo(8192));
        }

        [Test]
        public void SixtyFourCompatibleHeldParcelsFitWithinTheOriginalMatchingAndPredicateLimits()
        {
            const int parcels = 64;
            var budget = new StorageObservationPageBudget(8192, parcels, 1);
            int cells = 0;
            while (cells < 200 && budget.TryInclude(0, cells + 1)) cells++;
            var coordinates = Enumerable.Range(0, cells).Select(i => "cell-" + i).ToArray();
            var requests = Enumerable.Range(0, parcels).Select(i => new StorageAllocationRequest(
                new object(), new object(), new object(), 1, 75, coordinates)).ToArray();
            var result = StorageResourceAllocator.Allocate(
                coordinates.Select(c => new StorageAllocationCell(c, 1)).ToArray(),
                StorageAllocationState.Empty, requests, (a, b) => true, s => true,
                new StorageAllocationOptions(observationComplete: false, maximumRequests: 512));
            Assert.That(cells, Is.InRange(1, 199));
            Assert.That(result.CanPublish, Is.True);
            Assert.That(requests.All(r => result.AdmittedUnits(r) == 1), Is.True);
            Assert.That(result.State.Slices.Sum(s => s.Units), Is.EqualTo(parcels));
            Assert.That(result.Work, Is.LessThanOrEqualTo(100000));
        }

        [Test]
        public void ExtremeDemandStillCannotPublishAMatchingBudgetExhaustion()
        {
            var budget = new StorageObservationPageBudget(8192, 512, 1);
            Assert.That(budget.TryInclude(0, 1), Is.True);
            var requests = Enumerable.Range(0, 512).Select(i => new StorageAllocationRequest(
                new object(), new object(), new object(), 1, 75, new[] { "cell" })).ToArray();
            var result = StorageResourceAllocator.Allocate(new[] { new StorageAllocationCell("cell", 1) },
                StorageAllocationState.Empty, requests, (a, b) => true, s => true,
                new StorageAllocationOptions(observationComplete: false, maximumRequests: 512));
            Assert.That(result.Status, Is.EqualTo(StorageAllocationStatus.BudgetExhausted));
            Assert.That(result.CanPublish, Is.False);
            Assert.That(result.State, Is.SameAs(StorageAllocationState.Empty));
        }

        [Test]
        public void HeavyCellDoesNotSpendTheAllowanceForALaterLightCell()
        {
            var budget = new StorageObservationPageBudget(8192, 8, 1);
            Assert.That(budget.TryInclude(1000, 1), Is.False);
            Assert.That(budget.TryInclude(0, 2), Is.True);
        }

        [Test]
        public void RawResidentCountAndAlreadyChargedTopologyCannotBeIgnored()
        {
            var budget = new StorageObservationPageBudget(8192, 8, 1);
            Assert.That(budget.TryInclude(0, 8192), Is.False);
            Assert.That(budget.TryInclude(1000, 0), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => budget.TryInclude(-1, 0));
        }
    }
}
