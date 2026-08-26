using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Prioritize bulk loading {0}": a one-click order that sweeps nearby ground stacks the transporter group still
    /// needs into the pawn's inventory and loads them all in one trip (see
    /// <see cref="JobDriver_LoadTransportersInBulk"/>), replacing vanilla's one-stack-in-hands "Load X into
    /// transporter". Auto-discovered FloatMenuOptionProvider — no Harmony. The clicked thing is a transporter
    /// (its <see cref="CompTransporter"/>). Mirrors <see cref="FloatMenuOptionProvider_BulkLoadPackAnimal"/> /
    /// <see cref="FloatMenuOptionProvider_BulkUnloadCarrier"/>.
    ///
    /// Player orders skip the auto eligibility gate (deposit goes into a container → nothing strands), require only
    /// the physical manipulation capability (like vanilla load orders), and do NOT fire while the pawn is already
    /// under the boarding lord (let that be).
    /// </summary>
    public class FloatMenuOptionProvider_BulkLoadTransporter : FloatMenuOptionProvider
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
            if (s == null || !s.enableBulkLoadTransporters)
                yield break;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                yield break;
            // Player order: skips the AUTO eligibility gate (the swept loot is deposited into the transporter, so
            // nothing strands) but NOT the hauling-capability bar (#229). Loading a transporter IS hauling work in
            // vanilla — WorkGiverDef LoadTransporters declares <workType>Hauling</workType>
            // (Core/Defs/WorkGiverDefs/WorkGivers.xml:1251-1254) — so vanilla greys its own "Load X into
            // transporter" out for a pawn whose Hauling work type is disabled, and HD's bulk replacement must not
            // be a way around that. HaulOrderGate reads the WORK TYPE, not the WorkTags.Hauling bit an "incapable
            // of dumb labor" backstory leaves clear.
            if (HaulOrderGate.Blocks(pawn))
                yield break;
            // Don't offer this while the pawn is under the boarding lord (LoadAndEnterTransporters) — let vanilla's
            // own gather-and-board flow run.
            if (pawn.mindState?.duty?.def == DutyDefOf.LoadAndEnterTransporters)
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                var clicked = things[i];
                var comp = clicked?.TryGetComp<CompTransporter>();
                if (comp == null || !comp.AnyInGroupHasAnythingLeftToLoad)
                    continue;
                // MUTUAL EXCLUSION (unload side): don't even offer loading into a hold flagged "Bulk unload all", 
                // the two flows must never run at once (TryGiveBulkJob refuses this too; this just keeps the menu
                // honest instead of toasting).
                if (BulkUnloadTransporterGate.UnloadFlagActive(comp))
                    continue;
                if (!pawn.CanReach(clicked, PathEndMode.Touch, Danger.Deadly) || !pawn.CanReserve(clicked))
                    continue;
                // Don't double-order: skip if this pawn already runs HD's load for this group.
                if (pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk
                    && pawn.CurJob.GetTarget(TargetIndex.A).Thing?.TryGetComp<CompTransporter>()?.groupID == comp.groupID)
                    continue;
                var adapter = LoadTransportersAdapter.TryCreate(comp);
                if (adapter == null)
                    continue;

                var pawnLocal = pawn;
                var adapterLocal = adapter;
                var clickedLocal = clicked;
                var option = new FloatMenuOption(
                    "HaulersDream.LoadTransporter.Option".Translate(clicked.LabelShort), () =>
                    {
                        // No try/catch: a build failure is a real bug to surface; the null path shows the toast.
                        var job = TransportLoad.TryGiveBulkJob(pawnLocal, adapterLocal, playerOrder: true);
                        if (job == null)
                        {
                            Messages.Message("HaulersDream.LoadTransporter.CouldNotStart".Translate(), clickedLocal,
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
                yield break; // one bulk option per click; vanilla's single-stack options are suppressed (§H)
            }
        }
    }
}
