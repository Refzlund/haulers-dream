using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The entry point of the in-game issue reporter: it lists the player's OWN previously-submitted reports
    /// (with their GitHub issue status) and lets them open one to read its status + comments, post a comment,
    /// or create a new report. When the player has no reports yet, the create dialog opens automatically.
    /// Reads/writes are HTTP requests scoped by the per-install reporter id (sent as X-Reporter-Id), pumped per
    /// frame like <see cref="Dialog_ReportIssue"/> (no background thread, so Verse/Unity reads stay on the main
    /// thread). Comments are relayed to the report's GitHub issue, so they appear here, in the dashboard, and on
    /// GitHub alike.
    /// </summary>
    public class Dialog_MyReports : Window
    {
        private enum View { Loading, List, Detail, Error }
        private enum Pending { None, List, Thread, Comment }

        private View view = View.Loading;
        private Pending pending = Pending.None;
        private string statusMsg = "";
        private UnityWebRequest req;

        private List<ReportSummary> reports;
        private ReportThread thread;
        private string detailReportId;     // the report whose thread is open (for refresh after a comment)
        private string commentDraft = "";
        private string commentMsg = "";    // inline feedback under the comment box
        private bool resetScrollOnThread;
        private bool autoOpenedCreate;

        private Vector2 listScroll;
        private Vector2 detailScroll;

        public Dialog_MyReports()
        {
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(640f, 640f);

        public override void PostOpen()
        {
            base.PostOpen();
            LoadList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            PollRequest();

            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            var prevColor = GUI.color;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "HaulersDream.Report.MyReports.Title".Translate());
            Text.Font = GameFont.Small;

            var body = new Rect(0f, 42f, inRect.width, inRect.height - 42f);
            switch (view)
            {
                case View.Loading:
                    DrawCentered(body, statusMsg.NullOrEmpty() ? "HaulersDream.Report.MyReports.Loading".Translate() : statusMsg);
                    break;
                case View.Error:
                    DrawCentered(new Rect(body.x, body.y, body.width, body.height - 44f), statusMsg);
                    DrawListButtons(inRect);
                    break;
                case View.List:
                    DrawList(body, inRect);
                    break;
                case View.Detail:
                    DrawDetail(body, inRect);
                    break;
            }

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }

        // ---- list view ------------------------------------------------------------------------------

        private void DrawList(Rect body, Rect inRect)
        {
            float btnRowY = inRect.height - 36f;
            var listRect = new Rect(0f, body.y, inRect.width, btnRowY - body.y - 8f);

            if (reports == null || reports.Count == 0)
            {
                DrawCentered(listRect, "HaulersDream.Report.MyReports.Empty".Translate());
            }
            else
            {
                const float rowH = 54f;
                var viewRect = new Rect(0f, 0f, listRect.width - 16f, reports.Count * rowH);
                Widgets.BeginScrollView(listRect, ref listScroll, viewRect);
                float y = 0f;
                foreach (var r in reports)
                {
                    var row = new Rect(0f, y, viewRect.width, rowH - 4f);
                    if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                    DrawReportRow(row, r);
                    if (Widgets.ButtonInvisible(row)) LoadThread(r);
                    y += rowH;
                }
                Widgets.EndScrollView();
            }

            DrawListButtons(inRect);
        }

        private void DrawListButtons(Rect inRect)
        {
            float btnRowY = inRect.height - 36f;
            var createRect = new Rect(0f, btnRowY, 200f, 32f);
            if (Widgets.ButtonText(createRect, "HaulersDream.Report.MyReports.CreateNew".Translate()))
                OpenCreate();
            var refreshRect = new Rect(createRect.xMax + 8f, btnRowY, 120f, 32f);
            if (Widgets.ButtonText(refreshRect, "HaulersDream.Report.MyReports.Refresh".Translate()))
                LoadList();
        }

        private void DrawReportRow(Rect row, ReportSummary r)
        {
            var prevAnchor = Text.Anchor;
            var inner = row.ContractedBy(6f);

            const float pillW = 96f;
            DrawStatusPill(new Rect(inner.xMax - pillW, inner.y + 2f, pillW, 22f), r.status);

            Text.Anchor = TextAnchor.UpperLeft;
            string title = r.title.NullOrEmpty() ? "(" + TypeLabel(r.type) + ")" : r.title;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - pillW - 8f, 24f), title);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(inner.x, inner.y + 24f, inner.width, 18f), SubLine(r));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = prevAnchor;
        }

        // ---- detail view ----------------------------------------------------------------------------

        private void DrawDetail(Rect body, Rect inRect)
        {
            float btnRowY = inRect.height - 36f;
            bool hasIssue = thread != null && thread.issueNumber > 0;
            float commentAreaH = hasIssue ? 76f : 24f;
            float commentY = btnRowY - 8f - commentAreaH;

            var content = new Rect(0f, body.y, inRect.width, commentY - body.y - 8f);
            float width = content.width - 16f;
            int count = thread?.comments?.Count ?? 0;

            // measure scroll content
            float h = 26f; // status line
            if (thread != null && !thread.comment.NullOrEmpty()) h += Text.CalcHeight(thread.comment, width) + 14f;
            h += 26f; // comments header
            if (thread != null && thread.commentsUnavailable) h += 24f;
            else if (count == 0) h += 24f;
            else if (thread != null)
                foreach (var c in thread.comments) h += CommentHeight(c, width) + 8f;

            var viewRect = new Rect(0f, 0f, width, Mathf.Max(h, content.height));
            Widgets.BeginScrollView(content, ref detailScroll, viewRect);
            float y = 0f;

            string statusLine = "HaulersDream.Report.Detail.Status".Translate(StatusLabel(thread?.status));
            if (hasIssue) statusLine += "  ·  #" + ((long)thread.issueNumber).ToString(CultureInfo.InvariantCulture);
            Widgets.Label(new Rect(0f, y, width, 24f), statusLine);
            y += 26f;

            if (thread != null && !thread.comment.NullOrEmpty())
            {
                float ch = Text.CalcHeight(thread.comment, width);
                Widgets.Label(new Rect(0f, y, width, ch), thread.comment);
                y += ch + 14f;
            }

            Widgets.Label(new Rect(0f, y, width, 24f), "HaulersDream.Report.Detail.CommentsHeader".Translate(count));
            y += 26f;

            if (thread != null && thread.commentsUnavailable)
            {
                DrawDim(new Rect(0f, y, width, 24f), "HaulersDream.Report.Detail.Unavailable".Translate());
            }
            else if (count == 0)
            {
                DrawDim(new Rect(0f, y, width, 24f), "HaulersDream.Report.Detail.NoComments".Translate());
            }
            else
            {
                foreach (var c in thread.comments)
                {
                    float bh = CommentHeight(c, width);
                    var box = new Rect(0f, y, width, bh);
                    Widgets.DrawBoxSolid(box, RoleTint(c.role));
                    var inner = box.ContractedBy(6f);
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(1f, 1f, 1f, 0.6f);
                    Widgets.Label(new Rect(inner.x, inner.y, inner.width, 16f), CommentAuthor(c) + " · " + FmtIso(c.createdAt));
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    Widgets.Label(new Rect(inner.x, inner.y + 18f, inner.width, inner.height - 18f), c.body ?? "");
                    y += bh + 8f;
                }
            }

            Widgets.EndScrollView();

            // comment composer (only when the report is on the tracker)
            if (hasIssue)
            {
                var inputRect = new Rect(0f, commentY, inRect.width - 96f, commentAreaH);
                string edited = Widgets.TextArea(inputRect, commentDraft ?? "");
                if (edited != commentDraft)
                {
                    commentDraft = edited.Length > ReportApi.MaxCommentChars
                        ? edited.Substring(0, ReportApi.MaxCommentChars)
                        : edited;
                }

                var sendRect = new Rect(inRect.width - 88f, commentY, 88f, 30f);
                bool sending = pending == Pending.Comment;
                if (sending)
                {
                    DrawDim(new Rect(sendRect.x, commentY + 34f, 88f, 40f), "HaulersDream.Report.Detail.Sending".Translate());
                }
                else if (Widgets.ButtonText(sendRect, "HaulersDream.Report.Detail.Send".Translate()))
                {
                    SendComment();
                }
            }
            else
            {
                DrawDim(new Rect(0f, commentY, inRect.width, 24f), "HaulersDream.Report.Detail.NoIssue".Translate());
            }

            var backRect = new Rect(0f, btnRowY, 120f, 32f);
            if (Widgets.ButtonText(backRect, "HaulersDream.Report.Detail.Back".Translate()))
            {
                view = View.List;
                thread = null;
                commentDraft = "";
                commentMsg = "";
            }
            float x = backRect.xMax + 8f;
            if (thread != null && !thread.url.NullOrEmpty())
            {
                var ghRect = new Rect(x, btnRowY, 190f, 32f);
                if (Widgets.ButtonText(ghRect, "HaulersDream.Report.Detail.OpenOnGitHub".Translate()))
                    Application.OpenURL(thread.url);
                x = ghRect.xMax + 8f;
            }
            if (!commentMsg.NullOrEmpty())
                DrawDim(new Rect(x, btnRowY + 6f, inRect.width - x, 24f), commentMsg);
        }

        private static float CommentHeight(IssueComment c, float width)
        {
            float bodyH = Text.CalcHeight(c.body.NullOrEmpty() ? " " : c.body, width - 12f);
            return 18f + bodyH + 12f; // header line + body + padding
        }

        // ---- networking (mirrors Dialog_ReportIssue's per-frame poll) -------------------------------

        private void LoadList()
        {
            AbortReq();
            reports = null;
            thread = null;
            view = View.Loading;
            statusMsg = "HaulersDream.Report.MyReports.Loading".Translate();
            pending = Pending.List;
            StartGet(ReportApi.MyReportsUrl());
        }

        private void LoadThread(ReportSummary r)
        {
            AbortReq();
            thread = null;
            detailReportId = r.id;
            commentDraft = "";
            commentMsg = "";
            view = View.Loading;
            statusMsg = "HaulersDream.Report.Detail.Loading".Translate();
            pending = Pending.Thread;
            resetScrollOnThread = true;
            StartGet(ReportApi.ThreadUrl(r.id));
        }

        /// <summary>Re-fetch the open thread without flipping to the loading view (used after a comment posts).</summary>
        private void RefreshThreadSilent()
        {
            if (detailReportId.NullOrEmpty()) return;
            AbortReq();
            pending = Pending.Thread;
            resetScrollOnThread = false;
            StartGet(ReportApi.ThreadUrl(detailReportId));
        }

        private void SendComment()
        {
            if (commentDraft.NullOrEmpty() || commentDraft.Trim().Length == 0)
            {
                commentMsg = "HaulersDream.Report.Detail.CommentEmpty".Translate();
                return;
            }
            if (pending != Pending.None || detailReportId.NullOrEmpty()) return;
            commentMsg = "";
            pending = Pending.Comment;
            StartPost(ReportApi.CommentUrl(detailReportId), ReportApi.BuildCommentJson(commentDraft));
        }

        private void StartGet(string url)
        {
            req = new UnityWebRequest(url, "GET")
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 30
            };
            req.SetRequestHeader("User-Agent", ReportApi.UserAgent());
            req.SetRequestHeader("X-Reporter-Id", ReportApi.ReporterId());
            req.SendWebRequest();
        }

        private void StartPost(string url, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json ?? "{}");
            req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(bytes),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 30
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("User-Agent", ReportApi.UserAgent());
            req.SetRequestHeader("X-Reporter-Id", ReportApi.ReporterId());
            req.SendWebRequest();
        }

        private void PollRequest()
        {
            if (pending == Pending.None || req == null || !req.isDone)
                return;

            long code = req.responseCode;
            string err = req.error;
            string text = req.downloadHandler != null ? req.downloadHandler.text : null;
            var which = pending;
            req.Dispose();
            req = null;
            pending = Pending.None;

            bool ok = code >= 200 && code < 300;

            if (which == Pending.List)
            {
                if (!ok) { ShowError(code, err, text); return; }
                reports = ReportApi.ParseMyReports(text) ?? new List<ReportSummary>();
                view = View.List;
                if (reports.Count == 0 && !autoOpenedCreate)
                {
                    autoOpenedCreate = true; // open the create dialog once when the player has no reports yet
                    OpenCreate();
                }
            }
            else if (which == Pending.Thread)
            {
                if (!ok)
                {
                    // A silent refresh (after a comment posted) must NOT yank the player out of the detail
                    // view: keep the current thread + the "sent" message and let them reopen to see it. Only
                    // the initial open shows the full error screen.
                    if (!resetScrollOnThread && thread != null) { HDLog.Warn("thread refresh failed (HTTP " + code + ")"); return; }
                    ShowError(code, err, text);
                    return;
                }
                thread = ReportApi.ParseThread(text);
                if (thread == null) { ShowError(code, err, text); return; }
                view = View.Detail;
                if (resetScrollOnThread) detailScroll = Vector2.zero;
            }
            else // Comment
            {
                if (!ok)
                {
                    commentMsg = code == 0
                        ? "HaulersDream.Report.MyReports.NetworkError".Translate(err ?? "")
                        : "HaulersDream.Report.Detail.CommentFailed".Translate(code.ToString());
                    HDLog.Warn("comment post failed (HTTP " + code + "): " + (err ?? text));
                    return;
                }
                commentDraft = "";
                commentMsg = "HaulersDream.Report.Detail.CommentPosted".Translate();
                RefreshThreadSilent(); // pull the new comment back so it shows immediately
            }
        }

        private void ShowError(long code, string err, string respText)
        {
            view = View.Error;
            statusMsg = code == 0
                ? "HaulersDream.Report.MyReports.NetworkError".Translate(err ?? "")
                : "HaulersDream.Report.MyReports.LoadError".Translate(code.ToString());
            HDLog.Warn("my-reports load failed (HTTP " + code + "): " + (err ?? respText));
        }

        private void OpenCreate()
        {
            // Refresh the list after a successful submit so the new report appears with its status.
            Find.WindowStack.Add(new Dialog_ReportIssue(LoadList));
        }

        private void AbortReq()
        {
            if (req != null) { req.Abort(); req.Dispose(); req = null; }
            pending = Pending.None;
        }

        public override void PostClose()
        {
            base.PostClose();
            AbortReq();
        }

        // ---- helpers --------------------------------------------------------------------------------

        private static void DrawCentered(Rect rect, string text)
        {
            var prev = Text.Anchor;
            var prevColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(rect, text);
            Text.Anchor = prev;
            GUI.color = prevColor;
        }

        private static void DrawDim(Rect rect, string text)
        {
            var prevColor = GUI.color;
            var prevFont = Text.Font;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(rect, text);
            GUI.color = prevColor;
            Text.Font = prevFont;
        }

        private static void DrawStatusPill(Rect rect, string status)
        {
            Color bg =
                status == "open" ? new Color(0.16f, 0.5f, 0.28f, 0.5f)
                : status == "solved" ? new Color(0.4f, 0.3f, 0.62f, 0.55f)
                : status == "closed" ? new Color(0.5f, 0.22f, 0.22f, 0.5f)
                : new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawBoxSolid(rect, bg);
            var prevAnchor = Text.Anchor;
            var prevFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, StatusSymbol(status) + " " + StatusLabel(status));
            Text.Anchor = prevAnchor;
            Text.Font = prevFont;
        }

        private static string StatusSymbol(string status)
        {
            switch (status)
            {
                case "open": return "●";
                case "solved": return "✓";
                case "closed": return "✕";
                default: return "○";
            }
        }

        private static string StatusLabel(string status)
        {
            switch (status)
            {
                case "open": return "HaulersDream.Report.Status.Open".Translate();
                case "solved": return "HaulersDream.Report.Status.Solved".Translate();
                case "closed": return "HaulersDream.Report.Status.Closed".Translate();
                default: return "HaulersDream.Report.Status.Submitted".Translate();
            }
        }

        private static Color RoleTint(string role)
        {
            switch (role)
            {
                case "reporter": return new Color(0.3f, 0.5f, 0.85f, 0.12f); // the player's own comment
                case "maintainer": return new Color(0.16f, 0.5f, 0.28f, 0.12f); // the developer
                default: return new Color(1f, 1f, 1f, 0.04f);
            }
        }

        private static string CommentAuthor(IssueComment c)
        {
            switch (c.role)
            {
                case "reporter": return "HaulersDream.Report.Role.You".Translate();
                case "maintainer": return "HaulersDream.Report.Role.Developer".Translate();
                default: return c.author.NullOrEmpty() ? "GitHub" : c.author;
            }
        }

        private static string TypeLabel(string type)
        {
            switch (type)
            {
                case "bug": return "HaulersDream.Report.Type.Bug".Translate();
                case "feature": return "HaulersDream.Report.Type.Feature".Translate();
                case "compatibility": return "HaulersDream.Report.Type.Compatibility".Translate();
                default: return "HaulersDream.Report.Type.Other".Translate();
            }
        }

        private static string SubLine(ReportSummary r)
        {
            string t = FmtTime(r.createdAt);
            return r.type.NullOrEmpty() ? t : TypeLabel(r.type) + " · " + t;
        }

        private static string FmtTime(double ms)
        {
            // Guard the valid Unix-ms range (FromUnixTimeMilliseconds throws beyond ~year 9999) so a garbage
            // timestamp can't spam a per-frame exception from the list draw.
            if (ms <= 0 || ms > 253402300799999d) return "";
            var dt = System.DateTimeOffset.FromUnixTimeMilliseconds((long)ms).LocalDateTime;
            return dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static string FmtIso(string iso)
        {
            if (!iso.NullOrEmpty() && System.DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return iso ?? "";
        }
    }
}
