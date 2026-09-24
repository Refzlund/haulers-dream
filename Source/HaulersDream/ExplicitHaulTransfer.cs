using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using HaulersDream.Core;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    internal static class ExplicitHaulTransfer
    {
        internal sealed class SplitAttempt
        {
            internal SplitAttempt previous;
            internal ExplicitHaulOrder order;
            internal ExplicitHaulRemainder recovery;
            internal Thing source;
            internal int count;
            internal bool recorded;
        }
        [ThreadStatic] internal static SplitAttempt splitting;
        internal static void RecordSplit(SplitAttempt attempt, Thing piece)
        {
            if (attempt.recorded) return;
            attempt.order.piece = piece;
            attempt.order.tripUnits = attempt.count;
            if (piece == null || piece.Destroyed || piece.Spawned || piece.stackCount != attempt.count)
                throw new InvalidOperationException("Explicit haul split identity changed.");
            attempt.recorded = true;
            if (attempt.recovery == null) return;
            attempt.recovery.units -= attempt.count;
            attempt.recovery.observedCount = attempt.source.Destroyed || !attempt.source.Spawned ? 0 : attempt.source.stackCount;
            if (attempt.recovery.units == 0) attempt.order.remainders.Remove(attempt.recovery);
        }
        internal static bool Owns(ThingOwner owner, Thing thing) => owner != null && thing != null
            && !thing.Destroyed && !thing.Spawned && thing.holdingOwner == owner && owner.Contains(thing);

        internal static void Retain(Pawn pawn, ExplicitHaulOrder order)
        {
            var piece = order.piece;
            if (piece == null || piece.Destroyed || piece.Spawned || piece.holdingOwner != null) return;
            if (!pawn.GetComp<CompHauledToInventory>().ExplicitRecovery.TryAdd(piece, false)
                && !Owns(pawn.GetComp<CompHauledToInventory>().ExplicitRecovery, piece))
                throw new InvalidOperationException("Could not retain the exact explicit-haul parcel.");
        }
        internal static void Pickup(Pawn pawn, ExplicitHaulOrder order, Thing source, int count,
            ExplicitHaulRemainder recovery)
        {
            Exception error = null;
            try
            {
                var attempt = new SplitAttempt { previous = splitting, order = order, recovery = recovery,
                    source = source, count = count };
                splitting = attempt;
                try
                {
                    var actual = source.SplitOff(count);
                    if (!attempt.recorded) RecordSplit(attempt, actual);
                    if (!ReferenceEquals(actual, order.piece))
                        throw new InvalidOperationException("Explicit haul split returned another parcel.");
                }
                finally { splitting = attempt.previous; }
                if (!pawn.carryTracker.innerContainer.TryAdd(order.piece, false)
                    || !Owns(pawn.carryTracker.innerContainer, order.piece))
                    throw new InvalidOperationException("Explicit haul could not establish exact hands custody.");
                pawn.Map.resourceCounter.UpdateResourceCounts();
            }
            catch (Exception ex) { error = ex; order.Block("Pickup failed; retained parcel requires review"); }
            if (error != null) RecoverAndThrow(pawn, order, error);
        }
        internal static void RestoreHands(Pawn pawn, ExplicitHaulOrder order)
        {
            var owner = pawn.GetComp<CompHauledToInventory>().ExplicitRecovery;
            if (!Owns(owner, order.piece)) return;
            try
            {
                owner.Remove(order.piece);
                if (!pawn.carryTracker.innerContainer.TryAdd(order.piece, false))
                    throw new InvalidOperationException("Explicit parcel could not return to empty hands.");
            }
            catch (Exception ex) { order.Block("Recovery failed"); RecoverAndThrow(pawn, order, ex); }
        }
        private static void RecoverAndThrow(Pawn pawn, ExplicitHaulOrder order, Exception primary)
        {
            try { Retain(pawn, order); }
            catch (Exception secondary) { primary.Data["HaulersDream.ExplicitHaul.Recovery"] = secondary.ToString(); }
            ExceptionDispatchInfo.Capture(primary).Throw();
        }

        internal static int Place(Pawn pawn, ExplicitHaulOrder order)
        {
            var piece = order.piece;
            if (!Owns(pawn.carryTracker.innerContainer, piece) || piece.stackCount != order.tripUnits
                || piece.stackCount > piece.def.stackLimit)
                throw new InvalidOperationException("Placement requires one exact whole hands parcel.");
            int attempted = piece.stackCount;
            var destination = order.DeliveryCell;
            if (order.IsShelf && (order.shelfAllocation?.Live != true || !StorageCommitments.SameExplicitShelf(pawn, order)))
                throw new InvalidOperationException("Selected shelf allocation changed before placement.");
            var before = destination.GetThingList(pawn.Map).Where(t => t.def.category == ThingCategory.Item)
                .ToDictionary(t => t, t => t.stackCount);
            var receivers = new Dictionary<Thing, int>();
            Exception primary = null;
            try
            {
                // Whole known parcel avoids ThingOwner's hidden count-limited SplitOff local. It is
                // already bounded by requested remainder and native hands/stack capacity at pickup.
                pawn.carryTracker.TryDropCarriedThing(destination, ThingPlaceMode.Direct, out _,
                    (receiver, n) => { if (receiver != null && n > 0) receivers[receiver] =
                        (receivers.TryGetValue(receiver, out int old) ? old : 0) + n; });
            }
            catch (Exception ex) { primary = ex; }
            try
            {
                int accepted = 0;
                // Receiver deltas also cover a native callback that throws after the physical merge,
                // before placedAction. Only the selected cell, exact receiver identities and conserved
                // reduction of our retained whole parcel can establish credit.
                foreach (var t in destination.GetThingList(pawn.Map))
                {
                    if (t.def.category != ThingCategory.Item || t.def != piece.def) continue;
                    int prior = before.TryGetValue(t, out int n) ? n : 0;
                    int delta = t.stackCount - prior;
                    if (delta > 0 && (prior > 0 || ReferenceEquals(t, piece))) accepted = checked(accepted + delta);
                }
                int retained = !piece.Destroyed && !piece.Spawned ? piece.stackCount : 0;
                if (receivers.Any(r => !r.Key.Spawned || r.Key.Map != pawn.Map || r.Key.Position != destination
                    || r.Value > r.Key.stackCount - (before.TryGetValue(r.Key, out int old) ? old : 0)))
                    throw new InvalidOperationException("Explicit delivery callback disagrees with its receiver.");
                order.delivered = ExplicitHaulAmount.Credit(order.requested, order.delivered, attempted, retained, accepted);
                order.tripUnits = retained;
                if (retained == 0)
                {
                    if (pawn.carryTracker.innerContainer.Contains(piece)) pawn.carryTracker.innerContainer.Remove(piece);
                    order.piece = null;
                }
                else Retain(pawn, order);
                pawn.Map.resourceCounter.UpdateResourceCounts();
                if (order.Remaining == 0 && retained == 0)
                { order.state = ExplicitHaulState.Complete; order.reason = null; }
                else if (primary != null) order.Block("Placement threw after physical reconciliation");
                else if (retained > 0 && !order.IsShelf) order.Block("Selected location accepted only part of the parcel");
            }
            catch (Exception reconciliation)
            {
                order.Block("Placement custody changed; no retry permitted without review");
                if (primary == null) primary = reconciliation;
                else primary.Data["HaulersDream.ExplicitHaul.Reconciliation"] = reconciliation.ToString();
            }
            if (primary != null) RecoverAndThrow(pawn, order, primary);
            return attempted - order.tripUnits;
        }
    }

    // ThingWithComps calls comp.PostSplitOff after base returns. Capture our actual native
    // split before those callbacks can throw and hide the returned parcel from the caller.
    [HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    internal static class Patch_ExplicitHaulPickupSplit
    {
        static void Postfix(Thing __instance, int count, Thing __result)
        {
            var attempt = ExplicitHaulTransfer.splitting;
            if (attempt != null && ReferenceEquals(__instance, attempt.source) && count == attempt.count)
                ExplicitHaulTransfer.RecordSplit(attempt, __result);
        }
    }
}
