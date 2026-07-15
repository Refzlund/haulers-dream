using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Assembles the JSON payload + endpoint for an in-game issue report sent to the Hauler's Dream report
    /// backend (https://reports.refzlund.com). This is pure payload assembly run on the UI thread; the HTTP
    /// send + polling lifecycle lives in <see cref="Dialog_ReportIssue"/> (a UnityWebRequest pumped per frame).
    /// </summary>
    internal static class ReportApi
    {
        // Per-project ingest endpoint. The project id is a public, unguessable capability token (it is the URL),
        // not a secret — it only allows POSTing a rate-limited report to this one project.
        public const string Endpoint = "https://reports.refzlund.com/report/XfgH9t8FHTV39guTPe1V86xg";

        public const int MaxCommentChars = 4000;     // in-game cap for the report/comment box (GitHub's hard limit is far higher)
        public const int MaxAttachments = 12;         // matches the backend per-report attachment count cap
        public const long MaxAttachmentBytes = 100L * 1024 * 1024; // 100 MB/file (matches the backend cap)
        private const int LogTailBytes = 5 * 1024 * 1024;  // attach up to the backend's 5 MB log cap (full Player.log for most saves)

        /// <summary>A descriptive User-Agent so reports are attributable to the mod + game version.</summary>
        public static string UserAgent() => "HaulersDream/" + ModVersion() + " (RimWorld " + RwVersion() + ")";

        /// <summary>
        /// Create a <see cref="UnityWebRequest"/> for a report-endpoint call with the setup EVERY site needs:
        /// the first-party certificate handler and the descriptive User-Agent header. Callers attach their own
        /// upload/download handlers, timeout and extra headers, then SendWebRequest. Centralized so no report
        /// request can miss the certificate handler (the sites vary in handlers/timeouts but not in this).
        /// </summary>
        /// <param name="url">Absolute report-endpoint URL (one of the URL helpers on this class).</param>
        /// <param name="method">HTTP verb, "GET" or "POST".</param>
        /// <returns>An unsent request; dispose it (all callers do), which also disposes the handler.</returns>
        public static UnityWebRequest NewRequest(string url, string method)
        {
            var req = new UnityWebRequest(url, method)
            {
                // A fresh handler per request: UnityWebRequest.Dispose() disposes the attached handler
                // (disposeCertificateHandlerOnDispose defaults to true), so a shared instance would be
                // dead after the first request completes.
                certificateHandler = new ReportCertificateHandler()
            };
            req.SetRequestHeader("User-Agent", UserAgent());
            return req;
        }

        /// <summary>The stable per-install reporter token sent with each report and used (as the X-Reporter-Id
        /// header) to scope the "my reports" + thread reads to this install.</summary>
        public static string ReporterId() => HaulersDreamMod.Settings != null ? HaulersDreamMod.Settings.ReporterId : "";

        /// <summary>The running mod version (the assembly version, e.g. "1.13.0.0"), for the out-of-date check.</summary>
        public static string ModVersionString() => ModVersion();

        /// <summary>URL for posting one attachment to a just-created report.</summary>
        public static string AttachmentUrl(string reportId, string fileName) =>
            Endpoint + "/" + reportId + "/attachment?name=" + UnityWebRequest.EscapeURL(fileName ?? "attachment");

        /// <summary>URL for listing this install's own reports (GET; send the reporter id as X-Reporter-Id).</summary>
        public static string MyReportsUrl() => Endpoint + "/mine";

        /// <summary>URL for one report's status + comment thread (GET; send the reporter id as X-Reporter-Id).</summary>
        public static string ThreadUrl(string reportId) => Endpoint + "/" + reportId + "/thread";

        /// <summary>URL for posting a comment on one report (POST; send the reporter id as X-Reporter-Id).</summary>
        public static string CommentUrl(string reportId) => Endpoint + "/" + reportId + "/comment";

        /// <summary>URL for the per-report status feed: the current status (open / solved / closed) + last-comment
        /// time of each of the player's own reports (GET; send the reporter id as X-Reporter-Id). Served from D1
        /// only, so it is cheap to poll once per launch.</summary>
        public static string StatusUrl() => Endpoint + "/status";

        /// <summary>Build the comment POST body: <c>{ "body": "..." }</c>.</summary>
        public static string BuildCommentJson(string body) => "{\"body\":" + JsonStr(body ?? string.Empty) + "}";

        /// <summary>The image/video content-type for a file path, or null if the extension is not an accepted type.</summary>
        public static string ContentTypeFor(string path)
        {
            switch (Path.GetExtension(path ?? string.Empty).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".bmp": return "image/bmp";
                case ".mp4": return "video/mp4";
                case ".webm": return "video/webm";
                case ".mov": return "video/quicktime";
                case ".mkv": return "video/x-matroska";
                default: return null;
            }
        }

        /// <summary>
        /// Build the POST body: <c>{ comment, logs?: [{name,text}], meta }</c>. The tail of Hauler's Dream's own
        /// always-on disk trail is ALWAYS attached (HD-specific, bounded to a recent slice); the full game log
        /// (Player.log) is attached only when <paramref name="includeGameLog"/>. The active mod list, game/mod
        /// version and OS go in <c>meta</c>.
        /// Must run on the main thread (it reads Verse/Unity state).
        /// </summary>
        public static string BuildJson(string typeId, string comment, bool includeGameLog)
        {
            var sb = new StringBuilder(2048);
            sb.Append('{');
            sb.Append("\"comment\":").Append(JsonStr(comment ?? string.Empty));
            sb.Append(",\"reporterId\":").Append(JsonStr(ReporterId()));

            // Logs: HD's own buffer first (always), then the full game log when opted in.
            var logNames = new List<string>(2);
            var logTexts = new List<string>(2);
            // Drain the background writer's queue to disk FIRST, so the tail we read next includes the newest
            // lines (the events leading right up to hitting "report") instead of missing whatever was still
            // in-flight. Blocks briefly; the writer keeps running for the rest of the session.
            HDDebugLog.FlushBlocking();
            string hdLog = HDLog.GetReportLog();
            if (!string.IsNullOrEmpty(hdLog)) { logNames.Add("Hauler's Dream"); logTexts.Add(hdLog); }
            if (includeGameLog)
            {
                string player = ReadPlayerLogTail();
                if (!string.IsNullOrEmpty(player)) { logNames.Add("Player.log"); logTexts.Add(player); }
            }
            if (logNames.Count > 0)
            {
                sb.Append(",\"logs\":[");
                for (int i = 0; i < logNames.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"name\":").Append(JsonStr(logNames[i]))
                        .Append(",\"text\":").Append(JsonStr(logTexts[i])).Append('}');
                }
                sb.Append(']');
            }

            sb.Append(",\"meta\":{");
            sb.Append("\"type\":").Append(JsonStr(typeId));
            sb.Append(",\"modVersion\":").Append(JsonStr(ModVersion()));
            sb.Append(",\"rwVersion\":").Append(JsonStr(RwVersion()));
            sb.Append(",\"os\":").Append(JsonStr(SystemInfo.operatingSystem ?? string.Empty));

            // Identify the reporting Steam user (SteamID64 + current persona name) so the dashboard and
            // GitHub issue can show who reported. Absent on non-Steam (DRM-free) installs.
            SteamIdentity(out string steamId, out string steamName);
            if (!string.IsNullOrEmpty(steamId)) sb.Append(",\"steamId\":").Append(JsonStr(steamId));
            if (!string.IsNullOrEmpty(steamName)) sb.Append(",\"steamName\":").Append(JsonStr(steamName));

            var mods = LoadedModManager.RunningModsListForReading;
            sb.Append(",\"activeModCount\":").Append(mods.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"mods\":[");
            for (int i = 0; i < mods.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(JsonStr(mods[i].Name ?? string.Empty))
                    .Append(",\"packageId\":").Append(JsonStr(mods[i].PackageIdPlayerFacing ?? string.Empty)).Append('}');
            }
            sb.Append(']');

            sb.Append('}'); // meta
            sb.Append('}'); // root
            return sb.ToString();
        }

        /// <summary>
        /// The local Steam user's SteamID64 + current persona name, or (null, null) when not running on
        /// Steam (DRM-free build). Guarded by <see cref="Verse.Steam.SteamManager.Initialized"/>, which is
        /// false off Steam, so the Steamworks calls are only made when Steam is actually present.
        /// </summary>
        private static void SteamIdentity(out string steamId, out string personaName)
        {
            steamId = null;
            personaName = null;
            if (!Verse.Steam.SteamManager.Initialized) return;
            Steamworks.CSteamID id = Steamworks.SteamUser.GetSteamID();
            if (!id.IsValid()) return;
            steamId = id.m_SteamID.ToString(CultureInfo.InvariantCulture);
            personaName = Steamworks.SteamFriends.GetPersonaName();
        }

        private static string RwVersion()
        {
            string v = VersionControl.CurrentVersionStringWithRev;
            return string.IsNullOrEmpty(v) ? VersionControl.CurrentVersionString : v;
        }

        private static string ModVersion()
        {
            var v = typeof(HaulersDreamMod).Assembly.GetName().Version;
            return v != null ? v.ToString() : "unknown";
        }

        /// <summary>
        /// Read the tail of the current Player.log with a shared read handle (it works while the game still holds
        /// the file open). Returns null when there is no log. A missing file is handled via <c>File.Exists</c>;
        /// any genuine IO error is intentionally left to propagate so it surfaces rather than being hidden.
        /// </summary>
        private static string ReadPlayerLogTail()
        {
            string path = Application.consoleLogPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long start = Math.Max(0L, fs.Length - LogTailBytes);
                if (start > 0) fs.Seek(start, SeekOrigin.Begin);
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string text = sr.ReadToEnd();
                    if (start > 0)
                    {
                        // Drop the partial first line we landed mid-way into, and mark the truncation.
                        int nl = text.IndexOf('\n');
                        if (nl >= 0 && nl < text.Length - 1) text = text.Substring(nl + 1);
                        text = "[... truncated to the last " + (LogTailBytes / 1024) + " KB ...]\n" + text;
                    }
                    return text;
                }
            }
        }

        /// <summary>Quote + escape a string as a JSON string literal.</summary>
        private static string JsonStr(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ---- response parsing (the /mine list + /[id]/thread payloads) -------------------------------

        /// <summary>Parse the GET /mine response into the player's own report summaries.</summary>
        public static List<ReportSummary> ParseMyReports(string json)
        {
            var list = new List<ReportSummary>();
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) return list;
            if (root.TryGetValue("reports", out var arr) && arr is List<object> items)
            {
                foreach (var it in items)
                {
                    if (it is Dictionary<string, object> o)
                        list.Add(new ReportSummary
                        {
                            id = Str(o, "id"),
                            type = Str(o, "type"),
                            title = Str(o, "title"),
                            createdAt = Num(o, "createdAt"),
                            status = Str(o, "status"),
                            issueNumber = Num(o, "issueNumber"),
                            url = Str(o, "url")
                        });
                }
            }
            return list;
        }

        /// <summary>Parse the GET /[id]/thread response into one report's status + comments. Null on bad input.</summary>
        public static ReportThread ParseThread(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> o)) return null;
            var t = new ReportThread
            {
                id = Str(o, "id"),
                type = Str(o, "type"),
                comment = Str(o, "comment"),
                createdAt = Num(o, "createdAt"),
                status = Str(o, "status"),
                issueNumber = Num(o, "issueNumber"),
                url = Str(o, "url"),
                commentsUnavailable = Bool(o, "commentsUnavailable")
            };
            if (o.TryGetValue("comments", out var arr) && arr is List<object> items)
            {
                foreach (var it in items)
                {
                    if (it is Dictionary<string, object> c)
                        t.comments.Add(new IssueComment
                        {
                            author = Str(c, "author"),
                            body = Str(c, "body"),
                            createdAt = Str(c, "createdAt"),
                            role = Str(c, "role")
                        });
                }
            }
            return t;
        }

        /// <summary>Parse the GET /status response into the per-report status feed (one entry per report +
        /// latest version + server now).</summary>
        public static StatusFeed ParseReportStatuses(string json)
        {
            var feed = new StatusFeed();
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) return feed;
            feed.latestVersion = Str(root, "latestVersion");
            feed.now = (long)Num(root, "now");
            if (root.TryGetValue("reports", out var arr) && arr is List<object> items)
            {
                foreach (var it in items)
                {
                    if (it is Dictionary<string, object> o)
                        feed.reports.Add(new ReportStatus
                        {
                            ReportId = Str(o, "reportId"),
                            Title = Str(o, "title"),
                            Url = Str(o, "url"),
                            Status = Str(o, "status"),
                            StatusAt = (long)Num(o, "statusAt"),
                            LastCommentAt = (long)Num(o, "lastCommentAt")
                        });
                }
            }
            return feed;
        }

        private static string Str(Dictionary<string, object> o, string k) =>
            o != null && o.TryGetValue(k, out var v) && v is string s ? s : null;

        private static double Num(Dictionary<string, object> o, string k) =>
            o != null && o.TryGetValue(k, out var v) && v is double d ? d : 0;

        private static bool Bool(Dictionary<string, object> o, string k) =>
            o != null && o.TryGetValue(k, out var v) && v is bool b && b;
    }

    /// <summary>
    /// Accepts the server's TLS certificate for the report requests without validating the chain.
    ///
    /// <para>WHAT THIS EXPOSES (be honest): dropping chain validation means an active man-in-the-middle on these
    /// specific requests could read (or alter) their contents. What travels on the report / attachment / status
    /// requests is: the tail of Player.log (which can include local file paths and the OS username), the
    /// reporter's SteamID64 and Steam persona name, the active mod list, the X-Reporter-Id capability token
    /// (which scopes reads to this install's own reports), and any log or screenshot the user chose to attach.
    /// None of it is a password or payment detail, but it is not nothing.</para>
    ///
    /// <para>WHY IT STILL STANDS: these requests go ONLY to HD's own first-party report endpoint
    /// (reports.refzlund.com); some players' Unity/Mono TLS stacks cannot validate the (valid) Cloudflare
    /// certificate and fail every report with "Unknown Error", which blocks reporting entirely for them. The
    /// alternative fix, certificate pinning, would break on Cloudflare's routine certificate rotation and is not
    /// worth that fragility here. The handler is scoped strictly to these report requests via
    /// <see cref="ReportApi.NewRequest"/>, never a global ServicePointManager override, so nothing else in the
    /// game (or other mods) is affected.</para>
    /// </summary>
    internal sealed class ReportCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData) => true;
    }

    /// <summary>One of the player's own reports as shown in the My-reports list.</summary>
    internal class ReportSummary
    {
        public string id;
        public string type;     // bug / feature / compatibility / other (or null)
        public string title;    // first line of the comment
        public double createdAt; // ms since epoch
        public string status;   // open / solved / closed / submitted (canonical, from the server)
        public double issueNumber; // GitHub issue number (0 if none)
        public string url;      // GitHub issue html_url (or null)
    }

    /// <summary>One comment on a report's GitHub issue.</summary>
    internal class IssueComment
    {
        public string author;
        public string body;
        public string createdAt; // ISO-8601 (from GitHub)
        public string role;      // maintainer / reporter / github
    }

    /// <summary>The parsed GET /status response: the current status of each of the player's reports + the latest
    /// published version. One <see cref="ReportStatus"/> per report; the planner collapses each to a single card.</summary>
    internal class StatusFeed
    {
        public string latestVersion;                       // version live on the Steam Workshop (or null)
        public long now;                                   // server clock (ms) at the response
        public List<ReportStatus> reports = new List<ReportStatus>();
    }

    /// <summary>A report's full status + comment thread (the detail view).</summary>
    internal class ReportThread
    {
        public string id;
        public string type;
        public string comment;
        public double createdAt;
        public string status;   // open / solved / closed / submitted (canonical)
        public double issueNumber; // GitHub issue number (0 if none)
        public string url;
        public bool commentsUnavailable;
        public List<IssueComment> comments = new List<IssueComment>();
    }
}
