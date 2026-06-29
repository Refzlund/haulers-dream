using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The main-menu report notifications: once per launch it polls the backend for new activity on the
    /// player's OWN reports (new comments, status changes, fixes), and draws a closable card stack in the
    /// bottom-right of the title screen. Clicking a card opens the mod options + that report's in-game thread.
    /// A locally-cached cursor (in settings) means a card shows only while the player is "behind".
    ///
    /// Pumped + drawn from <see cref="Patch_MainMenuDrawer_Notifications"/> (a per-frame postfix on the main
    /// menu). All networking is a single UnityWebRequest pumped per frame on the main thread (no background
    /// thread), mirroring <see cref="Dialog_MyReports"/>. The whole entry point is wrapped so a draw fault can
    /// never break the menu; it degrades to no cards and one logged line.
    /// </summary>
    internal static class ReportNotifications
    {
        // poll lifecycle (per launch)
        private static bool started;
        private static UnityWebRequest req;

        // feed state
        private static List<NotifyEvent> events = new List<NotifyEvent>();   // the last full feed
        private static List<NotifyEvent> visible = new List<NotifyEvent>();  // the cards to draw (planned)
        private static string latestVersion;
        private static bool outdated;
        private static NotifyThreshold plannedThreshold = NotifyThreshold.All;

        // layout
        private const float CardW = 340f;
        private const float MarginRight = 24f;
        private const float MarginBottom = 22f;
        private const float CardGap = 8f;
        private const int MaxCards = 4;

        /// <summary>Per-frame entry: pump the poll, keep the plan current with the threshold, draw the cards.</summary>
        public static void OnMainMenuGUI()
        {
            try
            {
                Pump();
                var s = HaulersDreamMod.Settings;
                // A live threshold change (player edited it in options without relaunching) re-filters cheaply.
                if (s != null && events.Count > 0 && s.notifyThreshold != plannedThreshold)
                    Replan();
                Draw();
            }
            catch (Exception ex)
            {
                HDLog.ErrOnce("main-menu notifications failed: " + ex, unchecked((int)0x4D4E4F54));
            }
        }

        // ---- polling --------------------------------------------------------------------------------

        private static void Pump()
        {
            var s = HaulersDreamMod.Settings;
            if (s == null) return;

            if (!started)
            {
                if (s.notifyThreshold == NotifyThreshold.Never) return;   // opted out: don't poll, don't latch (re-check if turned on)
                string rid = s.reporterId;                                 // RAW field: do NOT generate an id just to poll
                if (rid.NullOrEmpty()) return;                             // no reports yet: re-check after the first one is filed
                started = true;                                            // latch only once a poll actually starts (so a first report this session can still show)
                StartPoll(rid, s.lastSeenEventCursor);
            }

            if (req != null && req.isDone)
            {
                long code = req.responseCode;
                string err = req.error;
                string text = req.downloadHandler != null ? req.downloadHandler.text : null;
                req.Dispose();
                req = null;

                if (code >= 200 && code < 300 && !text.NullOrEmpty())
                    Apply(text);
                else if (code != 0)
                    HDLog.WarnOnce("notification poll failed (HTTP " + code + "): " + (err ?? ""), unchecked((int)0x4E504C31));
                // code == 0 -> offline / network error: stay silent (no cards, no log spam)
            }
        }

        private static void StartPoll(string reporterId, long sinceCursor)
        {
            req = new UnityWebRequest(ReportApi.EventsUrl(sinceCursor), "GET")
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 20
            };
            req.SetRequestHeader("User-Agent", ReportApi.UserAgent());
            req.SetRequestHeader("X-Reporter-Id", reporterId);
            req.SendWebRequest();
        }

        private static void Apply(string text)
        {
            var feed = ReportApi.ParseEvents(text);
            events = feed.events ?? new List<NotifyEvent>();
            latestVersion = feed.latestVersion;
            outdated = VersionCompare.IsOutdated(ReportApi.ModVersionString(), latestVersion);
            Replan();
            HaulersDreamMod.Settings?.Write(); // persist the advanced cursor + pruned dismissed set
        }

        /// <summary>Re-run the pure planner over the last feed: recompute the cards, advance the cursor, prune.</summary>
        private static void Replan()
        {
            var s = HaulersDreamMod.Settings;
            if (s == null) { visible = new List<NotifyEvent>(); return; }
            var dismissed = new HashSet<string>(s.dismissedEventIds ?? new List<string>());
            var plan = NotificationPlanner.Plan(events, s.lastSeenEventCursor, dismissed, s.notifyThreshold);
            visible = plan.Visible;
            s.lastSeenEventCursor = plan.NewCursor;
            s.dismissedEventIds = new List<string>(plan.Dismissed);
            plannedThreshold = s.notifyThreshold;
        }

        // ---- interactions ---------------------------------------------------------------------------

        /// <summary>Open the mod options + the report's in-game thread, and mark the event handled.</summary>
        private static void Open(NotifyEvent e)
        {
            MarkHandled(e);
            if (HaulersDreamMod.Instance != null)
                Find.WindowStack.Add(new Dialog_ModSettings(HaulersDreamMod.Instance));
            if (!e.ReportId.NullOrEmpty())
                Find.WindowStack.Add(new Dialog_MyReports(e.ReportId));
        }

        /// <summary>Dismiss (hide) one card; it never returns once the cursor passes it.</summary>
        private static void Dismiss(NotifyEvent e) => MarkHandled(e);

        private static void MarkHandled(NotifyEvent e)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || e == null || e.Id.NullOrEmpty()) return;
            if (s.dismissedEventIds == null) s.dismissedEventIds = new List<string>();
            if (!s.dismissedEventIds.Contains(e.Id)) s.dismissedEventIds.Add(e.Id);
            Replan();   // recompute visible + advance the cursor over the now-handled event
            s.Write();  // persist immediately (the player can quit straight from the menu)
        }

        // ---- drawing --------------------------------------------------------------------------------

        private static void Draw()
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || s.notifyThreshold == NotifyThreshold.Never) return;
            if (visible == null || visible.Count == 0) return;

            int shown = Mathf.Min(visible.Count, MaxCards);
            int hidden = visible.Count - shown;

            float x = UI.screenWidth - CardW - MarginRight;
            float bottomY = UI.screenHeight - MarginBottom;

            NotifyEvent toOpen = null, toDismiss = null;

            // Draw the newest (end of the oldest-first list) closest to the corner, older cards above it.
            for (int k = 0; k < shown; k++)
            {
                var e = visible[visible.Count - 1 - k];
                bool warn = e.Kind == NotifyEventKind.Fixed && outdated;
                float cardH = warn ? 76f : 56f;
                float y = bottomY - cardH;
                DrawCard(new Rect(x, y, CardW, cardH), e, warn, ref toOpen, ref toDismiss);
                bottomY = y - CardGap;
            }

            if (hidden > 0)
            {
                var prevAnchor = Text.Anchor; var prevFont = Text.Font; var prevCol = GUI.color;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(new Rect(x, bottomY - 18f, CardW, 16f), "HaulersDream.Report.Notify.MoreCount".Translate(hidden));
                Text.Anchor = prevAnchor; Text.Font = prevFont; GUI.color = prevCol;
            }

            // Apply at most one action AFTER the draw loop (MarkHandled re-plans + reassigns `visible`).
            if (toDismiss != null) Dismiss(toDismiss);
            else if (toOpen != null) Open(toOpen);
        }

        private static void DrawCard(Rect card, NotifyEvent e, bool warn, ref NotifyEvent toOpen, ref NotifyEvent toDismiss)
        {
            var prevAnchor = Text.Anchor; var prevFont = Text.Font; var prevCol = GUI.color;

            var statusColor = StatusColorFor(e);
            Widgets.DrawBoxSolid(card, new Color(0.10f, 0.11f, 0.13f, 0.92f));
            Widgets.DrawBox(card, 1);
            Widgets.DrawBoxSolid(new Rect(card.x, card.y, 3f, card.height), statusColor); // left stripe = status colour

            var inner = card.ContractedBy(8f);
            const float closeW = 18f;

            // The status word, coloured by status (open=green, comment=yellow, resolved=purple, closed=red).
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = statusColor;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - closeW - 4f, 22f), StatusWord(e));

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(inner.x, inner.y + 22f, inner.width, 16f), e.Title ?? "");

            if (warn)
            {
                GUI.color = new Color(1f, 0.78f, 0.28f, 0.95f); // amber out-of-date hint
                Widgets.Label(new Rect(inner.x, inner.y + 40f, inner.width, 16f),
                    "HaulersDream.Report.Notify.OutOfDate".Translate(ReportApi.ModVersionString()));
            }

            // close glyph (top-right)
            var xRect = new Rect(card.xMax - closeW - 4f, card.y + 4f, closeW, closeW);
            if (Mouse.IsOver(xRect)) Widgets.DrawHighlight(xRect);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(xRect, "✕");

            Text.Font = prevFont; Text.Anchor = prevAnchor; GUI.color = prevCol;

            // Input: the close glyph first, then the rest of the card (mutually exclusive via else-if).
            if (Widgets.ButtonInvisible(xRect)) toDismiss = e;
            else if (Widgets.ButtonInvisible(card)) toOpen = e;
        }

        // The card's status colour, the "vibe at a glance": a comment is always yellow; otherwise the
        // colour follows the issue status (open=green, resolved=purple, closed=muted red).
        private static Color StatusColorFor(NotifyEvent e)
        {
            if (e.Kind == NotifyEventKind.Comment) return new Color(0.95f, 0.80f, 0.30f); // yellow
            switch (e.Status)
            {
                case "solved": return new Color(0.62f, 0.47f, 0.85f); // purple
                case "closed": return new Color(0.82f, 0.40f, 0.40f); // muted red
                default: return new Color(0.38f, 0.76f, 0.45f);       // open / submitted -> green
            }
        }

        // The coloured status word shown as the card's headline.
        private static string StatusWord(NotifyEvent e)
        {
            if (e.Kind == NotifyEventKind.Comment) return "HaulersDream.Report.Notify.Word.Comment".Translate();
            switch (e.Status)
            {
                case "solved": return "HaulersDream.Report.Notify.Word.Resolved".Translate();
                case "closed": return "HaulersDream.Report.Notify.Word.Closed".Translate();
                default: return "HaulersDream.Report.Notify.Word.Open".Translate();
            }
        }
    }
}
