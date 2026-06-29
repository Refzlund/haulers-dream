namespace HaulersDream.Core
{
    /// <summary>The kind of activity a notification event represents (parsed from the backend feed's `kind`).</summary>
    public enum NotifyEventKind
    {
        /// <summary>An unrecognized kind (a newer server event type). Never shown; never blocks the cursor.</summary>
        Unknown = 0,

        /// <summary>A new non-internal comment from someone other than the player (e.g. the developer replied).</summary>
        Comment = 1,

        /// <summary>An issue status change other than a fix (reopened / closed as not-planned / etc.).</summary>
        State = 2,

        /// <summary>The issue was closed as completed (solved). Carries the "you are not on the latest version" check.</summary>
        Fixed = 3
    }

    /// <summary>
    /// One notification event as parsed from the backend feed. A pure data carrier (no Verse/Unity types)
    /// so the planner stays headless-testable; the game maps the feed JSON into these and renders the
    /// visible ones as cards. Only <see cref="Id"/>, <see cref="Kind"/> and <see cref="CreatedAt"/> affect
    /// planning; the rest are display fields carried through untouched.
    /// </summary>
    public class NotifyEvent
    {
        /// <summary>Stable server-issued event id (used for individual dismissal + dedup).</summary>
        public string Id;

        public NotifyEventKind Kind;

        /// <summary>Server clock, ms since epoch. Used as the "caught up" cursor (never the device clock).</summary>
        public long CreatedAt;

        public string ReportId;

        /// <summary>The report's title (first line of the player's comment).</summary>
        public string Title;

        /// <summary>Canonical status at the event: open | solved | closed | submitted.</summary>
        public string Status;

        /// <summary>The GitHub issue URL, when the report is linked (else null).</summary>
        public string Url;

        /// <summary>For comment events: maintainer | reporter | github. Null for other kinds.</summary>
        public string Role;

        /// <summary>Parse the backend `kind` string into the enum (unrecognized values map to Unknown).</summary>
        public static NotifyEventKind ParseKind(string kind)
        {
            switch (kind)
            {
                case "comment": return NotifyEventKind.Comment;
                case "state": return NotifyEventKind.State;
                case "fixed": return NotifyEventKind.Fixed;
                default: return NotifyEventKind.Unknown;
            }
        }
    }
}
