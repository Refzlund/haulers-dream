using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace HaulersDream
{
    /// <summary>
    /// A flat, draw-decoupled record of one interactive settings control, produced by a "collect" pass over the
    /// settings layout (see <see cref="SettingsCtx.Collecting"/>). Used by the settings search to score/jump to
    /// controls without re-running their input/draw side effects. Decoupled from the private <c>SettingsCat</c>
    /// enum via <see cref="CatId"/> = <c>(int)SettingsCat</c>.
    /// </summary>
    public sealed class OptionEntry
    {
        public int CatId;        // (int)SettingsCat
        public string Header;    // current section header text (may be null)
        public string Name;      // the control's label (already translated)
        public string Desc;      // the control's help text (already translated; may be null)
        public int Ordinal;      // stable per-(CatId, Ordinal) id — the Nth recorded control in that category
        public float StartY;     // CurY at the control's top (content-view-local; same space as the real draw)
        public float Height;     // total laid-out height of the control

        // Lower-cased (invariant) Name/Desc, precomputed ONCE when the registry is built (see
        // HaulersDreamSettings.EnsureSearchRegistry) so the per-keystroke scorer never re-lower-cases these fixed
        // strings — the FPS fix for issue #138. Null when the source field is null. Search-scoring use only
        // (fed to SettingsSearch.OptionScoreLower).
        public string NameLower;
        public string DescLower;
    }

    /// <summary>
    /// One category row in a <see cref="HDSettingsUI.YieldMatrix"/>: a label/help plus the currently-selected
    /// column index (<see cref="Value"/>, mutated in place when the user clicks a radio). <see cref="AllowDirect"/>
    /// = false hides the last column (e.g. stripping can never go straight to inventory), so that column shows an
    /// inert dash instead of a radio.
    /// </summary>
    public sealed class YieldMatrixRow
    {
        public string Label;
        public string Help;
        public int Value;
        public bool AllowDirect = true;
    }

    // Help belongs to a rendered control, independently of its translated text or current value.
    // Negative ordinals identify window navigation; matrix-header parts are negative and row/option parts positive.
    internal readonly struct SettingsHelpKey
    {
        internal readonly int Category, Ordinal, Part;
        internal SettingsHelpKey(int category, int ordinal, int part = 0)
        { Category = category; Ordinal = ordinal; Part = part; }
        internal bool Matches(SettingsHelpKey other) =>
            Category == other.Category && Ordinal == other.Ordinal && Part == other.Part;
    }

    internal readonly struct SettingsHelpDocument
    {
        internal readonly SettingsHelpKey Key;
        internal readonly string Title, Body, Status;
        internal readonly Color StatusColor;
        internal readonly Action<Rect> Extra;
        internal SettingsHelpDocument(SettingsHelpKey key, string title, string body, string status,
            Color statusColor, Action<Rect> extra)
        { Key = key; Title = title; Body = body; Status = status; StatusColor = statusColor; Extra = extra; }
    }

    // One selection and one payload, refreshed by the existing draw traversal. No per-control cache or extra pass.
    internal sealed class SettingsHelpState
    {
        private SettingsHelpKey? selected;
        private SettingsHelpDocument document, displayed;
        private bool hasDisplayed, refreshed, resetScroll;

        internal void Clear()
        {
            selected = null;
            document = default(SettingsHelpDocument);
            displayed = default(SettingsHelpDocument);
            hasDisplayed = refreshed = false;
            resetScroll = true;
        }

        internal void BeginEvent() => refreshed = false;

        internal bool Wants(SettingsHelpKey key, bool hovered) =>
            hovered || (selected.HasValue && selected.Value.Matches(key));

        internal void Offer(SettingsHelpKey key, string title, string body, string status,
            Color statusColor, Action<Rect> extra, bool hovered)
        {
            if (!Wants(key, hovered)) return;
            selected = key;
            document = new SettingsHelpDocument(key, title, body, status, statusColor, extra);
            refreshed = true;
        }

        internal SettingsHelpDocument Resolve(SettingsHelpDocument fallback)
        {
            if (!refreshed || !selected.HasValue)
            {
                selected = null; // the selected control did not exist in this traversal
                document = fallback;
            }
            // A parent row may offer first and its hovered option second. Only the final displayed document
            // determines scrolling; intermediate offers must not reset a stable option on every repaint.
            if (!hasDisplayed || !displayed.Key.Matches(document.Key)
                || displayed.Title != document.Title || displayed.Body != document.Body)
                resetScroll = true;
            displayed = document;
            hasDisplayed = true;
            return document;
        }

        internal bool TakeScrollReset()
        {
            bool result = resetScroll;
            resetScroll = false;
            return result;
        }
    }

    /// <summary>
    /// Immediate-mode vertical layout cursor for the settings content column. Replaces Listing_Standard so
    /// every widget can compute its own dynamic height (wrapped labels, two-line sliders, cards) and the
    /// total content height is the TRUE single-column height — which is what drives the scroll viewRect.
    /// (The old window derived the scroll height from a lagged, hard-coded cache + Listing column-wrap, which
    /// under-sized the viewport on the tall tabs and clipped the bottom rows — the reported "bugged panel".)
    /// </summary>
    public sealed class SettingsCtx
    {
        public readonly float Width;
        public float CurY;

        // ---- collect-mode (settings search) ----
        // When Collecting is true, the helpers run their EXACT layout (so CurY advances identically) but perform
        // NO draw/input side effects, and each interactive control appends an OptionEntry to Sink. Default off/null,
        // so the normal input and layout behavior is unchanged.
        public bool Collecting;
        public List<OptionEntry> Sink;
        public int CurrentCatId;
        public string CurrentHeader;
        public int Ordinal;
        internal SettingsHelpState Help;

        // ---- filter-render mode (settings search results: draw ONLY the matching controls, real + editable) ----
        // When RenderOrdinals is non-null, the helpers run their EXACT ordinal counting (mirroring collect mode 1:1),
        // but DRAW + take input only for controls whose ordinal is in the set; a non-matching control is skipped with
        // NO draw, NO input, and NO CurY advance, so the matching controls pack together under the search section
        // header. Headers/Notes are skipped entirely (the results view draws its own section headers). Default null,
        // so normal input and layout are unchanged. Normal mode also counts ordinals for stable help identity.
        public HashSet<int> RenderOrdinals;

        public SettingsCtx(float width)
        {
            Width = width;
            CurY = 0f;
        }

        public Rect Row(float h, float indent = 0f)
        {
            var r = new Rect(indent, CurY, Width - indent, h);
            CurY += h;
            return r;
        }

        public void Gap(float h = 8f) => CurY += h;
    }

    /// <summary>
    /// Reusable widget helpers for the 3-pane settings window (icon nav · options · info panel). Every helper
    /// shares one shape: it lays out a row via <see cref="SettingsCtx"/>, registers hover help into the right
    /// panel (<see cref="HoverTitle"/>/<see cref="HoverBody"/>), supports a greyed <c>enabled=false</c> state
    /// (sub-options under an off master stay visible but inert — which also keeps the page height stable), and
    /// an <c>indent</c> with an accent rail for nested options. All save/restore global IMGUI state.
    /// </summary>
    public static class HDSettingsUI
    {
        // Legacy per-event hover API. The window uses complete control-keyed offers for retained help.
        public static string HoverTitle;
        public static string HoverBody;
        // A short coloured "current value" line for the hovered control (e.g. On/Off, the chosen option, a %).
        public static string HoverStatus;
        public static Color HoverStatusColor;
        // Optional extra drawer for the info panel (e.g. a graph), set by a control on hover, drawn by DrawHelp.
        public static Action<Rect> HoverExtra;

        // Status colours: enabled = green, disabled = muted red, a value/choice = soft blue.
        public static readonly Color OnColor = new Color(0.5f, 0.82f, 0.5f);
        public static readonly Color OffColor = new Color(0.82f, 0.55f, 0.55f);
        public static readonly Color ValueColor = new Color(0.62f, 0.78f, 0.95f);

        public static void ResetHover()
        {
            HoverTitle = null;
            HoverBody = null;
            HoverStatus = null;
            HoverExtra = null;
        }

        public static void SetHelp(string title, string body)
        {
            HoverTitle = title;
            HoverBody = body;
        }

        public static void SetStatus(string status, Color color)
        {
            HoverStatus = status;
            HoverStatusColor = color;
        }

        // The localized On/Off status string + colour for a boolean control.
        private static void BoolStatus(bool value) =>
            SetStatus((value ? "HaulersDream.Common.On" : "HaulersDream.Common.Off").Translate(),
                value ? OnColor : OffColor);

        // Vertical gap inserted after each interactive option row so options don't clamp together. (Feature
        // cards on the hub manage their own spacing and don't use these helpers.)
        private const float RowGap = 6f;

        // Faint hover wash + register the control's help into the right panel.
        private static void Hover(Rect r, string title, string body)
        {
            if (!Mouse.IsOver(r)) return;
            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.04f));
            if (title != null || body != null)
                SetHelp(title, body);
        }


        private static void OfferHelp(SettingsCtx c, int ordinal, string title, string body, bool hovered,
            string status = null, Color? statusColor = null, Action<Rect> extra = null, int part = 0)
        {
            c.Help?.Offer(new SettingsHelpKey(c.CurrentCatId, ordinal, part), title, body, status,
                statusColor ?? ValueColor, extra, hovered);
        }

        private static void OfferBooleanHelp(SettingsCtx c, int ordinal, string title, string body,
            bool value, bool hovered)
        {
            var key = new SettingsHelpKey(c.CurrentCatId, ordinal);
            if (c.Help == null || !c.Help.Wants(key, hovered)) return;
            c.Help.Offer(key, title, body,
                (value ? "HaulersDream.Common.On" : "HaulersDream.Common.Off").Translate(),
                value ? OnColor : OffColor, null, hovered);
        }

        private static void DrawIndentRail(Rect r, float indent)
        {
            if (indent <= 0f) return;
            Widgets.DrawBoxSolid(new Rect(indent - 10f, r.y + 4f, 3f, Mathf.Max(4f, r.height - 8f)),
                new Color(0.45f, 0.6f, 0.7f, 0.5f));
        }

        // ---- section header bar ----
        public static void Header(SettingsCtx c, string label)
        {
            // Filter-render (search results): the results view draws its OWN section headers, so skip this one
            // entirely — NO Gap/Row/draw, NO CurY advance — so the filtered controls pack tight under the search header.
            if (c.RenderOrdinals != null)
                return;
            c.Gap(32f); // generous separation between sections
            var r = c.Row(26f);
            if (c.Collecting)
            {
                // Record the active header so subsequent controls are tagged with it; skip the box/label draw.
                c.CurrentHeader = label;
                c.Gap(12f); // keep CurY identical to the drawn path
                return;
            }
            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.09f));
            var f = Text.Font;
            var col = GUI.color;
            var anchor = Text.Anchor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft; // vertically centre the label within the boxed header
            GUI.color = new Color(0.86f, 0.88f, 0.95f);
            var tr = r;
            tr.xMin += 8f;
            Widgets.Label(tr, label);
            GUI.color = col;
            Text.Anchor = anchor;
            Text.Font = f;
            c.Gap(12f); // padding between the heading bar and its first option
        }

        // ---- thin divider ----
        public static void GapLine(SettingsCtx c)
        {
            c.Gap(6f);
            var r = c.Row(1f);
            var col = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawLineHorizontal(r.x, r.y, r.width);
            GUI.color = col;
            c.Gap(6f);
        }

        // ---- descriptive paragraph / note ---- (`color`, when set, overrides the default muted grey — e.g. a warning hue)
        public static void Note(SettingsCtx c, string text, float indent = 0f, Color? color = null)
        {
            // Filter-render (search results): Notes are omitted from results (they're prose, not editable controls) —
            // skip entirely with NO CurY advance so only the matching controls show under the search section header.
            if (c.RenderOrdinals != null)
                return;
            var f = Text.Font;
            Text.Font = GameFont.Tiny;
            // Height computation stays under the same Tiny font so CurY advances identically while collecting.
            float h = Mathf.Max(18f, Text.CalcHeight(text, c.Width - indent));
            var r = c.Row(h, indent);
            if (!c.Collecting) // Notes record nothing; just advance CurY and skip the draw.
            {
                var col = GUI.color;
                GUI.color = color ?? new Color(0.72f, 0.72f, 0.76f);
                Widgets.Label(r, text);
                GUI.color = col;
            }
            Text.Font = f;
        }

        // ---- checkbox row (returns the new value; never changes when disabled) ----
        public static bool Checkbox(SettingsCtx c, string label, bool value, string help = null,
            bool enabled = true, float indent = 0f)
        {
            // Filter-render (search results): count the ordinal EXACTLY as collect mode does, then either skip this
            // control entirely (no draw/input/CurY advance) or fall through to the normal editable draw below.
            int ordinal = c.Ordinal++;
            if (c.RenderOrdinals != null && !c.RenderOrdinals.Contains(ordinal))
                return value;
            float startY = c.CurY;
            var f = Text.Font;
            Text.Font = GameFont.Small;
            // Height computation stays under the same Small font so CurY advances identically while collecting.
            float h = Mathf.Max(26f, Text.CalcHeight(label, c.Width - indent - 28f));
            var r = c.Row(h, indent);
            if (!c.Collecting)
            {
                DrawIndentRail(r, indent);
                Hover(r, label, help);
                bool newVal = value;
                Widgets.CheckboxLabeled(r, label, ref newVal, disabled: !enabled);
                if (Mouse.IsOver(r)) BoolStatus(enabled ? newVal : value);
                OfferBooleanHelp(c, ordinal, label, help, enabled ? newVal : value, Mouse.IsOver(r));
                Text.Font = f;
                c.Gap(RowGap);
                return enabled ? newVal : value;
            }
            Text.Font = f;
            c.Gap(RowGap);
            c.Sink.Add(new OptionEntry
            {
                CatId = c.CurrentCatId, Header = c.CurrentHeader, Name = label, Desc = help,
                Ordinal = ordinal, StartY = startY, Height = c.CurY - startY,
            });
            return value;
        }

        // ---- slider row: label + right-aligned readout, slider below (returns the new value) ----
        // `graph` (optional): an extra info-panel drawer registered while this control is hovered (e.g. a curve).
        public static float Slider(SettingsCtx c, string label, float value, float min, float max,
            string readout, string help = null, bool enabled = true, float indent = 0f, Action<Rect> graph = null)
        {
            // Filter-render (search results): count the ordinal EXACTLY as collect mode does, then either skip this
            // control entirely (no draw/input/CurY advance) or fall through to the normal editable draw below.
            int ordinal = c.Ordinal++;
            if (c.RenderOrdinals != null && !c.RenderOrdinals.Contains(ordinal))
                return value;
            float startY = c.CurY;
            var f = Text.Font;
            Text.Font = GameFont.Small;
            var top = c.Row(24f, indent);
            if (c.Collecting)
            {
                // Skip every draw/input; advance the second row + gap exactly like the drawn path.
                c.Row(26f, indent);
                Text.Font = f;
                c.Gap(RowGap);
                c.Sink.Add(new OptionEntry
                {
                    CatId = c.CurrentCatId, Header = c.CurrentHeader, Name = label, Desc = help,
                    Ordinal = ordinal, StartY = startY, Height = c.CurY - startY,
                });
                return value;
            }
            DrawIndentRail(top, indent);
            Hover(top, label, help);
            var col = GUI.color;
            if (!enabled) GUI.color = new Color(col.r, col.g, col.b, 0.5f);

            // Readout sits on the right of the row. Keep it a SINGLE line and size its box to the actual text
            // (capped so the label keeps room) so long value labels like "Fair (balanced)" / "No slowdown —
            // carry freely" never wrap into the 24px row and clip. The label takes the remaining width.
            var anchor = Text.Anchor;
            var ww = Text.WordWrap;
            Text.WordWrap = false;
            float readoutW = Mathf.Min(Text.CalcSize(readout).x + 4f, top.width - 70f);

            var labelRect = top;
            labelRect.width = Mathf.Max(40f, top.width - readoutW - 8f);
            Widgets.Label(labelRect, label);

            Text.Anchor = TextAnchor.MiddleRight;
            var valRect = new Rect(top.xMax - readoutW, top.y, readoutW, top.height);
            GUI.color = enabled ? new Color(0.8f, 0.85f, 0.95f) : new Color(0.8f, 0.85f, 0.95f, 0.5f);
            Widgets.Label(valRect, readout);
            Text.Anchor = anchor;
            Text.WordWrap = ww;
            GUI.color = col;

            var sr = c.Row(26f, indent);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = enabled;
            float nv = Widgets.HorizontalSlider(sr, value, min, max, middleAlignment: true);
            GUI.enabled = oldEnabled;
            if (Mouse.IsOver(top) || Mouse.IsOver(sr))
            {
                SetStatus(readout, ValueColor);
                if (graph != null) HoverExtra = graph;
            }
            Text.Font = f;
            c.Gap(RowGap);
            OfferHelp(c, ordinal, label, help, Mouse.IsOver(top) || Mouse.IsOver(sr),
                readout, ValueColor, graph);
            return enabled ? nv : value;
        }

        // ---- inline segmented selector (all options visible; returns the chosen index) ----
        public static int Segmented(SettingsCtx c, string label, int selected, string[] options,
            string[] optionHelp = null, string help = null, bool enabled = true, float indent = 0f)
        {
            // Filter-render (search results): count the ordinal EXACTLY as collect mode does, then either skip this
            // control entirely (no draw/input/CurY advance) or fall through to the normal editable draw below.
            int ordinal = c.Ordinal++;
            if (c.RenderOrdinals != null && !c.RenderOrdinals.Contains(ordinal))
                return selected;
            float startY = c.CurY;
            var f = Text.Font;
            Text.Font = GameFont.Small;
            var top = c.Row(24f, indent);
            if (c.Collecting)
            {
                // Skip every draw/input; advance the segment row + gap exactly like the drawn path.
                c.Row(30f, indent);
                Text.Font = f;
                c.Gap(RowGap);
                c.Sink.Add(new OptionEntry
                {
                    CatId = c.CurrentCatId, Header = c.CurrentHeader, Name = label, Desc = help,
                    Ordinal = ordinal, StartY = startY, Height = c.CurY - startY,
                });
                return selected;
            }
            DrawIndentRail(top, indent);
            Hover(top, label, help);
            var col = GUI.color;
            if (!enabled) GUI.color = new Color(col.r, col.g, col.b, 0.5f);
            Widgets.Label(top, label);
            GUI.color = col;

            var br = c.Row(30f, indent);
            int n = Mathf.Max(1, options.Length);
            const float segGap = 4f;
            float bw = (br.width - segGap * (n - 1)) / n;
            int chosen = selected;

            var anchor = Text.Anchor;
            var ww = Text.WordWrap;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = true;
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < n; i++)
            {
                var seg = new Rect(br.x + i * (bw + segGap), br.y, bw, br.height);
                bool sel = i == selected;
                Widgets.DrawBoxSolid(seg, sel
                    ? new Color(0.28f, 0.45f, 0.55f, enabled ? 0.7f : 0.35f)
                    : new Color(1f, 1f, 1f, enabled ? 0.06f : 0.03f));
                var bcol = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, sel ? 0.45f : 0.18f);
                Widgets.DrawBox(seg);
                GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                Widgets.Label(seg, options[i]);
                GUI.color = bcol;
                if (optionHelp != null && i < optionHelp.Length)
                    Hover(seg, options[i], optionHelp[i]);
                if (enabled && Widgets.ButtonInvisible(seg))
                    chosen = i;
            }
            int effSel = enabled ? chosen : selected;
            if (Mouse.IsOver(top) || Mouse.IsOver(br))
                SetStatus(options[Mathf.Clamp(effSel, 0, options.Length - 1)], ValueColor);
            Text.Anchor = anchor;
            Text.WordWrap = ww;
            Text.Font = f;
            c.Gap(RowGap);
            OfferHelp(c, ordinal, label, help, Mouse.IsOver(top) || Mouse.IsOver(br),
                options[Mathf.Clamp(effSel, 0, options.Length - 1)], ValueColor);
            // Refresh the selected option even after the pointer has moved into the help panel.
            if (optionHelp != null)
                for (int i = 0; i < n && i < optionHelp.Length; i++)
                {
                    var seg = new Rect(br.x + i * (bw + segGap), br.y, bw, br.height);
                    OfferHelp(c, ordinal, options[i], optionHelp[i], Mouse.IsOver(seg),
                        options[Mathf.Clamp(effSel, 0, options.Length - 1)], ValueColor, part: i + 1);
                }
            return enabled ? chosen : selected;
        }

        // ---- radio matrix: one row per category, a shared set of columns drawn once as headers ----
        // A compact table where every row picks one of the SAME options (e.g. the per-category yield behaviour).
        // The column names are shown once at the top instead of repeating on every row, and each cell is a native
        // radio. Integrates with the three ctx modes exactly like the other helpers: NORMAL draws header + every row;
        // COLLECT records one OptionEntry per row (advancing CurY identically so nav StartY/Height are correct, no
        // draw); FILTER-RENDER consumes one ordinal per row and draws the column header plus ONLY the matching rows
        // (so a searched category stays editable in the results, with its columns labelled for context). Selected
        // index is read+written through each row's Value (mutated in place on click; the caller maps it back).
        private const float YieldLabelFrac = 0.44f;   // fraction of the row width given to the category label column

        public static void YieldMatrix(SettingsCtx c, string[] colLabels, string[] colHelps, IList<YieldMatrixRow> rows)
        {
            int baseOrd = c.Ordinal;
            c.Ordinal += rows.Count;
            // FILTER-RENDER: the same row range as collect mode; header/option help takes no extra ordinals.
            if (c.RenderOrdinals != null)
            {
                bool any = false;
                for (int i = 0; i < rows.Count; i++)
                    if (c.RenderOrdinals.Contains(baseOrd + i)) { any = true; break; }
                if (!any) return;
                MatrixHeader(c, colLabels, colHelps, baseOrd);
                for (int i = 0; i < rows.Count; i++)
                    if (c.RenderOrdinals.Contains(baseOrd + i))
                        MatrixRow(c, colLabels, colHelps, rows[i], baseOrd + i);
                return;
            }

            // NORMAL + COLLECT: header (advances CurY in both; draws only when not collecting), then every row.
            MatrixHeader(c, colLabels, colHelps, baseOrd);
            for (int i = 0; i < rows.Count; i++)
            {
                float startY = c.CurY;
                MatrixRow(c, colLabels, colHelps, rows[i], baseOrd + i);
                if (c.Collecting)
                {
                    c.Sink.Add(new OptionEntry
                    {
                        CatId = c.CurrentCatId, Header = c.CurrentHeader, Name = rows[i].Label, Desc = rows[i].Help,
                        Ordinal = baseOrd + i, StartY = startY, Height = c.CurY - startY,
                    });
                }
            }
        }

        // The column-name header for a YieldMatrix. Advances CurY whether or not it draws (so COLLECT mode lays the
        // table out identically and the rows' recorded StartY match the real draw). Not a searchable control — never
        // touches Ordinal.
        private static void MatrixHeader(SettingsCtx c, string[] colLabels, string[] colHelps, int baseOrd)
        {
            const float hH = 34f;
            var r = c.Row(hH);
            if (c.Collecting) return;
            float labelW = c.Width * YieldLabelFrac;
            int cols = Mathf.Max(1, colLabels.Length);
            float colW = (c.Width - labelW) / cols;
            var f = Text.Font;
            var anchor = Text.Anchor;
            var ww = Text.WordWrap;
            var col = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;   // sit the labels at the bottom, hugging the rows below
            Text.WordWrap = true;
            GUI.color = new Color(0.80f, 0.84f, 0.92f);
            for (int i = 0; i < cols; i++)
            {
                var cell = new Rect(labelW + i * colW, r.y, colW, r.height);
                Widgets.Label(cell, colLabels[i]);
                if (colHelps != null && i < colHelps.Length)
                {
                    Hover(cell, colLabels[i], colHelps[i]);
                    OfferHelp(c, baseOrd, colLabels[i], colHelps[i], Mouse.IsOver(cell), part: -i - 1);
                }
            }
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawLineHorizontal(r.x, r.yMax - 1f, r.width);
            GUI.color = col;
            Text.WordWrap = ww;
            Text.Anchor = anchor;
            Text.Font = f;
        }

        // One category row of a YieldMatrix: label on the left, a native radio centred under each column. Advances
        // CurY whether or not it draws. The whole row gets a faint top rule; hovering the label or a column cell
        // registers the matching help into the info panel.
        private static void MatrixRow(SettingsCtx c, string[] colLabels, string[] colHelps, YieldMatrixRow row, int ordinal)
        {
            var f = Text.Font;
            Text.Font = GameFont.Small;
            float labelW = c.Width * YieldLabelFrac;
            float h = Mathf.Max(32f, Text.CalcHeight(row.Label, labelW - 10f));
            var r = c.Row(h);
            if (c.Collecting) { Text.Font = f; return; }

            int cols = Mathf.Max(1, colLabels.Length);
            float colW = (c.Width - labelW) / cols;

            var col = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.07f);
            Widgets.DrawLineHorizontal(r.x, r.y, r.width);   // faint row separator
            GUI.color = col;

            // Anywhere on the row, show the row's current choice as the info-panel value line (like the other helpers).
            if (Mouse.IsOver(r))
                SetStatus(colLabels[Mathf.Clamp(row.Value, 0, colLabels.Length - 1)], ValueColor);

            var labelRect = new Rect(r.x, r.y, labelW - 6f, r.height);
            if (Mouse.IsOver(labelRect))
            {
                Widgets.DrawBoxSolid(labelRect, new Color(1f, 1f, 1f, 0.04f));
                SetHelp(row.Label, row.Help);
            }
            var anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.90f, 0.90f, 0.94f);
            Widgets.Label(labelRect, row.Label);
            GUI.color = col;
            Text.Anchor = anchor;

            for (int i = 0; i < cols; i++)
            {
                var cell = new Rect(labelW + i * colW, r.y, colW, r.height);
                // A column the row doesn't support (e.g. "collect directly" for stripping): inert dash, no radio.
                if (!row.AllowDirect && i == cols - 1)
                {
                    var dc = GUI.color;
                    var da = Text.Anchor;
                    GUI.color = new Color(1f, 1f, 1f, 0.20f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(cell, "—");
                    Text.Anchor = da;
                    GUI.color = dc;
                    continue;
                }
                if (Mouse.IsOver(cell))
                {
                    Widgets.DrawBoxSolid(cell, new Color(1f, 1f, 1f, 0.04f));
                    if (colHelps != null && i < colHelps.Length) SetHelp(colLabels[i], colHelps[i]);
                }
                float dotX = cell.x + (cell.width - 24f) / 2f;
                float dotY = cell.y + (cell.height - 24f) / 2f;
                if (Widgets.RadioButton(dotX, dotY, row.Value == i))
                    row.Value = i;
            }

            Text.Font = f;
            string status = colLabels[Mathf.Clamp(row.Value, 0, colLabels.Length - 1)];
            OfferHelp(c, ordinal, row.Label, row.Help, Mouse.IsOver(r), status, ValueColor);
            for (int i = 0; i < cols; i++)
            {
                if (!row.AllowDirect && i == cols - 1) continue; // the inert dash uses the row document
                if (colHelps == null || i >= colHelps.Length) continue;
                var cell = new Rect(labelW + i * colW, r.y, colW, r.height);
                OfferHelp(c, ordinal, colLabels[i], colHelps[i], Mouse.IsOver(cell),
                    status, ValueColor, part: i + 1);
            }
        }

        // ---- a left-aligned button that opens a dialog ----
        public static void Button(SettingsCtx c, string label, Action onClick, string help = null,
            bool enabled = true, float indent = 0f)
        {
            // Filter-render (search results): count the ordinal EXACTLY as collect mode does, then either skip this
            // control entirely (no draw/input/CurY advance) or fall through to the normal editable draw below.
            int ordinal = c.Ordinal++;
            if (c.RenderOrdinals != null && !c.RenderOrdinals.Contains(ordinal))
                return;
            float startY = c.CurY;
            var r = c.Row(32f, indent);
            if (c.Collecting)
            {
                // Skip the draw + onClick; advance the gap exactly like the drawn path, then record.
                c.Gap(RowGap);
                c.Sink.Add(new OptionEntry
                {
                    CatId = c.CurrentCatId, Header = c.CurrentHeader, Name = label, Desc = help,
                    Ordinal = ordinal, StartY = startY, Height = c.CurY - startY,
                });
                return;
            }
            DrawIndentRail(r, indent);
            Hover(r, label, help);
            var br = new Rect(r.x, r.y + 1f, Mathf.Min(340f, r.width), 28f);
            if (Widgets.ButtonText(br, label, active: enabled) && enabled)
                onClick();
            c.Gap(RowGap);
            OfferHelp(c, ordinal, label, help, Mouse.IsOver(r));
        }


        internal readonly struct FeatureCardLayout
        {
            internal readonly float Height;
            internal readonly Rect Icon, Toggle, Name, Blurb;
            internal FeatureCardLayout(float height, Rect icon, Rect toggle, Rect name, Rect blurb)
            { Height = height; Icon = icon; Toggle = toggle; Name = name; Blurb = blurb; }
        }

        // The same native measurement supplies collect and draw geometry. Requesting Tiny may resolve to Small.
        internal static FeatureCardLayout MeasureFeatureCard(float width, string name, string blurb)
        {
            const float pad = 10f, icon = 24f, toggle = 24f, textGap = 8f, verticalPad = 7f;
            float textX = pad + icon + pad;
            float toggleX = width - pad - toggle;
            float textWidth = toggleX - textX - textGap;
            if (float.IsNaN(width) || float.IsInfinity(width) || textWidth <= 0f)
                throw new ArgumentOutOfRangeException(nameof(width), "Feature card needs a positive text width.");
            var font = Text.Font;
            var anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            try
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                Text.Font = GameFont.Small;
                float nameHeight = name.NullOrEmpty() ? 0f
                    : Mathf.Ceil(Mathf.Max(Text.LineHeight, Text.CalcHeight(name, textWidth)));
                Text.Font = GameFont.Tiny;
                float blurbHeight = blurb.NullOrEmpty() ? 0f
                    : Mathf.Ceil(Mathf.Max(Text.LineHeight, Text.CalcHeight(blurb, textWidth)));
                float gap = nameHeight > 0f && blurbHeight > 0f ? 2f : 0f;
                float height = Mathf.Max(54f, verticalPad * 2f + nameHeight + gap + blurbHeight);
                return new FeatureCardLayout(height,
                    new Rect(pad, (height - icon) / 2f, icon, icon),
                    new Rect(toggleX, (height - toggle) / 2f, toggle, toggle),
                    new Rect(textX, verticalPad, textWidth, nameHeight),
                    new Rect(textX, verticalPad + nameHeight + gap, textWidth, blurbHeight));
            }
            finally
            {
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = wrap;
            }
        }

        private static Rect InCard(Rect relative, Rect card) =>
            new Rect(card.x + relative.x, card.y + relative.y, relative.width, relative.height);

        // ---- a feature "card": icon + name + blurb + a toggle; the whole card is clickable ----
        public static bool FeatureCard(SettingsCtx c, Texture2D icon, string name, string blurb, bool value,
            string help = null, bool enabled = true)
        {
            int ordinal = c.Ordinal++;
            if (c.RenderOrdinals != null && !c.RenderOrdinals.Contains(ordinal))
                return value;
            float startY = c.CurY;
            var layout = MeasureFeatureCard(c.Width, name, blurb);
            var r = c.Row(layout.Height);
            c.Gap(4f);
            if (c.Collecting)
            {
                c.Sink.Add(new OptionEntry
                {
                    CatId = c.CurrentCatId, Header = c.CurrentHeader, Name = name, Desc = help ?? blurb,
                    Ordinal = ordinal, StartY = startY, Height = c.CurY - startY,
                });
                return value;
            }

            var font = Text.Font;
            var anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            var color = GUI.color;
            try
            {
                Widgets.DrawHighlightIfMouseover(r);
                if (icon != null)
                {
                    GUI.color = (enabled && value) ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                    GUI.DrawTexture(InCard(layout.Icon, r), icon, ScaleMode.ScaleToFit);
                    GUI.color = color;
                }
                var toggle = InCard(layout.Toggle, r);
                Widgets.CheckboxDraw(toggle.x, toggle.y, value, disabled: !enabled, toggle.width);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                Text.Font = GameFont.Small;
                GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                if (layout.Name.height > 0f) Widgets.Label(InCard(layout.Name, r), name);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.74f, 0.74f, 0.78f, enabled ? 1f : 0.6f);
                if (layout.Blurb.height > 0f) Widgets.Label(InCard(layout.Blurb, r), blurb);
            }
            finally
            {
                GUI.color = color;
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = wrap;
            }

            bool newVal = value;
            if (enabled && Widgets.ButtonInvisible(r))
            {
                newVal = !value;
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            bool hovered = Mouse.IsOver(r);
            if (hovered)
            {
                SetHelp(name, help ?? blurb);
                BoolStatus(newVal);
            }
            OfferBooleanHelp(c, ordinal, name, help ?? blurb, newVal, hovered);
            return newVal;
        }
    }
}
