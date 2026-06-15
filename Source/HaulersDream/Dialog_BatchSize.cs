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

        public override Vector2 InitialSize => new Vector2(420f, 210f);

        public Dialog_BatchSize(Bill bill)
        {
            this.bill = bill;
            size = Mathf.Max(1, HaulersDreamGameComponent.Instance?.BatchSizeOf(bill) ?? 10);
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

            // Apply live, so closing via the X or click-outside keeps the value (no OK button needed). The bill is
            // already batching (this dialog is only reachable from a batching bill's dropdown), so on=true is safe.
            HaulersDreamGameComponent.Instance?.SetBatch(bill, true, size);
        }
    }
}
