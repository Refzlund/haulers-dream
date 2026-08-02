using System;
using System.Globalization;

namespace HaulersDream.Core
{
    /// <summary>
    /// The pure text-to-number rules behind HD's numeric entry boxes — the small "type an exact value" field
    /// that sits beside a slider (issue #237: the slider alone can't hit a precise number).
    ///
    /// It exists because the obvious approach, Verse's <c>Widgets.TextFieldNumeric</c>, is a one-way street.
    /// That widget handles typing INTO a value well enough — it writes only when the text changed this frame,
    /// and an unfinished entry never resolves — but it has no path back OUT: it never re-renders its buffer
    /// from the value, so the moment a slider (or any other writer) moves that value the field is still showing
    /// the old text, and the player's next keystroke edits a stale number. It also corrects the buffer under
    /// the caret when it clamps, so typing "500" into a 1-200 box becomes "200" halfway through; it has no
    /// notion of a lattice to round a half-hour onto; it refuses "," as a decimal separator; and its control
    /// name is hard-coded, so two of them in one window fight over focus.
    ///
    /// The box built on these rules splits ownership by FOCUS instead: while the box is focused the typed text
    /// owns the value, and while it is unfocused the value owns the text — which is the re-render path
    /// TextFieldNumeric lacks. Nothing outside the focused branch ever parses the buffer, so a stale buffer can
    /// only ever be a one-frame cosmetic glitch, never a value corruption.
    ///
    /// That split forces the second rule encoded here: a PARTIAL entry ("", ".", "3.") must be accepted as
    /// text but must NOT be turned into a number, or typing "25" over "10" would pass through the intermediate
    /// "2" and a cleared box would write 0. Hence <see cref="IsAcceptable"/> ("may the player type this?") is
    /// deliberately separate from the TryParse pair ("is this a finished number?").
    ///
    /// All four values this serves (batch size, batch overshoot, plan-craft repetitions, plan-craft timeout
    /// hours) have a minimum of zero or above, so '-' is deliberately NOT an acceptable character.
    ///
    /// Culture: parsing accepts BOTH '.' and ',' as the decimal separator, because the mod ships to 15 locales
    /// and a German player types "3,5" — but every parse and every render goes through
    /// <see cref="CultureInfo.InvariantCulture"/>, the same convention the settings profile codec uses. The
    /// text a box renders therefore always parses back to the number it came from, whatever the player's
    /// system locale is.
    /// </summary>
    public static class NumberEntryPolicy
    {
        /// <summary>
        /// How many characters a numeric box accepts. Six digits (999999) is far past every range this serves
        /// and stays well inside <see cref="int"/>, so a full-length entry can never overflow the parse.
        /// </summary>
        public const int MaxLength = 6;

