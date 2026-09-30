using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    public class StorageDiscoveryQueueTests
    {
        [Test] public void SuccessfulReadCoordinatesDoNotScheduleBackgroundDiscovery()
        {
            var q = new StorageProgressQueue(); var group = new object(); var entry = q.Get(group);
            entry.NextCell = 200; entry.Remember(201, 201);
            for (int tick = 0; tick < 120; tick++) Assert.That(q.TakeTurn(), Is.Null);
            Assert.That(q.Get(group), Is.SameAs(entry)); Assert.That(entry.NextCell, Is.EqualTo(200));
            Assert.That(entry.Hints, Is.EqualTo(new[] { 201 })); Assert.That(entry.Turns, Is.Zero);
        }
        [Test] public void RepeatedChangingQueriesKeepPendingFairPositionAndHints()
        {
            var q = new StorageProgressQueue(); var a = new object(); var b = new object();
            var first = q.Request(a); q.Request(b); first.WorkerCell = 17; first.Remember(41, 100);
            for (int i = 0; i < 500; i++) q.Request(a);
            Assert.That(q.TakeTurn().Group, Is.SameAs(a));
            for (int i = 0; i < 500; i++) q.Request(a);
            Assert.That(q.TakeTurn().Group, Is.SameAs(b)); Assert.That(q.TakeTurn().Group, Is.SameAs(a));
            Assert.That(first.WorkerCell, Is.EqualTo(17)); Assert.That(first.Hints, Is.EqualTo(new[] { 41 }));
        }
        [Test] public void CompletionSleepsUntilANewQueryWithoutErasingCoordinates()
        {
            var q = new StorageProgressQueue(); var group = new object(); var e = q.Request(group);
            e.NextCell = 201; e.Remember(200, 201); q.TakeTurn();
            Assert.That(q.Complete(group, e.RequestVersion), Is.True);
            Assert.That(q.TakeTurn(), Is.Null); Assert.That(q.PendingCount, Is.Zero);
            Assert.That(q.Request(group), Is.SameAs(e)); Assert.That(q.TakeTurn(), Is.SameAs(e));
            Assert.That(e.NextCell, Is.EqualTo(201)); Assert.That(e.Hints, Is.EqualTo(new[] { 200 }));
        }
        [Test] public void NewDemandDuringDiscoveryCannotBeLostByOlderCompletion()
        {
            var q = new StorageProgressQueue(); var group = new object(); var e = q.Request(group);
            long captured = e.RequestVersion; q.TakeTurn(); q.Request(group);
            Assert.That(q.Complete(group, captured), Is.False); Assert.That(q.TakeTurn(), Is.SameAs(e));
            Assert.That(q.Complete(group, e.RequestVersion), Is.True); Assert.That(q.TakeTurn(), Is.Null);
        }
        [Test] public void TopologyEditPreservesPendingPositionAndDormantRemovalIsSafe()
        {
            var q = new StorageProgressQueue(); var a = new object(); var b = new object(); var dormant = new object();
            var e = q.Request(a); q.Request(b); q.Get(dormant); q.Remove(dormant);
            e.TopologyChanged(); Assert.That(q.TakeTurn().Group, Is.SameAs(a));
            q.Complete(a, e.RequestVersion); q.Remove(a);
            Assert.That(q.Count, Is.EqualTo(1)); Assert.That(q.TakeTurn().Group, Is.SameAs(b));
        }
    }
}
