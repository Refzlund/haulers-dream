using System;
using System.Globalization;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    internal sealed class Dialog_ExplicitHaulAmount : Window
    {
        private readonly ExplicitHaulSelection selection;
        private string buffer;
        private int amount;
        private bool queue, submitted;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(600f, 470f);

        internal Dialog_ExplicitHaulAmount(ExplicitHaulSelection selection)
        {
            this.selection = selection; amount = selection.OpeningCount; queue = selection.Queue;
            buffer = amount.ToString(CultureInfo.InvariantCulture);
            doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = true; closeOnAccept = false;
        }

        private bool Read(out int chosen) => int.TryParse(buffer, NumberStyles.None, CultureInfo.InvariantCulture, out chosen)
            && chosen >= 1 && chosen <= selection.OpeningCount;

        public override void DoWindowContents(Rect rect)
        {
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (enter) Event.current.Use();
            var font = Text.Font; var anchor = Text.Anchor; bool wrap = Text.WordWrap, enabled = GUI.enabled;
            try
            {
                Text.WordWrap = true; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Medium;
                string title = "HD_ExplicitAmountTitle".Translate(); float titleHeight = Text.CalcHeight(title, rect.width - 24f);
                Widgets.Label(new Rect(0, 0, rect.width - 24f, titleHeight), title); Text.Font = GameFont.Small;
                string confirm = "HD_ExplicitChooseDestination".Translate(), cancel = "Cancel".Translate();
                float buttonWidth = (rect.width - 12f) / 2f;
                float buttonHeight = Math.Max(38f, Math.Max(Text.CalcHeight(confirm, buttonWidth - 16), Text.CalcHeight(cancel, buttonWidth - 16)) + 12f);
                float buttonsY = rect.height - buttonHeight;
                var viewport = new Rect(0, titleHeight + 12f, rect.width, buttonsY - titleHeight - 24f);
                float width = viewport.width - 20f;
                string context = "HD_ExplicitAmountContext".Translate(selection.Pawn.LabelShortCap, selection.Source.LabelNoCount, selection.OpeningCount);
                string queueLabel = "HD_ExplicitQueue".Translate();
                bool valid = Read(out int chosen); if (valid) amount = chosen;
                string status = !selection.Live ? "HD_ExplicitChanged".Translate().ToString()
                    : valid ? "HD_ExplicitAmountHint".Translate(chosen).ToString() : "HD_DropAmountInvalid".Translate(selection.OpeningCount).ToString();
                float contextHeight = Text.CalcHeight(context, width), queueHeight = Math.Max(30f, Text.CalcHeight(queueLabel, width - 30f));
                float body = contextHeight + queueHeight + Text.CalcHeight(status, width) + 128f;
                Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, width, Math.Max(body, viewport.height)));
                try
                {
                    Widgets.Label(new Rect(0, 0, width, contextHeight), context);
                    float y = contextHeight + 16f;
                    buffer = Widgets.TextField(new Rect(0, y, Math.Min(220f, width), 32f), buffer); y += 44f;
                    float fraction = (float)((amount - 1d) / Math.Max(1d, selection.OpeningCount - 1d));
                    float selected = Widgets.HorizontalSlider(new Rect(0, y, width, 24f), fraction, 0f, 1f);
                    if (selected != fraction)
                    {
                        amount = (int)Math.Max(1d, Math.Min(selection.OpeningCount, 1d + Math.Round(selected * (selection.OpeningCount - 1d))));
                        buffer = amount.ToString(CultureInfo.InvariantCulture);
                    }
                    y += 38f; Widgets.CheckboxLabeled(new Rect(0, y, width, queueHeight), queueLabel, ref queue); y += queueHeight + 12f;
                    valid = Read(out chosen);
                    status = !selection.Live ? "HD_ExplicitChanged".Translate().ToString()
                        : valid ? "HD_ExplicitAmountHint".Translate(chosen).ToString() : "HD_DropAmountInvalid".Translate(selection.OpeningCount).ToString();
                    Widgets.Label(new Rect(0, y, width, Text.CalcHeight(status, width)), status);
                }
                finally { Widgets.EndScrollView(); }
                GUI.enabled = enabled && valid && selection.Live && !submitted;
                if (Widgets.ButtonText(new Rect(0, buttonsY, buttonWidth, buttonHeight), confirm)) Confirm();
                GUI.enabled = enabled;
                if (Widgets.ButtonText(new Rect(buttonWidth + 12f, buttonsY, buttonWidth, buttonHeight), cancel)) Close();
                if (enter && valid) Confirm();
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.enabled = enabled; }
        }

        private void Confirm()
        {
            if (submitted || !Read(out int chosen)) return;
            if (!selection.Live) { ExplicitHaulUi.Reject("HD_ExplicitChanged"); Close(); return; }
            submitted = true; Close(); selection.Target(chosen, queue);
        }
    }
}
