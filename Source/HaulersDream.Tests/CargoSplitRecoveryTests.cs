using System;
using NUnit.Framework;
using Verse;

namespace HaulersDream.Tests
{
    [TestFixture, NonParallelizable]
    public sealed class CargoSplitRecoveryTests
    {
        [SetUp]
        public void Reset()
        { StorageSplitScope.Current = null; StorageCommitments.BarrierDepth = 0; StorageCommitments.DisposeFailure = null; }

        private static ThrowingSplitThing Source(int count = 20)
            => new ThrowingSplitThing { stackCount = count, Failure = new InvalidOperationException("PostSplitOff") };

        [Test]
        public void BaselinePartialSplitThrowsBeforeAssignmentAndLeavesUnownedPiece()
        {
            var source = Source(); source.Spawned = true;
            Thing returned = null;
            Assert.Throws<InvalidOperationException>(() => returned = source.SplitOff(7));
            Assert.That(returned, Is.Null);
            Assert.That(source.stackCount, Is.EqualTo(13));
            Assert.That(source.LastSplit.stackCount, Is.EqualTo(7));
            Assert.That(source.LastSplit.holdingOwner, Is.Null);
            Assert.That(source.LastSplit.Spawned, Is.False);
        }

        [Test]
        public void BillPartialPostSplitFailureRetainsExactFragmentAndTagsInventory()
        {
            var pawn = new Pawn(); var source = Source(); source.Spawned = true;
            var scope = new CargoSplitRecovery(pawn, source, 7);
            Exception failure = Assert.Throws<InvalidOperationException>(() => source.SplitOff(7));
            scope.Finish(ref failure);
            Assert.That(failure, Is.SameAs(source.Failure));
            Assert.That(source.stackCount + source.LastSplit.stackCount, Is.EqualTo(20));
            Assert.That(scope.Fragments, Is.EqualTo(new[] { source.LastSplit }));
            Assert.That(pawn.inventory.innerContainer.Contains(source.LastSplit), Is.True);
            Assert.That(pawn.Comp.PeekHashSet(), Does.Contain(source.LastSplit));
            Assert.That(scope.RecoveredDetached, Is.True);
            Assert.That(scope.HasInventoryCargo, Is.True);
            Assert.That(pawn.inventory.innerContainer.MergeArguments, Is.EqualTo(new[] { false }));
        }

        [Test]
        public void BulkPartialPostSplitFailureReturnsFragmentToOriginalHoldWithoutMerging()
        {
            var pawn = new Pawn(); var owner = new ThingOwner(); var source = Source(); owner.TryAdd(source, false);
            var scope = new CargoSplitRecovery(pawn, source, 7, owner);
            Exception failure = Assert.Throws<InvalidOperationException>(() => owner.TryTransfer(source,
                pawn.inventory.innerContainer, 7, out _));
            scope.Finish(ref failure);
            Assert.That(failure, Is.SameAs(source.Failure));
            Assert.That(owner.Contains(source), Is.True);
            Assert.That(owner.Contains(source.LastSplit), Is.True);
            Assert.That(owner.Count, Is.EqualTo(2));
            Assert.That(source.stackCount + source.LastSplit.stackCount, Is.EqualTo(20));
            Assert.That(owner.MergeArguments, Is.All.False);
            Assert.That(pawn.Comp.RegisterCalls, Is.Zero);
        }

        [Test]
        public void WholeSourceIsCapturedBeforeRemovalAndRestoredOnce()
        {
            var pawn = new Pawn(); var owner = new ThingOwner(); var source = Source(7); owner.TryAdd(source, false);
            var scope = new CargoSplitRecovery(pawn, source, 7, owner);
            Exception failure = Assert.Throws<InvalidOperationException>(() => source.SplitOff(7));
            scope.Finish(ref failure);
            Assert.That(scope.Fragments, Is.EqualTo(new[] { source }));
            Assert.That(owner.Contains(source), Is.True);
            Assert.That(owner.Count, Is.EqualTo(1));
            Assert.That(source.stackCount, Is.EqualTo(7));
        }

