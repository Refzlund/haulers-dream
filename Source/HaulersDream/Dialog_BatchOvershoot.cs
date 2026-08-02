using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// A small popup for setting a "Do until you have X" (TargetCount) batch's "overshoot by Y" amount (issue #3):
    /// once the batch has started (vanilla starts it while the world count is below X), keep crafting up to X+Y so the
    /// pawn finishes to a useful round number "while it's already there", instead of stopping the instant the count
    /// crosses X. Mirrors the slider UX of <see cref="Dialog_BatchSize"/>; writes Y live to
    /// <see cref="HaulersDreamGameComponent"/> via the synced shim. Y == 0 means no overshoot (stop exactly at X).
    /// The slider value is only the REQUESTED overshoot — every run is still capped at craft time by available
    /// materials and the bill's own state. Beside the slider sits a numeric box (issue #237) for players who
    /// want an exact number rather than a draggable approximation; both edit the same value over the same range.
    /// </summary>
    public class Dialog_BatchOvershoot : Window
    {
        // The designed range for an overshoot, shared by the slider and the box so the two can never disagree.
        // Zero is a meaningful value here, not a floor artefact: it means "off — stop exactly at the target".
        private const int OvershootMin = 0;
        private const int OvershootMax = 200;

        private readonly Bill bill;
        private int overshoot;
        // The value the dialog opened with (== the value committed last time). Used in PreClose to detect whether
        // the player actually changed anything, so a no-op open/close issues no write at all.
        private readonly int initialOvershoot;

        // The numeric box's pending text. Null means "no pending text — re-read `overshoot`". EVERY writer of
        // `overshoot` other than the box itself must null this in the same place; there is exactly one such
        // writer here, the slider adoption in DoWindowContents.
        private string overshootBuf;

        public override Vector2 InitialSize => new Vector2(420f, 210f);

        public Dialog_BatchOvershoot(Bill bill)
        {
            this.bill = bill;
            // Clamp the OPENING value so `overshoot` and `initialOvershoot` agree from the first frame — the
            // stored amount is only bounded by the game component's BatchSizeMax, and letting the box's settle
            // pull an out-of-range one down later would read as a player edit and fire a synced multiplayer
            // command just for opening and closing the window. Same reasoning as Dialog_BatchSize.
            overshoot = Mathf.Clamp(Mathf.Max(0, HaulersDreamGameComponent.Instance?.BatchOvershootOf(bill) ?? 0), OvershootMin, OvershootMax);
            initialOvershoot = overshoot;
            forcePause = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            // closeOnAccept deliberately stays at its default (true) despite the new text field — see the same
            // note in Dialog_BatchSize: here the close IS the commit, so Enter already does the right thing.
        }

        public override void DoWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);

            Text.Font = GameFont.Medium;
            l.Label("HaulersDream.Batch.OvershootDialogTitle".Translate(bill.recipe?.ProducedThingDef?.label ?? bill.LabelCap));
            Text.Font = GameFont.Small;
            l.Gap(8f);

            // Caption, then slider, then box — in that order, every frame. The box is carved out of the SLIDER's
            // row rather than added below it, so the dialog keeps its height in every language.
            // 0 reads as "off" (stop exactly at X); otherwise show the +Y the controls hold.
            l.Label(overshoot > 0
                ? "HaulersDream.Batch.OvershootLabel".Translate(overshoot)
                : "HaulersDream.Batch.OvershootLabelOff".Translate());
            var row = l.GetRect(HDNumberField.SliderRowH);
            l.Gap(HDNumberField.SliderRowGap); // == what Listing_Standard.Slider leaves behind its own row
            HDNumberField.SplitRow(row, out var sliderRect, out var boxRect);

            // Adopt the slider's value only when the slider actually MOVED, so it can't overwrite a typed number.
            int fed = Mathf.Clamp(overshoot, OvershootMin, OvershootMax);
            int fromSlider = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, fed, OvershootMin, OvershootMax, middleAlignment: true));
            if (fromSlider != fed)
            {
                overshoot = fromSlider;
                overshootBuf = null;
            }
            HDNumberField.Int(boxRect, "HD_BatchOvershoot", ref overshoot, ref overshootBuf, OvershootMin, OvershootMax,
                "HaulersDream.Common.NumberBoxTip".Translate(OvershootMin, OvershootMax));

            l.Gap(6f);
            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            l.Label("HaulersDream.Batch.OvershootDesc".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            l.End();

            // No write here. The slider only edits the LOCAL `overshoot`; the synced write happens ONCE in PreClose
            // (same reasoning as Dialog_BatchSize: writing the scribed dict every frame would spam MP commands and
            // desync, since DoWindowContents runs at frame rate).
        }

        // Commit the chosen overshoot once, on close (X / click-outside — there is no OK button, matching the
        // live-edit UX of Dialog_BatchSize). Routed through the [SyncMethod] shim so the single write replays on every
        // client in MP; runs inline in single-player. Skip entirely when nothing changed so a no-op open/close issues
        // no command. SetBatchOvershoot clamps + removes the key on 0, so committing 0 is the "turn off" path.
        public override void PreClose()
        {
            base.PreClose();
            // Settle before comparing and committing: Enter, click-outside and the X all close this window
            // WITHOUT the numeric box ever losing focus, so the on-blur settle may never have run. Settling is
            // idempotent and the ctor already clamped the opening value, so an untouched open/close issues no
            // synced command for any stored amount.
            overshoot = NumberEntryPolicy.ClampInt(overshoot, OvershootMin, OvershootMax);
            if (overshoot != initialOvershoot)
                MultiplayerCompat.SetBillBatchOvershoot(bill, overshoot);
        }
    }
}
