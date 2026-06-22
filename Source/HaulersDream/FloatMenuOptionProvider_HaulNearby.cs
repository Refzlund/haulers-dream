using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Haul everything nearby": a one-click order that starts the bulk sweep directly (pick up the clicked
    /// haulable AND everything haulable around it into inventory, then one storage trip), so the player needn't
    /// right-click → "Prioritize hauling" twice to trigger bulk hauling. The explicit counterpart to the
    /// automatic SecondTasked behavior — it always sweeps regardless of the trigger setting. Auto-discovered
    /// FloatMenuOptionProvider (no Harmony, no registration), shown alongside vanilla "Prioritize hauling".
    /// Falls back to a normal forced haul if there's nothing worth sweeping (single stack that fits in hands).
    /// </summary>
    public class FloatMenuOptionProvider_HaulNearby : FloatMenuOptionProvider
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
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.bulkHaul || !s.haulNearbyOption)
                yield break; // bulk hauling off, or this button disabled in mod options
            // Match BuildBulkJob's non-home gate: with the mod inert on non-home maps the sweep would have no
            // storage to unload to there, so don't offer it.
            if (!MapGate.HdActiveOnMap(pawn.Map))
                yield break;
            if (pawn.GetComp<CompHauledToInventory>() == null)
                yield break; // the sweep loads into inventory, tracked via the comp
            // The vanilla can-haul bar for a PLAYER ORDER (matches FloatMenuOptionProvider_HaulToSite and vanilla
            // "Prioritize hauling"): a hauling-capable, manipulation-capable pawn. (The bulk builder still applies
            // YieldRouter.IsEligible internally and degrades to a plain haul for an ineligible pawn, so the swept
            // inventory is always serviceable by the unload side — no black hole.)
            if (pawn.WorkTagIsDisabled(WorkTags.Hauling) || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                var clicked = things[i];
                // Skip non-spawned / contained things (e.g. eggs held inside an egg box, which reach ClickedThings
                // via vanilla SelectableContainedThings): they have no map/position, so the spawned-only haul check
                // below would NRE on them (issue #2). The sweep is over spawned ground stacks only.
                if (clicked == null || !clicked.Spawned)
                    continue;
                if (clicked.def == null || clicked.def.category != ThingCategory.Item || !clicked.def.EverHaulable)
                    continue;
                // The same bar vanilla "Prioritize hauling" uses (EverHaulable / fogged / forbidden+allowed-area /
                // reservable / reachable). forced:true mirrors a player order.
                if (!HaulAIUtility.PawnCanAutomaticallyHaul(pawn, clicked, forced: true))
                    continue;

                var clickedLocal = clicked;
                var option = new FloatMenuOption("HaulersDream.HaulNearby.Option".Translate(), () =>
                {
                    // No try/catch: a failure to build the order is a real bug to surface, not mask as the benign
                    // toast; the genuine null path below still shows the friendly message.
                    Job job = BulkHaul.BuildBulkJobForced(pawn, clickedLocal)
                              ?? HaulAIUtility.HaulToStorageJob(pawn, clickedLocal, forced: true);
                    if (job == null)
                    {
                        Messages.Message("HaulersDream.HaulNearby.CouldNotStart".Translate(), clickedLocal,
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
                yield break; // one bulk option per click; vanilla's single "Prioritize hauling" still appears alongside
            }
        }
    }
}
