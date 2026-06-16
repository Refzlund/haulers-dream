using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Prioritize bulk refuelling {0}": a one-click order that sweeps enough nearby fuel stacks into the pawn's
    /// inventory and fills the refuelable in one trip (see <see cref="JobDriver_BulkRefuel"/>), replacing vanilla's
    /// one-stack-in-hands "Refuel". Auto-discovered FloatMenuOptionProvider — no Harmony, no XML. The clicked thing is
    /// a refuelable (its <see cref="CompRefuelable"/>). Mirrors
    /// <see cref="FloatMenuOptionProvider_BulkLoadTransporter"/>.
    ///
    /// Player orders skip the auto eligibility gate (the fuel is deposited into the refuelable → nothing strands),
    /// require only the physical manipulation capability (like vanilla refuel orders).
    /// </summary>
    public class FloatMenuOptionProvider_BulkRefuel : FloatMenuOptionProvider
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
            if (!BulkRefuel.FeatureEnabled)
                yield break;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                yield break;
            // Player order: only the physical capability is required (like vanilla refuel orders), NOT the Hauling
            // work-tag / auto eligibility — the swept fuel is deposited into the refuelable, so nothing strands.
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                var clicked = things[i];
                var comp = clicked?.TryGetComp<CompRefuelable>();
                if (comp == null || comp.IsFull)
                    continue;
                if (comp.Props != null && comp.Props.atomicFueling)
                    continue; // atomic refuelables are vanilla RefuelAtomic's job — don't offer the bulk order
                if (!pawn.CanReach(clicked, PathEndMode.Touch, Danger.Deadly) || !pawn.CanReserve(clicked))
                    continue;
                // Don't double-order: skip if this pawn already runs HD's bulk refuel on this same thing.
                if (pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_BulkRefuel
                    && pawn.CurJob.GetTarget(TargetIndex.A).Thing == clicked)
                    continue;

                var pawnLocal = pawn;
                var clickedLocal = clicked;
                var option = new FloatMenuOption(
                    "HaulersDream.BulkRefuel.Option".Translate(clicked.LabelShort), () =>
                    {
                        // No try/catch: a build failure is a real bug to surface; the null path shows the toast.
                        var job = BulkRefuel.TryGiveBulkRefuelJob(pawnLocal, clickedLocal, playerOrder: true);
                        if (job == null)
                        {
                            Messages.Message("HaulersDream.BulkRefuel.CouldNotStart".Translate(), clickedLocal,
                                MessageTypeDefOf.RejectInput, historical: false);
                            return;
                        }
                        job.playerForced = true;
                        pawnLocal.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    })
                {
                    iconThing = clicked,
                };
                yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, clicked);
                yield break; // one bulk option per click; vanilla's single-stack refuel still appears alongside
            }
        }
    }
}
