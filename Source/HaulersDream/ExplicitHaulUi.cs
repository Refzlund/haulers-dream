using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    // UI selections own no world state. Only the existing synchronized command allocates an order.
    internal static class ExplicitHaulUi
    {
        internal static bool Local(Pawn pawn, int mapId) => MultiplayerCompat.ExplicitHaulLocalUi
            && pawn?.Map != null && pawn.Map.uniqueID == mapId && pawn.Map == Find.CurrentMap
            && Find.Selector.SingleSelectedThing == pawn && pawn.Faction == Faction.OfPlayerSilentFail;

        internal static bool SourceAvailable(Pawn pawn, Thing source) => ExplicitHaulCommand.ActorReason(pawn) == null
            && NearbyHaulCommand.IsGroundTarget(pawn, source);

        internal static void Reject(string key) => Messages.Message(key.Translate(), MessageTypeDefOf.RejectInput, false);

        internal static void DrawPrompt(string text)
        {
            var font = Text.Font; var anchor = Text.Anchor; bool wrap = Text.WordWrap; var color = GUI.color;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                float width = Math.Min(380f, UI.screenWidth - 24f), height = Text.CalcHeight(text, width - 16f) + 16f;
                var mouse = Event.current.mousePosition;
                var rect = new Rect(Mathf.Clamp(mouse.x + 24f, 8f, UI.screenWidth - width - 8f),
                    Mathf.Clamp(mouse.y + 24f, 8f, Math.Max(8f, UI.screenHeight - height - 8f)), width, height);
                // Targeter GUI precedes the inspect pane and gizmo rows. Use the native mouse
                // attachment layer so their later drawing cannot cover a wrapped prompt.
                Find.WindowStack.ImmediateWindow(782196441, rect, WindowLayer.Super,
                    () => DrawPromptWindow(rect.AtZero(), text), doBackground: false,
                    absorbInputAroundWindow: false, shadowAlpha: 0f);
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
        }

        private static void DrawPromptWindow(Rect rect, string text)
        {
            var font = Text.Font; var anchor = Text.Anchor; bool wrap = Text.WordWrap; var color = GUI.color;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 1f));
                Widgets.Label(rect.ContractedBy(8f), text);
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
        }

        internal static void Open(Pawn pawn, Thing source, int mapId, bool queue)
        {
            if (!Local(pawn, mapId) || !SourceAvailable(pawn, source)) { Reject("HD_ExplicitChanged"); return; }
            var selection = new ExplicitHaulSelection(pawn, source, queue);
            if (selection.OpeningCount == 1) selection.Target(1, queue);
            else Find.WindowStack.Add(new Dialog_ExplicitHaulAmount(selection));
        }

        internal static void SelectSource(Pawn pawn, int mapId, bool queue)
        {
            if (!Local(pawn, mapId) || ExplicitHaulCommand.ActorReason(pawn) != null) { Reject("HD_ExplicitActorUnavailable"); return; }
            var parameters = new TargetingParameters
            {
                canTargetPawns = false, canTargetBuildings = false, canTargetPlants = false,
                canTargetItems = true, canTargetLocations = false, canTargetCorpses = true,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = target => Local(pawn, mapId) && SourceAvailable(pawn, target.Thing)
            };
            Find.DesignatorManager.Deselect();
            Find.Targeter.BeginTargeting(parameters,
                target => Open(pawn, target.Thing, mapId, queue), null,
                target => Local(pawn, mapId) && SourceAvailable(pawn, target.Thing), pawn,
                mouseAttachment: ExplicitHaulUiTextures.Icon,
                onGuiAction: target => DrawPrompt("HD_ExplicitSelectSource".Translate()));
        }

        internal static string Reason(ExplicitHaulOrder order)
        {
            if (order.state != ExplicitHaulState.Blocked) return "HD_ExplicitState" + order.state;
            switch (order.reason)
            {
                case "Selected source exhausted or replaced":
                case "Selected source permission changed": return "HD_ExplicitSourceChanged";
                case "Selected source is reserved": return "HD_ExplicitSourceReserved";
                case "Selected bare location unavailable or reserved":
                case "Selected shelf unavailable or reserved":
                case "Selected shelf moved or replaced":
                case "Selected location accepted only part of the parcel":
                case "No current carry or destination capacity": return "HD_ExplicitDestinationBlocked";
                case "Selected route is unavailable": return "HD_ExplicitRouteBlocked";
                case "Hands already contain unrelated cargo": return "HD_ExplicitHandsBusy";
                case "Order identity or pawn permission changed": return "HD_ExplicitActorUnavailable";
                case "Order interrupted; resume explicitly":
                case "Captured job was not restored": return "HD_ExplicitInterrupted";
                case "Native job admission refused":
                case "Native job admission failed": return "HD_ExplicitAdmissionFailed";
                default: return "HD_ExplicitNeedsReview";
            }
        }

        internal static bool HasLinkedJob(Pawn pawn, ExplicitHaulOrder order) =>
            pawn.CurJob?.loadID == order.jobId || pawn.jobs.jobQueue.Any(q => q?.job?.loadID == order.jobId);
    }

    internal sealed class ExplicitHaulSelection
    {
        internal readonly Pawn Pawn;
        internal readonly Thing Source;
        internal readonly int MapId, OpeningCount;
        internal readonly bool Queue;
        private readonly IntVec3 sourceCell;
        private readonly bool forbidden;
        private bool dispatched;

        internal ExplicitHaulSelection(Pawn pawn, Thing source, bool queue)
        {
            Pawn = pawn; Source = source; Queue = queue;
            MapId = pawn.Map.uniqueID; OpeningCount = source.stackCount;
            sourceCell = source.Position; forbidden = source.IsForbidden(pawn);
        }

        internal bool Live => !dispatched && ExplicitHaulUi.Local(Pawn, MapId)
            && ExplicitHaulUi.SourceAvailable(Pawn, Source) && Source.Position == sourceCell
            && Source.stackCount == OpeningCount && Source.IsForbidden(Pawn) == forbidden;

        private Building_Storage ShelfAt(LocalTargetInfo target) => target.Thing as Building_Storage
            ?? (target.Cell.IsValid && target.Cell.InBounds(Pawn.Map)
                ? Pawn.Map.haulDestinationManager.SlotGroupAt(target.Cell)?.parent as Building_Storage : null);
        private bool DestinationAllowed(LocalTargetInfo target, int total)
        {
            if (!Live || !target.IsValid) return false;
            var shelf = ShelfAt(target);
            return shelf != null ? ExplicitHaulCommand.CanIssueShelf(Pawn, Source, shelf, total, MapId)
                : ExplicitHaulCommand.CanIssue(Pawn, Source, target.Cell, total, MapId);
        }

        internal void Target(int total, bool queue)
        {
            if (!Live || total < 1 || total > OpeningCount) { ExplicitHaulUi.Reject("HD_ExplicitChanged"); return; }
            var parameters = new TargetingParameters
            {
                canTargetLocations = true, canTargetPawns = false, canTargetBuildings = true,
                canTargetPlants = false, canTargetItems = false, canTargetCorpses = false,
                mapObjectTargetsMustBeAutoAttackable = false
            };
            Find.DesignatorManager.Deselect();
            Find.Targeter.BeginTargeting(parameters,
                target =>
                {
                    if (!DestinationAllowed(target, total))
                    { ExplicitHaulUi.Reject("HD_ExplicitChanged"); return; }
                    dispatched = true;
                    var shelf = ShelfAt(target);
                    if (shelf != null) ExplicitHaulCommand.DispatchShelf(Pawn, Source, shelf, total, MapId, queue);
                    else ExplicitHaulCommand.Dispatch(Pawn, Source, target.Cell, total, MapId, queue);
                }, null,
                target =>
                {
                    if (!Live) { ExplicitHaulUi.Reject("HD_ExplicitChanged"); Find.Targeter.StopTargeting(); return false; }
                    if (DestinationAllowed(target, total)) return true;
                    ExplicitHaulUi.Reject("HD_ExplicitDestinationBlocked"); return false;
                }, Pawn, mouseAttachment: ExplicitHaulUiTextures.Icon, onGuiAction: target => ExplicitHaulUi.DrawPrompt(
                    "HD_ExplicitSelectDestination".Translate(total, Source.LabelNoCount,
                        (queue ? "HD_ExplicitQueuedHint" : "HD_ExplicitReplaceHint").Translate())));
        }
    }
}
