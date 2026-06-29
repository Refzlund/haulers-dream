using System.Collections.Generic;
using System.Linq;

namespace HaulersDream.Core
{
    /// <summary>The outcome of planning one poll: the cards to show, plus the advanced "caught up" state.</summary>
    public class NotifyPlan
    {
        /// <summary>The events to draw as cards, oldest first.</summary>
        public List<NotifyEvent> Visible = new List<NotifyEvent>();

        /// <summary>The advanced cursor (newest event the player is caught up on). Persist this.</summary>
        public long NewCursor;

        /// <summary>The pruned set of individually-dismissed event ids still above the cursor. Persist this.</summary>
        public HashSet<string> Dismissed = new HashSet<string>();
    }

    /// <summary>
    /// Pure decision logic for the main-menu notifications. Given the events the backend returned (all
    /// newer than the last cursor), the local "caught up" cursor, the set of individually-dismissed event
    /// ids, and the threshold, it computes which cards to show and advances the cursor over everything the
    /// player is now caught up on. Headless-testable; the game owns polling, persistence, and drawing.
    /// </summary>
    public static class NotificationPlanner
    {
        /// <summary>Whether an event of this kind is shown at the given threshold (nested supersets).</summary>
        public static bool PassesThreshold(NotifyEventKind kind, NotifyThreshold threshold)
        {
            switch (threshold)
            {
                case NotifyThreshold.Never:
                    return false;
                case NotifyThreshold.FixedOnly:
                    return kind == NotifyEventKind.Fixed;
                case NotifyThreshold.Comments:
                    return kind == NotifyEventKind.Comment || kind == NotifyEventKind.Fixed;
                case NotifyThreshold.All:
                    return kind == NotifyEventKind.Comment || kind == NotifyEventKind.State || kind == NotifyEventKind.Fixed;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Plan a poll. <paramref name="events"/> are the backend feed's events (any order; treated
        /// oldest-first). <paramref name="cursor"/> is the newest event timestamp the player was caught up
        /// on. <paramref name="dismissed"/> is the set of event ids the player closed individually. Returns
        /// the cards to show, the advanced cursor, and the pruned dismissed set (persist both).
        ///
        /// The cursor never moves past an event that is still shown, so a shown-but-not-yet-dismissed card
        /// is re-fetched on the next launch rather than silently lost. Events that are filtered out by the
        /// threshold or already dismissed are "handled" and let the cursor advance over them.
        /// </summary>
        public static NotifyPlan Plan(IEnumerable<NotifyEvent> events, long cursor, ISet<string> dismissed, NotifyThreshold threshold)
        {
            var seen = dismissed ?? new HashSet<string>();
            // Only events strictly newer than the cursor are in play. The server already filters by `since`,
            // but applying it here too makes Plan idempotent: a client-side re-plan after a dismiss (which
            // advances the cursor while reusing the same fetched list) can never resurrect a subsumed event.
            var ordered = (events ?? Enumerable.Empty<NotifyEvent>())
                .Where(e => e != null && !string.IsNullOrEmpty(e.Id) && e.CreatedAt > cursor)
                .OrderBy(e => e.CreatedAt)
                .ToList();

            var visible = ordered.Where(e => PassesThreshold(e.Kind, threshold) && !seen.Contains(e.Id)).ToList();

            long newCursor = cursor;
            if (visible.Count == 0)
            {
                // Nothing to show: caught up to the newest event we know about.
                foreach (var e in ordered)
                    if (e.CreatedAt > newCursor) newCursor = e.CreatedAt;
            }
            else
            {
                // Advance only over events strictly older than the EARLIEST shown event, so every shown card
                // stays above the cursor (hence re-fetchable) until it is dismissed. This also keeps a shown
                // card that shares a timestamp with a handled one from being skipped on the next poll.
                long firstShown = visible[0].CreatedAt;
                foreach (var e in ordered)
                {
                    if (e.CreatedAt >= firstShown) break;
                    if (e.CreatedAt > newCursor) newCursor = e.CreatedAt;
                }
            }

            // Keep only dismissed ids still above the watermark (present this batch, newer than the cursor);
            // everything else is now subsumed by the cursor and can be forgotten, so the set stays small.
            var keep = new HashSet<string>();
            foreach (var e in ordered)
                if (e.CreatedAt > newCursor && seen.Contains(e.Id)) keep.Add(e.Id);

            return new NotifyPlan { Visible = visible, NewCursor = newCursor, Dismissed = keep };
        }
    }
}
