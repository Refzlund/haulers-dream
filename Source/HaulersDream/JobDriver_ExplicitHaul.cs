using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    public sealed class JobDriver_ExplicitHaul : JobDriver
    {
        private ExplicitHaulOrder Order => pawn?.GetComp<CompHauledToInventory>()?.ExplicitOrder(job);
        // Native target-only deduplication must never swallow a new 7/9 (or 7/7) intent.
        public override bool? IsSameJobAs(Job other) => ReferenceEquals(job, other);
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => ExplicitHaulCommand.Identified(pawn, job) && Order.state != ExplicitHaulState.Blocked
                && ExplicitHaulCommand.ActorReason(pawn) == null;

        public override IEnumerable<Toil> MakeNewToils()
        {
            var toil = ToilMaker.MakeToil("ExplicitHaulTrip");
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.tickAction = Advance;
            yield return toil;
        }
        private void Stop(ExplicitHaulOrder order, string reason)
        { order?.Block(reason); EndJobWith(JobCondition.Incompletable); }

        private void Advance()
        {
            var o = Order;
            if (o == null || !ExplicitHaulCommand.Identified(pawn, job) || pawn.CurJob != job || pawn.jobs.curDriver != this || o.Terminal
                || o.mapId != pawn.Map?.uniqueID || ExplicitHaulCommand.ActorReason(pawn) != null)
            { Stop(o, "Order identity or pawn permission changed"); return; }
            if (o.state == ExplicitHaulState.Blocked) { EndJobWith(JobCondition.Incompletable); return; }
            if (o.Remaining == 0)
            { o.state = ExplicitHaulState.Complete; o.reason = null; EndJobWith(JobCondition.Succeeded); return; }
            o.state = ExplicitHaulState.Active;
            if (o.piece != null)
            {
                if (pawn.carryTracker.CarriedThing == null) ExplicitHaulTransfer.RestoreHands(pawn, o);
                if (!ExplicitHaulTransfer.Owns(pawn.carryTracker.innerContainer, o.piece)
                    || pawn.carryTracker.CarriedThing != o.piece || o.piece.stackCount != o.tripUnits)
                { Stop(o, "Recorded parcel custody changed"); return; }
                if (!Destination(o, o.piece, out _)) return;
                if (pawn.Position != o.DeliveryCell) { Walk(o, o.DeliveryCell, PathEndMode.OnCell); return; }
                try { ExplicitHaulTransfer.Place(pawn, o); }
                finally { StorageCommitments.ReleaseExplicitShelf(pawn, o); }
                if (o.state == ExplicitHaulState.Blocked) { EndJobWith(JobCondition.Incompletable); return; }
                o.pathStarted = 0; job.targetA = LocalTargetInfo.Invalid;
                if (o.Remaining == 0) { o.state = ExplicitHaulState.Complete; EndJobWith(JobCondition.Succeeded); }
                return;
            }
            if (pawn.carryTracker.CarriedThing != null) { Stop(o, "Hands already contain unrelated cargo"); return; }
            var recovery = o.remainders.Count > 0 ? o.remainders[0] : null;
            Thing source = recovery?.thing ?? o.source;
            if (source == null || !source.Spawned || source.Map != pawn.Map || source.Destroyed
                || (recovery == null && source.thingIDNumber != o.sourceId)
                || (recovery != null && (source.stackCount != recovery.observedCount || source.Position != recovery.cell
                    || recovery.units <= 0 || recovery.units > source.stackCount)))
            { Stop(o, recovery == null ? "Selected source exhausted or replaced" : "Dropped parcel changed custody"); return; }
            // A deliberately selected forbidden source is a forced order. A later forbid on a
            // previously allowed source pauses that intent, including while queued or walking.
            if (!NearbyHaulCommand.IsGroundTarget(pawn, source) || (!o.sourceForbiddenAtIssue && source.IsForbidden(pawn)))
            { Stop(o, "Selected source permission changed"); return; }
            if (!Destination(o, source, out int capacity)) return;
            if (!ExplicitHaulCommand.Reserve(pawn, job, source)) { Stop(o, "Selected source is reserved"); return; }
            job.targetA = source;
            if (!pawn.CanReachImmediate(source, PathEndMode.ClosestTouch)) { Walk(o, source, PathEndMode.ClosestTouch); return; }
            int limit = recovery?.units ?? source.stackCount;
            // This is hands cargo. CE's CanFitInInventory measures the personal backpack, not hands.
            int count = ExplicitHaulAmount.Trip(o.Remaining, limit, pawn.carryTracker.MaxStackSpaceEver(source.def), capacity);
            if (count <= 0) { Stop(o, "No current carry or destination capacity"); return; }
            ExplicitHaulTransfer.Pickup(pawn, o, source, count, recovery);
            if (pawn.Map.reservationManager.ReservedBy(source, pawn, job)) pawn.Map.reservationManager.Release(source, pawn, job);
            o.pathStarted = 0;
        }
        private bool Destination(ExplicitHaulOrder order, Thing subject, out int capacity)
        {
            capacity = 0;
            if (order.IsShelf)
            {
                if (StorageCommitments.AcquireExplicitShelf(pawn, job, order, subject, out capacity)) return true;
                Stop(order, StorageCommitments.SameExplicitShelf(pawn, order)
                    ? "Selected shelf unavailable or reserved" : "Selected shelf moved or replaced"); return false;
            }
            if (!ExplicitHaulCell.Observe(pawn, subject, order.destination, out capacity, out _)
                || capacity <= 0 || !ExplicitHaulCommand.Reserve(pawn, job, order.destination))
            { Stop(order, "Selected bare location unavailable or reserved"); return false; }
            return true;
        }
        private void Walk(ExplicitHaulOrder order, LocalTargetInfo target, PathEndMode mode)
        {
            if (order.pathStarted == 0) order.pathStarted = Find.TickManager.TicksGame;
            if (Find.TickManager.TicksGame - order.pathStarted > 18000 || !pawn.CanReach(target, mode, Danger.Some))
            { Stop(order, "Selected route is unavailable"); return; }
            if (!pawn.pather.Moving) pawn.pather.StartPath(target, mode);
        }
    }
}
