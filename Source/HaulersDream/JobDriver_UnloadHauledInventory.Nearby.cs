using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    public partial class JobDriver_UnloadHauledInventory
    {
        private NearbyDeliveryTransit nearbyTransit = new NearbyDeliveryTransit();
        private HashSet<Thing> nearbyCandidates;
        private const int NearbyNoProgressLimit = 3;

        private IEnumerable<Toil> MakeNearbyDeliveryToils()
        {
            nearbyCandidates = NearbyHaulDelivery.Candidates(pawn, job);
            // Install recovery before any first-toil rejection. A lost/invalid receipt never adopts hands.
            AddFinishAction(_ => nearbyTransit?.Return(pawn, job, nearbyCandidates));
            AddEndCondition(() => nearbyTransit != null && nearbyTransit.Valid
                && NearbyHaulCommand.CanContinue(pawn, job) ? JobCondition.Ongoing : JobCondition.Incompletable);
            var begin = Toils_General.Wait(3);
            loopToil = begin;
            yield return begin;
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    // Resolve a saved/reentered exact receipt before FindTarget can overwrite targetA.
                    if (nearbyTransit == null || !nearbyTransit.Valid)
                    { EndJobWith(JobCondition.Incompletable); return; }
                    if (!nearbyTransit.Empty)
                    {
                        nearbyTransit.Return(pawn, job, nearbyCandidates);
                        if (!nearbyTransit.CanReset(pawn))
                        { EndJobWith(JobCondition.Incompletable); return; }
                        nearbyTransit.Reset(pawn);
                    }
                    if (pawn.carryTracker.CarriedThing != null || nearbyTransit.noProgress >= NearbyNoProgressLimit)
                        EndJobWith(JobCondition.Incompletable); // no unrelated hands or saved exhausted retry budget
                }
            };
            yield return FindTargetOrDrop(nearbyCandidates, begin);
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    Thing source = job.targetA.Thing;
                    if (!NearbyHaulCommand.CanContinue(pawn, job) || source == null
                        || !NearbyDeliveryTransit.Owns(pawn.inventory.innerContainer, source)
                        || pawn.GetComp<CompHauledToInventory>()?.PeekHashSet().Contains(source) != true
                        || pawn.carryTracker.CarriedThing != null || !source.def.EverStorable(false))
                    { EndJobWith(JobCondition.Incompletable); return; }
                    // The returned remainder re-enters this same fresh live keep/surplus calculation.
                    int count = Math.Min(countToDrop, UnloadableCountOf(source));
                    if (count <= 0)
                    { nearbyCandidates.Remove(source); JumpToToil(begin); return; }
                    Thing piece = nearbyTransit.Withdraw(pawn, job, source, count);
                    job.count = count;
                    job.SetTarget(TargetIndex.A, piece);
                    if (NearbyHaulDelivery.Remaining(job, source) <= 0) nearbyCandidates.Remove(source);
                    piece.SetForbidden(false, false);
                    job.haulMode = HaulMode.ToCellStorage;
                }
            };
            var carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B, PathEndMode.ClosestTouch);
            var place = new Toil { defaultCompleteMode = ToilCompleteMode.Instant, initAction = PlaceNearbyCargo };
            yield return Toils_Jump.JumpIf(carryToCell, () => !TargetB.HasThing);
            yield return Toils_Haul.CarryHauledThingToContainer();
            yield return Toils_Jump.Jump(place);
            yield return carryToCell;
            yield return place;
            yield return Toils_Jump.Jump(begin);
        }

        private void ReleaseNearbyReservation()
        {
            // A native deposit notification may end this job. Never release a later order's claim.
            var map = job.globalTarget.Map;
            if (map != null && ReferenceEquals(map, pawn.Map)
                && map.reservationManager.ReservedBy(job.targetB, pawn, job))
                map.reservationManager.Release(job.targetB, pawn, job);
        }

        private bool NearbyCellLive(Thing held)
        {
            var cell = job.targetB.Cell;
            if (pawn.Map == null || !cell.InBounds(pawn.Map) || held.def.destroyOnDrop
                || !ReachabilityImmediate.CanReachImmediate(pawn.Position, cell, pawn.Map,
                    PathEndMode.ClosestTouch, pawn)) return false;
            var group = cell.GetSlotGroup(pawn.Map);
            if (group == null || (group.parent is Thing owner && owner.Faction != pawn.Faction)) return false;
            // IsGoodStoreCell(pawn) uses CanReserveNew and rejects even this job's existing reservation.
            // Use its supported carrier/faction-null physical checks, retaining the actor's explicit
            // forbiddance, reservation and immediate-reach gates here. IsValidStorageFor reads the live
            // storage parent/filter and native/patched capacity; no capacity arithmetic is copied.
            return !cell.IsForbidden(pawn)
                && (pawn.Map.reservationManager.ReservedBy(cell, pawn, job) || pawn.CanReserve(cell))
                && cell.IsValidStorageFor(pawn.Map, held)
                && StoreUtility.IsGoodStoreCell(cell, pawn.Map, held, null, null);
        }

        private bool NearbyContainerLive(Thing held, out Thing target, out ThingOwner owner, out int count)
        {
            target = job.targetB.Thing; owner = null; count = 0;
            if (target == null || target.Destroyed || !target.Spawned || target.Map != pawn.Map
                || target.Faction != pawn.Faction || target.IsForbidden(pawn)
                || !(target is IHaulDestination destination) || !destination.Accepts(held)
                || !pawn.Map.reservationManager.ReservedBy(target, pawn, job)
                || !ReachabilityImmediate.CanReachImmediate(pawn.Position, target, pawn.Map, PathEndMode.Touch, pawn))
                return false;
            owner = target.TryGetInnerInteractableThingOwner();
            if (owner == null || !owner.CanAcceptAnyOf(held)) return false;
            count = held.stackCount;
            if (target is IHaulEnroute enroute)
                count = Math.Min(count, enroute.GetSpaceRemainingWithEnroute(held.def, pawn));
            return count > 0;
        }

        private bool SizeNearbyContainerCargo(ref Thing held, out Thing target, out ThingOwner owner,
            out Thing settledSource)
        {
            settledSource = null;
            if (!NearbyContainerLive(held, out target, out owner, out int count)) return false;
            if (count < held.stackCount)
            {
                // Settle ALL of the old receipt first. A limited combined transfer would hide a
                // second split local if destination TryAdd throws before publishing its out reference.
                settledSource = nearbyTransit.Return(pawn, job, nearbyCandidates);
                if (settledSource == null || !nearbyTransit.CanReset(pawn)
                    || !NearbyDeliveryTransit.Owns(pawn.inventory.innerContainer, settledSource)) return false;
                nearbyTransit.Reset(pawn);
                held = null;
                if (!ReferenceEquals(pawn.CurJob, job) || !NearbyHaulCommand.CanContinue(pawn, job)
                    || pawn.carryTracker.CarriedThing != null
                    || pawn.GetComp<CompHauledToInventory>()?.PeekHashSet().Contains(settledSource) != true
                    || !settledSource.def.EverStorable(false)
                    || !NearbyContainerLive(settledSource, out target, out owner, out count)) return false;
                // This is a NEW receipt after actual return/credit, with fresh keep/manifest/capacity.
                count = Math.Min(count, Math.Min(settledSource.stackCount, UnloadableCountOf(settledSource)));
                if (count <= 0 || !ReferenceEquals(pawn.CurJob, job)) return false;
                held = nearbyTransit.Withdraw(pawn, job, settledSource, count);
                job.count = count;
                job.SetTarget(TargetIndex.A, held);
                if (NearbyHaulDelivery.Remaining(job, settledSource) <= 0) nearbyCandidates.Remove(settledSource);
                held.SetForbidden(false, false);
            }
            // At most ONE settlement/re-withdrawal per visit. A further capacity decrease returns
            // the new known piece and steps over this source; there is no resize/retry loop.
            return ReferenceEquals(pawn.CurJob, job) && NearbyHaulCommand.CanContinue(pawn, job)
                && NearbyContainerLive(held, out target, out owner, out count) && count >= held.stackCount
                && ReferenceEquals(nearbyTransit.Held(pawn), held) && ReferenceEquals(pawn.CurJob, job);
        }

        private bool NearbyPlacementProgress(Thing attempted, int units, ThingOwner owner, Map map, IntVec3 cell)
        {
            if (attempted == null) return false; // inventory settlement/re-withdrawal is never delivery
            if (attempted.Destroyed || NearbyDeliveryTransit.Owns(owner, attempted)) return true;
            if (attempted.Spawned) return attempted.Map == map && attempted.Position == cell;
            // Native partial merge leaves this SAME whole attempted piece smaller, even if its
            // notification throws before rollback/out publication. Do not infer from arbitrary hands.
            return attempted.stackCount > 0 && attempted.stackCount < units
                && (attempted.holdingOwner == null
                    || NearbyDeliveryTransit.Owns(pawn.carryTracker?.innerContainer, attempted)
                    || NearbyDeliveryTransit.Owns(pawn.inventory?.innerContainer, attempted));
        }

        private void PlaceNearbyCargo()
        {
            Thing held = nearbyTransit?.Held(pawn);
            if (held == null || !NearbyHaulCommand.CanContinue(pawn, job))
            { EndJobWith(JobCondition.Incompletable); return; }
            Thing selectedSource = nearbyTransit.Source, settledSource = null, attempted = null;
            ThingOwner attemptedOwner = null;
            Map attemptedMap = null;
            IntVec3 attemptedCell = IntVec3.Invalid;
            int attemptedUnits = 0;
            Exception primary = null, recovery = null;
            try
            {
                if (TargetB.HasThing)
                {
                    if (SizeNearbyContainerCargo(ref held, out var target, out var owner, out settledSource))
                    {
                        attempted = held; attemptedUnits = held.stackCount; attemptedOwner = owner;
                        // Always the WHOLE retained piece. Native SplitOff receives its full count,
                        // so the transfer's unpublished local cannot be an unrecorded sibling.
                        int moved = pawn.carryTracker.innerContainer.TryTransferToContainer(held, owner, held.stackCount);
                        if (moved > 0)
                        {
                            if (target is IHaulEnroute enroute) target.Map.enrouteManager.ReleaseFor(enroute, pawn);
                            if (target is INotifyHauledTo notification) notification.Notify_HauledTo(pawn, held, moved);
                            if (target is ThingWithComps withComps)
                                foreach (var comp in withComps.AllComps)
                                    if (comp is INotifyHauledTo notify) notify.Notify_HauledTo(pawn, held, moved);
                        }
                    }
                }
                else if (NearbyCellLive(held))
                {
                    attempted = held; attemptedUnits = held.stackCount;
                    attemptedMap = pawn.Map; attemptedCell = job.targetB.Cell;
                    // Exact direct cell only. No PlaceHauledThingInCell, Near, haul-aside or destroy fallback.
                    pawn.carryTracker.TryDropCarriedThing(job.targetB.Cell, ThingPlaceMode.Direct, out _);
                }
            }
            catch (Exception error) { primary = error; }
            Thing returned = null;
            bool unsettled = false, stop = false;
            try
            {
                bool progressed = NearbyPlacementProgress(attempted, attemptedUnits, attemptedOwner,
                    attemptedMap, attemptedCell);
                // A settlement can return ten and a fresh withdrawal debit four. Only this new
                // receipt's actual remainder is credited here; already placed units stay spent.
                returned = nearbyTransit.Return(pawn, job, nearbyCandidates);
                ReleaseNearbyReservation();
                if (progressed) nearbyTransit.noProgress = 0;
                else nearbyTransit.noProgress++;
                unsettled = !nearbyTransit.CanReset(pawn);
                if (!unsettled)
                {
                    nearbyTransit.Reset(pawn);
                    if (!progressed)
                    {
                        // Skip this actual transaction lineage immediately so zero-progress A
                        // cannot consume the budget before B. Same-def unrelated sources stay eligible.
                        if (selectedSource != null) skippedThisJob.Add(selectedSource);
                        if (settledSource != null) skippedThisJob.Add(settledSource);
                        if (returned != null) skippedThisJob.Add(returned);
                        stop = nearbyTransit.noProgress >= NearbyNoProgressLimit || RemainingCandidateCount() == 0;
                    }
                }
            }
            catch (Exception error) { recovery = error; }
            NearbyDeliveryTransit.Rethrow(primary, recovery); // never skip the original error on a failed recovery
            if ((unsettled || stop) && ReferenceEquals(pawn.CurJob, job))
                EndJobWith(JobCondition.Incompletable);
        }

        private void NearbyPatherFailed()
        {
            // No targetA/CarriedThing fallback. Return only a receipt-backed piece, then try other stock.
            Thing returned = nearbyTransit?.Return(pawn, job, nearbyCandidates);
            if (returned != null) skippedThisJob.Add(returned);
            ReleaseNearbyReservation();
            if (nearbyTransit == null || !nearbyTransit.Valid || !nearbyTransit.CanReset(pawn))
            { EndJobWith(JobCondition.Incompletable); return; }
            nearbyTransit.noProgress++;
            nearbyTransit.Reset(pawn);
            if (loopToil != null && nearbyTransit.noProgress < NearbyNoProgressLimit && RemainingCandidateCount() > 0)
                JumpToToil(loopToil);
            else EndJobWith(JobCondition.Incompletable);
        }
    }
}
