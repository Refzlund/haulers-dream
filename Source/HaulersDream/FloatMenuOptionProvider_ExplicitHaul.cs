using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    public sealed class FloatMenuOptionProvider_ExplicitHaul : FloatMenuOptionProvider
    {
        public override bool Drafted => false;
        public override bool Undrafted => true;
        public override bool Multiselect => false;
        public override bool MechanoidCanDo => true;
        public override bool CanSelfTarget => false;

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            var pawn = context?.FirstSelectedPawn;
            if (pawn?.Map == null || context.ClickedThings == null || !ExplicitHaulUi.Local(pawn, pawn.Map.uniqueID)) yield break;
            int mapId = pawn.Map.uniqueID;
            foreach (var source in context.ClickedThings)
            {
                if (!NearbyHaulCommand.IsGroundTarget(pawn, source)) continue;
                var selected = source;
                string label = "HD_ExplicitHaulTo".Translate(source.LabelNoCount);
                if (!ExplicitHaulUi.SourceAvailable(pawn, source))
                    yield return new FloatMenuOption(label + ": " + "HD_ExplicitActorUnavailable".Translate(), null);
                else
                    yield return new FloatMenuOption(label, () => ExplicitHaulUi.Open(pawn, selected, mapId,
                        KeyBindingDefOf.QueueOrder.IsDownEvent)) { iconThing = selected };
            }
        }
    }
}
