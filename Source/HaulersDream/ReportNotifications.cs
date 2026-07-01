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
    /// The main-menu report notifications: once per launch it polls the backend for the current status of the
    /// player's OWN reports, collapses each to a SINGLE card (its status, escalated to a "comment" card when
    /// there is an unseen reply), and draws a closable stack in the bottom-right of the title screen. Clicking a
    /// card opens the mod options + that report's in-game thread; pressing x hides it until genuinely new activity.
    ///
    /// Two per-report watermarks in settings drive it: <c>notifySeenComment</c> (advanced on click, so a comment
    /// card falls back to the plain status) and <c>notifyDismissed</c> (advanced on x). So only one card per
    /// issue is ever present, and it changes (e.g. open -> commented) as activity arrives.
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
        private static List<ReportStatus> reports = new List<ReportStatus>(); // the last full per-report feed
        private static List<NotifyCard> visible = new List<NotifyCard>();     // the cards to draw (planned)
        private static string latestVersion;
        private static bool outdated;
        private static NotifyThreshold plannedThreshold = NotifyThreshold.All;

        // layout — a small, quiet translucent card (status line + title/source line)
        private const float CardW = 300f;
        private const float CardH = 50f;
        private const float CardHWarn = 68f;   // taller when the out-of-date warning row shows
        private const float MarginRight = 24f;
        private const float MarginBottom = 22f;
        private const float CardGap = 7f;
        private const int MaxCards = 4;

        /// <summary>Per-frame entry: pump the poll, keep the plan current with the threshold, draw the cards.</summary>
        public static void OnMainMenuGUI()
        {
            try
            {
                Pump();
                var s = HaulersDreamMod.Settings;
                // A live threshold change (player edited it in options without relaunching) re-filters cheaply.
                if (s != null && reports.Count > 0 && s.notifyThreshold != plannedThreshold)
                    Replan();
                Draw();
            }
            catch (Exception ex)
            {
                HDLog.ErrOnce("main-menu notifications failed: " + ex, unchecked((int)0x4D4E4F54));
            }
        }

        /// <summary>
        /// Force a fresh poll on the next main-menu frame (call right after the player files a report, so its
        /// card appears without a relaunch). Aborts any in-flight poll, clears the once-per-launch latch and the
        /// cached feed. Safe to call any time on the main thread; harmless when the menu is not showing.
        /// </summary>
        public static void Refresh()
        {
            if (req != null) { req.Abort(); req.Dispose(); req = null; }
            started = false;
            reports = new List<ReportStatus>();
            visible = new List<NotifyCard>();
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
                StartPoll(rid);
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

        private static void StartPoll(string reporterId)
        {
            req = new UnityWebRequest(ReportApi.StatusUrl(), "GET")
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
            var feed = ReportApi.ParseReportStatuses(text);
            reports = feed.reports ?? new List<ReportStatus>();
            latestVersion = feed.latestVersion;
            outdated = VersionCompare.IsOutdated(ReportApi.ModVersionString(), latestVersion);
            Replan();
            // No watermark is advanced by polling — only an explicit click/x mutates settings — so nothing to persist here.
        }

        /// <summary>Re-run the pure planner over the last feed: one card per report, filtered by the threshold.</summary>
        private static void Replan()
        {
            var s = HaulersDreamMod.Settings;
            if (s == null) { visible = new List<NotifyCard>(); return; }
            visible = ReportNotifyPlanner.Plan(reports, s.notifySeenComment, s.notifyDismissed, s.notifyThreshold);
            plannedThreshold = s.notifyThreshold;
        }

        // ---- interactions ---------------------------------------------------------------------------

        /// <summary>Click a card: mark its replies seen (so it reverts from "comment" to its status), then open
        /// the mod options + the report's in-game thread.</summary>
        private static void Open(NotifyCard c)
        {
            MarkSeen(c);
            if (HaulersDreamMod.Instance != null)
                Find.WindowStack.Add(new Dialog_ModSettings(HaulersDreamMod.Instance));
            if (!c.ReportId.NullOrEmpty())
                Find.WindowStack.Add(new Dialog_MyReports(c.ReportId));
        }

        // Click = "I've seen this report's replies": advance seenComment to its last comment and clear any dismiss
        // watermark, so the card drops back from "comment" to its plain status (and re-escalates on the NEXT reply).
        private static void MarkSeen(NotifyCard c)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || c == null || c.ReportId.NullOrEmpty()) return;
            if (s.notifySeenComment == null) s.notifySeenComment = new Dictionary<string, long>();
            s.notifySeenComment[c.ReportId] = ReportNotifyPlanner.SeenAfterClick(AsStatus(c));
            s.notifyDismissed?.Remove(c.ReportId); // un-dismiss: the status card returns
            Replan();
            s.Write(); // persist immediately (the player can quit straight from the menu)
        }

        // x = "hide this until something new happens": set the dismiss watermark to the report's current activity
        // time; the card returns only when a newer comment or status change arrives.
        private static void Dismiss(NotifyCard c)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || c == null || c.ReportId.NullOrEmpty()) return;
            if (s.notifyDismissed == null) s.notifyDismissed = new Dictionary<string, long>();
            s.notifyDismissed[c.ReportId] = ReportNotifyPlanner.DismissedAfterX(AsStatus(c));
            Replan();
            s.Write();
        }

        // A card mirrors its report's StatusAt/LastCommentAt, so a lightweight ReportStatus reconstructs the exact
        // inputs the planner's watermark helpers expect (keeping that math in the planner, the single source).
        private static ReportStatus AsStatus(NotifyCard c) => new ReportStatus
        {
            ReportId = c.ReportId, Title = c.Title, Url = c.Url,
            Status = c.Status, StatusAt = c.StatusAt, LastCommentAt = c.LastCommentAt
        };

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

            NotifyCard toOpen = null, toDismiss = null;
            bool toRefresh = false;

            // The plan is newest-activity-first; draw the newest closest to the corner, older cards above it.
            for (int k = 0; k < shown; k++)
            {
                var c = visible[k];
                bool warn = c.Kind == NotifyCardKind.Resolved && outdated;
                float cardH = warn ? CardHWarn : CardH;
                float y = bottomY - cardH;
                DrawCard(new Rect(x, y, CardW, cardH), c, warn, ref toOpen, ref toDismiss, ref toRefresh);
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

            // Apply at most one action AFTER the draw loop (each re-plans + reassigns `visible`).
            if (toRefresh) Refresh();
            else if (toDismiss != null) Dismiss(toDismiss);
            else if (toOpen != null) Open(toOpen);
        }

        private static void DrawCard(Rect card, NotifyCard c, bool warn, ref NotifyCard toOpen, ref NotifyCard toDismiss, ref bool toRefresh)
        {
            var prevAnchor = Text.Anchor; var prevFont = Text.Font; var prevCol = GUI.color;
            var statusColor = StatusColorFor(c);

            // A quiet translucent scrim (no window/border) so the card reads as a light overlay, not a panel.
            Widgets.DrawBoxSolid(card, new Color(0.05f, 0.06f, 0.08f, 0.55f));
            if (Mouse.IsOver(card)) Widgets.DrawHighlight(card);

            const float pad = 9f;
            const float dot = 7f;
            const float btn = 16f;    // icon-button size (refresh + close)
            const float btnGap = 5f;
            float textX = card.x + pad + dot + 7f;   // status + title left edge, just past the status dot

            // Top line: a small status-coloured dot + the status word (the only splash of colour), then a
            // refresh button and the close glyph, both aligned to this line (they brighten on hover).
            var xRect = new Rect(card.xMax - pad - btn, card.y + 7f, btn, btn);
            var refreshRect = new Rect(xRect.x - btnGap - btn, card.y + 7f, btn, btn);
            bool overX = Mouse.IsOver(xRect);
            bool overRefresh = Mouse.IsOver(refreshRect);

            GUI.color = statusColor;
            GUI.DrawTexture(new Rect(card.x + pad, card.y + 12f, dot, dot), DotTex);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Lighten(statusColor);
            Widgets.Label(new Rect(textX, card.y + 4f, refreshRect.x - textX - 6f, 24f), StatusWord(c));

            // Refresh: re-pulls the latest state (the same as re-launching the game).
            GUI.color = new Color(1f, 1f, 1f, overRefresh ? 0.95f : 0.5f);
            GUI.DrawTexture(refreshRect.ContractedBy(1f), RefreshTex);
            if (overRefresh) TooltipHandler.TipRegion(refreshRect, "HaulersDream.Report.MyReports.Refresh".Translate());

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 1f, 1f, overX ? 0.95f : 0.42f);
            Widgets.Label(xRect, "✕");

            // Second line: the report title (left) + the mod name (right, faded so the source is there but quiet).
            float line2Y = card.y + 28f;
            const float sourceW = 92f;
            var srcRect = new Rect(card.xMax - pad - sourceW, line2Y, sourceW, 16f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(textX, line2Y, srcRect.x - textX - 8f, 16f), c.Title ?? "");

            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(1f, 1f, 1f, 0.32f);
            Widgets.Label(srcRect, "HaulersDream.Report.Notify.Source".Translate());

            if (warn)
            {
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = new Color(1f, 0.78f, 0.28f, 0.9f); // amber out-of-date hint
                Widgets.Label(new Rect(textX, line2Y + 16f, card.xMax - pad - textX, 16f),
                    "HaulersDream.Report.Notify.OutOfDate".Translate(ReportApi.ModVersionString()));
            }

            Text.Font = prevFont; Text.Anchor = prevAnchor; GUI.color = prevCol;

            // Input: the two glyph buttons first, then the rest of the card (mutually exclusive via else-if).
            if (Widgets.ButtonInvisible(refreshRect)) toRefresh = true;
            else if (Widgets.ButtonInvisible(xRect)) toDismiss = c;
            else if (Widgets.ButtonInvisible(card)) toOpen = c;
        }

        // A soft round dot texture (generated once, tinted per-status at draw time) for the status marker.
        private static Texture2D dotTex;
        private static Texture2D DotTex
        {
            get
            {
                if (dotTex == null)
                {
                    const int s = 16;
                    var tex = new Texture2D(s, s, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp };
                    var px = new Color[s * s];
                    float r = s / 2f - 0.5f;
                    var mid = new Vector2(s / 2f - 0.5f, s / 2f - 0.5f);
                    for (int y = 0; y < s; y++)
                        for (int x = 0; x < s; x++)
                            px[y * s + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - Vector2.Distance(new Vector2(x, y), mid) + 0.5f));
                    tex.SetPixels(px);
                    tex.Apply();
                    dotTex = tex;
                }
                return dotTex;
            }
        }

        // A circular-arrow "refresh" icon (generated once, tinted at draw time): a ring with a gap and a small
        // arrowhead at the gap. Drawn white so GUI.color can tint it.
        private static Texture2D refreshTex;
        private static Texture2D RefreshTex
        {
            get
            {
                if (refreshTex == null)
                {
                    const int s = 32;
                    var tex = new Texture2D(s, s, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp };
                    var px = new Color[s * s];
                    float cx = (s - 1) / 2f, cy = (s - 1) / 2f;
                    const float rMid = 9.5f, band = 2.0f;
                    const float gapLo = 35f, gapHi = 95f; // the open wedge (degrees)

                    // Arrowhead at the gapLo end of the arc, pointing along the clockwise tangent.
                    float ar = gapLo * Mathf.Deg2Rad;
                    var pEnd = new Vector2(cx + rMid * Mathf.Cos(ar), cy + rMid * Mathf.Sin(ar));
                    var radial = new Vector2(Mathf.Cos(ar), Mathf.Sin(ar));
                    var tangent = new Vector2(Mathf.Sin(ar), -Mathf.Cos(ar));
                    var tip = pEnd + tangent * 6.5f;
                    var wing1 = pEnd + radial * 4.5f;
                    var wing2 = pEnd - radial * 4.5f;

                    for (int y = 0; y < s; y++)
                        for (int x = 0; x < s; x++)
                        {
                            var p = new Vector2(x, y);
                            float d = Vector2.Distance(p, new Vector2(cx, cy));
                            float deg = Mathf.Atan2(y - cy, x - cx) * Mathf.Rad2Deg;
                            if (deg < 0f) deg += 360f;
                            float a = 0f;
                            if (deg < gapLo || deg > gapHi) a = Mathf.Clamp01(band - Mathf.Abs(d - rMid) + 0.5f);
                            if (InTriangle(p, tip, wing1, wing2)) a = 1f;
                            px[y * s + x] = new Color(1f, 1f, 1f, a);
                        }
                    tex.SetPixels(px);
                    tex.Apply();
                    refreshTex = tex;
                }
                return refreshTex;
            }
        }

        // Whether a point lies inside the triangle (a, b, c) — used to stamp the refresh arrowhead.
        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        // Nudge a status colour toward white so the word stays legible on the dark scrim.
        private static Color Lighten(Color c, float t = 0.28f) =>
            new Color(Mathf.Lerp(c.r, 1f, t), Mathf.Lerp(c.g, 1f, t), Mathf.Lerp(c.b, 1f, t), 1f);

        // The card's status colour, the "vibe at a glance": a comment is always yellow; otherwise the colour
        // follows the issue status (open=green, resolved=purple, closed=muted red).
        private static Color StatusColorFor(NotifyCard c)
        {
            switch (c.Kind)
            {
                case NotifyCardKind.Comment: return new Color(0.95f, 0.80f, 0.30f);  // yellow
                case NotifyCardKind.Resolved: return new Color(0.62f, 0.47f, 0.85f); // purple
                case NotifyCardKind.Closed: return new Color(0.82f, 0.40f, 0.40f);   // muted red
                default: return new Color(0.38f, 0.76f, 0.45f);                       // Open -> green
            }
        }

        // The coloured status word shown as the card's headline.
        private static string StatusWord(NotifyCard c)
        {
            switch (c.Kind)
            {
                case NotifyCardKind.Comment: return "HaulersDream.Report.Notify.Word.Comment".Translate();
                case NotifyCardKind.Resolved: return "HaulersDream.Report.Notify.Word.Resolved".Translate();
                case NotifyCardKind.Closed: return "HaulersDream.Report.Notify.Word.Closed".Translate();
                default: return "HaulersDream.Report.Notify.Word.Open".Translate();
            }
        }
    }
}
