using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// A small popup for setting a bill's per-batch quantity — mirrors the slider UX of the right-click
    /// Plan-Craft dialog the player already uses. Opened from the repeat-mode dropdown's "Batch size: N…" entry;
    /// writes the size live to <see cref="HaulersDreamGameComponent"/>. The slider value is only the REQUESTED
    /// size — every run is still capped at craft time by available materials and the bill's own remaining count.
    /// Beside the slider sits a numeric box (issue #237) for players who want an exact number rather than a
    /// draggable approximation; both edit the same value over the same range.
    /// </summary>
    public class Dialog_BatchSize : Window
    {
        // The designed range for a batch. Shared by the slider and the box so the two can never disagree about
        // what a legal size is. (The game component tolerates up to BatchSizeMax, but 200 is the size the UI has
        // always offered and the default respects; widening it is a separate design decision.)
        private const int SizeMin = 1;
        private const int SizeMax = 200;

        private readonly Bill bill;
        private int size;
        // The value the dialog opened with (== the value committed last time). Used in PreClose to detect whether
        // the player actually changed anything, so a no-op open/close issues no write at all.
        private readonly int initialSize;

        // The numeric box's pending text. Null means "no pending text — re-read `size`". EVERY writer of `size`
        // other than the box itself must null this in the same place, or the box would keep showing the old
        // number. There is exactly one such writer here: the slider adoption in DoWindowContents.
        private string sizeBuf;

        public override Vector2 InitialSize => new Vector2(420f, 210f);

        public Dialog_BatchSize(Bill bill)
        {
            this.bill = bill;
            // Clamp the OPENING value, not just the committed one, so `size` and `initialSize` agree from the
            // first frame. The stored size is not bounded by SizeMax — the game component allows up to
            // BatchSizeMax and a pasted settings profile can carry a defaultBatchSize past 200 (its own slider
            // stops there, so this needs a hand-edited token) — and the box's own settle would pull such a value
            // down on frame one. Without this line that settle would then read as a player edit and fire a
            // synced multiplayer command just for opening and closing the window. Clamping here instead means
            // the dialog shows and offers only what it can actually set, and a stored value beyond its range is
            // left exactly as it was unless the player really does change something.
            size = Mathf.Clamp(Mathf.Max(1, HaulersDreamGameComponent.Instance?.BatchSizeOf(bill) ?? 10), SizeMin, SizeMax);
            initialSize = size;
            forcePause = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            // closeOnAccept deliberately stays at its default (true), even though this window now holds a text
            // field. The usual "a dialog with a text field needs closeOnAccept = false" rule exists so Return can
            // reach a handler that DOES something (see Dialog_NameProfile / Dialog_ReportIssue). Here closing IS
            // the commit: the typed value is already in `size`, and Close() runs PreClose, which settles and
            // writes it. Turning the flag off would only make Enter do nothing.
        }

        public override void DoWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);

            Text.Font = GameFont.Medium;
            l.Label("HaulersDream.Batch.SizeDialogTitle".Translate(bill.recipe?.ProducedThingDef?.label ?? bill.LabelCap));
            Text.Font = GameFont.Small;
            l.Gap(8f);

            // Caption, then slider, then box — in that order, every frame. Carving the box out of the SLIDER's
            // row (rather than adding one) keeps the dialog exactly as tall as it was: the caption embeds the
            // number, so its height already varies with the language, and this window has barely any slack.
            l.Label("HaulersDream.Batch.SizeLabel".Translate(size));
            var row = l.GetRect(HDNumberField.SliderRowH);
            l.Gap(HDNumberField.SliderRowGap); // == what Listing_Standard.Slider leaves behind its own row
            HDNumberField.SplitRow(row, out var sliderRect, out var boxRect);

            // Adopt the slider's value only when the slider actually MOVED. Reading it unconditionally would let
            // it overwrite a number typed into the box, and this way the box's re-seed happens in the same frame.
            int fed = Mathf.Clamp(size, SizeMin, SizeMax);
            int fromSlider = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, fed, SizeMin, SizeMax, middleAlignment: true));
            if (fromSlider != fed)
            {
                size = fromSlider;
                sizeBuf = null;
            }
            HDNumberField.Int(boxRect, "HD_BatchSize", ref size, ref sizeBuf, SizeMin, SizeMax,
                "HaulersDream.Common.NumberBoxTip".Translate(SizeMin, SizeMax));

            l.Gap(6f);
            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            l.Label("HaulersDream.Batch.SizeDesc".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            l.End();

            // No write here. The slider only edits the LOCAL `size`; the synced write happens ONCE in PreClose.
            // MP: SetBatch writes the SCRIBED batchBills dict (synced world state). Writing it every frame here would
            // both spam commands and desync in multiplayer (DoWindowContents runs at frame rate, untimed across
            // clients). Committing once on close gives exactly one synced write per edit session.
        }

        // Commit the chosen size once, on close (X / click-outside — there is no OK button, matching the live-edit
        // UX the player expects: the value is kept on close). Routed through the [SyncMethod] shim so the single
        // write replays on every client in MP; runs inline in single-player. Skip entirely when nothing changed so a
        // no-op open/close issues no command. on=true is safe: this dialog is only reachable from a batching bill's
        // dropdown.
        public override void PreClose()
        {
            base.PreClose();
            // Settle before comparing and committing. Every realistic way out of this window — Enter,
            // click-outside, the X — closes it WITHOUT the numeric box ever losing focus, so the on-blur settle
            // may never have run. This is therefore the one place that guarantees the committed size obeys the
            // dialog's own range, however the player left. Settling is idempotent and the ctor already clamped
            // the opening value, so an untouched open/close leaves size == initialSize for EVERY stored value
            // and issues no synced command at all.
            size = NumberEntryPolicy.ClampInt(size, SizeMin, SizeMax);
            if (size != initialSize)
                MultiplayerCompat.SetBillBatch(bill, true, size);
        }
    }
}
