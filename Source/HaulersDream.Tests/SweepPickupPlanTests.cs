using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class SweepPickupPlanTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void RetiringCurrentOrFuturePickupPreservesEveryOtherSavedSlot(int handedOff)
        {
            var targets = new List<string> { "clicked", "steel", "wood" };
            var counts = new List<int> { 17, 23, 31 };
            var beforeTargets = targets.ToArray();
            var beforeCounts = counts.ToArray();
            Assert.That(SweepPickupPlan.Retire(targets, counts, targets[handedOff], null), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(targets.Count, Is.EqualTo(3));
                Assert.That(counts.Count, Is.EqualTo(3));
                for (int slot = 0; slot < 3; slot++)
                {
                    Assert.That(targets[slot], Is.EqualTo(slot == handedOff ? null : beforeTargets[slot]));
                    Assert.That(counts[slot], Is.EqualTo(slot == handedOff ? 0 : beforeCounts[slot]));
                }
                Assert.That(counts.Sum() + beforeCounts[handedOff], Is.EqualTo(beforeCounts.Sum()));
            });
        }

        [Test]
        public void RetiredAnchorCannotPromoteAnExtraToSlotZero()
        {
            var targets = new List<string> { "clicked forbidden stack", "unrelated forbidden extra" };
            var counts = new List<int> { 10, 20 };
            SweepPickupPlan.Retire(targets, counts, targets[0], null);
            Assert.That(targets[0], Is.Null);
            Assert.That(targets[1], Is.EqualTo("unrelated forbidden extra"));
            Assert.That(counts, Is.EqualTo(new[] { 0, 20 }));
        }

        [Test]
        public void DuplicateReferencesRetireTogetherAndRepeatedHandoffIsANoOp()
        {
            var targets = new List<string> { "steel", "wood", "steel", "cloth" };
            var counts = new List<int> { 5, 12, 8, 19 };
            Assert.That(SweepPickupPlan.Retire(targets, counts, "steel", null), Is.True);
            var firstTargets = targets.ToArray();
            var firstCounts = counts.ToArray();
            Assert.That(SweepPickupPlan.Retire(targets, counts, "steel", null), Is.False);
            Assert.That(targets, Is.EqualTo(firstTargets));
            Assert.That(counts, Is.EqualTo(firstCounts));
            Assert.That(targets, Is.EqualTo(new[] { null, "wood", null, "cloth" }));
            Assert.That(counts, Is.EqualTo(new[] { 0, 12, 0, 19 }));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void MalformedCountsCannotPartlyRetireAPlan(int countLength)
        {
            var targets = new List<string> { "steel", "wood" };
            var counts = Enumerable.Repeat(9, countLength).ToList();
            var originalCounts = counts.ToArray();
            Assert.That(SweepPickupPlan.Retire(targets, counts, "steel", null), Is.False);
            Assert.That(targets, Is.EqualTo(new[] { "steel", "wood" }));
            Assert.That(counts, Is.EqualTo(originalCounts));
        }

        [Test]
        public void MissingOrAlreadyInvalidSourceCannotAlterOtherCounts()
        {
            var targets = new List<string> { null, "wood" };
            var counts = new List<int> { 0, 11 };
            Assert.That(SweepPickupPlan.Retire(targets, counts, "steel", null), Is.False);
            Assert.That(SweepPickupPlan.Retire(targets, counts, null, null), Is.False);
            Assert.That(SweepPickupPlan.Retire<string>(null, counts, "steel", null), Is.False);
            Assert.That(SweepPickupPlan.Retire(targets, null, "wood", null), Is.False);
            Assert.That(targets, Is.EqualTo(new[] { null, "wood" }));
            Assert.That(counts, Is.EqualTo(new[] { 0, 11 }));
        }

        [Test]
        public void HandedOffPickupReleasesOnlyPlannedCapacityAndKeepsHeldSurplusProtected()
        {
            var alice = new object();
            var bob = new object();
            var shelf = new object();
            var steel = new object();
            var targets = new List<string> { "contested", "remaining" };
            var counts = new List<int> { 25, 18 };
            const int held = 17, kept = 7, surplus = held - kept;
            var rows = StorageClaimLedger.Add(StorageClaimLedger.Empty, alice, shelf, steel, surplus + counts.Sum());
            rows = StorageClaimLedger.Add(rows, bob, shelf, steel, 25);
            StorageClaimEvidence evidence = (pawn, def) => ReferenceEquals(pawn, alice) ? surplus + counts.Sum() : 25;
            var originalRows = rows;
            Assert.That(StorageClaimLedger.ClaimedByPawn(rows, shelf, steel, alice, evidence), Is.EqualTo(53));

            SweepPickupPlan.Retire(targets, counts, "contested", null);
            Assert.That(StorageClaimLedger.ClaimedByPawn(rows, shelf, steel, alice, evidence), Is.EqualTo(28));
            Assert.That(StorageClaimLedger.ClaimedTotal(rows, shelf, steel, evidence), Is.EqualTo(53));
            SweepPickupPlan.Retire(targets, counts, "remaining", null);
            Assert.That(StorageClaimLedger.ClaimedByPawn(rows, shelf, steel, alice, evidence), Is.EqualTo(surplus));
            Assert.That(rows, Is.SameAs(originalRows), "handoff changes evidence, not destination rows");
        }

        [Test]
        public void ReplayingHandOffsOnAnIndependentPlanPreservesCountsAndFutureCursor()
        {
            var left = new List<string> { "a", "b", "c", "d" };
            var right = new List<string>(left);
            var leftCounts = new List<int> { 3, 5, 7, 11 };
            var rightCounts = new List<int>(leftCounts);
            foreach (var target in new[] { "c", "a", "c" })
                SweepPickupPlan.Retire(left, leftCounts, target, null);
            foreach (var target in new[] { "a", "c" })
                SweepPickupPlan.Retire(right, rightCounts, target, null);
            Assert.That(left, Is.EqualTo(right));
            Assert.That(leftCounts, Is.EqualTo(rightCounts));
            Assert.That(left.Skip(1).Where(t => t != null), Is.EqualTo(new[] { "b", "d" }));
            Assert.That(leftCounts.Sum(), Is.EqualTo(16));
        }
    }
}