        [Test]
        public void ThrowAfterDestinationAddKeepsActualOwnershipAndRepairsTag()
        {
            var pawn = new Pawn(); var owner = new ThingOwner(); var source = Source(); source.Failure = null;
            owner.TryAdd(source, false);
            var expected = new InvalidOperationException("NotifyAdded");
            pawn.inventory.innerContainer.AfterAdd = _ => throw expected;
            var scope = new CargoSplitRecovery(pawn, source, 7, owner);
            Exception failure = Assert.Throws<InvalidOperationException>(() => owner.TryTransfer(source,
                pawn.inventory.innerContainer, 7, out _));
            scope.Finish(ref failure);
            Assert.That(failure, Is.SameAs(expected));
            Assert.That(source.LastSplit.holdingOwner, Is.SameAs(pawn.inventory.innerContainer));
            Assert.That(pawn.inventory.innerContainer.Count, Is.EqualTo(1));
            Assert.That(owner.Count, Is.EqualTo(1));
            Assert.That(pawn.Comp.RegisterCalls, Is.EqualTo(1));
            Assert.That(scope.RecoveredDetached, Is.False);
        }

        [Test]
        public void SecondarySourceAddFailureFallsBackWithoutReplacingPrimary()
        {
            var pawn = new Pawn(); var owner = new ThingOwner(); var source = Source(); owner.TryAdd(source, false);
            owner.BeforeAdd = _ => throw new ArgumentException("return callback");
            var scope = new CargoSplitRecovery(pawn, source, 7, owner);
            Exception failure = Assert.Throws<InvalidOperationException>(() => source.SplitOff(7));
            scope.Finish(ref failure);
            Assert.That(failure, Is.SameAs(source.Failure));
            Assert.That(failure.Data.Count, Is.EqualTo(1));
            Assert.That(source.LastSplit.holdingOwner, Is.SameAs(pawn.inventory.innerContainer));
            Assert.That(pawn.Comp.PeekHashSet(), Does.Contain(source.LastSplit));
        }

        [Test]
        public void RecoveryAddThrowsAfterInsertionDoesNotTryAnotherOwner()
        {
            var pawn = new Pawn(); var source = Source(); source.Spawned = true;
            pawn.inventory.innerContainer.AfterAdd = _ => throw new ArgumentException("NotifyAdded");
            var scope = new CargoSplitRecovery(pawn, source, 7);
            Exception failure = Assert.Throws<InvalidOperationException>(() => source.SplitOff(7));
            scope.Finish(ref failure);
            Assert.That(failure, Is.SameAs(source.Failure));
            Assert.That(failure.Data.Count, Is.EqualTo(1));
            Assert.That(pawn.inventory.innerContainer.Count, Is.EqualTo(1));
            Assert.That(pawn.carryTracker.innerContainer.MergeArguments, Is.Empty);
            Assert.That(pawn.Comp.RegisterCalls, Is.EqualTo(1));
        }

        [Test]
        public void TagCallbackFailurePreservesOwnershipAndPrimaryException()
        {
            var pawn = new Pawn(); var source = Source(); source.Spawned = true;
            pawn.Comp.AfterTag = _ => throw new ArgumentException("CE notification");
            var scope = new CargoSplitRecovery(pawn, source, 7);
            Exception failure = Assert.Throws<InvalidOperationException>(() => source.SplitOff(7));
            scope.Finish(ref failure);
            Assert.That(failure, Is.SameAs(source.Failure));
            Assert.That(failure.Data.Count, Is.EqualTo(1));
            Assert.That(source.LastSplit.holdingOwner, Is.SameAs(pawn.inventory.innerContainer));
            Assert.That(pawn.Comp.PeekHashSet(), Does.Contain(source.LastSplit));
            Assert.That(StorageSplitScope.Current, Is.Null);
        }

        [Test]
        public void ForeignOwnedCapturedPieceAndUnrelatedPersonalStockAreNotMovedOrTagged()
        {
            var pawn = new Pawn(); var source = Source(); source.Failure = null;
            var personal = new Thing { stackCount = 3 }; pawn.inventory.innerContainer.TryAdd(personal, false);
            var foreign = new ThingOwner();
            var scope = new CargoSplitRecovery(pawn, source, 7);
            var piece = source.SplitOff(7); foreign.TryAdd(piece, false);
            Exception failure = null; scope.Finish(ref failure);
            Assert.That(failure, Is.Null);
            Assert.That(piece.holdingOwner, Is.SameAs(foreign));
            Assert.That(pawn.inventory.innerContainer.Count, Is.EqualTo(1));
            Assert.That(pawn.Comp.RegisterCalls, Is.Zero);
        }

