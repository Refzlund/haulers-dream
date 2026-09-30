using System;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    public class StorageWorkAccountTests
    {
        private static long[] Vector(long amount)
        { var v = StorageWorkAccount.NewVector(); for (int i = 0; i < v.Length; i++) v[i] = amount; return v; }
        private static StorageWorkAccount Account(long limit = 100)
        { var account = new StorageWorkAccount(Vector(limit), Vector(limit)); account.BeginTick(1); return account; }

        [Test] public void NestedCallCannotSpendReservedFinalValidation()
        {
            var account = Account();
            using var outer = account.TryReserve(StorageWorkLane.Optional, Vector(60), Vector(40));
            Assert.That(account.TryReserve(StorageWorkLane.Optional, Vector(1), Vector(0)), Is.Null);
            Assert.That(outer.TryCharge(StorageWorkKind.Grid, 61), Is.False);
            Assert.That(outer.TryCharge(StorageWorkKind.Grid, 60), Is.True);
            outer.BeginFinal();
            Assert.That(outer.TryCharge(StorageWorkKind.Grid, 40), Is.True);
            Assert.That(outer.TryCharge(StorageWorkKind.Grid, 1), Is.False);
            Assert.That(account.Used(StorageWorkLane.Optional, StorageWorkKind.Grid), Is.EqualTo(100));
        }
        [Test] public void DisposalReleasesOnlyUnusedReservationAndOnlyOnce()
        {
            var account = Account();
            var token = account.TryReserve(StorageWorkLane.Optional, Vector(60), Vector(40));
            token.TryCharge(StorageWorkKind.Grid, 23); token.Dispose(); token.Dispose();
            Assert.That(account.Used(StorageWorkLane.Optional, StorageWorkKind.Grid), Is.EqualTo(23));
            Assert.That(account.Reserved(StorageWorkLane.Optional, StorageWorkKind.Grid), Is.Zero);
            var next = account.TryReserve(StorageWorkLane.Optional, Vector(77), Vector(0));
            Assert.That(next, Is.Not.Null);
            Assert.That(account.TryReserve(StorageWorkLane.Optional, Vector(1), Vector(0)), Is.Null);
            next.Dispose();
        }
        [Test] public void ReturnedCoreWorkRefundsOnceButCallbackEscapeRetainsCharge()
        {
            var account = Account();
            using var token = account.TryReserve(StorageWorkLane.Optional, Vector(100), Vector(0));
            var returned = token.ReserveMatching(50); returned.Returned(7);
            Assert.That(account.Used(StorageWorkLane.Optional, StorageWorkKind.Matching), Is.EqualTo(7));
            Assert.Throws<InvalidOperationException>(() => returned.Returned(7));
            var escaped = token.ReserveMatching(50); // No Returned: callback escaped.
            Assert.That(escaped, Is.Not.Null);
            token.Dispose();
            Assert.That(account.Used(StorageWorkLane.Optional, StorageWorkKind.Matching), Is.EqualTo(57));
        }
        [Test] public void OldTickTokenCannotChargeRefundOrDisposeNewTickReservation()
        {
            var account = Account();
            var old = account.TryReserve(StorageWorkLane.Optional, Vector(100), Vector(0));
            var core = old.ReserveMatching(50);
            account.BeginTick(2);
            using var next = account.TryReserve(StorageWorkLane.Optional, Vector(100), Vector(0));
            Assert.That(old.TryCharge(StorageWorkKind.Grid, 1), Is.False);
            core.Returned(1); old.Dispose();
            Assert.That(account.Used(StorageWorkLane.Optional, StorageWorkKind.Matching), Is.Zero);
            Assert.That(account.Reserved(StorageWorkLane.Optional, StorageWorkKind.Matching), Is.EqualTo(100));
        }
        [Test] public void MandatoryOverrunCannotConsumeFairShareOrCreateNextTickDebt()
        {
            var account = Account();
            for (int tick = 1; tick <= 20; tick++)
            {
                account.BeginTick(tick);
                using var mandatory = account.TryReserve(StorageWorkLane.Mandatory, Vector(1), Vector(0));
                Assert.That(mandatory.TryCharge(StorageWorkKind.Grid, 1000), Is.True);
                Assert.That(account.MandatoryOverrun, Is.True);
                Assert.That(account.TryReserve(StorageWorkLane.Optional, Vector(1), Vector(0)), Is.Null);
                using var fair = account.TryReserve(StorageWorkLane.Fair, Vector(60), Vector(40));
                Assert.That(fair, Is.Not.Null);
                Assert.That(fair.TryCharge(StorageWorkKind.Grid, 60), Is.True);
                fair.BeginFinal(); Assert.That(fair.TryCharge(StorageWorkKind.Grid, 40), Is.True);
            }
            account.BeginTick(21);
            Assert.That(account.MandatoryOverrun, Is.False);
            Assert.That(account.TryReserve(StorageWorkLane.Optional, Vector(100), Vector(0)), Is.Not.Null);
        }
        [Test] public void FairDiscoveryCannotConsumeTheSameTicksRealAdmissionTransaction()
        {
            var account = new StorageWorkAccount(Vector(600), Vector(600)); account.BeginTick(1);
            using var native = account.TryReserve(StorageWorkLane.Mandatory, Vector(1), Vector(0));
            native.TryCharge(StorageWorkKind.RawGuard, 10000);
            var discovery = account.TryReserve(StorageWorkLane.Fair, Vector(200), Vector(100));
            foreach (StorageWorkKind kind in Enum.GetValues(typeof(StorageWorkKind)))
                if (kind != StorageWorkKind.Count) Assert.That(discovery.TryCharge(kind, 200), Is.True);
            discovery.BeginFinal();
            foreach (StorageWorkKind kind in Enum.GetValues(typeof(StorageWorkKind)))
                if (kind != StorageWorkKind.Count) Assert.That(discovery.TryCharge(kind, 100), Is.True);
            discovery.Dispose();
            using var admission = account.TryReserve(StorageWorkLane.Fair, Vector(200), Vector(100));
            Assert.That(admission, Is.Not.Null, "Continuation ran before the actual query in this tick");
            Assert.That(account.TryReserve(StorageWorkLane.Fair, Vector(1), Vector(0)), Is.Null);
            Assert.That(account.TryReserve(StorageWorkLane.Optional, Vector(1), Vector(0)), Is.Null);
        }
        [Test] public void TemporaryFinalPassCannotSpendInitialShareOrLoseRemainingFinalShare()
        {
            var account = Account();
            using var token = account.TryReserve(StorageWorkLane.Optional, Vector(60), Vector(40));
            token.BeginFinal(); token.TryCharge(StorageWorkKind.Grid, 15); token.RestorePhase(false);
            Assert.That(token.Remaining(StorageWorkKind.Grid), Is.EqualTo(60));
            token.BeginFinal(); Assert.That(token.Remaining(StorageWorkKind.Grid), Is.EqualTo(25));
        }
        [Test] public void GroupIdentityOwnsProgressAcrossChangingSourcesAndTopologyEdits()
        {
            var queue = new StorageProgressQueue(); var groups = new[] { new object(), new object(), new object() };
            foreach (var group in groups) queue.Request(group);
            queue.Get(groups[0]).NextCell = 200;
            queue.Get(groups[0]).WorkerCell = 17;
            queue.Get(groups[0]).Remember(201, 201);
            Assert.That(queue.Get(groups[0]).NextCell, Is.EqualTo(200));
            Assert.That(queue.Get(groups[0]).WorkerCell, Is.EqualTo(17));
            Assert.That(queue.TakeTurn().Group, Is.SameAs(groups[0]));
            queue.Get(groups[0]).TopologyChanged();
            Assert.That(queue.Get(groups[0]).Hints, Is.Empty);
            Assert.That(queue.Get(groups[0]).NextCell, Is.Zero);
            Assert.That(queue.Get(groups[0]).WorkerCell, Is.Zero);
            Assert.That(queue.TakeTurn().Group, Is.SameAs(groups[1]));
            Assert.That(queue.TakeTurn().Group, Is.SameAs(groups[2]));
            Assert.That(queue.TakeTurn().Group, Is.SameAs(groups[0]));
        }
        [Test] public void DistinctGroupsReceiveOneTurnPerRoundUnderSustainedMandatoryLoad()
        {
            var account = Account(); var queue = new StorageProgressQueue();
            var groups = new[] { new object(), new object(), new object(), new object() };
            foreach (var group in groups) queue.Request(group);
            for (int tick = 1; tick <= 40; tick++)
            {
                account.BeginTick(tick);
                using var mandatory = account.TryReserve(StorageWorkLane.Mandatory, Vector(0), Vector(0));
                mandatory.TryCharge(StorageWorkKind.Matching, 1000);
                var group = queue.TakeTurn();
                using var turn = account.TryReserve(StorageWorkLane.Fair, Vector(60), Vector(40));
                Assert.That(turn, Is.Not.Null);
                Assert.That(group.Group, Is.SameAs(groups[(tick - 1) % groups.Length]));
                turn.TryCharge(StorageWorkKind.Matching, 60);
            }
            foreach (var group in groups) Assert.That(queue.Get(group).Turns, Is.EqualTo(10));
        }
        [Test] public void HeavyAddressIsRetainedOnceWithoutResettingOrdinaryCursor()
        {
            var queue = new StorageProgressQueue(); var group = queue.Get(new object());
            group.NextCell = 1; group.HeavyCell = 0; group.Larger = true;
            Assert.That(group.Remember(0, 2), Is.True); Assert.That(group.Remember(0, 2), Is.True);
            Assert.That(group.Hints.Count, Is.EqualTo(1)); Assert.That(group.NextCell, Is.EqualTo(1));
            Assert.That(group.Remember(1, 2), Is.True); Assert.That(group.Remember(2, 2), Is.False);
            Assert.That(group.Reason, Is.EqualTo("coordinate-hint-envelope"));
        }
    }
}
