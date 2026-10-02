using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    public partial class CompHauledToInventory
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            var pawn = parent as Pawn;
            if (pawn?.Map == null || !ExplicitHaulUi.Local(pawn, pawn.Map.uniqueID)) yield break;
            int mapId = pawn.Map.uniqueID;
            // The source targeter also admits supported robots without changing native CanTakeOrder.
            var start = new Command_Action
            {
                defaultLabel = "HD_ExplicitStart".Translate(), defaultDesc = "HD_ExplicitStartDesc".Translate(),
                icon = ExplicitHaulUiTextures.Icon, Order = float.MaxValue,
                action = () => ExplicitHaulUi.SelectSource(pawn, mapId, KeyBindingDefOf.QueueOrder.IsDownEvent)
            };
            if (ExplicitHaulCommand.ActorReason(pawn) != null) start.Disable("HD_ExplicitActorUnavailable".Translate());
            yield return start;
            if (!ExplicitOrders.Any()) yield break;
            yield return new Command_Action
            {
                defaultLabel = "HD_ExplicitOrders".Translate(), defaultDesc = "HD_ExplicitOrdersDesc".Translate(),
                icon = ExplicitHaulUiTextures.Icon, Order = float.MaxValue,
                action = () => { if (ExplicitHaulUi.Local(pawn, mapId)) Find.WindowStack.Add(new Dialog_ExplicitHaulOrders(pawn, mapId)); }
            };
        }
    }

    [StaticConstructorOnStartup]
    internal static class ExplicitHaulUiTextures
    {
        internal static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/Buttons/Drop", false) ?? BaseContent.BadTex;
    }
}
