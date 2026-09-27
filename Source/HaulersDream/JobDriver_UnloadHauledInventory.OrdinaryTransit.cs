using System;
using System.Collections.Generic;
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
                    bool reserved = destinationMap.reservationManager.Reserve(pawn, activation, destination);
                    if (!OrdinaryUnloadCurrent(activation, receipt)) return false;
                    sameDestination = ReferenceEquals(pawn.Map, destinationMap) && activation.targetB == destination;
                    if (!reserved || !sameDestination)
                    {
                        if (!sameDestination) ReleaseOrdinaryDestination(destinationMap, destination, activation, receipt);
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
                    var reservations = destinationMap.reservationManager;
                    bool reserved = reservations.ReservedBy(destination, pawn, activation);
                    if (!OrdinaryUnloadCurrent(activation, expectedReceipt)) return null;
                    if (!ReferenceEquals(pawn.Map, destinationMap) || activation.targetB != destination)
                    {
                        ReleaseOrdinaryDestination(destinationMap, destination, activation, expectedReceipt);
                        return null;
                    }
                    if (!reserved) reserved = reservations.Reserve(pawn, activation, destination);
                    if (!OrdinaryUnloadCurrent(activation, expectedReceipt)) return null;
                    if (!ReferenceEquals(pawn.Map, destinationMap) || activation.targetB != destination)
                    {
                        ReleaseOrdinaryDestination(destinationMap, destination, activation, expectedReceipt);
                        return null;
                    }
                    if (!reserved) return null;
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
