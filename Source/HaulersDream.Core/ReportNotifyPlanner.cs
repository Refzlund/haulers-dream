using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure decision logic for the main-menu notifications: ONE card per report, showing the report's current
    /// status, escalated to a "comment" card when there is an unseen reply. Two per-report watermarks drive it:
    /// <c>seenComment</c> (advanced when the player clicks a card, so it falls back from "comment" to the status)
    /// and <c>dismissed</c> (advanced when the player presses x, hiding the card until genuinely new activity).
    /// Headless-testable; the game owns polling, the watermark maps, and drawing.
    /// </summary>
    public static class ReportNotifyPlanner
    {
        /// <summary>Whether a card of this kind shows at the given threshold (nested supersets).</summary>
        public static bool PassesThreshold(NotifyCardKind kind, NotifyThreshold threshold)
        {
            switch (threshold)
            {
                case NotifyThreshold.Never: return false;
                case NotifyThreshold.FixedOnly: return kind == NotifyCardKind.Resolved;
                case NotifyThreshold.Comments: return kind == NotifyCardKind.Comment || kind == NotifyCardKind.Resolved;
                case NotifyThreshold.All: return true; // open / comment / resolved / closed
                default: return false;
            }
        }

        /// <summary>Map a canonical status string to its card kind, or null when it is not a notifiable status.</summary>
        public static NotifyCardKind? StatusKind(string status)
        {
            switch (status)
            {
                case "open": return NotifyCardKind.Open;
                case "solved": return NotifyCardKind.Resolved;
                case "closed": return NotifyCardKind.Closed;
                default: return null; // 'submitted' / unknown -> no card
            }
        }

        /// <summary>
        /// The card to show for one report given its watermarks + threshold, or null for none. An unseen comment
        /// (newer than BOTH watermarks) escalates to a Comment card; otherwise the current status shows while it
        /// is "active" (its change is newer than the dismiss watermark). When the escalated Comment card is
        /// filtered out by the threshold, it falls back to the status card if that passes.
        /// </summary>
        public static NotifyCard CardFor(ReportStatus r, long seenCommentAt, long dismissedAt, NotifyThreshold threshold)
        {
            if (r == null || string.IsNullOrEmpty(r.ReportId)) return null;

            bool unseenComment = r.LastCommentAt > 0 && r.LastCommentAt > seenCommentAt && r.LastCommentAt > dismissedAt;
            if (unseenComment && PassesThreshold(NotifyCardKind.Comment, threshold))
                return Make(r, NotifyCardKind.Comment);

            if (r.StatusAt > dismissedAt)
            {
                var k = StatusKind(r.Status);
                if (k.HasValue && PassesThreshold(k.Value, threshold))
                    return Make(r, k.Value);
            }
            return null;
        }

        /// <summary>Plan all visible cards (one per report), most-recent activity first.</summary>
        public static List<NotifyCard> Plan(IEnumerable<ReportStatus> reports,
            IDictionary<string, long> seenComment, IDictionary<string, long> dismissed, NotifyThreshold threshold)
        {
            var cards = new List<NotifyCard>();
            if (reports == null) return cards;
            foreach (var r in reports)
            {
                var card = CardFor(r, Get(seenComment, r == null ? null : r.ReportId), Get(dismissed, r == null ? null : r.ReportId), threshold);
                if (card != null) cards.Add(card);
            }
            cards.Sort((a, b) => Activity(b).CompareTo(Activity(a))); // newest activity first
            return cards;
        }

        /// <summary>The seenComment watermark value after the player CLICKS this report's card (its replies seen).</summary>
        public static long SeenAfterClick(ReportStatus r) => r != null ? r.LastCommentAt : 0L;

        /// <summary>The dismissed watermark value after the player presses x (hide until activity newer than this).</summary>
        public static long DismissedAfterX(ReportStatus r) => r != null ? Max(r.StatusAt, r.LastCommentAt) : 0L;

        static NotifyCard Make(ReportStatus r, NotifyCardKind kind)
        {
            return new NotifyCard
            {
                ReportId = r.ReportId,
                Kind = kind,
                Title = r.Title,
                Url = r.Url,
                Status = r.Status,
                StatusAt = r.StatusAt,
                LastCommentAt = r.LastCommentAt
            };
        }

        static long Activity(NotifyCard c) => Max(c.StatusAt, c.LastCommentAt);
        static long Max(long a, long b) => a > b ? a : b;

        static long Get(IDictionary<string, long> map, string key)
            => map != null && key != null && map.TryGetValue(key, out var v) ? v : 0L;
    }
}