        /// <summary>
        /// May the player's edited text stand as the box's contents? This is the KEYSTROKE gate, not a number
        /// check: it says yes to partial entries the player is still typing, so the caller can keep the text
        /// while leaving the underlying value alone.
        /// </summary>
        /// <param name="text">The candidate contents, straight from the text widget. Null is rejected so the
        /// caller keeps whatever it had.</param>
        /// <param name="allowDecimal">True for a fractional value (accepts one '.' or ','); false for a whole
        /// number, where any separator is rejected outright.</param>
        /// <param name="maxLen">Character budget, normally <see cref="MaxLength"/>. Anything longer is
        /// rejected rather than truncated, so the rejection reads as "that keystroke did nothing".</param>
        /// <returns>True for the empty string, and for any run of digits carrying at most one decimal
        /// separator. False for letters, signs, whitespace, a second separator, or over-length text.</returns>
        public static bool IsAcceptable(string text, bool allowDecimal, int maxLen)
        {
            if (text == null)
                return false;
            if (text.Length > maxLen)
                return false;

            // An empty box is a legal intermediate state: it is what the player sees after selecting all and
            // pressing delete, on the way to typing a replacement. It just never parses (see TryParse*).
            bool sawSeparator = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                    continue;
                if (allowDecimal && (c == '.' || c == ','))
                {
                    // Both spellings count against the SAME budget: "3.5,1" is not a number in any locale.
                    if (sawSeparator)
                        return false;
                    sawSeparator = true;
                    continue;
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// Read finished whole-number text. Deliberately strict: no sign, no whitespace, no separator — so a
        /// still-being-typed or malformed entry reports failure and the caller leaves its value untouched.
        /// </summary>
        /// <param name="text">Box contents; null, empty and over-long input all report failure (an
        /// over-long run of digits overflows <see cref="int"/> and is rejected rather than wrapped).</param>
        /// <param name="value">The parsed number on success; 0 on failure, which the caller must NOT use —
        /// writing that 0 back is exactly the TextFieldNumeric bug this class exists to avoid.</param>
        /// <returns>True only when the whole string is a finished non-negative integer.</returns>
        public static bool TryParseInt(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
                return false;
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Read finished fractional text, accepting either decimal separator by normalising ',' to '.' first
        /// (a German player types "3,5" for the same number an English one types "3.5").
        /// </summary>
        /// <param name="text">Box contents. "" and "." report failure — they are mid-typing states, not
        /// numbers. A trailing point ("3.") does parse, as 3, which is correct: the player has committed the
        /// integer part and is still choosing the fraction.</param>
        /// <param name="value">The parsed number on success; 0 on failure, which the caller must not use.</param>
        /// <returns>True only when the whole string is a finished non-negative decimal.</returns>
        public static bool TryParseFloat(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(text))
                return false;
            string normalized = text.Replace(',', '.');
            return float.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Hold a whole number inside its designed range.</summary>
        /// <param name="value">The candidate.</param>
        /// <param name="min">Inclusive floor; must not exceed <paramref name="max"/>.</param>
        /// <param name="max">Inclusive ceiling.</param>
        /// <returns>The nearest in-range value.</returns>
        public static int ClampInt(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        /// <summary>Hold a fractional number inside its designed range.</summary>
        /// <param name="value">The candidate. NaN needs an explicit answer because it compares false against
        /// both bounds and would otherwise pass straight through, then render as an unparseable "NaN" the box
        /// could never recover from; it resolves to <paramref name="min"/>, there being no nearer end to pick.
        /// The infinities need no special case — they simply exceed a bound, so +∞ lands on
        /// <paramref name="max"/> and -∞ on <paramref name="min"/>.</param>
        /// <param name="min">Inclusive floor; must not exceed <paramref name="max"/>.</param>
        /// <param name="max">Inclusive ceiling.</param>
        /// <returns>The nearest in-range value.</returns>
        public static float ClampFloat(float value, float min, float max)
        {
            if (float.IsNaN(value))
                return min;
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        /// <summary>
        /// Settle a fractional value onto its lattice and into its range — the "you typed 3.7, the setting is
        /// in half hours, so it becomes 3.5" step. Snapping happens BEFORE clamping so a value just past the
        /// ceiling (8.4 on a 0-8 range) rounds up to 8.5 and is then pulled back to 8, rather than escaping.
        /// </summary>
        /// <param name="value">The candidate, in the lattice's own units (hours here, not ticks).</param>
        /// <param name="step">Lattice spacing, e.g. 0.5 for half hours. Zero or negative means the value is
        /// free-floating and only the range applies.</param>
        /// <param name="min">Inclusive floor.</param>
        /// <param name="max">Inclusive ceiling.</param>
        /// <returns>An in-range value that is an exact multiple of <paramref name="step"/> whenever the step
        /// is positive. Exact midpoints round AWAY from zero (0.25 becomes 0.5, not 0), so a player who types
        /// the halfway point gets the larger of the two neighbours instead of a surprising drop to nothing.</returns>
        public static float SnapFloat(float value, float step, float min, float max)
        {
            if (step <= 0f || float.IsNaN(value))
                return ClampFloat(value, min, max);

            // Compute in double: a float division/multiplication round-trip can land a fraction of a ULP off
            // the lattice, which then renders as "3.4999999" instead of "3.5".
            double steps = Math.Round((double)value / step, MidpointRounding.AwayFromZero);
            return ClampFloat((float)(steps * step), min, max);
        }

        /// <summary>Render a whole number for display in a box, so that reading it back yields the same
        /// number ("007" typed in becomes "7" on settle).</summary>
        /// <param name="value">The number to show.</param>
        /// <returns>Locale-independent digits, always re-readable by <see cref="TryParseInt"/>.</returns>
        public static string RenderInt(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Render a fractional number for display in a box.</summary>
        /// <param name="value">The number to show; expected to be already settled onto its lattice, since the
        /// format decides how many fractional digits survive.</param>
        /// <param name="format">A .NET numeric format string, e.g. "0.#" to drop a trailing ".0". It must keep
        /// enough precision to describe the lattice, or the rendered text will not read back as the same
        /// number.</param>
        /// <returns>Locale-independent text using '.' as the separator, always re-readable by
        /// <see cref="TryParseFloat"/>.</returns>
        public static string RenderFloat(float value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
