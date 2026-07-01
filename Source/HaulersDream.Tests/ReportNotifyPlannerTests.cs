using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class ReportNotifyPlannerTests
    {
        static ReportStatus R(string id, string status, long statusAt, long lastCommentAt = 0)
        {
            return new ReportStatus { ReportId = id, Title = id, Status = status, StatusAt = statusAt, LastCommentAt = lastCommentAt };
        }

        static NotifyCard Card(ReportStatus r, long seen, long dismissed, NotifyThreshold t = NotifyThreshold.All)
        {
            return ReportNotifyPlanner.CardFor(r, seen, dismissed, t);
        }

        // --- threshold + status mapping -------------------------------------------------------

        [Test]
        public void Threshold_All_ShowsEveryKind()
        {
            foreach (NotifyCardKind k in System.Enum.GetValues(typeof(NotifyCardKind)))
                Assert.That(ReportNotifyPlanner.PassesThreshold(k, NotifyThreshold.All), Is.True, k.ToString());
        }

        [Test]
        public void Threshold_Comments_ShowsCommentAndResolvedOnly()
        {
            Assert.That(ReportNotifyPlanner.PassesThreshold(NotifyCardKind.Comment, NotifyThreshold.Comments), Is.True);
            Assert.That(ReportNotifyPlanner.PassesThreshold(NotifyCardKind.Resolved, NotifyThreshold.Comments), Is.True);
            Assert.That(ReportNotifyPlanner.PassesThreshold(NotifyCardKind.Open, NotifyThreshold.Comments), Is.False);
            Assert.That(ReportNotifyPlanner.PassesThreshold(NotifyCardKind.Closed, NotifyThreshold.Comments), Is.False);
        }

        [Test]
        public void Threshold_FixedOnly_ShowsResolvedOnly()
        {
            Assert.That(ReportNotifyPlanner.PassesThreshold(NotifyCardKind.Resolved, NotifyThreshold.FixedOnly), Is.True);
            Assert.That(ReportNotifyPlanner.PassesThreshold(NotifyCardKind.Comment, NotifyThreshold.FixedOnly), Is.False);
            Assert.That(ReportNotifyPlanner.PassesThreshold(NotifyCardKind.Open, NotifyThreshold.FixedOnly), Is.False);
        }

        [Test]
        public void Threshold_Never_ShowsNothing()
        {
            foreach (NotifyCardKind k in System.Enum.GetValues(typeof(NotifyCardKind)))
                Assert.That(ReportNotifyPlanner.PassesThreshold(k, NotifyThreshold.Never), Is.False);
        }

        [Test]
        public void StatusKind_Mapping()
        {
            Assert.That(ReportNotifyPlanner.StatusKind("open"), Is.EqualTo(NotifyCardKind.Open));
            Assert.That(ReportNotifyPlanner.StatusKind("solved"), Is.EqualTo(NotifyCardKind.Resolved));
            Assert.That(ReportNotifyPlanner.StatusKind("closed"), Is.EqualTo(NotifyCardKind.Closed));
            Assert.That(ReportNotifyPlanner.StatusKind("submitted"), Is.Null);
            Assert.That(ReportNotifyPlanner.StatusKind("weird"), Is.Null);
        }

        // --- the user's canonical flow: open -> commented -> click -> open --------------------

        [Test]
        public void OpenReport_ShowsOpen()
        {
            var card = Card(R("a", "open", 100), 0, 0);
            Assert.That(card, Is.Not.Null);
            Assert.That(card.Kind, Is.EqualTo(NotifyCardKind.Open));
        }

        [Test]
        public void NewComment_EscalatesToComment()
        {
            var card = Card(R("a", "open", 100, 200), 0, 0);
            Assert.That(card.Kind, Is.EqualTo(NotifyCardKind.Comment));
        }

        [Test]
        public void AfterClick_FallsBackToOpen()
        {
            // click sets seenComment = lastCommentAt and clears dismissed
            var r = R("a", "open", 100, 200);
            long seen = ReportNotifyPlanner.SeenAfterClick(r); // 200
            var card = Card(r, seen, 0);
            Assert.That(card.Kind, Is.EqualTo(NotifyCardKind.Open));
        }

        [Test]
        public void AfterX_Hidden()
        {
            var r = R("a", "open", 100, 200);
            long dismissed = ReportNotifyPlanner.DismissedAfterX(r); // max(100,200)=200
            Assert.That(Card(r, 0, dismissed), Is.Null);
        }

        [Test]
        public void AfterX_NewCommentRevives()
        {
            var r0 = R("a", "open", 100, 200);
            long dismissed = ReportNotifyPlanner.DismissedAfterX(r0); // 200
            // a newer comment arrives at 300
            var r1 = R("a", "open", 100, 300);
            var card = Card(r1, 0, dismissed);
            Assert.That(card, Is.Not.Null);
            Assert.That(card.Kind, Is.EqualTo(NotifyCardKind.Comment));
        }

        [Test]
        public void AfterX_StatusChangeRevives_AsResolved()
        {
            var r0 = R("a", "open", 100, 200);
            long dismissed = ReportNotifyPlanner.DismissedAfterX(r0); // 200
            // later the issue is resolved at 400
            var r1 = R("a", "solved", 400, 200);
            var card = Card(r1, 0, dismissed);
            Assert.That(card, Is.Not.Null);
            Assert.That(card.Kind, Is.EqualTo(NotifyCardKind.Resolved));
        }

        // --- status kinds + threshold fallback ------------------------------------------------

        [Test]
        public void Resolved_ShowsAtFixedThreshold()
        {
            Assert.That(Card(R("a", "solved", 100), 0, 0, NotifyThreshold.FixedOnly).Kind, Is.EqualTo(NotifyCardKind.Resolved));
        }

        [Test]
        public void OpenReport_HiddenAtCommentsThreshold()
        {
            Assert.That(Card(R("a", "open", 100), 0, 0, NotifyThreshold.Comments), Is.Null);
        }

        [Test]
        public void CommentOnResolved_AtFixedThreshold_FallsBackToResolved()
        {
            // unseen comment would escalate, but Fixed threshold filters Comment -> falls back to the Resolved status
            var card = Card(R("a", "solved", 100, 200), 0, 0, NotifyThreshold.FixedOnly);
            Assert.That(card.Kind, Is.EqualTo(NotifyCardKind.Resolved));
        }

        [Test]
        public void CommentOnOpen_AtFixedThreshold_ShowsNothing()
        {
            // comment filtered, and Open is not shown at Fixed -> no card
            Assert.That(Card(R("a", "open", 100, 200), 0, 0, NotifyThreshold.FixedOnly), Is.Null);
        }

        [Test]
        public void SubmittedStatus_NoCard()
        {
            Assert.That(Card(R("a", "submitted", 100), 0, 0), Is.Null);
        }

        // --- Plan over many reports -----------------------------------------------------------

        [Test]
        public void Plan_OneCardPerReport_NewestActivityFirst()
        {
            var reports = new[]
            {
                R("a", "open", 100),            // activity 100
                R("b", "open", 120, 400),       // activity 400 (unseen comment)
                R("c", "solved", 300)           // activity 300
            };
            var cards = ReportNotifyPlanner.Plan(reports, new Dictionary<string, long>(), new Dictionary<string, long>(), NotifyThreshold.All);
            Assert.That(cards.Select(c => c.ReportId), Is.EqualTo(new[] { "b", "c", "a" }));
            Assert.That(cards.Single(c => c.ReportId == "b").Kind, Is.EqualTo(NotifyCardKind.Comment));
            Assert.That(cards.Single(c => c.ReportId == "c").Kind, Is.EqualTo(NotifyCardKind.Resolved));
        }

        [Test]
        public void Plan_RespectsPerReportWatermarks()
        {
            var reports = new[] { R("a", "open", 100, 200), R("b", "open", 100) };
            var seen = new Dictionary<string, long> { { "a", 200 } };       // a's comment already seen
            var dismissed = new Dictionary<string, long> { { "b", 100 } };  // b dismissed
            var cards = ReportNotifyPlanner.Plan(reports, seen, dismissed, NotifyThreshold.All);
            // a falls back to Open (comment seen); b is hidden (dismissed, nothing new)
            Assert.That(cards.Select(c => c.ReportId), Is.EqualTo(new[] { "a" }));
            Assert.That(cards[0].Kind, Is.EqualTo(NotifyCardKind.Open));
        }
    }
}
