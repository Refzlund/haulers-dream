using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    public class DirectedLoadCargoBudgetTests
    {
        private static DirectedLoadCargoBudget<object, object> Budget(object def, int claim, int needed, long other = 0)
            => new DirectedLoadCargoBudget<object, object>(new Dictionary<object, int> { [def] = claim },
                new Dictionary<object, int> { [def] = needed }, new Dictionary<object, long> { [def] = other });

        [Test]
        public void CapturedMixedShelfKeepsNativeTwoAfterDirectedPayloadIsRemoved()
        {
            // MP24's observed 180-cell exclusivity leaves only the three native slots at181.
            // This discriminates the duplicate ownership's capacity effect, not the unlogged
            // native Deferred branch. All incoming ordinary/native quantities stay unchanged.
            var wood = new object(); var cloth = new object(); var gold = new object();
            var uranium = new object(); var steel = new object();
            StorageAllocationRequest Request(object subject, int units)
                => new StorageAllocationRequest(new object(), new object(), subject, units, 75, new[] { "181" });
            var ordinary = new[] { Request(wood, 8), Request(cloth, 17), Request(cloth, 19) };
            var native = Request(steel, 2);
            var duplicated = ordinary.Concat(new[] { Request(gold, 7), Request(uranium, 9), native }).ToArray();
            var cell = new[] { new StorageAllocationCell("181", 3) };
            var baseline = StorageResourceAllocator.Allocate(cell, StorageAllocationState.Empty, duplicated,
                ReferenceEquals, slice => true);
            Assert.That(baseline.AdmittedUnits(native), Is.Zero);
            Assert.That(duplicated.Any(r => baseline.AdmittedUnits(r) < r.Units), Is.True);
            Assert.That(Budget(gold, 7, 7).Take(gold, new object(), 7, 7), Is.EqualTo(7));
            Assert.That(Budget(uranium, 9, 9).Take(uranium, new object(), 9, 9), Is.EqualTo(9));
            var retained = ordinary.Concat(new[] { native }).ToArray();
            var candidate = StorageResourceAllocator.Allocate(cell, StorageAllocationState.Empty, retained,
                ReferenceEquals, slice => true);
            Assert.That(retained.All(r => candidate.AdmittedUnits(r) == r.Units), Is.True);
            Assert.That(candidate.AdmittedUnits(native), Is.EqualTo(2));
        }

        [Test]
        public void CompatibleSplitAndMergedParcelsShareOneManifestBudget()
        {
            var def = new object(); var entry = new object(); var budget = Budget(def, 9, 9);
            Assert.That(budget.Take(def, entry, 7, 3), Is.EqualTo(3));
            Assert.That(budget.Take(def, entry, 7, 8), Is.EqualTo(4));
            Assert.That(budget.Take(def, entry, 7, 2), Is.Zero);
        }

        [Test]
        public void TwoCompatibleManifestEntriesSpendOnePawnClaim()
        {
            var def = new object(); var budget = Budget(def, 5, 12);
            Assert.That(budget.Take(def, new object(), 6, 4), Is.EqualTo(4));
            Assert.That(budget.Take(def, new object(), 6, 4), Is.EqualTo(1));
        }

        [Test]
        public void PostKeepSurplusAndManifestBothBoundTheExclusion()
        {
            var def = new object(); var budget = Budget(def, 20, 20); var manifest = new object();
            // Three protected units never enter actualSurplus; the helper cannot spend them.
            Assert.That(budget.Take(def, manifest, 9, 7), Is.EqualTo(7));
            Assert.That(budget.Take(def, manifest, 9, 8), Is.EqualTo(2));
        }

        [Test]
        public void ManifestShrinkNeverSpendsTwoCouriersOverlappingClaims()
        {
            var def = new object(); var manifest = new object();
            int first = Budget(def, 5, 5, 5).Take(def, manifest, 5, 5);
            int second = Budget(def, 5, 5, 5).Take(def, manifest, 5, 5);
            Assert.That(first + second, Is.Zero);
            Assert.That(10 - first - second, Is.EqualTo(10));
            // Still enough remaining manifest for both unchanged claims: each owns only five.
            Assert.That(Budget(def, 5, 10, 5).Take(def, manifest, 10, 5), Is.EqualTo(5));
        }

        [Test]
        public void SameSmallerManifestCannotBeSpentTwiceByDifferentCouriers()
        {
            var def = new object(); var manifest = new object();
            Assert.That(Budget(def, 5, 10, 5).Take(def, manifest, 5, 5), Is.Zero);
            Assert.That(Budget(def, 5, 10, 5).Take(def, manifest, 5, 5), Is.Zero);
        }

        [Test]
        public void UnknownPeerVariantConservativelyRetainsCompatibleCargo()
        {
            var def = new object(); var variantA = new object(); var variantB = new object();
            Assert.That(Budget(def, 5, 10, 5).Take(def, variantA, 5, 5), Is.Zero);
            Assert.That(Budget(def, 5, 10, 5).Take(def, variantB, 5, 5), Is.Zero);
        }

        [TestCase(0, 7, 7, 7)]
        [TestCase(7, 0, 7, 7)]
        [TestCase(7, 7, 0, 7)]
        [TestCase(7, 7, 7, 0)]
        public void MissingClaimDemandManifestOrSurplusRetainsCargo(int claim, int needed, int manifest, int surplus)
        {
            var def = new object();
            Assert.That(Budget(def, claim, needed).Take(def, new object(), manifest, surplus), Is.Zero);
        }

        [Test]
        public void UnboundParcelAndDifferentDefinitionCannotUseAClaim()
        {
            var def = new object(); var budget = Budget(def, 7, 7);
            Assert.That(budget.Take(def, null, 7, 7), Is.Zero);
            Assert.That(budget.Take(new object(), new object(), 7, 7), Is.Zero);
        }

        [Test]
        public void ClaimSnapshotsAndFreshReleasedPassAreIndependent()
        {
            var def = new object(); var claims = new Dictionary<object, int> { [def] = 7 };
            var needed = new Dictionary<object, int> { [def] = 7 };
            var others = new Dictionary<object, long>();
            var budget = new DirectedLoadCargoBudget<object, object>(claims, needed, others);
            Assert.That(budget.Take(def, new object(), 7, 7), Is.EqualTo(7));
            Assert.That(claims[def], Is.EqualTo(7)); Assert.That(needed[def], Is.EqualTo(7));
            Assert.That(others, Is.Empty);
            claims.Clear();
            var released = new DirectedLoadCargoBudget<object, object>(claims, needed, others);
            Assert.That(released.Take(def, new object(), 7, 7), Is.Zero);
        }

        [Test]
        public void LargeForeignClaimsDoNotOverflowIntoAnExclusion()
        {
            var def = new object();
            Assert.That(Budget(def, int.MaxValue, int.MaxValue, long.MaxValue)
                .Take(def, new object(), int.MaxValue, int.MaxValue), Is.Zero);
        }
    }
}
