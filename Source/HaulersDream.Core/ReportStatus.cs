namespace HaulersDream.Core
{
    /// <summary>The kind of card shown for a report (drives its colour, word, and threshold filtering).</summary>
    public enum NotifyCardKind
    {
        /// <summary>The issue is open. Green.</summary>
        Open,

        /// <summary>There is an unseen reply. Yellow. Overrides the status until the player clicks the card.</summary>
        Comment,

        /// <summary>The issue was resolved (closed as completed). Purple.</summary>
        Resolved,

        /// <summary>The issue was closed for another reason. Muted red.</summary>
        Closed
    }

    /// <summary>
    /// One of the player's reports with the bits the notification planner needs: its current status and the
    /// timestamps of the last status change and the last (developer) comment. Pure data (no Verse/Unity types)
    /// so the planner stays headless-testable; the game maps the backend feed into these.
    /// </summary>
    public class ReportStatus
    {
        /// <summary>Stable report id (the per-report notification key).</summary>
        public string ReportId;

        /// <summary>The report's title (first line of the player's comment).</summary>
        public string Title;

        /// <summary>The GitHub issue URL, when linked (else null).</summary>
        public string Url;

        /// <summary>Canonical status: open | solved | closed | submitted.</summary>
        public string Status;

        /// <summary>Server ms when the current status began (issue opened, or the last status change).</summary>
        public long StatusAt;

        /// <summary>Server ms of the latest non-reporter comment (0 if none).</summary>
        public long LastCommentAt;
    }

    /// <summary>One notification card: a single report collapsed to its current attention state.</summary>
    public class NotifyCard
    {
        public string ReportId;
        public NotifyCardKind Kind;
        public string Title;
        public string Url;

        /// <summary>The underlying report status (drives the Open/Resolved/Closed colour + word).</summary>
        public string Status;

        public long StatusAt;
        public long LastCommentAt;
    }
}
