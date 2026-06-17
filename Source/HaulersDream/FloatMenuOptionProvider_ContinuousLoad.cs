using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Load until complete (continuous) — {0}": a one-click order (B4 / BLFT continuous-loading parity, opt-in
    /// <c>enableContinuousLoading</c>, default OFF) that sends ONE courier through every nearby load target of the
    /// clicked target's family, chaining group after group until none still needs loading — handy for filling a big
    /// caravan in a single command. The clicked thing can be a transporter/shuttle (its <see cref="CompTransporter"/>),
    /// a <see cref="MapPortal"/>, or a Vehicle Framework vehicle.
    ///
    /// It is deliberately a THIN entry over the existing bulk-load path: it builds the SAME player-forced bulk-load job
    /// the per-target "Prioritize bulk loading {0}" option does (<see cref="TransportLoad.TryGiveBulkJob"/> with
    /// <c>playerOrder: true</c>), and the chaining itself lives in the drivers' finish action via
    /// <see cref="ContinuousLoad"/> — every player-forced bulk-load SUCCESS hops to the next target while the setting is
    /// on. So this option's only job is to KICK OFF a player-forced load on the clicked target with an explicit,
    /// discoverable label; the courier then continues on its own (even drafted).
    ///
    /// Each family is offered only when ITS bulk-load is enabled (so the option can actually do something — otherwise
    /// <see cref="TransportLoad.TryGiveBulkJob"/> returns null because the family feature flag is off). With
    /// <c>enableContinuousLoading</c> off this provider yields nothing, so HD behaves identically to before.
    /// </summary>
    public class FloatMenuOptionProvider_ContinuousLoad : FloatMenuOptionProvider
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
            if (s == null || !s.enableContinuousLoading)
                yield break;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                yield break;
            // Player order: only the physical manipulation capability is required (like vanilla load orders) — the
            // swept loot is deposited into the target, so nothing strands. Works even drafted (the chain too).
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                yield break;
            // Don't offer this while the pawn is under a boarding lord — let vanilla's gather-and-board flow run.
            var dutyDef = pawn.mindState?.duty?.def;
            if (dutyDef == DutyDefOf.LoadAndEnterTransporters || dutyDef == DutyDefOf.LoadAndEnterPortal)
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                var clicked = things[i];
                if (clicked == null)
                    continue;

                IManagedLoadable adapter = TryResolveLoadable(clicked, s);
                if (adapter == null)
                    continue;
                if (!pawn.CanReach(clicked, PathEndMode.Touch, Danger.Deadly) || !pawn.CanReserve(clicked))
                    continue;

                var pawnLocal = pawn;
                var adapterLocal = adapter;
                var clickedLocal = clicked;
                var option = new FloatMenuOption(
                    "HaulersDream.ContinuousLoad.Option".Translate(clicked.LabelShort), () =>
                    {
                        // No try/catch: a build failure is a real bug to surface; the null path shows the toast.
                        var job = TransportLoad.TryGiveBulkJob(pawnLocal, adapterLocal, playerOrder: true);
                        if (job == null)
                        {
                            Messages.Message("HaulersDream.ContinuousLoad.CouldNotStart".Translate(), clickedLocal,
                                MessageTypeDefOf.RejectInput, historical: false);
                            return;
                        }
                        // playerForced is the chain trigger (ContinuousLoad.ShouldChain): on this job's SUCCESS the
                        // driver finish action hops to the next nearby same-family target, and so on.
                        job.playerForced = true;
                        pawnLocal.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    })
                {
                    iconThing = clicked,
                };
                yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, clicked);
                yield break; // one continuous-load option per click
            }
        }

        /// <summary>Resolve the clicked thing to a bulk-load adapter — but ONLY when that family's bulk-load feature is
        /// on (so the resulting <see cref="TransportLoad.TryGiveBulkJob"/> isn't a guaranteed null). Null otherwise.</summary>
        private static IManagedLoadable TryResolveLoadable(Thing clicked, HaulersDreamSettings s)
        {
            // Transporter / shuttle group.
            var comp = clicked.TryGetComp<CompTransporter>();
            if (comp != null)
            {
                if (!s.enableBulkLoadTransporters || !comp.AnyInGroupHasAnythingLeftToLoad)
                    return null;
                return LoadTransportersAdapter.TryCreate(comp);
            }
            // Map portal (pit gate / cave or vault exit / "enter map" portal).
            if (clicked is MapPortal portal)
            {
                if (!s.enableBulkLoadPortal || !portal.LoadInProgress)
                    return null;
                return MapPortalBulkTarget.TryCreate(portal);
            }
            // Vehicle Framework vehicle (held as a plain Thing; IsVehicle is false when VF is absent).
            if (s.enableVehicleFramework && s.enableBulkLoadVehicles && VehicleFrameworkCompat.IsActive
                && VehicleFrameworkCompat.IsVehicle(clicked))
            {
                var cargo = VehicleFrameworkCompat.CargoToLoad(clicked);
                if (cargo == null || cargo.Count == 0)
                    return null;
                return VehicleLoadTarget.TryCreate(clicked);
            }
            return null;
        }
    }
}
