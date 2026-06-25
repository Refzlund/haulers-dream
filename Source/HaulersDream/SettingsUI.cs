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
        // so the normal draw path is byte-for-byte unchanged.
        public bool Collecting;
        public List<OptionEntry> Sink;
        public int CurrentCatId;
        public string CurrentHeader;
        public int Ordinal;

        // ---- filter-render mode (settings search results: draw ONLY the matching controls, real + editable) ----
        // When RenderOrdinals is non-null, the helpers run their EXACT ordinal counting (mirroring collect mode 1:1),
        // but DRAW + take input only for controls whose ordinal is in the set; a non-matching control is skipped with
        // NO draw, NO input, and NO CurY advance, so the matching controls pack together under the search section
        // header. Headers/Notes are skipped entirely (the results view draws its own section headers). Default null,
        // so the normal draw path (no Collecting, no RenderOrdinals) is byte-for-byte unchanged.
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
        // Right-panel help, populated by hover each frame and read by the window when it draws the info column.
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
            if (c.RenderOrdinals != null)
            {
                int ord = c.Ordinal++;
                if (!c.RenderOrdinals.Contains(ord))
                    return value;
            }
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
                Text.Font = f;
                c.Gap(RowGap);
                return enabled ? newVal : value;
            }
            Text.Font = f;
            c.Gap(RowGap);
            c.Sink.Add(new OptionEntry
            {
                CatId = c.CurrentCatId, Header = c.CurrentHeader, Name = label, Desc = help,
                Ordinal = c.Ordinal++, StartY = startY, Height = c.CurY - startY,
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
            if (c.RenderOrdinals != null)
            {
                int ord = c.Ordinal++;
                if (!c.RenderOrdinals.Contains(ord))
                    return value;
            }
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
                    Ordinal = c.Ordinal++, StartY = startY, Height = c.CurY - startY,
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
            return enabled ? nv : value;
        }

        // ---- inline segmented selector (all options visible; returns the chosen index) ----
        public static int Segmented(SettingsCtx c, string label, int selected, string[] options,
            string[] optionHelp = null, string help = null, bool enabled = true, float indent = 0f)
        {
            // Filter-render (search results): count the ordinal EXACTLY as collect mode does, then either skip this
            // control entirely (no draw/input/CurY advance) or fall through to the normal editable draw below.
            if (c.RenderOrdinals != null)
            {
                int ord = c.Ordinal++;
                if (!c.RenderOrdinals.Contains(ord))
                    return selected;
            }
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
                    Ordinal = c.Ordinal++, StartY = startY, Height = c.CurY - startY,
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
            return enabled ? chosen : selected;
        }

        // ---- a left-aligned button that opens a dialog ----
        public static void Button(SettingsCtx c, string label, Action onClick, string help = null,
            bool enabled = true, float indent = 0f)
        {
            // Filter-render (search results): count the ordinal EXACTLY as collect mode does, then either skip this
            // control entirely (no draw/input/CurY advance) or fall through to the normal editable draw below.
            if (c.RenderOrdinals != null)
            {
                int ord = c.Ordinal++;
                if (!c.RenderOrdinals.Contains(ord))
                    return;
            }
            float startY = c.CurY;
            var r = c.Row(32f, indent);
            if (c.Collecting)
            {
                // Skip the draw + onClick; advance the gap exactly like the drawn path, then record.
                c.Gap(RowGap);
                c.Sink.Add(new OptionEntry
                {
                    CatId = c.CurrentCatId, Header = c.CurrentHeader, Name = label, Desc = help,
                    Ordinal = c.Ordinal++, StartY = startY, Height = c.CurY - startY,
                });
                return;
            }
            DrawIndentRail(r, indent);
            Hover(r, label, help);
            var br = new Rect(r.x, r.y + 1f, Mathf.Min(340f, r.width), 28f);
            if (Widgets.ButtonText(br, label, active: enabled) && enabled)
                onClick();
            c.Gap(RowGap);
        }

        // ---- a feature "card": icon + name + blurb + a toggle; the whole card is clickable ----
        public static bool FeatureCard(SettingsCtx c, Texture2D icon, string name, string blurb, bool value,
            string help = null, bool enabled = true)
        {
            // Filter-render (search results): count the ordinal EXACTLY as collect mode does, then either skip this
            // control entirely (no draw/input/CurY advance) or fall through to the normal editable draw below.
            if (c.RenderOrdinals != null)
            {
                int ord = c.Ordinal++;
                if (!c.RenderOrdinals.Contains(ord))
                    return value;
            }
            float startY = c.CurY;
            const float h = 54f;
            var r = c.Row(h);
            c.Gap(4f);
            if (c.Collecting)
            {
                // Skip every draw/input + the toggle sound; CurY already advanced by Row(h)+Gap(4). Record.
                c.Sink.Add(new OptionEntry
                {
                    CatId = c.CurrentCatId, Header = c.CurrentHeader, Name = name, Desc = help ?? blurb,
                    Ordinal = c.Ordinal++, StartY = startY, Height = c.CurY - startY,
                });
                return value;
            }
            // No persistent background/outline (clean look) — just a clear highlight on hover for interactivity.
            Widgets.DrawHighlightIfMouseover(r);
            if (Mouse.IsOver(r))
            {
                SetHelp(name, help ?? blurb);
                BoolStatus(value);
            }

            const float pad = 10f;
            const float iconSize = 24f;
            var iconBox = new Rect(r.x + pad, r.y + (h - iconSize) / 2f, iconSize, iconSize);
            if (icon != null)
            {
                var col = GUI.color;
                GUI.color = (enabled && value) ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(iconBox, icon, ScaleMode.ScaleToFit);
                GUI.color = col;
            }

            const float tgl = 24f;
            var tr = new Rect(r.xMax - pad - tgl, r.y + (h - tgl) / 2f, tgl, tgl);
            Widgets.CheckboxDraw(tr.x, tr.y, value, disabled: !enabled, tgl);

            float textX = iconBox.xMax + pad;
            float textW = tr.x - textX - 8f;
            var f = Text.Font;
            var col2 = GUI.color;
            Text.Font = GameFont.Small;
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(textX, r.y + 7f, textW, 22f), name);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.74f, 0.74f, 0.78f, enabled ? 1f : 0.6f);
            Widgets.Label(new Rect(textX, r.y + 28f, textW, 20f), blurb);
            GUI.color = col2;
            Text.Font = f;

            bool newVal = value;
            if (enabled && Widgets.ButtonInvisible(r))
            {
                newVal = !value;
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            return newVal;
        }
    }
}
