using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class BillGatherSelectionTests
    {
        private sealed class Stack
        {
            internal int Count;
            internal Stack(int count) { Count = count; }
        }

        [Test]
        public void PartialTransferIntoSelectedInventoryPreservesTheFloorShortfall()
        {
            var held = new Stack(17); // originally 12; exactly five actually merged
            var floor = new Stack(3);
            var targets = new List<Stack> { held, floor };
            var counts = new List<int> { 12, 8 };
            Assert.That(BillGatherSelection.TryRemap(targets, counts, 1,
                new[] { new BillGatherReceipt<Stack>(held, 5) }), Is.True);
            Assert.That(targets[1], Is.SameAs(floor)); // cursor indices remain stable
            Assert.That(counts, Is.EqualTo(new[] { 12, 3, 5 }));
            Assert.That(BillGatherSelection.TryNormalize(targets, counts, t => t.Count,
                out var normalized, out var amounts), Is.True);
            Assert.That(normalized, Is.EqualTo(new[] { held, floor }));
            Assert.That(amounts, Is.EqualTo(new[] { 17, 3 }));
        }

        [Test]
        public void FullTransferCanHaveSeveralActualMergeRecipients()
        {
            var source = new Stack(0);
            var first = new Stack(75);
            var second = new Stack(5);
            var targets = new List<Stack> { source };
            var counts = new List<int> { 10 };
            Assert.That(BillGatherSelection.TryRemap(targets, counts, 0,
                new[] { new BillGatherReceipt<Stack>(first, 5), new BillGatherReceipt<Stack>(second, 5) }), Is.True);
            Assert.That(targets, Is.EqualTo(new[] { first, second }));
            Assert.That(counts, Is.EqualTo(new[] { 5, 5 })); // do not consume first's pre-existing 70
        }

        [Test]
        public void IncrementalReceiptsDoNotReplayEarlierMovesWhenTheLastMoveReplacesTheFloorRow()
        {
            var held = new Stack(17); // original 12 plus the first observed move of five
            var floor = new Stack(5);
            var targets = new List<Stack> { held, floor };
            var counts = new List<int> { 12, 10 };
            Assert.That(BillGatherSelection.TryRemap(targets, counts, 1,
                new[] { new BillGatherReceipt<Stack>(held, 5) }), Is.True);
            Assert.That(counts, Is.EqualTo(new[] { 12, 5, 5 }));
            held.Count += 5;
            floor.Count = 0;
            Assert.That(BillGatherSelection.TryRemap(targets, counts, 1,
                new[] { new BillGatherReceipt<Stack>(held, 5) }), Is.True);
            Assert.That(BillGatherSelection.TryNormalize(targets, counts, t => t.Count,
                out var normalized, out var amounts), Is.True);
            Assert.That(normalized, Is.EqualTo(new[] { held }));
            Assert.That(amounts, Is.EqualTo(new[] { 22 })); // original 12 plus ten moved, never fifteen
        }

        [Test]
        public void FailedPickupLeavesTheCompleteNativeRequirement()
        {
            var source = new Stack(10);
            var targets = new List<Stack> { source };
            var counts = new List<int> { 8 };
            Assert.That(BillGatherSelection.TryRemap(targets, counts, 0,
                new BillGatherReceipt<Stack>[0]), Is.True);
            Assert.That(counts[0], Is.EqualTo(8));
            Assert.That(targets[0], Is.SameAs(source));
        }

        [Test]
        public void InvalidReceiptsCannotPartiallyRewriteASelection()
        {
            var source = new Stack(10);
            var recipient = new Stack(20);
            var targets = new List<Stack> { source };
            var counts = new List<int> { 8 };
            Assert.That(BillGatherSelection.TryRemap(targets, counts, 0,
                new[] { new BillGatherReceipt<Stack>(recipient, 9) }), Is.False);
            Assert.That(BillGatherSelection.TryRemap(targets, counts, 0,
                new[] { new BillGatherReceipt<Stack>(recipient, 2), new BillGatherReceipt<Stack>(null, 2) }), Is.False);
            Assert.That(targets, Is.EqualTo(new[] { source }));
            Assert.That(counts, Is.EqualTo(new[] { 8 }));
        }

        [Test]
        public void LostFloorRemainderInvalidatesInsteadOfReducingTheRecipe()
        {
            var held = new Stack(5);
            var floor = new Stack(2); // three still required; another actor took one
            Assert.That(BillGatherSelection.TryNormalize(new[] { floor, held }, new[] { 3, 5 },
                t => t.Count, out _, out _), Is.False);
        }

        [Test]
        public void TwoRowsCannotReserveTheSamePhysicalUnitsTwice()
        {
            var shared = new Stack(7);
            Assert.That(BillGatherSelection.TryNormalize(new[] { shared, shared }, new[] { 5, 5 },
                t => t.Count, out _, out _), Is.False);
        }

        [Test]
        public void SameDefinitionVariantsAreNotCoalescedByTheirAppearance()
        {
            var highQuality = new Stack(4);
            var lowQuality = new Stack(7);
            Assert.That(BillGatherSelection.TryNormalize(new[] { highQuality, lowQuality }, new[] { 4, 7 },
                t => t.Count, out var targets, out var counts), Is.True);
            Assert.That(targets.Count, Is.EqualTo(2));
            Assert.That(counts, Is.EqualTo(new[] { 4, 7 }));
        }

        [Test]
        public void OneMealUsesFiveUnitsOfEachFoodWithoutChangingItsSelection()
        {
            var available = new[] { 5, 5 };
            Assert.That(BillGatherSelection.SatisfiesRecipe(available, new[] { 1, 2 },
                new double[,] { { 10, 10 } }, new[] { true }), Is.True);
            Assert.That(available, Is.EqualTo(new[] { 5, 5 }));
            Assert.That(BillGatherSelection.SatisfiesRecipe(new[] { 5, 4 }, new[] { 1, 2 },
                new double[,] { { 10, 10 } }, new[] { true }), Is.False);
        }

        [Test]
        public void OverlappingFiltersBacktrackInsteadOfSpendingTheRestrictedFoodEarly()
        {
            // Slot 0 takes either food; slot 1 only the first. A greedy allocation of first to slot 0 fails.
            Assert.That(BillGatherSelection.SatisfiesRecipe(new[] { 5, 5 }, new[] { 1, 2 },
                new double[,] { { 5, 5 }, { 5, 0 } }, new[] { true, true }), Is.True);
            Assert.That(BillGatherSelection.SatisfiesRecipe(new[] { 5, 4 }, new[] { 1, 2 },
                new double[,] { { 5, 5 }, { 5, 0 } }, new[] { true, true }), Is.False);
        }

        [Test]
        public void OneLargeNutritionUnitCannotBeSplitBetweenTwoRecipeSlots()
        {
            Assert.That(BillGatherSelection.SatisfiesRecipe(new[] { 1 }, new[] { 1 },
                new double[,] { { 0.5 }, { 0.5 } }, new[] { true, true }), Is.False);
            Assert.That(BillGatherSelection.SatisfiesRecipe(new[] { 2 }, new[] { 1 },
                new double[,] { { 0.5 }, { 0.5 } }, new[] { true, true }), Is.True);
        }

        [Test]
        public void NonMixingSlotMaySpanStacksButNotDifferentKinds()
        {
            var requirement = new double[,] { { 10, 10 } };
            Assert.That(BillGatherSelection.SatisfiesRecipe(new[] { 5, 5 }, new[] { 1, 1 },
                requirement, new[] { false }), Is.True);
            Assert.That(BillGatherSelection.SatisfiesRecipe(new[] { 5, 5 }, new[] { 1, 2 },
                requirement, new[] { false }), Is.False);
        }

        [Test]
        public void ChangedFilterAndExhaustedValidationCannotAuthorizeCrafting()
        {
            Assert.That(BillGatherSelection.SatisfiesRecipe(new[] { 10 }, new[] { 1 },
                new double[,] { { 0 } }, new[] { true }), Is.False);
            Assert.That(BillGatherSelection.SatisfiesRecipe(new[] { 10 }, new[] { 1 },
                new double[,] { { 10 } }, new[] { true }, searchBudget: 1), Is.False);
        }

        [Test]
        public void AnOversizedRecipeCannotExhaustTheRuntimeCallStack()
        {
            Assert.That(BillGatherSelection.SatisfiesRecipe(new int[600], new int[600],
                new double[1, 600], new[] { true }), Is.False);
        }
    }
}
