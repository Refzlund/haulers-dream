using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Autonomous transporter/shuttle BULK UNLOAD, the work-giving side of the "Bulk unload all" toggle
    /// (<see cref="Patch_CompTransporter_Gizmos_BulkUnloadAll"/>). Vanilla has NO transporter-unload workgiver
    /// (its Unload/Cancel-load button dumps the whole hold on the floor), so HD ships this net-new
    /// <c>WorkGiverDef</c> under the <c>Hauling</c> work type: while a transporter is flagged, any eligible
    /// hauling colonist takes <see cref="HaulersDreamDefOf.HaulersDream_UnloadTransporterInBulk"/>, ONE
    /// backpack-filling visit, then ships to storage as usual. The multi-trip loop is emergent: after the
    /// storage dropoff the pawn's next think cycle re-finds the still-flagged transporter until the driver's
    /// finalize clears the flag (hold emptied of pullable stacks).
    ///
    /// <para>Concurrency mirrors loading: the transporter is not reserved, so SEVERAL haulers may empty one hold
    /// at once (per-pull Contains/count guards in the driver make concurrent pulls safe), and others pipeline
    /// behind them, hauling storage trips while someone else keeps pulling. Autonomous pulls skip FORBIDDEN
    /// stacks (the player's forbid is respected, unlike the right-click order, which is an explicit override and
    /// takes anything); if every remaining stack is forbidden or a passenger, the scan simply goes quiet.</para>
    ///
    /// <para>Gated on the master switch (<see cref="MasterEnable.Active"/>) like every automatic behaviour, and
    /// on the shared conflict seam (<see cref="BulkUnloadTransporterGate.ConflictActive"/>) so it never fights a
    /// load flowing INTO the hold.</para>
    /// </summary>
    public class WorkGiver_BulkUnloadTransporters : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Transporter);

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableBulkUnloadTransporters)
                return false;
            // The master switch stops AUTOMATIC behaviours only, the right-click order and the toggle stay live.
            if (!MasterEnable.Active)
                return false;
            if (pawn?.inventory == null || pawn.carryTracker?.innerContainer == null)
                return false;
            var comp = t?.TryGetComp<CompTransporter>();
            if (comp == null || !t.Spawned)
                return false;
            // [UC1-parity] A VF VehiclePawn's cargo is VF's to manage, never an autonomous HD target (no-op when
            // VF is absent).
            if (VehicleFrameworkCompat.IsVehicle(t))
                return false;
            var ledger = HaulersDreamGameComponent.Instance;
            if (ledger == null || !ledger.BulkUnloadAllActive(t.thingIDNumber))
                return false;

            // Same hauler-shape checks as the order path: able to carry, comp for tagging, hands EMPTY (each visit
            // ends by overflowing one stack into them), and enough free backpack room to make the visit worthwhile.
            if (pawn.GetComp<CompHauledToInventory>() == null)
                return false;
            if (pawn.carryTracker.innerContainer.Count > 0)
                return false;
            // Unloading a transporter IS hauling work (#229 parity): never a way around a disabled Hauling type.
            if (HaulOrderGate.Blocks(pawn))
                return false;

            // At least one pullable, NON-forbidden stack, the autonomous pass respects forbiddance.
            bool anyPullable = false;
            var hold = comp.innerContainer;
            for (int i = 0; hold != null && i < hold.Count; i++)
            {
                var stack = hold[i];
                if (stack == null || stack.Destroyed || stack is Pawn || stack.IsForbidden(pawn))
                    continue;
                anyPullable = true;
                break;
            }
            if (!anyPullable)
                return false;

            // Never fight a load flowing IN (lord / HD couriers / vanilla haul-to-transporter / caravan), and
            // never while a recorded load session still has an open manifest — the SAME two-part answer the
            // toggle grey-out, float-menu offer and driver FailOn use, so all four surfaces can't drift.
            if (BulkUnloadTransporterGate.ConflictActive(comp)
                || BulkUnloadTransporterGate.LoadSessionHasOpenManifest(comp))
                return false;

            // Enough backpack room for the visit to be worth starting (mirrors the carrier unload's autonomous gate).
            if (!BulkUnloadCarrierPolicy.HasEnoughBackpackRoom(
                    MassUtility.EncumbrancePercent(pawn), s.minFreeSpaceToUnloadCarrierPct))
                return false;

            if (!pawn.CanReach(t, PathEndMode, Danger.Deadly))
                return false;
            return pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadTransporterInBulk, t);
            job.playerForced = forced;
            return job;
        }
    }
}
