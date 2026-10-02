using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    public partial class JobDriver_UnloadHauledInventory
    {
        private OrdinaryUnloadTransit ordinaryTransit;
        private int ordinaryTransitVersion = 1;
        private readonly HashSet<Toil> ordinaryPostPullToils = new HashSet<Toil>();
        internal Thing StorageBoundOrdinaryHands => ordinaryTransit?.Held(this);
        internal int StorageBoundOrdinaryHandCount => ordinaryTransit?.HeldUnits(this) ?? 0;

        internal Thing ReturnOrdinaryHandsAfterLoad(StorageCommitments.LoadRecoveryTicket ticket)
            => ordinaryTransit?.ReturnForLoad(this, ticket);

        private Toil OrdinaryPostPull(Toil toil)
        { ordinaryPostPullToils.Add(toil); return toil; }

        internal void RestoreOrdinaryTransitAfterLoad()
        {
            if (ordinaryTransit != null || ordinaryTransitVersion != 0 || NearbyHaulDelivery.Applies(job)
                || job?.def != HaulersDreamDefOf.HaulersDream_UnloadInventory
                || pawn?.CurJob != job || pawn.jobs.curDriver != this
                || !ordinaryPostPullToils.Contains(CurToil) || countToDrop <= 0
                || job.count != countToDrop) return;
            Thing held = pawn.carryTracker?.CarriedThing;
            if (held == null || !ReferenceEquals(job.targetA.Thing, held) || held.stackCount > countToDrop
                || !OrdinaryUnloadTransit.Owns(pawn.carryTracker.innerContainer, held)) return;
            // The unchanged old pull assigned A only AFTER native transfer returned. Its out
            // parameter is the incoming/split object, including a destroyed merge input. Thus a
            // live A==hands at an actually constructed post-pull toil proves unmerged custody.
            // No speculative reader invokes SetupToils or manufactures a delivery phase.
            ordinaryTransit = OrdinaryUnloadTransit.Migrate(job, held, countToDrop);
            ordinaryTransitVersion = 1;
        }

        private bool PrepareOrdinaryDestination(ThingCount next, HashSet<Thing> carried, Toil begin)
        {
            Job activation = job;
            OrdinaryUnloadTransit receipt = ordinaryTransit;
            try
            {
                Map destinationMap = pawn.Map;
                LocalTargetInfo destination = activation.targetB;
                var group = destination.HasThing ? null : GroupAt(destination.Cell);
                var status = StorageCommitments.UnloadResourceTransfer.Prepare(this, next.Thing, next.Count,
                    group, out _, out int allowed);
                if (!OrdinaryUnloadCurrent(activation, receipt)) return false;
                bool sameDestination = ReferenceEquals(pawn.Map, destinationMap) && activation.targetB == destination;
                if (status == StorageCommitments.ResourceAllowance.Deferred
                    || (status == StorageCommitments.ResourceAllowance.Observed && allowed <= 0) || !sameDestination)
                {
                    if (!sameDestination) ReleaseOrdinaryDestination(destinationMap, destination, activation, receipt);
                    if (!OrdinaryUnloadCurrent(activation, receipt)) return false;
                    skippedThisJob.Add(next.Thing);
                    JumpToToil(begin);
                    return false;
                }
                if (status == StorageCommitments.ResourceAllowance.Unsupported)
                {
                    if (!ReserveOrdinaryFallback(next.Thing, next.Count, group, destinationMap,
                        destination, activation, receipt, out _, out allowed))
                    {
                        if (!OrdinaryUnloadCurrent(activation, receipt)) return false;
                        skippedThisJob.Add(next.Thing);
                        JumpToToil(begin);
                        return false;
                    }
                }
                countToDrop = allowed;
                lastDeliveredDef = next.Thing.def;
                return true;
            }
            catch (Exception error)
            {
                HandleOrdinaryUnloadFailure(activation, receipt, error);
                return false;
            }
        }

        private Thing PullOrdinaryInventory(Thing source)
        {
            Job activation = job;
            OrdinaryUnloadTransit expectedReceipt = ordinaryTransit;
            try
            {
                if (pawn.carryTracker?.innerContainer == null || pawn.carryTracker.innerContainer.Count != 0)
                    return null;
                Map destinationMap = pawn.Map;
                LocalTargetInfo destination = activation.targetB;
                var group = destination.HasThing ? null : GroupAt(destination.Cell);
                var status = StorageCommitments.UnloadResourceTransfer.Prepare(this, source, countToDrop,
                    group, out var responsibility, out int allowed);
                if (!OrdinaryUnloadCurrent(activation, expectedReceipt)) return null;
                if (!ReferenceEquals(pawn.Map, destinationMap) || activation.targetB != destination)
                {
                    ReleaseOrdinaryDestination(destinationMap, destination, activation, expectedReceipt);
                    return null;
                }
                if (responsibility == null || status == StorageCommitments.ResourceAllowance.Deferred) return null;
                if (status == StorageCommitments.ResourceAllowance.Unsupported)
                {
                    if (!ReserveOrdinaryFallback(source, countToDrop, group, destinationMap,
                        destination, activation, expectedReceipt, out responsibility, out allowed)) return null;
                }
                expectedReceipt = new OrdinaryUnloadTransit(job, source, allowed);
                ordinaryTransit = expectedReceipt;
                countToDrop = allowed;
                return expectedReceipt.Withdraw(this, allowed, responsibility);
            }
            catch (Exception error)
            {
                HandleOrdinaryUnloadFailure(activation, expectedReceipt, error);
                return null;
            }
        }

        private bool ReserveOrdinaryFallback(Thing source, int wanted, ISlotGroup group, Map map,
            LocalTargetInfo destination, Job activation, OrdinaryUnloadTransit receipt,
            out StorageCommitments.UnloadResourceTransfer responsibility, out int allowed)
        {
            responsibility = null; allowed = 0;
            int jobId = activation.loadID;
            bool Current() => activation.loadID == jobId && OrdinaryUnloadCurrent(activation, receipt)
                && ReferenceEquals(pawn.Map, map) && activation.targetB == destination;
            var reservations = map.reservationManager;
            bool existed = reservations.ReservedBy(destination, pawn, activation);
            if (!Current()) return false;
            bool keep = false;
            Exception failure = null;
            try
            {
                if (!existed && !reservations.Reserve(pawn, activation, destination)) return false;
                if (!Current()) return false;
                // Reservation callbacks can admit another parcel, change provider support or
                // retarget this activation. Observe again before withdrawing any tagged cargo.
                var status = StorageCommitments.UnloadResourceTransfer.Prepare(this, source, wanted,
                    group, out responsibility, out allowed);
                keep = Current() && status == StorageCommitments.ResourceAllowance.Unsupported
                    && responsibility != null && responsibility.Current && allowed > 0;
                return keep;
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                if (!keep && !existed && activation.loadID == jobId)
                {
                    try
                    {
                        // A changed current job does not own this old lease. Release only the
                        // exact lease added here, never an existing or recycled-job reservation.
                        if (reservations.ReservedBy(destination, pawn, activation)
                            && activation.loadID == jobId)
                            reservations.Release(destination, pawn, activation);
                    }
                    catch (Exception cleanup)
                    {
                        if (failure == null) throw;
                        failure.Data["HaulersDream.OrdinaryFallback.Cleanup"] = cleanup;
                    }
                }
            }
        }

        private void ReleaseOrdinaryDestination(Map map, LocalTargetInfo destination, Job activation,
            OrdinaryUnloadTransit receipt)
        {
            var reservations = map?.reservationManager;
            if (reservations?.ReservedBy(destination, pawn, activation) == true
                && OrdinaryUnloadCurrent(activation, receipt))
                reservations.Release(destination, pawn, activation);
        }
    }
}
