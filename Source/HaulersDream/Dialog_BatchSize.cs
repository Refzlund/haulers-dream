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
    /// </summary>
    public class Dialog_BatchSize : Window
    {
        private readonly Bill bill;
        private int size;
        // The value the dialog opened with (== the value committed last time). Used in PreClose to detect whether
        // the player actually changed anything, so a no-op open/close issues no write at all.
        private readonly int initialSize;

        public override Vector2 InitialSize => new Vector2(420f, 210f);

        public Dialog_BatchSize(Bill bill)
        {
            this.bill = bill;
            size = Mathf.Max(1, HaulersDreamGameComponent.Instance?.BatchSizeOf(bill) ?? 10);
            initialSize = size;
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
            l.Label("HaulersDream.Batch.SizeDialogTitle".Translate(bill.recipe?.ProducedThingDef?.label ?? bill.LabelCap));
            Text.Font = GameFont.Small;
            l.Gap(8f);

            l.Label("HaulersDream.Batch.SizeLabel".Translate(size));
            size = Mathf.RoundToInt(l.Slider(size, 1f, 200f));

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
            if (size != initialSize)
                MultiplayerCompat.SetBillBatch(bill, true, size);
        }
    }
}
