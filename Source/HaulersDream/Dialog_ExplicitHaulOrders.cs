using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    internal sealed class Dialog_ExplicitHaulOrders : Window
    {
        private readonly Pawn pawn;
        private readonly int mapId;
        private Vector2 scroll;
        private bool queue;
        public override Vector2 InitialSize => new Vector2(680f, 590f);

        internal Dialog_ExplicitHaulOrders(Pawn pawn, int mapId)
        {
            this.pawn = pawn; this.mapId = mapId;
            doCloseX = true; doCloseButton = true; absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect rect)
        {
            var font = Text.Font; var anchor = Text.Anchor; bool wrap = Text.WordWrap, enabled = GUI.enabled;
            try
            {
                Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true;
                string title = "HD_ExplicitOrders".Translate(); float titleHeight = Text.CalcHeight(title, rect.width - 24f);
                Widgets.Label(new Rect(0, 0, rect.width - 24f, titleHeight), title); Text.Font = GameFont.Small;
                if (!ExplicitHaulUi.Local(pawn, mapId))
                { Widgets.Label(new Rect(0, titleHeight + 12f, rect.width, 100f), "HD_ExplicitChanged".Translate()); return; }
                string queueLabel = "HD_ExplicitQueueResume".Translate();
                float queueHeight = Math.Max(30f, Text.CalcHeight(queueLabel, rect.width - 30f));
                Widgets.CheckboxLabeled(new Rect(0, titleHeight + 12f, rect.width, queueHeight), queueLabel, ref queue);
                var viewport = new Rect(0, titleHeight + queueHeight + 24f, rect.width, rect.height - titleHeight - queueHeight - 90f);
                float width = viewport.width - 20f, buttonWidth = (width - 12f) / 2f;
                string resume = "HD_ExplicitResume".Translate(), cancel = "HD_ExplicitCancel".Translate();
                float buttonHeight = Math.Max(36f, Math.Max(Text.CalcHeight(resume, buttonWidth - 16f), Text.CalcHeight(cancel, buttonWidth - 16f)) + 12f);
                var orders = pawn.GetComp<CompHauledToInventory>().ExplicitOrders.Where(o => o != null).Reverse().ToArray();
                var labels = orders.Select(o => "HD_ExplicitProgress".Translate(o.id, o.source?.LabelNoCount ?? "HD_ExplicitMissingItem".Translate().ToString(),
                    o.delivered, o.requested, o.destination.x, o.destination.z).ToString()
                    + (o.IsShelf ? "\n" + "HD_ExplicitShelf".Translate(o.shelf?.LabelNoCount ?? "HD_ExplicitMissingShelf".Translate().ToString()) : "")
                    + "\n" + ExplicitHaulUi.Reason(o).Translate()).ToArray();
                var heights = labels.Select(label => Text.CalcHeight(label, width) + buttonHeight + 30f).ToArray();
                Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, width, Math.Max(viewport.height, heights.Sum())));
                try
                {
                    float y = 0;
                    for (int i = 0; i < orders.Length; i++)
                    {
                        var order = orders[i]; float labelHeight = heights[i] - buttonHeight - 30f;
                        Widgets.Label(new Rect(0, y, width, labelHeight), labels[i]); y += labelHeight + 8f;
                        GUI.enabled = enabled && order.state == ExplicitHaulState.Blocked && !ExplicitHaulUi.HasLinkedJob(pawn, order)
                            && ExplicitHaulCommand.ActorReason(pawn) == null;
                        if (Widgets.ButtonText(new Rect(0, y, buttonWidth, buttonHeight), resume))
                            ExplicitHaulCommand.DispatchResume(pawn, order.id, mapId, queue);
                        GUI.enabled = enabled && (order.state != ExplicitHaulState.Complete && (order.state != ExplicitHaulState.Cancelled || order.piece != null));
                        if (Widgets.ButtonText(new Rect(buttonWidth + 12f, y, buttonWidth, buttonHeight), cancel))
                            ExplicitHaulCommand.DispatchCancel(pawn, order.id, mapId);
                        GUI.enabled = enabled; y += buttonHeight + 22f;
                    }
                }
                finally { Widgets.EndScrollView(); }
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.enabled = enabled; }
        }
    }
}
