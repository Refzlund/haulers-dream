using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Prioritize hauling materials to X": an ordered HAUL-ONLY delivery to a blueprint/frame, offered whenever
    /// the site still needs materials that exist on the map — even while the build itself can't proceed yet
    /// (other material types missing) and even for pawns who can haul but not construct. This is the material-
    /// allocation tool: get the steel TO the high-priority comms console now; building happens when it can.
    /// Distinct from vanilla's "Prioritize constructing" (which our conversion tethers into haul AND build).
    /// Auto-discovered FloatMenuOptionProvider — no Harmony.
    /// </summary>
    public class FloatMenuOptionProvider_HaulToSite : FloatMenuOptionProvider
    {
        public override bool Drafted => false;
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
            if (HaulersDreamMod.Settings == null || !HaulersDreamMod.Settings.haulToSiteOption)
                yield break; // the haul-to-site order is disabled in mod options
            if (pawn.GetComp<CompHauledToInventory>() == null)
                yield break; // the delivery driver tracks leftovers via the comp
            if (pawn.WorkTagIsDisabled(WorkTags.Hauling) || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                var site = things[i];
                if (site == null || !(site is Blueprint || site is Frame) || !(site is IConstructible))
                    continue;
                if (site.Faction != pawn.Faction || site is Blueprint_Install)
                    continue;

                // No try/catch: a throw here is a real bug to surface, not silently hide the option.
                bool offer = InventoryConstructDelivery.AnyNeededMaterialAvailable(pawn, site)
                             && pawn.CanReach(site, PathEndMode.Touch, Danger.Deadly);
                if (!offer)
                    continue;

                var siteLocal = site;
                var option = new FloatMenuOption("HaulersDream.HaulToSite.Option".Translate(site.LabelShort), () =>
                {
                    // No try/catch: a build-order failure is a real bug to surface, not mask as the benign
                    // "couldn't start" toast; the genuine null path below still shows that friendly message.
                    Job job = InventoryConstructDelivery.TryBuildHaulOnlyOrder(pawn, siteLocal);
                    if (job == null)
                    {
                        Messages.Message("HaulersDream.HaulToSite.CouldNotStart".Translate(), siteLocal,
                            MessageTypeDefOf.RejectInput, historical: false);
                        return;
                    }
                    job.playerForced = true;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                })
                {
                    iconThing = site,
                };
                yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, site);
                yield break; // one option per click is enough (the first constructible under the cursor)
            }
        }
    }
}
