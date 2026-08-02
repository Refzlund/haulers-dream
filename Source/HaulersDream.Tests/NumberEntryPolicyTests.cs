using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Covers the text-to-number rules behind the numeric entry boxes added beside HD's batch-crafting sliders
    /// (issue #237). The tests are grouped by the guarantee they protect rather than by method, because the
    /// bugs these boxes are built to avoid are all cross-method: a partial entry silently becoming 0, a typed
    /// value being snapped mid-keystroke, or a settle step that keeps nudging the value and so makes an
    /// untouched open/close look like an edit.
    /// </summary>
    [TestFixture]
    public class NumberEntryPolicyTests
    {
        // The four live ranges, so a test failure names the real setting it would break.
        const int SizeMin = 1, SizeMax = 200;         // Dialog_BatchSize
        const int OvershootMin = 0;                   // Dialog_BatchOvershoot
        const float TimeoutMin = 0f, TimeoutMax = 8f; // Dialog_PlanCraft
        const float TimeoutStep = 0.5f;

        // ---- the "must not silently write 0" guard ----

        [Test]
        public void EmptyText_IsAcceptable_ButDoesNotParse()
        {
            // A cleared box is a legal thing to be looking at while typing a replacement...
            Assert.That(NumberEntryPolicy.IsAcceptable("", allowDecimal: false, maxLen: NumberEntryPolicy.MaxLength), Is.True);
            Assert.That(NumberEntryPolicy.IsAcceptable("", allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.True);

            // ...but it is NOT a number, so the caller keeps the value it already had rather than writing a
            // fallback. Reporting failure is the ONLY thing that stops the box from putting a 0 (or the
            // minimum) where the player's number was, since the caller has no other signal to go on.
            Assert.That(NumberEntryPolicy.TryParseInt("", out _), Is.False);
            Assert.That(NumberEntryPolicy.TryParseFloat("", out _), Is.False);
        }

        [Test]
        public void LoneSeparator_IsAcceptable_ButDoesNotParse()
        {
            // "." is what the player sees for one keystroke on the way to ".5".
            Assert.That(NumberEntryPolicy.IsAcceptable(".", allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.True);
            Assert.That(NumberEntryPolicy.TryParseFloat(".", out _), Is.False);
        }

        [Test]
        public void PartialEntry_KeepsTheDigitsAlreadyTyped()
        {
            // Typing "25" over "10" passes through "2": that must parse to 2 (live, clamped), never be treated
            // as a failure that reverts the box, and never be snapped while the player is still typing.
            Assert.That(NumberEntryPolicy.TryParseInt("2", out int typed), Is.True);
            Assert.That(typed, Is.EqualTo(2));

            // A committed integer part with the fraction still to come.
            Assert.That(NumberEntryPolicy.IsAcceptable("3.", allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.True);
            Assert.That(NumberEntryPolicy.TryParseFloat("3.", out float partial), Is.True);
            Assert.That(partial, Is.EqualTo(3f));
        }

        // ---- what the box refuses to let the player type ----

        [Test]
        public void RejectsLettersAndWhitespace()
        {
            Assert.That(NumberEntryPolicy.IsAcceptable("12a", allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.False);
            Assert.That(NumberEntryPolicy.IsAcceptable("abc", allowDecimal: false, maxLen: NumberEntryPolicy.MaxLength), Is.False);
            Assert.That(NumberEntryPolicy.IsAcceptable("1 2", allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.False);
        }

        [Test]
        public void RejectsMinus_BecauseEveryRangeStartsAtZeroOrAbove()
        {
            Assert.That(NumberEntryPolicy.IsAcceptable("-5", allowDecimal: false, maxLen: NumberEntryPolicy.MaxLength), Is.False);
            Assert.That(NumberEntryPolicy.IsAcceptable("-0.5", allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.False);
        }

        [Test]
        public void RejectsSecondSeparator_EitherSpelling()
        {
            Assert.That(NumberEntryPolicy.IsAcceptable("1.2.3", allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.False);
            Assert.That(NumberEntryPolicy.IsAcceptable("1,2,3", allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.False);
            // '.' and ',' share ONE budget — a mixed pair is not a number in any locale.
            Assert.That(NumberEntryPolicy.IsAcceptable("1.2,3", allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.False);
        }

        [Test]
        public void RejectsAnySeparator_WhenTheValueIsAWholeNumber()
        {
            Assert.That(NumberEntryPolicy.IsAcceptable("1.5", allowDecimal: false, maxLen: NumberEntryPolicy.MaxLength), Is.False);
            Assert.That(NumberEntryPolicy.IsAcceptable("1,5", allowDecimal: false, maxLen: NumberEntryPolicy.MaxLength), Is.False);
        }

        [Test]
        public void RejectsOverLength_RatherThanTruncating()
        {
            Assert.That(NumberEntryPolicy.IsAcceptable("123456", allowDecimal: false, maxLen: NumberEntryPolicy.MaxLength), Is.True);
            Assert.That(NumberEntryPolicy.IsAcceptable("1234567", allowDecimal: false, maxLen: NumberEntryPolicy.MaxLength), Is.False);
        }

        [Test]
        public void RejectsNull_SoTheCallerKeepsWhatItHad()
        {
            Assert.That(NumberEntryPolicy.IsAcceptable(null, allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.False);
            Assert.That(NumberEntryPolicy.TryParseInt(null, out _), Is.False);
            Assert.That(NumberEntryPolicy.TryParseFloat(null, out _), Is.False);
        }

        // ---- leading zeros, and the settle that tidies them ----

        [Test]
        public void LeadingZeros_AreTypableAndTidyOnSettle()
        {
            Assert.That(NumberEntryPolicy.IsAcceptable("007", allowDecimal: false, maxLen: NumberEntryPolicy.MaxLength), Is.True);
            Assert.That(NumberEntryPolicy.TryParseInt("007", out int v), Is.True);
            Assert.That(v, Is.EqualTo(7));
            Assert.That(NumberEntryPolicy.RenderInt(v), Is.EqualTo("7"));
        }

        // ---- both decimal separators, in any locale ----

        [Test]
        public void TryParseFloat_AcceptsBothSeparators()
        {
            Assert.That(NumberEntryPolicy.TryParseFloat("3.5", out float dot), Is.True);
            Assert.That(dot, Is.EqualTo(3.5f));
            Assert.That(NumberEntryPolicy.TryParseFloat("3,5", out float comma), Is.True);
            Assert.That(comma, Is.EqualTo(3.5f));
        }

        [Test]
        [SetCulture("de-DE")]
        public void ParseAndRender_AreLocaleIndependent()
        {
            // The mod ships to 15 locales. Under a comma-decimal system culture the box must still read BOTH
            // spellings, and must still RENDER with '.', so the text it shows reads back as the same number.
            Assert.That(NumberEntryPolicy.TryParseFloat("3,5", out float comma), Is.True);
            Assert.That(comma, Is.EqualTo(3.5f));
            Assert.That(NumberEntryPolicy.TryParseFloat("3.5", out float dot), Is.True);
            Assert.That(dot, Is.EqualTo(3.5f));

            Assert.That(NumberEntryPolicy.RenderFloat(3.5f, "0.#"), Is.EqualTo("3.5"));
            Assert.That(NumberEntryPolicy.RenderInt(1234), Is.EqualTo("1234")); // no thousands separator
        }

        // ---- range holding ----

        [Test]
        public void ClampInt_HoldsTheBatchSizeRange()
        {
            Assert.That(NumberEntryPolicy.ClampInt(500, SizeMin, SizeMax), Is.EqualTo(200));
            Assert.That(NumberEntryPolicy.ClampInt(0, SizeMin, SizeMax), Is.EqualTo(1));
            Assert.That(NumberEntryPolicy.ClampInt(37, SizeMin, SizeMax), Is.EqualTo(37));
            // Overshoot legitimately reaches 0 — "off", stop exactly at the target.
            Assert.That(NumberEntryPolicy.ClampInt(0, OvershootMin, SizeMax), Is.EqualTo(0));
        }

        [Test]
        public void ClampFloat_HandlesNonFinite()
        {
            // NaN has no nearer end — it compares false against both bounds — so it resolves to the minimum
            // rather than escaping as an unrenderable value. The infinities just exceed a bound as usual.
            Assert.That(NumberEntryPolicy.ClampFloat(float.NaN, TimeoutMin, TimeoutMax), Is.EqualTo(0f));
            Assert.That(NumberEntryPolicy.ClampFloat(float.PositiveInfinity, TimeoutMin, TimeoutMax), Is.EqualTo(8f));
            Assert.That(NumberEntryPolicy.ClampFloat(float.NegativeInfinity, TimeoutMin, TimeoutMax), Is.EqualTo(0f));
        }

        // ---- the half-hour lattice ----

        [Test]
        public void SnapFloat_RoundsToTheNearestHalfHour()
        {
            Assert.That(NumberEntryPolicy.SnapFloat(3.7f, TimeoutStep, TimeoutMin, TimeoutMax), Is.EqualTo(3.5f));
            Assert.That(NumberEntryPolicy.SnapFloat(3.2f, TimeoutStep, TimeoutMin, TimeoutMax), Is.EqualTo(3f));
            Assert.That(NumberEntryPolicy.SnapFloat(0.24f, TimeoutStep, TimeoutMin, TimeoutMax), Is.EqualTo(0f));
        }

        [Test]
        public void SnapFloat_MidpointsRoundAwayFromZero()
        {
            // 0.25 is the discriminating case: banker's rounding would drop it to 0, which reads as the box
            // eating the player's entry entirely.
            Assert.That(NumberEntryPolicy.SnapFloat(0.25f, TimeoutStep, TimeoutMin, TimeoutMax), Is.EqualTo(0.5f));
            Assert.That(NumberEntryPolicy.SnapFloat(3.75f, TimeoutStep, TimeoutMin, TimeoutMax), Is.EqualTo(4f));
        }

        [Test]
        public void SnapFloat_StaysInRange_EvenWhenRoundingPushesPastTheEdge()
        {
            // 8.4 rounds UP to 8.5 and must then be pulled back — snapping must not be an escape hatch.
            Assert.That(NumberEntryPolicy.SnapFloat(8.4f, TimeoutStep, TimeoutMin, TimeoutMax), Is.EqualTo(8f));
            Assert.That(NumberEntryPolicy.SnapFloat(99f, TimeoutStep, TimeoutMin, TimeoutMax), Is.EqualTo(8f));
            Assert.That(NumberEntryPolicy.SnapFloat(-1f, TimeoutStep, TimeoutMin, TimeoutMax), Is.EqualTo(0f));
        }

        [Test]
        public void SnapFloat_ZeroStepIsClampOnly()
        {
            Assert.That(NumberEntryPolicy.SnapFloat(3.7f, 0f, TimeoutMin, TimeoutMax), Is.EqualTo(3.7f));
            Assert.That(NumberEntryPolicy.SnapFloat(3.7f, -1f, TimeoutMin, TimeoutMax), Is.EqualTo(3.7f));
            Assert.That(NumberEntryPolicy.SnapFloat(12f, 0f, TimeoutMin, TimeoutMax), Is.EqualTo(8f));
        }

        [Test]
        public void SnapFloat_IsNotIdentityOffTheLattice_WhichIsWhyItCannotBeAComparand()
        {
            // Pins the property behind the timeout row's "did the slider move?" test. That test compares the
            // slider's RAW return against the value fed to it, because Widgets.HorizontalSlider hands its input
            // straight back when nobody is dragging it. Snapping the comparand instead looks equivalent and is
            // not: mid-entry the value is deliberately OFF the lattice (typing ".25" onto "2" passes through
            // 2.2), and there Snap(v) != v — so the comparison would report a move that never happened, adopt
            // the snapped value and wipe the buffer under the player's caret.
            foreach (float midTyping in new[] { 0.1f, 2.2f, 3.7f, 7.9f })
                Assert.That(NumberEntryPolicy.SnapFloat(midTyping, TimeoutStep, TimeoutMin, TimeoutMax), Is.Not.EqualTo(midTyping),
                    $"{midTyping} is off-lattice, so snapping it changes it — as a comparand that reads as a slider move");

            // A settled value, by contrast, survives a snap untouched, which is why the ADOPT side can snap freely.
            foreach (float settled in new[] { 0f, 0.5f, 2f, 3.5f, 8f })
                Assert.That(NumberEntryPolicy.SnapFloat(settled, TimeoutStep, TimeoutMin, TimeoutMax), Is.EqualTo(settled));
        }

        // ---- render/parse round-trips: the text a box shows must read back unchanged ----

        [Test]
        public void RenderInt_RoundTripsAcrossTheRange()
        {
            foreach (int v in new[] { SizeMin, (SizeMin + SizeMax) / 2, SizeMax, OvershootMin })
            {
                string text = NumberEntryPolicy.RenderInt(v);
                Assert.That(NumberEntryPolicy.IsAcceptable(text, allowDecimal: false, maxLen: NumberEntryPolicy.MaxLength), Is.True,
                    $"rendered '{text}' is not re-typable");
                Assert.That(NumberEntryPolicy.TryParseInt(text, out int back), Is.True);
                Assert.That(back, Is.EqualTo(v));
            }
        }

        [Test]
        public void RenderFloat_RoundTripsAcrossTheHalfHourLattice()
        {
            for (float v = TimeoutMin; v <= TimeoutMax; v += TimeoutStep)
            {
                string text = NumberEntryPolicy.RenderFloat(v, "0.#");
                Assert.That(NumberEntryPolicy.IsAcceptable(text, allowDecimal: true, maxLen: NumberEntryPolicy.MaxLength), Is.True,
                    $"rendered '{text}' is not re-typable");
                Assert.That(NumberEntryPolicy.TryParseFloat(text, out float back), Is.True);
                Assert.That(back, Is.EqualTo(v), $"'{text}' did not read back as {v}");
            }
        }

        // ---- settling is idempotent: an untouched open/close is not an edit ----

        [Test]
        public void SettleInt_IsIdempotent_SoAnUntouchedDialogWritesNothing()
        {
            // Both batch dialogs commit on close and skip the (multiplayer-synced) write when the value equals
            // the one they opened with. If settling ever nudged an already-valid value, merely opening and
            // closing a batch dialog would issue a spurious synced write.
            foreach (int v in new[] { -5, 0, 1, 37, 200, 201, 999 })
            {
                int once = NumberEntryPolicy.ClampInt(v, SizeMin, SizeMax);
                Assert.That(NumberEntryPolicy.ClampInt(once, SizeMin, SizeMax), Is.EqualTo(once));
            }
            // The in-range case stated directly: size == initialSize, so PreClose issues no command at all.
            for (int v = SizeMin; v <= SizeMax; v++)
                Assert.That(NumberEntryPolicy.ClampInt(v, SizeMin, SizeMax), Is.EqualTo(v));
        }

        [Test]
        public void SettleFloat_IsIdempotent()
        {
            foreach (float v in new[] { -1f, 0f, 0.24f, 0.25f, 2f, 3.7f, 3.75f, 8f, 8.4f, 99f })
            {
                float once = NumberEntryPolicy.SnapFloat(NumberEntryPolicy.ClampFloat(v, TimeoutMin, TimeoutMax), TimeoutStep, TimeoutMin, TimeoutMax);
                float twice = NumberEntryPolicy.SnapFloat(NumberEntryPolicy.ClampFloat(once, TimeoutMin, TimeoutMax), TimeoutStep, TimeoutMin, TimeoutMax);
                Assert.That(twice, Is.EqualTo(once), $"settling {v} was not idempotent");
            }
        }
    }
}
