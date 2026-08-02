using HaulersDream.Core;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /*
        ──────────────────────────────────────────────
                  Numeric entry beside a slider
        ──────────────────────────────────────────────
        Issue #237: a slider can't be dragged to an exact number. These boxes are carved out of the RIGHT of
        the slider's own row, so a dialog gains precision without gaining height.

        → KEY: the buffer is never PARSED outside the focused branch. It is of course read every frame to draw
          the text, but nothing turns it back into a number unless the box holds keyboard focus; everywhere
          else the value owns the text. That one rule is what makes a stale buffer a one-frame cosmetic glitch
          instead of a corrupted value — do not add a parse outside the focused branch.
        → GOTCHA: Widgets.TextFieldNumeric cannot be used here. It buffers correctly on the way IN (it writes
          the value only when the text changed this frame, and an unfinished entry never resolves), but it has
          no way back OUT: it never re-renders its buffer from the value, so a slider drag leaves the old text
          sitting there for the next keystroke to edit — the field and the slider disagree about what the value
          is. It also rewrites the buffer under the caret when it clamps: type 5, 0, 0 into a 1-200 box and the
          third keystroke leaves "200" on screen, not what you typed. And it has no lattice to snap a half-hour
          onto, rejects "," as a decimal separator, hard-codes its control name (two of them in one window would
          fight over focus), and formats floats as "0.##########".
        → draw order per frame is fixed: caption, then slider, then box. The slider nulls the buffer when it
          moved, and the box re-seeds from the new value in that same frame.
        → the sliders are drawn with middleAlignment: true, unlike Listing_Standard.Slider, so the bar lines up
          with the 22px box beside it (and it matches HD's own SettingsUI.Slider). Widgets.HorizontalSlider
          shifts the rect down by (height - 10) / 2 = 6px for that, and the shifted rect is also its hit
          region, so the click band spills ~4px past the row. Checked at all four call sites: what follows each
          row is a plain label or a gap, never a control, so nothing loses a click.
    */

    /// <summary>
    /// A thin numeric text box for a value that a slider also edits, implementing the focus-split contract
    /// described above. Every decision (what may be typed, what parses, how a value settles onto its range and
    /// lattice) is delegated to <see cref="NumberEntryPolicy"/> in the headless core; this type owns only the
    /// Verse drawing and the buffer bookkeeping.
    ///
    /// The buffer the caller passes by reference is the box's memory of what the player has typed. Null means
    /// "no pending text — re-read the value", which is how any writer OTHER than the box (a slider drag, a
    /// selection change) tells the box its value moved underneath it.
    /// </summary>
    public static class HDNumberField
    {
        /// <summary>Box width. Matches the amount field in the item-unload settings (58×22), the mod's only
        /// other numeric box, so the two read as the same control.</summary>
        public const float Width = 58f;

        /// <summary>Breathing room between the shortened slider and the box.</summary>
        public const float Gap = 6f;

        /// <summary>Height of one <c>Listing_Standard.Slider</c> row.</summary>
        public const float SliderRowH = 22f;

        /// <summary>The gap <c>Listing_Standard.Slider</c> leaves after its row. Reproducing the row height and
        /// this gap around a manual <c>GetRect</c> is what keeps a converted dialog exactly as tall as it was —
        /// the batch dialogs have only ~23px of slack in English and less in German or Russian.</summary>
        public const float SliderRowGap = 2f;

        /// <summary>
        /// Divide a full-width row into the slider's share and the box's share.
        /// </summary>
        /// <param name="row">The whole row, normally from <c>Listing_Standard.GetRect(SliderRowH)</c>.</param>
        /// <param name="sliderRect">Receives the left portion. Floored at 40px so a very narrow dialog degrades
        /// to a cramped-but-usable slider rather than an inverted rect.</param>
        /// <param name="boxRect">Receives a fixed-width box flush with the row's right edge, so the boxes in a
        /// dialog line up with each other whatever their sliders do.</param>
        public static void SplitRow(Rect row, out Rect sliderRect, out Rect boxRect)
        {
            boxRect = new Rect(row.xMax - Width, row.y, Width, row.height);
            sliderRect = new Rect(row.x, row.y, Mathf.Max(40f, row.width - Width - Gap), row.height);
        }

        /// <summary>
        /// Draw a whole-number box.
        /// </summary>
        /// <param name="boxRect">Where to draw, normally from <see cref="SplitRow"/>.</param>
        /// <param name="controlName">A name unique within the window, used to ask Unity whether this box holds
        /// keyboard focus. Two boxes sharing a name would each think the other's focus was their own.</param>
        /// <param name="value">The edited value. Written only from text the player has actually finished
        /// typing, and only ever clamped — never rounded — while they are typing.</param>
        /// <param name="buf">The box's pending text; pass null to (re)seed it from <paramref name="value"/>.</param>
        /// <param name="min">Inclusive floor applied to anything typed.</param>
        /// <param name="max">Inclusive ceiling applied to anything typed.</param>
        /// <param name="tooltip">Optional hover text. A bare box beside a slider does not announce what it
        /// accepts, so callers should pass one naming the range.</param>
        public static void Int(Rect boxRect, string controlName, ref int value, ref string buf, int min, int max,
            string tooltip = null)
        {
            if (buf == null)
                buf = NumberEntryPolicy.RenderInt(value);

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(boxRect, tooltip);

            GUI.SetNextControlName(controlName);
            string typed = Widgets.TextField(boxRect, buf);

            // Focus has to be read AFTER the draw: the click that focuses this box happens inside TextField.
            if (GUI.GetNameOfFocusedControl() == controlName)
            {
                // Focused: the player owns the text. Refusing a keystroke means handing the previous string
                // back to the widget, which is how Verse's own validating TextField overload rejects input.
                if (NumberEntryPolicy.IsAcceptable(typed, allowDecimal: false, maxLen: NumberEntryPolicy.MaxLength))
                    buf = typed;
                // A half-typed entry ("" on the way to a replacement) parses as nothing and leaves the value
                // alone. Writing a fallback here is precisely the bug that rules out TextFieldNumeric.
                if (NumberEntryPolicy.TryParseInt(buf, out int parsed))
                    value = NumberEntryPolicy.ClampInt(parsed, min, max);
            }
            else
            {
                // Unfocused: the value owns the text. This is where "007" becomes "7", an out-of-range entry
                // is pulled back, and a box left empty refills with the value it never managed to replace.
                value = NumberEntryPolicy.ClampInt(value, min, max);
                buf = NumberEntryPolicy.RenderInt(value);
            }
        }

        /// <summary>
        /// Draw a fractional-number box.
        /// </summary>
        /// <param name="boxRect">Where to draw, normally from <see cref="SplitRow"/>.</param>
        /// <param name="controlName">A name unique within the window; see <see cref="Int"/>.</param>
        /// <param name="value">The edited value. While the box is focused it is only clamped, never snapped:
        /// snapping "3.7" the instant it is typed would fight the player mid-entry. The correction happens on
        /// blur, where they can see it.</param>
        /// <param name="buf">The box's pending text; pass null to (re)seed it from <paramref name="value"/>.</param>
        /// <param name="min">Inclusive floor.</param>
        /// <param name="max">Inclusive ceiling.</param>
        /// <param name="step">Lattice the value settles onto when the box loses focus, e.g. 0.5 for half
        /// hours. Zero leaves the value free-floating.</param>
        /// <param name="format">Numeric format for the settled text; must preserve enough precision to
        /// describe <paramref name="step"/>.</param>
        /// <param name="tooltip">Optional hover text naming the range and the rounding.</param>
        public static void Float(Rect boxRect, string controlName, ref float value, ref string buf, float min, float max,
            float step, string format, string tooltip = null)
        {
            if (buf == null)
                buf = NumberEntryPolicy.RenderFloat(value, format);

            if (!tooltip.NullOrEmpty())
                TooltipHandler.TipRegion(boxRect, tooltip);

            GUI.SetNextControlName(controlName);
            string typed = Widgets.TextField(boxRect, buf);

            if (GUI.GetNameOfFocusedControl() == controlName)
            {
                if (NumberEntryPolicy.IsAcceptable(typed, allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength))
                    buf = typed;
                if (NumberEntryPolicy.TryParseFloat(buf, out float parsed))
                    value = NumberEntryPolicy.ClampFloat(parsed, min, max);
            }
            else
            {
                value = NumberEntryPolicy.SnapFloat(value, step, min, max);
                buf = NumberEntryPolicy.RenderFloat(value, format);
            }
        }
    }
}
