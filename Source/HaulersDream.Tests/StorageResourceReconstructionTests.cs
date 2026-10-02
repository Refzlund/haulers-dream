using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    public class StorageResourceReconstructionTests
    {
        private static StorageAllocationRequest Knife(IEnumerable<string> cells)
            => new StorageAllocationRequest(new object(), new object(), new object(), 1, 1, cells);

        private static StorageAllocationResult Allocate(StorageAllocationCell[] cells,
            StorageAllocationState baseline, params StorageAllocationRequest[] requests)
            => StorageResourceAllocator.Allocate(cells, baseline, requests, (a, b) => false, s => true);

        [Test]
        public void EveryExactCandidateFitsBesideTwentyThreeFlexibleUnstackableParcels()
        {
            // Native admission observes each searched candidate first. It is therefore always
            // the first temporary vacancy spent by the old two-stage reconstruction, not just
            // one unfortunate cell in an otherwise successful search.
            var names = Enumerable.Range(0, 24).Select(i => "cell-" + i).ToArray();
            foreach (string candidate in names)
            {
                var cells = new[] { candidate }.Concat(names.Where(c => c != candidate))
                    .Select(c => new StorageAllocationCell(c, 1)).ToArray();
                var prior = Enumerable.Range(0, 23).Select(i => Knife(names)).ToArray();
                var incoming = Knife(new[] { candidate });
                var before = Allocate(cells, StorageAllocationState.Empty, prior);
                var frozen = Allocate(cells, before.State, incoming);
                Assert.That(before.Status, Is.EqualTo(StorageAllocationStatus.Complete));
                Assert.That(frozen.AdmittedUnits(incoming), Is.Zero,
                    "Reproduces the native adapter's prematurely frozen scratch placement.");

                var together = Allocate(cells, StorageAllocationState.Empty, prior.Concat(new[] { incoming }).ToArray());
                Assert.Multiple(() =>
                {
                    Assert.That(together.Status, Is.EqualTo(StorageAllocationStatus.Complete), candidate);
                    Assert.That(together.AdmittedUnits(incoming), Is.EqualTo(1), candidate);
                    Assert.That(prior.All(p => together.AdmittedUnits(p) == 1), Is.True);
                    Assert.That(together.State.Slices.Sum(s => s.Units), Is.EqualTo(24));
                    Assert.That(together.State.Slices.Select(s => s.CellKey).Distinct().Count(), Is.EqualTo(24));
                    Assert.That(together.State.SlicesFor(incoming.Owner, incoming.Parcel).Single().CellKey, Is.EqualTo(candidate));
                    Assert.That(before.State.Slices.Count, Is.EqualTo(23), "The original scratch result stays immutable.");
                    Assert.That(prior.All(p => before.State.UnitsFor(p.Owner, p.Parcel) == 1), Is.True);
                });
            }
        }

        [Test]
        public void ExactPriorDestinationCannotBeTakenByAnotherExactCandidate()
        {
            var names = Enumerable.Range(0, 24).Select(i => "cell-" + i).ToArray();
            var cells = names.Select(c => new StorageAllocationCell(c, 1)).ToArray();
            var exactPrior = Knife(new[] { names[0] });
            var others = Enumerable.Range(0, 22).Select(i => Knife(names)).ToArray();
            var incoming = Knife(new[] { names[0] });
            var result = Allocate(cells, StorageAllocationState.Empty,
                new[] { exactPrior }.Concat(others).Concat(new[] { incoming }).ToArray());
            Assert.Multiple(() =>
            {
                Assert.That(result.AdmittedUnits(exactPrior), Is.EqualTo(1));
                Assert.That(result.State.SlicesFor(exactPrior.Owner, exactPrior.Parcel).Single().CellKey, Is.EqualTo(names[0]));
                Assert.That(others.All(p => result.AdmittedUnits(p) == 1), Is.True);
                Assert.That(result.AdmittedUnits(incoming), Is.Zero);
                Assert.That(result.State.Slices.Sum(s => s.Units), Is.EqualTo(23));
            });
        }

        [Test]
        public void RealFixedBaselineCannotMoveEvenWhenAnotherCellIsVacant()
        {
            var names = Enumerable.Range(0, 24).Select(i => "cell-" + i).ToArray();
            var cells = names.Select(c => new StorageAllocationCell(c, 1)).ToArray();
            var committed = Knife(names);
            var baseline = Allocate(cells, StorageAllocationState.Empty, committed).State;
            var fixedSlice = baseline.Slices.Single();
            var others = Enumerable.Range(0, 22).Select(i => Knife(names)).ToArray();
            var incoming = Knife(new[] { fixedSlice.CellKey });
            var result = Allocate(cells, baseline, others.Concat(new[] { incoming }).ToArray());
            Assert.Multiple(() =>
            {
                Assert.That(result.AdmittedUnits(incoming), Is.Zero);
                Assert.That(result.State.SlicesFor(committed.Owner, committed.Parcel).Single(), Is.SameAs(fixedSlice));
                Assert.That(others.All(p => result.AdmittedUnits(p) == 1), Is.True);
                Assert.That(result.State.Slices.Sum(s => s.Units), Is.EqualTo(23));
                Assert.That(baseline.Slices.Single(), Is.SameAs(fixedSlice));
            });
        }
    }
}
