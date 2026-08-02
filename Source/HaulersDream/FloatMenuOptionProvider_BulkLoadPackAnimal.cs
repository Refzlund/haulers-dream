using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Load nearby items onto pack animal (bulk)": a one-click order that sweeps the nearby haulable stacks into
    /// the pawn's inventory and then loads them all onto a pack animal in one trip — the bulk counterpart to
    /// vanilla's one-stack-in-hands "Load onto pack animal" (which still appears alongside this for single stacks).
    /// Auto-discovered FloatMenuOptionProvider — no Harmony. Appears under the SAME gate as the vanilla order
    /// (non-home map, not while a caravan is forming), so it never fights vanilla caravan-formation gathering.
    /// </summary>
    public class FloatMenuOptionProvider_BulkLoadPackAnimal : FloatMenuOptionProvider
    {
        public override bool Drafted => true;
        public override bool Undrafted => true;
        public override bool Multiselect => false;
        public override bool MechanoidCanDo => false;
        public override bool CanSelfTarget => false;

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            var pawn = context?.FirstSelectedPawn;
            var things = context?.ClickedThings;
            if (pawn == null || things == null || pawn.Map == null)
                yield break;
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.loadPackAnimalBulk || !s.enableOnNonHomeMaps)
                yield break; // feature disabled, or the mod is set inert on non-home maps
            // Mirror vanilla FloatMenuOptionProvider_LoadOntoPackAnimal.AppliesInt: only on a non-home map, and
            // not while a caravan is forming (forming uses the gather-to-carrier lord — leave that to vanilla).
            if (pawn.IsFormingCaravan() || pawn.Map.IsPlayerHome)
                yield break;
            if (pawn.GetComp<CompHauledToInventory>() == null)
                yield break;
            // THE ONE EXCEPTION to #229's rule that an HD haul order applies vanilla's hauling-capability bar.
            // Vanilla's twin here is genuinely UNGATED: RimWorld.FloatMenuOptionProvider_LoadOntoPackAnimal
            // .GetOptionsFor applies no capability gate at all (not even manipulation), and its AppliesInt only
            // restricts it to !IsFormingCaravan && !map.IsPlayerHome — it is not routed through a Hauling
            // WorkGiverDef the way loading a transporter/portal/vehicle or refuelling is. So a specialist
            // incapable of dumb-labor hauling can still be ordered to load a pack animal, exactly as vanilla
            // allows, and HD keeps the manipulation-only bar to match.
            //
            // HONEST CAVEAT (do not restate the old "nothing can strand" claim): the swept stacks ARE tagged in the
            // pawn's OWN pack for the whole sweep -> walk -> deposit trip (JobDriver_LoadPackAnimal registers each
            // split on CompHauledToInventory), so an interruption mid-trip does leave tagged cargo on an incapable
            // pawn that HD's automatic unload will refuse. That is caught by the anti-softlock auto-drop, whose
            // incapability gate was corrected in #229 to read the work TYPE (see
            // HaulersDreamGameComponent.Softlock); the per-pawn "Unload inventory" button is the manual recovery.
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                yield break;
            var carrier = GiveToPackAnimalUtility.UsablePackAnimalWithTheMostFreeSpace(pawn);
            if (carrier == null)
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                var clicked = things[i];
                if (clicked == null || clicked.def == null
                    || clicked.def.category != ThingCategory.Item || !clicked.def.EverHaulable)
                    continue;
                if (!pawn.CanReach(clicked, PathEndMode.ClosestTouch, Danger.Deadly))
                    continue;

                var clickedLocal = clicked;
                var carrierLocal = carrier;
                var option = new FloatMenuOption("HaulersDream.LoadPackAnimal.BulkOption".Translate(), () =>
                {
                    // No try/catch: a failure to build the order is a real bug to surface, not mask as the benign
                    // toast; the genuine null path below still shows the friendly message.
                    Job job = PackAnimalLoad.TryBuildBulkLoadJob(pawn, clickedLocal, carrierLocal);
                    if (job == null)
                    {
                        Messages.Message("HaulersDream.LoadPackAnimal.CouldNotStart".Translate(), clickedLocal,
                            MessageTypeDefOf.RejectInput, historical: false);
                        return;
                    }
                    job.playerForced = true;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                })
                {
                    iconThing = clicked,
                };
                yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, clicked);
                yield break; // one bulk option per click; vanilla's single-stack options still appear alongside
            }
        }
    }
}
