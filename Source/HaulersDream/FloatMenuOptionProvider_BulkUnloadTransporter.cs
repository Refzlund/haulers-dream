using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    // Adapted from nullpat's GH267. Drafted=true exposes a disabled explanation, never a drafted job.
    public sealed class FloatMenuOptionProvider_BulkUnloadTransporter : FloatMenuOptionProvider
    {
        public override bool Drafted => true;
        public override bool Undrafted => true;
        public override bool Multiselect => false;
        public override bool MechanoidCanDo => true;
        public override bool CanSelfTarget => false;

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            var pawn = context?.FirstSelectedPawn;
            if (pawn?.Map == null || context.ClickedThings == null || !MultiplayerCompat.TransporterLocalUi) yield break;
            foreach (var target in context.ClickedThings)
            {
                var comp = target?.TryGetComp<CompTransporter>();
                if (!BulkUnloadTransporterGate.IsSupported(comp) || !BulkUnloadTransporterGate.UnloadFlagActive(comp)) continue;
                int mapId = pawn.Map.uniqueID;
                string label = "HaulersDream.UnloadTransporter.Option".Translate(target.LabelShort);
                string reason = TransporterCommand.UnloadBlock(pawn, target);
                if (reason != null) yield return new FloatMenuOption(label + ": " + reason, null);
                else
                {
                    var option = new FloatMenuOption(label,
                        () => TransporterCommand.Dispatch(pawn, target, mapId, true, KeyBindingDefOf.QueueOrder.IsDownEvent))
                    { iconThing = target };
                    yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, target);
                }
                yield break;
            }
        }
    }
}
