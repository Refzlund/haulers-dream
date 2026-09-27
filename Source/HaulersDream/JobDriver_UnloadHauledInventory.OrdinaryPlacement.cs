using System;
using System.Runtime.ExceptionServices;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    public partial class JobDriver_UnloadHauledInventory
    {
        // Keep one placement toil at the old index for current and legacy in-flight saves.
        // Native TryDrop interception remains active; native placement-factory wrappers do not.
        // In particular, its unreserved retarget/haul-aside/destroy fallback is not a receipt handoff.
        private Toil PlaceOrdinaryCargo() => new Toil { initAction = PlaceOrdinaryCargoNow };

        private void PlaceOrdinaryCargoNow()
        {
            Job activation = job;
            OrdinaryUnloadTransit receipt = ordinaryTransit;
            try { PlaceOrdinaryCargo(activation, receipt); }
            catch (Exception error) { HandleOrdinaryUnloadFailure(activation, receipt, error); }
        }

        private void PlaceOrdinaryCargo(Job activation, OrdinaryUnloadTransit receipt)
        {
            if (!OrdinaryUnloadCurrent(activation, receipt)) return;
            Thing held = StorageBoundOrdinaryHands;
            if (held == null) { EndJobWith(JobCondition.Incompletable); return; }
            Map placementMap = pawn.Map;
            LocalTargetInfo destination = activation.targetB;
            int before = held.stackCount;
            Exception failure = null;
            ISlotGroup retiredDestination = null;
            try
            {
                var cell = destination.Cell;
                var group = placementMap.haulDestinationManager.SlotGroupAt(cell);
                var budgetGroup = BulkHaul.BudgetGroupOf(group);
                bool accepts = group != null && group.Settings.AllowedToAccept(held);
                if (!OrdinaryUnloadCurrent(activation, receipt) || !ReferenceEquals(StorageBoundOrdinaryHands, held)) return;
                if (group != null && !accepts && ReferenceEquals(pawn.Map, placementMap)
                    && activation.targetB == destination
                    && ReferenceEquals(placementMap.haulDestinationManager.SlotGroupAt(cell), group))
                    retiredDestination = budgetGroup;
                if (accepts && ReferenceEquals(pawn.Map, placementMap) && activation.targetB == destination)
                    placementMap.designationManager.TryRemoveDesignationOn(held, DesignationDefOf.Haul);
                if (!OrdinaryUnloadCurrent(activation, receipt) || !ReferenceEquals(StorageBoundOrdinaryHands, held)) return;

                // Native direct drop can place part and return false. Queries from native/provider
                // callbacks must not admit another parcel between that mutation and its observation.
                // A callback can retarget this SAME job: settle/reselect instead of dropping at
                // the stale cell, and leave any new target's provider reservation alone.
                // Direct placement only checks physical room. A live storage filter can reject
                // this cargo while the pawn walks here; return it without retaining that promise.
                // A deliberate bare-home destination has no group and keeps its existing behavior.
                if ((group == null || accepts) && ReferenceEquals(pawn.Map, placementMap)
                    && activation.targetB == destination)
                    using (StorageCommitments.BeginResourceTransfer())
                        pawn.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out _);
            }
            catch (Exception error) { failure = error; }

            if (!OrdinaryUnloadCurrent(activation, receipt)) { RethrowOrdinaryPlacement(failure); return; }
            Thing remainder = StorageBoundOrdinaryHands;
            bool progressed = ReferenceEquals(remainder, held) && remainder.stackCount < before;
            Thing returned = null;
            if (remainder != null)
            {
                // Return must capture the POST-placement responsibility outside the placement
                // barrier; it enters its own barrier before changing custody/tag/claim ownership.
                try { returned = receipt.Return(this, retiredDestination); }
                catch (Exception error) { PreserveOrdinaryPlacement(ref failure, error, "return"); }
                if (!OrdinaryUnloadCurrent(activation, receipt)) { RethrowOrdinaryPlacement(failure); return; }
                // A tag/provider callback can throw after the exact item reached inventory.
                // Release only this activation's old reservation even on that error path.
                if (returned == null)
                {
                    Thing retained = receipt.Retained(this);
                    if (OrdinaryUnloadTransit.Owns(pawn.inventory?.innerContainer, retained)) returned = retained;
                }
            }

            if (remainder == null || returned != null)
            {
                try
                {
                    ReleaseOrdinaryDestination(placementMap, destination, activation, receipt);
                }
                catch (Exception error) { PreserveOrdinaryPlacement(ref failure, error, "release"); }
            }
            RethrowOrdinaryPlacement(failure);
            if (!OrdinaryUnloadCurrent(activation, receipt)) return;
            if (remainder == null)
            {
                // We released the captured attempted lease. Do not fall through to the generic
                // current-B release toil: a successful-drop callback may have reserved a new B.
                JumpToToil(loopToil);
                return;
            }
            if (returned == null)
            {
                // Finish tags the exact held receipt, then native cleanup may try a Near drop.
                // Do not claim inventory recovery or discard cargo through placement's destroy bail.
                HaulChurnGuard.StampBackoff(remainder);
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            activation.SetTarget(TargetIndex.A, returned);
            if (!OrdinaryUnloadCurrent(activation, receipt)) return;
            if (!progressed)
            {
                skippedThisJob.Add(returned);
                HaulChurnGuard.StampBackoff(returned);
                pathFailuresThisJob++;
                int remaining = RemainingCandidateCount();
                if (!OrdinaryUnloadCurrent(activation, receipt)) return;
                if (UnreachableDestinationPolicy.Choose(pathFailuresThisJob, remaining)
                    != UnreachableDestinationAction.SetAsideAndContinue)
                { EndJobWith(JobCondition.Succeeded); return; }
            }
            // A productive remainder stays eligible. The existing selector rechecks keep/surplus,
            // storage and fresh admission before a new withdrawal; no in-hands target rewrite.
            JumpToToil(loopToil);
        }

        private static void PreserveOrdinaryPlacement(ref Exception primary, Exception secondary, string stage)
        {
            if (primary == null) primary = secondary;
            else primary.Data["HaulersDream.OrdinaryPlacement." + stage] = DescribeOrdinaryUnloadFailure(secondary);
        }

        private static void RethrowOrdinaryPlacement(Exception failure)
        { if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw(); }
    }
}