        [TestCase(true, false), TestCase(false, true)]
        public void SpawnedOrDestroyedPieceIsNotRecovered(bool spawned, bool destroyed)
        {
            var pawn = new Pawn(); var source = Source(); source.Failure = null;
            var scope = new CargoSplitRecovery(pawn, source, 7);
            var piece = source.SplitOff(7); piece.Spawned = spawned; piece.Destroyed = destroyed;
            Exception failure = null; scope.Finish(ref failure);
            Assert.That(failure, Is.Null);
            Assert.That(pawn.inventory.innerContainer.Count, Is.Zero);
            Assert.That(pawn.Comp.RegisterCalls, Is.Zero);
        }

        [Test]
        public void NormalAlreadyTaggedTransferDoesNotRegisterOrAddAgain()
        {
            var pawn = new Pawn(); var owner = new ThingOwner(); var source = Source(); source.Failure = null;
            owner.TryAdd(source, false);
            var scope = new CargoSplitRecovery(pawn, source, 7, owner);
            int moved = owner.TryTransfer(source, pawn.inventory.innerContainer, 7, out var piece);
            pawn.Comp.RegisterHauledItem(piece);
            Exception failure = null; scope.Finish(ref failure);
            Assert.That(failure, Is.Null);
            Assert.That(moved, Is.EqualTo(7));
            Assert.That(pawn.Comp.RegisterCalls, Is.EqualTo(1));
            Assert.That(pawn.inventory.innerContainer.MergeArguments, Is.EqualTo(new[] { false }));
        }

        [Test]
        public void NestedScopesRestorePriorScopeAndBarrier()
        {
            var pawn = new Pawn(); var source = Source();
            var outer = new CargoSplitRecovery(pawn, source, 7);
            var inner = new CargoSplitRecovery(pawn, Source(), 5);
            Exception failure = null; inner.Finish(ref failure);
            Assert.That(StorageSplitScope.Current, Is.SameAs(outer));
            Assert.That(StorageCommitments.BarrierDepth, Is.EqualTo(1));
            outer.Finish(ref failure);
            Assert.That(StorageSplitScope.Current, Is.Null);
            Assert.That(StorageCommitments.BarrierDepth, Is.Zero);
        }

        [Test]
        public void DisposeFailureIsSecondaryAndDoesNotLeakActiveScope()
        {
            var pawn = new Pawn(); var source = Source();
            var scope = new CargoSplitRecovery(pawn, source, 7);
            StorageCommitments.DisposeFailure = new ArgumentException("barrier");
            Exception failure = source.Failure; scope.Finish(ref failure);
            Assert.That(failure, Is.SameAs(source.Failure));
            Assert.That(failure.Data.Count, Is.EqualTo(1));
            Assert.That(StorageSplitScope.Current, Is.Null);
            Assert.That(StorageCommitments.BarrierDepth, Is.Zero);
        }

        [Test]
        public void FailedNativeCustodyDoesNotDisplacePassengerOrReportSuccess()
        {
            var pawn = new Pawn(); var source = Source();
            var passenger = new Thing { stackCount = 1 }; pawn.carryTracker.innerContainer.TryAdd(passenger, false);
            pawn.inventory.innerContainer.BeforeAdd = _ => throw new ArgumentException("inventory");
            var scope = new CargoSplitRecovery(pawn, source, 7);
            Exception failure = Assert.Throws<InvalidOperationException>(() => source.SplitOff(7));
            scope.Finish(ref failure);
            Assert.That(failure, Is.SameAs(source.Failure));
            Assert.That(failure.Data.Count, Is.EqualTo(2));
            Assert.That(pawn.carryTracker.innerContainer.Count, Is.EqualTo(1));
            Assert.That(passenger.holdingOwner, Is.SameAs(pawn.carryTracker.innerContainer));
            Assert.That(scope.HasInventoryCargo, Is.False);
        }

        [Test]
        public void UnrelatedSplitIsNotCapturedByActiveScope()
        {
            var pawn = new Pawn(); var source = Source();
            var unrelated = Source(); unrelated.Failure = null;
            var scope = new CargoSplitRecovery(pawn, source, 7);
            var piece = unrelated.SplitOff(7);
            Exception failure = null; scope.Finish(ref failure);
            Assert.That(scope.Fragments, Is.Empty);
            Assert.That(piece.holdingOwner, Is.Null);
            Assert.That(pawn.inventory.innerContainer.Count, Is.Zero);
        }
    }
}
