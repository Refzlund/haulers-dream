using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// In-game "Report issue" dialog: pick an issue type, describe it, optionally attach the full game log and
    /// Steam screenshots, and POST it to the Hauler's Dream report backend. Sending is two stages — first the report
    /// (JSON), then each screenshot to the new report's attachment endpoint. Every request is a
    /// <see cref="UnityWebRequest"/> pumped by Unity's loop and polled each frame in <see cref="DoWindowContents"/>
    /// (no background thread, so all Verse/Unity reads stay on the main thread).
    /// </summary>
    public class Dialog_ReportIssue : Window
    {
        private enum ReportKind { Bug, Feature, Compatibility, Other }
        private enum Phase { Editing, Sending, Sent, Failed }
        private enum Stage { Report, Attachment }

        private struct TypeDef
        {
            public ReportKind kind;
            public string id;              // sent as meta.type
            public string labelKey;
            public string placeholderKey;  // ghost text guiding what to write for this report kind
            public TypeDef(ReportKind kind, string id, string labelKey, string placeholderKey)
            { this.kind = kind; this.id = id; this.labelKey = labelKey; this.placeholderKey = placeholderKey; }
        }

        private static readonly TypeDef[] Types =
        {
            new TypeDef(ReportKind.Bug,           "bug",           "HaulersDream.Report.Type.Bug",           "HaulersDream.Report.Placeholder.Bug"),
            new TypeDef(ReportKind.Feature,       "feature",       "HaulersDream.Report.Type.Feature",       "HaulersDream.Report.Placeholder.Feature"),
            new TypeDef(ReportKind.Compatibility, "compatibility", "HaulersDream.Report.Type.Compatibility", "HaulersDream.Report.Placeholder.Compatibility"),
            new TypeDef(ReportKind.Other,         "other",         "HaulersDream.Report.Type.Other",         "HaulersDream.Report.Placeholder.Other"),
        };

        private ReportKind kind = ReportKind.Bug;
        private string description = "";
        private bool includeGameLog = true;
        private readonly List<string> attachments = new List<string>(); // selected screenshot file paths
        private Vector2 descScroll;

        private Phase phase = Phase.Editing;
        private Stage stage = Stage.Report;
        private string statusMsg = "";
        private string sentNote = "";
        private UnityWebRequest req;
        private string reportId;
        private int attachIndex;
        private int attachFailed;

        // Optional callback fired once after a report is successfully submitted (the My-reports view uses it to
        // refresh its list). Null when the dialog is opened standalone.
        private readonly System.Action onSent;

        public Dialog_ReportIssue(System.Action onSent = null)
        {
            this.onSent = onSent;
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false; // don't lose a half-written report on a stray outside click
            draggable = true;
            // Let Enter start a new line in the description instead of catching it as the window's accept key.
            // WindowStack.Notify_PressedAccept only steals Return/KeypadEnter from a window whose closeOnAccept is
            // true (its default), before DoWindowContents runs, so the text area never sees the key. Every vanilla
            // dialog with a text field turns this off for the same reason; the report is submitted with the Send
            // button, never Enter, so nothing else depends on the accept key. Escape still closes (closeOnCancel).
            closeOnAccept = false;
        }

        public override Vector2 InitialSize => new Vector2(560f, 560f);

        public override void DoWindowContents(Rect inRect)
        {
            PollRequest(); // reflect an in-flight request's result this frame

            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            var prevColor = GUI.color;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "HaulersDream.Report.DialogTitle".Translate());
            Text.Font = GameFont.Small;

            if (phase == Phase.Sent)
            {
                DrawSent(inRect);
                Text.Font = prevFont;
                Text.Anchor = prevAnchor;
                GUI.color = prevColor;
                return;
            }

            bool editable = phase == Phase.Editing || phase == Phase.Failed;
            float y = 40f;

            // Intro line.
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(0f, y, inRect.width, 30f), "HaulersDream.Report.DialogIntro".Translate());
            GUI.color = prevColor;
            Text.Font = GameFont.Small;
            y += 30f;

            // Issue type — 2x2 grid of radio buttons.
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), "HaulersDream.Report.TypeLabel".Translate());
            y += 26f;
            const float colGap = 8f;
            float colW = (inRect.width - colGap) / 2f;
            for (int i = 0; i < Types.Length; i++)
            {
                int col = i % 2, rowIdx = i / 2;
                var rr = new Rect(col * (colW + colGap), y + rowIdx * 30f, colW, 28f);
                if (Widgets.RadioButtonLabeled(rr, Types[i].labelKey.Translate(), kind == Types[i].kind) && editable)
                    kind = Types[i].kind;
            }
            y += 2f * 30f + 8f;

            // Description.
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), "HaulersDream.Report.DescribeLabel".Translate());
            y += 24f;
            float btnRowY = inRect.height - 36f;
            const float checkH = 26f, shotH = 34f, noteH = 22f;
            // Reserve the status line's height from the ACTUAL rendered message, not a fixed guess. A fixed
            // height clips when (a) a long localized NetworkError wraps its second line onto a third (Polish /
            // Russian / German / Japanese all overflow the inner width), or (b) Tiny is unavailable
            // (DisableTinyText / Steam Deck / a language whose font canBeTiny is false), where the game
            // substitutes the taller Small font at draw time. So we set Text.Font = Tiny FIRST (which flips to
            // Small when unsupported, exactly as the draw will), then measure the real message via CalcHeight
            // (it wraps at inRect.width and uses the current font). A two-Tiny-line floor keeps a sane reserve
            // when the message is empty/short. The description area shrinks to compensate (it has its own 80f
            // floor), so even a three-line message never overlaps the button row.
            var prevFontForMetric = Text.Font;
            Text.Font = GameFont.Tiny;
            float twoLineFloor = Text.LineHeightOf(GameFont.Tiny) * 2f + 2f;
            float statusH = statusMsg.NullOrEmpty()
                ? twoLineFloor
                : Mathf.Max(twoLineFloor, Text.CalcHeight(statusMsg, inRect.width));
            Text.Font = prevFontForMetric;
            float reserveBelowDesc = 6f + checkH + shotH + noteH + statusH + 8f;
            var descRect = new Rect(0f, y, inRect.width, Mathf.Max(80f, btnRowY - reserveBelowDesc - y));
            description = WidgetsCompat.TextAreaScrollable(descRect, description, ref descScroll);
            if (description != null && description.Length > ReportApi.MaxCommentChars)
                description = description.Substring(0, ReportApi.MaxCommentChars);
            // Greyed placeholder guiding what to write, drawn over the (empty) description box. It swaps with the
            // selected report type and vanishes the moment the player types. Mirrors the settings search box's
            // empty-field placeholder; a Label is non-interactive, so it never eats the text area's input.
            if (editable && description.NullOrEmpty())
            {
                var phFont = Text.Font;
                var phAnchor = Text.Anchor;
                var phColor = GUI.color;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = new Color(0.7f, 0.7f, 0.74f, 0.6f);
                var ph = descRect;
                ph.xMin += 6f;   // match the text area's left text inset
                ph.yMin += 4f;
                ph.xMax -= 16f;  // stay clear of the scroll gutter so lines wrap close to where real text would
                Widgets.Label(ph, PlaceholderKey().Translate());
                GUI.color = phColor;
                Text.Anchor = phAnchor;
                Text.Font = phFont;
            }
            y = descRect.yMax + 6f;

            // Attach full game log toggle.
            var logRect = new Rect(0f, y, inRect.width, checkH);
            Widgets.CheckboxLabeled(logRect, "HaulersDream.Report.IncludeLog".Translate(), ref includeGameLog);
            TooltipHandler.TipRegion(logRect, "HaulersDream.Report.IncludeLog.Help".Translate());
            y += checkH;

            // Attach screenshots.
            var addRect = new Rect(0f, y, 200f, 28f);
            if (Widgets.ButtonText(addRect, "HaulersDream.Report.AddScreenshots".Translate(), active: editable) && editable)
                Find.WindowStack.Add(new Dialog_PickScreenshots(attachments, ReportApi.MaxAttachments,
                    sel => { attachments.Clear(); attachments.AddRange(sel); }));
            if (attachments.Count > 0)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                Widgets.Label(new Rect(addRect.xMax + 10f, y + 6f, 130f, 22f),
                    "HaulersDream.Report.ScreenshotsSelected".Translate(attachments.Count));
                GUI.color = prevColor;
                Text.Font = GameFont.Small;
                var clearRect = new Rect(addRect.xMax + 140f, y, 80f, 28f);
                if (Widgets.ButtonText(clearRect, "HaulersDream.Report.ClearScreenshots".Translate(), active: editable) && editable)
                    attachments.Clear();
            }
            y += shotH;

            // Auto-included metadata note.
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(0f, y, inRect.width, noteH), "HaulersDream.Report.AutoMeta".Translate());
            GUI.color = prevColor;
            Text.Font = GameFont.Small;
            y += noteH;

            // Status line (sending / error).
            if (!statusMsg.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = phase == Phase.Failed ? new Color(0.92f, 0.5f, 0.5f) : new Color(1f, 1f, 1f, 0.7f);
                Widgets.Label(new Rect(0f, y, inRect.width, statusH), statusMsg);
                GUI.color = prevColor;
                Text.Font = GameFont.Small;
            }

            // Buttons.
            bool sending = phase == Phase.Sending;
            var cancelRect = new Rect(inRect.width - 290f, btnRowY, 130f, 32f);
            var submitRect = new Rect(inRect.width - 150f, btnRowY, 150f, 32f);
            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()) && !sending)
                Close();

            bool canSubmit = !sending && !description.Trim().NullOrEmpty();
            string submitLabel = sending
                ? "HaulersDream.Report.Sending".Translate()
                : phase == Phase.Failed
                    ? "HaulersDream.Report.Retry".Translate()
                    : "HaulersDream.Report.Submit".Translate();
            if (Widgets.ButtonText(submitRect, submitLabel, active: canSubmit) && canSubmit)
                Submit();

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }

        private void DrawSent(Rect inRect)
        {
            var prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            string body = "HaulersDream.Report.SentBody".Translate();
            if (!sentNote.NullOrEmpty()) body += "\n\n" + sentNote;
            Widgets.Label(new Rect(0f, 40f, inRect.width, inRect.height - 40f - 44f), body);
            Text.Anchor = prevAnchor;
            var closeRect = new Rect(inRect.width / 2f - 75f, inRect.height - 36f, 150f, 32f);
            if (Widgets.ButtonText(closeRect, "CloseButton".Translate()))
                Close();
        }

        // The placeholder (ghost text) key for the currently-selected report type; falls back to Other.
        private string PlaceholderKey()
        {
            foreach (var t in Types)
                if (t.kind == kind)
                    return t.placeholderKey;
            return "HaulersDream.Report.Placeholder.Other";
        }

        private void Submit()
        {
            // Never overwrite an in-flight request without freeing it (Submit is gated, but keep it leak-proof).
            if (req != null) { req.Abort(); req.Dispose(); req = null; }

            string typeId = "other";
            foreach (var t in Types)
                if (t.kind == kind) { typeId = t.id; break; }

            string json = ReportApi.BuildJson(typeId, description.Trim(), includeGameLog);
            byte[] body = Encoding.UTF8.GetBytes(json);

            req = ReportApi.NewRequest(ReportApi.Endpoint, "POST");
            req.uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json; charset=utf-8" };
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 30;
            req.SendWebRequest();

            stage = Stage.Report;
            phase = Phase.Sending;
            reportId = null;
            attachIndex = 0;
            attachFailed = 0;
            statusMsg = "HaulersDream.Report.Sending".Translate();
        }

        // Start uploading attachments[attachIndex], skipping any that are unsupported / missing / too large.
        private void StartNextAttachment()
        {
            while (attachIndex < attachments.Count)
            {
                string path = attachments[attachIndex];
                string contentType = ReportApi.ContentTypeFor(path);
                long size = File.Exists(path) ? new FileInfo(path).Length : 0L;
                if (contentType == null || size <= 0L || size > ReportApi.MaxAttachmentBytes)
                {
                    HDLog.Warn("skipping report attachment (unsupported type, missing, or too large): " + path);
                    attachFailed++;
                    attachIndex++;
                    continue;
                }

                byte[] bytes = File.ReadAllBytes(path);
                req = ReportApi.NewRequest(ReportApi.AttachmentUrl(reportId, Path.GetFileName(path)), "POST");
                req.uploadHandler = new UploadHandlerRaw(bytes) { contentType = contentType };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 120;
                req.SendWebRequest();
                stage = Stage.Attachment;
                statusMsg = "HaulersDream.Report.Uploading".Translate(attachIndex + 1, attachments.Count);
                return;
            }
            FinishSend();
        }

        private void FinishSend()
        {
            phase = Phase.Sent;
            statusMsg = "";
            sentNote = attachFailed > 0 ? "HaulersDream.Report.SentPartial".Translate(attachFailed) : "";
            HDLog.Msg("issue report submitted" + (attachFailed > 0 ? $" ({attachFailed} attachment(s) failed)" : "") + ".");
            onSent?.Invoke();
            // Re-poll the main-menu notifications so the new report's card appears without a relaunch. The 201
            // response already means the report + its issue + the "open" event are committed, so this is race-free.
            ReportNotifications.Refresh();
        }

        private void PollRequest()
        {
            if (phase != Phase.Sending || req == null || !req.isDone)
                return;

            long code = req.responseCode;
            string err = req.error;
            string respText = req.downloadHandler != null ? req.downloadHandler.text : null;
            req.Dispose();
            req = null;

            bool ok = code >= 200 && code < 300;

            if (stage == Stage.Report)
            {
                if (!ok) { Fail(code, err, respText); return; }
                reportId = ExtractReportId(respText);
                // A 2xx with NO report id means the backend did not actually create a report — the response came
                // from something other than our ingest (a proxy/redirect/CDN page). Do NOT tell the player it
                // worked; surface it as a failure and log the body so it can be diagnosed.
                if (reportId == null)
                {
                    phase = Phase.Failed;
                    statusMsg = "HaulersDream.Report.ServerError".Translate(code.ToString());
                    HDLog.Warn("issue report accepted (HTTP " + code + ") but no report id was returned; nothing was persisted. Body: " + Trunc(respText));
                    return;
                }
                HDLog.Msg("issue report accepted (HTTP " + code + "), id=" + reportId + ".");
                if (attachments.Count > 0)
                {
                    attachIndex = 0;
                    attachFailed = 0;
                    StartNextAttachment();
                }
                else
                {
                    FinishSend();
                }
                return;
            }

            // Attachment stage: the report already landed, so a failure here is partial (noted, never fatal).
            if (!ok)
            {
                attachFailed++;
                HDLog.Warn("report attachment upload failed (HTTP " + code + "): " + (err ?? respText));
            }
            attachIndex++;
            StartNextAttachment();
        }

        private void Fail(long code, string err, string respText)
        {
            phase = Phase.Failed;
            if (code == 429)
            {
                statusMsg = "HaulersDream.Report.RateLimited".Translate();
            }
            else if (code == 0)
            {
                statusMsg = "HaulersDream.Report.NetworkError".Translate(err ?? "");
                HDLog.Warn("issue report failed (network): " + err);
            }
            else
            {
                statusMsg = "HaulersDream.Report.ServerError".Translate(code.ToString());
                HDLog.Warn("issue report failed (HTTP " + code + "): " + respText);
            }
        }

        private static string ExtractReportId(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var m = Regex.Match(json, "\"id\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Collapse a response body onto one line and cap it, for a single diagnostic log entry.
        private static string Trunc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 300 ? s.Substring(0, 300) + "..." : s;
        }

        public override void PostClose()
        {
            base.PostClose();
            if (req != null)
            {
                req.Abort();
                req.Dispose();
                req = null;
            }
        }
    }
}
