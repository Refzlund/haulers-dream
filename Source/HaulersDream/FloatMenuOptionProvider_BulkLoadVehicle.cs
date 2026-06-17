using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Prioritize bulk loading {0}": a one-click order that sweeps nearby ground stacks the VEHICLE's cargo manifest
    /// still needs into the pawn's inventory and loads them all in one trip (see
    /// <see cref="JobDriver_LoadVehicleInBulk"/>), replacing VF's one-stack-in-hands pack job. Auto-discovered
    /// FloatMenuOptionProvider — no Harmony. The clicked thing is a Vehicle Framework <c>VehiclePawn</c> (held as a
    /// plain <see cref="Thing"/> through <see cref="VehicleFrameworkCompat"/>, NO <c>Vehicles.*</c> type). Mirrors
    /// <see cref="FloatMenuOptionProvider_BulkLoadTransporter"/>.
    ///
    /// This is allowed alongside VF's own selected-vehicle float menu: VF's <c>FloatMenuOptionProvider.Applies</c>
    /// block only fires when the vehicle is the SELECTED actor — here the selected actor is a colonist clicking a
    /// vehicle, so the two never collide. Player orders skip the auto eligibility gate (deposit goes into the
    /// vehicle's cargo → nothing strands), require only the physical manipulation capability (like vanilla load
    /// orders), and do NOT fire while the pawn is under the boarding lord (let that be).
    ///
    /// Gated on the master <c>enableVehicleFramework</c> + the sub <c>enableBulkLoadVehicles</c> + VF actually active
    /// (<see cref="VehicleFrameworkCompat.IsActive"/>) — so with VF absent / the feature off this provider yields
    /// nothing and HD behaves identically to a no-VF install.
    /// </summary>
    public class FloatMenuOptionProvider_BulkLoadVehicle : FloatMenuOptionProvider
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
            if (s == null || !s.enableVehicleFramework || !s.enableBulkLoadVehicles)
                yield break;
            if (!VehicleFrameworkCompat.IsActive)
                yield break;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                yield break;
            // Player order: only the physical capability is required (like vanilla load orders), NOT the Hauling
            // work-tag / auto eligibility — the swept loot is deposited into the vehicle, so nothing strands.
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                yield break;
            // Don't offer this while the pawn is under the boarding lord (LoadAndEnterTransporters) — let vanilla's
            // own gather-and-board flow run.
            if (pawn.mindState?.duty?.def == DutyDefOf.LoadAndEnterTransporters)
                yield break;

            for (int i = 0; i < things.Count; i++)
            {
                var clicked = things[i];
                // VF vehicle with a non-empty pending-load manifest (IsVehicle returns false when VF is absent).
                if (clicked == null || !VehicleFrameworkCompat.IsVehicle(clicked))
                    continue;
                var cargo = VehicleFrameworkCompat.CargoToLoad(clicked);
                if (cargo == null || cargo.Count == 0)
                    continue;
                if (!pawn.CanReach(clicked, PathEndMode.Touch, Danger.Deadly) || !pawn.CanReserve(clicked))
                    continue;
                // Don't double-order: skip if this pawn already runs HD's vehicle-load for this exact vehicle.
                if (pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_LoadVehicleInBulk
                    && pawn.CurJob.GetTarget(TargetIndex.A).Thing?.thingIDNumber == clicked.thingIDNumber)
                    continue;
                var adapter = VehicleLoadTarget.TryCreate(clicked);
                if (adapter == null)
                    continue;

                var pawnLocal = pawn;
                var adapterLocal = adapter;
                var clickedLocal = clicked;
                var option = new FloatMenuOption(
                    "HaulersDream.LoadVehicle.Option".Translate(clicked.LabelShort), () =>
                    {
                        // No try/catch: a build failure is a real bug to surface; the null path shows the toast.
                        var job = TransportLoad.TryGiveBulkJob(pawnLocal, adapterLocal, playerOrder: true);
                        if (job == null)
                        {
                            Messages.Message("HaulersDream.LoadVehicle.CouldNotStart".Translate(), clickedLocal,
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
                // one bulk option per click. (VF loads cargo via a Hauling work-scan, not a float menu, so there is no
                // VF single-stack right-click option to stand down here; the autonomous scan is upgraded to bulk by
                // Patch_WorkGiver_PackVehicle_Redirect.)
                yield break;
            }
        }
    }
}
