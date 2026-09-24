using System;
using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    internal sealed class Dialog_DropInventoryAmount : Window
    {
        private readonly Pawn pawn;
        private readonly Thing source;
        private readonly ThingOwner owner;
        private readonly int openingCount, mapId;
        private readonly InventoryDropUiPolicy policy;
        private readonly string pawnLabel, itemLabel;
        private int amount;
        private string buffer;
        private bool submitted;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(600f, 430f);

        internal Dialog_DropInventoryAmount(Pawn pawn, Thing source, InventoryDropUiPolicy policy)
        {
            this.pawn = pawn;
            this.source = source;
            this.policy = policy;
            owner = pawn.inventory.innerContainer;
            openingCount = source.stackCount;
            mapId = pawn.Map.uniqueID;
            pawnLabel = pawn.LabelShortCap;
            itemLabel = source.LabelNoCount;
            amount = openingCount;
            buffer = amount.ToString(CultureInfo.InvariantCulture);
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
        }

        private bool ReadAmount(out int parsed) => int.TryParse(buffer, NumberStyles.None,
            CultureInfo.InvariantCulture, out parsed) && parsed >= 1 && parsed <= openingCount;

        public override void DoWindowContents(Rect inRect)
        {
            // A focused Unity text field may consume Return before the controls below finish drawing.
            bool enterPressed = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (enterPressed)
                Event.current.Use();
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap, oldEnabled = GUI.enabled;
            try
            {
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Medium;
                string title = "HD_DropAmountTitle".Translate();
                float titleHeight = Text.CalcHeight(title, inRect.width - 25f);
                Widgets.Label(new Rect(0, 0, inRect.width - 25f, titleHeight), title);
                Text.Font = GameFont.Small;

                string confirm = "HD_DropAmountConfirm".Translate();
                string cancel = "Cancel".Translate();
                float buttonWidth = (inRect.width - 12f) / 2f;
                float buttonHeight = Math.Max(38f, Math.Max(Text.CalcHeight(confirm, buttonWidth - 16f),
                    Text.CalcHeight(cancel, buttonWidth - 16f)) + 12f);
                float buttonsY = inRect.height - buttonHeight;
                var viewport = new Rect(0, titleHeight + 12f, inRect.width, buttonsY - titleHeight - 24f);
                float width = viewport.width - 20f;
                string context = "HD_DropAmountContext".Translate(pawnLabel, itemLabel, openingCount);
                string quantity = "HD_DropAmountQuantity".Translate();
                bool valid = ReadAmount(out int parsed);
                if (valid) amount = parsed;
                string remainder = valid
                    ? "HD_DropAmountRemaining".Translate(openingCount - parsed).ToString()
                    : "HD_DropAmountInvalid".Translate(openingCount).ToString();
                float contextHeight = Text.CalcHeight(context, width);
                float quantityHeight = Text.CalcHeight(quantity, width);
                float bodyHeight = contextHeight + quantityHeight + 114f + Text.CalcHeight(remainder, width);
                Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, width, Math.Max(viewport.height, bodyHeight)));
                try
                {
                    float y = 0;
                    Widgets.Label(new Rect(0, y, width, contextHeight), context);
                    y += contextHeight + 16f;
                    Widgets.Label(new Rect(0, y, width, quantityHeight), quantity);
                    y += quantityHeight + 6f;
                    buffer = Widgets.TextField(new Rect(0, y, Math.Min(220f, width), 32f), buffer);
                    y += 44f;
                    float fraction = (float)((amount - 1d) / (openingCount - 1d));
                    float selected = Widgets.HorizontalSlider(new Rect(0, y, width, 24f), fraction, 0f, 1f);
                    if (selected != fraction)
                    {
                        // Floating-point sliders cannot select every huge-stack integer. The exact field can;
                        // double arithmetic also keeps the final endpoint away from an Int32 float overflow.
                        amount = (int)Math.Max(1d, Math.Min(openingCount,
                            1d + Math.Round((double)selected * (openingCount - 1d))));
                        buffer = amount.ToString(CultureInfo.InvariantCulture);
                    }
                    y += 38f;
                    valid = ReadAmount(out parsed);
                    remainder = valid ? "HD_DropAmountRemaining".Translate(openingCount - parsed).ToString()
                        : "HD_DropAmountInvalid".Translate(openingCount).ToString();
                    Widgets.Label(new Rect(0, y, width, Text.CalcHeight(remainder, width)), remainder);
                }
                finally { Widgets.EndScrollView(); }

                GUI.enabled = oldEnabled && valid && !submitted;
                if (Widgets.ButtonText(new Rect(0, buttonsY, buttonWidth, buttonHeight), confirm))
                    Confirm();
                GUI.enabled = oldEnabled;
                if (Widgets.ButtonText(new Rect(buttonWidth + 12f, buttonsY, buttonWidth, buttonHeight), cancel))
                    Close();
                if (enterPressed)
                {
                    if (valid) Confirm();
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.enabled = oldEnabled;
            }
        }

        private void Confirm()
        {
            if (submitted || !ReadAmount(out int chosen))
                return;
            if (!MultiplayerCompat.InventoryQuantityDropLocalUi)
                return;
            if (!ReferenceEquals(pawn.inventory?.innerContainer, owner)
                || !ReferenceEquals(InventoryDropCommand.Resolve(pawn, source.thingIDNumber, chosen,
                    openingCount, mapId, policy), source))
            {
                LocalFeedback("HD_DropAmountChanged");
                Close();
                return;
            }
            if (!MultiplayerCompat.InventoryQuantityDropAvailable || !InventoryDropCompat.AvailableFor(pawn, source))
            {
                LocalFeedback("HD_DropAmountUnavailable");
                Close();
                return;
            }
            submitted = true;
            InventoryDropCommand.DropInventoryCountSynced(pawn, source.thingIDNumber, chosen,
                openingCount, mapId, (int)policy);
            Close();
        }

        internal static void LocalFeedback(string key) =>
            Messages.Message(key.Translate(), MessageTypeDefOf.RejectInput, historical: false);
    }
}
