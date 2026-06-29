using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class NotificationPlannerTests
    {
        static NotifyEvent Ev(string id, NotifyEventKind kind, long t)
        {
            return new NotifyEvent { Id = id, Kind = kind, CreatedAt = t };
        }

        static NotifyPlan Plan(IEnumerable<NotifyEvent> events, long cursor, NotifyThreshold t, params string[] dismissed)
        {
            return NotificationPlanner.Plan(events, cursor, new HashSet<string>(dismissed), t);
        }

        // --- threshold mapping -----------------------------------------------------------------

        [Test]
        public void Threshold_All_ShowsKnownKinds_NotUnknown()
        {
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.Comment, NotifyThreshold.All), Is.True);
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.State, NotifyThreshold.All), Is.True);
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.Fixed, NotifyThreshold.All), Is.True);
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.Unknown, NotifyThreshold.All), Is.False);
        }

        [Test]
        public void Threshold_Comments_DropsBareState()
        {
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.Comment, NotifyThreshold.Comments), Is.True);
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.Fixed, NotifyThreshold.Comments), Is.True);
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.State, NotifyThreshold.Comments), Is.False);
        }

        [Test]
        public void Threshold_FixedOnly_ShowsOnlyFixed()
        {
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.Fixed, NotifyThreshold.FixedOnly), Is.True);
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.Comment, NotifyThreshold.FixedOnly), Is.False);
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.State, NotifyThreshold.FixedOnly), Is.False);
        }

        [Test]
        public void Threshold_Never_ShowsNothing()
        {
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.Fixed, NotifyThreshold.Never), Is.False);
            Assert.That(NotificationPlanner.PassesThreshold(NotifyEventKind.Comment, NotifyThreshold.Never), Is.False);
        }

        // --- planning --------------------------------------------------------------------------

        [Test]
        public void EmptyEvents_LeavesCursorUnchanged()
        {
            var plan = Plan(new NotifyEvent[0], 50, NotifyThreshold.All);
            Assert.That(plan.Visible, Is.Empty);
            Assert.That(plan.NewCursor, Is.EqualTo(50));
        }

        [Test]
        public void FirstShownEvent_KeepsCursorBelowIt()
        {
            var plan = Plan(new[] { Ev("a", NotifyEventKind.Comment, 100) }, 0, NotifyThreshold.All);
            Assert.That(plan.Visible.Select(e => e.Id), Is.EqualTo(new[] { "a" }));
            // can't advance past the only (still-shown) event, or it would be lost next launch
            Assert.That(plan.NewCursor, Is.EqualTo(0));
        }

        [Test]
        public void DismissedPrefix_AdvancesCursorAndIsPruned()
        {
            var events = new[] { Ev("a", NotifyEventKind.Comment, 100), Ev("b", NotifyEventKind.Comment, 200) };
            var plan = Plan(events, 0, NotifyThreshold.All, "a");
            Assert.That(plan.Visible.Select(e => e.Id), Is.EqualTo(new[] { "b" }));
            Assert.That(plan.NewCursor, Is.EqualTo(100)); // 'a' dismissed and older than the first shown -> compacted
            Assert.That(plan.Dismissed, Does.Not.Contain("a")); // pruned below the watermark
        }

        [Test]
        public void ThresholdFilteredPrefix_Compacts()
        {
            var events = new[] { Ev("c", NotifyEventKind.Comment, 100), Ev("f", NotifyEventKind.Fixed, 200) };
            var plan = Plan(events, 0, NotifyThreshold.FixedOnly);
            Assert.That(plan.Visible.Select(e => e.Id), Is.EqualTo(new[] { "f" }));
            Assert.That(plan.NewCursor, Is.EqualTo(100)); // the comment is filtered out and older than the fix
        }

        [Test]
        public void AllHandled_AdvancesToNewest()
        {
            var events = new[] { Ev("a", NotifyEventKind.Comment, 100), Ev("b", NotifyEventKind.State, 200) };
            var plan = Plan(events, 0, NotifyThreshold.FixedOnly); // both filtered out
            Assert.That(plan.Visible, Is.Empty);
            Assert.That(plan.NewCursor, Is.EqualTo(200));
        }

        [Test]
        public void EqualTimestamp_ShownEventStaysFetchable()
        {
            // 'a' (dismissed) and 'b' (shown) share t=100. The cursor must stay below 100 so 'b' is re-fetched.
            var events = new[] { Ev("a", NotifyEventKind.Comment, 100), Ev("b", NotifyEventKind.Comment, 100) };
            var plan = Plan(events, 0, NotifyThreshold.All, "a");
            Assert.That(plan.Visible.Select(e => e.Id), Is.EqualTo(new[] { "b" }));
            Assert.That(plan.NewCursor, Is.EqualTo(0)); // firstShown=100, nothing strictly older -> unchanged
            Assert.That(plan.Dismissed, Does.Contain("a")); // kept (100 > cursor 0)
        }

        [Test]
        public void IslandDismissal_BetweenShownEvents_StaysInSet()
        {
            var events = new[]
            {
                Ev("s1", NotifyEventKind.Comment, 100),
                Ev("d", NotifyEventKind.Comment, 150),
                Ev("s2", NotifyEventKind.Comment, 200)
            };
            var plan = Plan(events, 0, NotifyThreshold.All, "d");
            Assert.That(plan.Visible.Select(e => e.Id), Is.EqualTo(new[] { "s1", "s2" }));
            Assert.That(plan.NewCursor, Is.EqualTo(0)); // nothing strictly before the first shown (s1@100)
            Assert.That(plan.Dismissed, Does.Contain("d")); // can't compact past it -> persists
        }

        [Test]
        public void Visible_OrderedOldestFirst_EvenIfInputUnsorted()
        {
            var events = new[] { Ev("b", NotifyEventKind.Comment, 200), Ev("a", NotifyEventKind.Comment, 100) };
            var plan = Plan(events, 0, NotifyThreshold.All);
            Assert.That(plan.Visible.Select(e => e.Id), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void Replan_AfterCursorAdvanced_DoesNotResurrectSubsumedEvents()
        {
            // Same fetched batch, re-planned across dismissals (the in-session flow). Once an event is
            // subsumed by the cursor (or pruned from the dismissed set), it must never reappear.
            var events = new[] { Ev("a", NotifyEventKind.Comment, 100), Ev("b", NotifyEventKind.Comment, 200) };
            var p1 = Plan(events, 0, NotifyThreshold.All, "a"); // a dismissed -> b shown, cursor->100, a pruned
            Assert.That(p1.Visible.Select(e => e.Id), Is.EqualTo(new[] { "b" }));
            Assert.That(p1.NewCursor, Is.EqualTo(100));
            Assert.That(p1.Dismissed, Is.Empty);

            // now dismiss b too, re-planning the SAME events with the advanced cursor + pruned set
            var p2 = NotificationPlanner.Plan(events, p1.NewCursor, new HashSet<string>(p1.Dismissed) { "b" }, NotifyThreshold.All);
            Assert.That(p2.Visible, Is.Empty); // a must NOT come back even though it is no longer in `dismissed`
            Assert.That(p2.NewCursor, Is.EqualTo(200));
        }

        [Test]
        public void DismissingFirstShown_OnReplan_AdvancesCursor()
        {
            // simulate the in-session flow: show, then dismiss the (only) card, re-plan -> caught up
            var events = new[] { Ev("a", NotifyEventKind.Comment, 100) };
            var first = Plan(events, 0, NotifyThreshold.All);
            Assert.That(first.Visible, Has.Count.EqualTo(1));
            var afterDismiss = Plan(events, first.NewCursor, NotifyThreshold.All, "a");
            Assert.That(afterDismiss.Visible, Is.Empty);
            Assert.That(afterDismiss.NewCursor, Is.EqualTo(100));
            Assert.That(afterDismiss.Dismissed, Is.Empty); // pruned once subsumed by the cursor
        }
    }
}
