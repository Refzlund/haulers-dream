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
    /// materials and the bill's own state.
    /// </summary>
    public class Dialog_BatchOvershoot : Window
    {
        private readonly Bill bill;
        private int overshoot;
        // The value the dialog opened with (== the value committed last time). Used in PreClose to detect whether
        // the player actually changed anything, so a no-op open/close issues no write at all.
        private readonly int initialOvershoot;

        public override Vector2 InitialSize => new Vector2(420f, 210f);

        public Dialog_BatchOvershoot(Bill bill)
        {
            this.bill = bill;
            overshoot = Mathf.Max(0, HaulersDreamGameComponent.Instance?.BatchOvershootOf(bill) ?? 0);
            initialOvershoot = overshoot;
            forcePause = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);

            Text.Font = GameFont.Medium;
            l.Label("HaulersDream.Batch.OvershootDialogTitle".Translate(bill.recipe?.ProducedThingDef?.label ?? bill.LabelCap));
            Text.Font = GameFont.Small;
            l.Gap(8f);

            // 0 reads as "off" (stop exactly at X); otherwise show the +Y the slider holds.
            l.Label(overshoot > 0
                ? "HaulersDream.Batch.OvershootLabel".Translate(overshoot)
                : "HaulersDream.Batch.OvershootLabelOff".Translate());
            overshoot = Mathf.RoundToInt(l.Slider(overshoot, 0f, 200f));

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
            if (overshoot != initialOvershoot)
                MultiplayerCompat.SetBillBatchOvershoot(bill, overshoot);
        }
    }
}
