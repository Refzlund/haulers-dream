using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    // One exact withdrawn portion. The inventory manifest is debited once when this receipt takes custody;
    // only its actual unmerged inventory return can credit it again. A destination is never a custody key.
    internal sealed class NearbyDeliveryTransit : IExposable
    {
        private int version = 1;
        private Thing source, piece;
        private int sourceBefore, units;
        private bool debited, returnAttempted, reattachAttempted, credited, failed;
        internal int noProgress;

        public NearbyDeliveryTransit() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref version, "version", 0);
            Scribe_References.Look(ref source, "source");
            Scribe_References.Look(ref piece, "piece");
            Scribe_Values.Look(ref sourceBefore, "sourceBefore");
            Scribe_Values.Look(ref units, "units");
            Scribe_Values.Look(ref debited, "debited");
            Scribe_Values.Look(ref returnAttempted, "returnAttempted");
            Scribe_Values.Look(ref reattachAttempted, "reattachAttempted");
            Scribe_Values.Look(ref credited, "credited");
            Scribe_Values.Look(ref failed, "failed");
            Scribe_Values.Look(ref noProgress, "noProgress");
        }

        internal bool Valid => version == 1 && !failed && units >= 0 && noProgress >= 0
            && (units == 0 ? piece == null && source == null && sourceBefore == 0 && !debited && !credited
                : piece != null && debited && sourceBefore >= units);
        internal bool Empty => units == 0 && piece == null && !debited;
        internal Thing Piece => piece;
        internal Thing Source => source;

        internal static bool Owns(ThingOwner owner, Thing item) => owner != null && item != null
            && !item.Destroyed && !item.Spawned && ReferenceEquals(item.holdingOwner, owner)
            && owner.Contains(item);

        internal Thing Held(Pawn pawn)
        {
            if (!Valid || !debited || credited || piece == null || piece.stackCount <= 0 || piece.stackCount > units)
                return null;
            return Owns(pawn.carryTracker?.innerContainer, piece)
                && ReferenceEquals(pawn.carryTracker.CarriedThing, piece) ? piece : null;
        }

        internal Thing Withdraw(Pawn pawn, Job job, Thing from, int count)
        {
            var inventory = pawn.inventory.innerContainer;
            var hands = pawn.carryTracker.innerContainer;
            if (!Valid || !Empty || hands.Count != 0 || pawn.carryTracker.CarriedThing != null
                || !Owns(inventory, from) || count <= 0 || count > from.stackCount
                || count > NearbyHaulDelivery.Remaining(job, from))
                throw new InvalidOperationException("Nearby delivery withdrawal lacks exact empty-hands custody.");
            source = from; sourceBefore = from.stackCount;
            Exception primary = null, recovery = null;
            try
            {
                // Retain the actual split BEFORE TryAdd/NotifyAdded. The native combined transfer hides
                // its split local when TryAdd throws before its out reference is published.
                piece = from.SplitOff(count);
                units = count;
                if (piece == null || piece.Destroyed || piece.Spawned || piece.stackCount != count
                    || (ReferenceEquals(piece, from) ? sourceBefore != count
                        : !Owns(inventory, from) || from.stackCount != sourceBefore - count))
                    throw new InvalidOperationException("Nearby delivery split identity/quantity changed.");
                if (inventory.Contains(piece)) inventory.Remove(piece);
                if (piece.holdingOwner != null || piece.Spawned)
                    throw new InvalidOperationException("Nearby delivery split has an unexpected owner.");
                NearbyHaulDelivery.Debit(job, from, count);
                debited = true;
                bool added = hands.TryAdd(piece, canMergeWithExistingStacks: false);
                if (!added || Held(pawn) == null)
                    throw new InvalidOperationException("Nearby delivery withdrawal did not establish exact hands custody.");
            }
            catch (Exception error) { primary = error; }
            if (primary != null)
            {
                // Reconcile the retained exact piece. No CarriedThing fallback, same-def search or second withdrawal.
                try { Return(pawn, job, null); }
                catch (Exception error) { recovery = error; }
                failed = true;
                Rethrow(primary, recovery);
            }
            return piece;
        }

        internal Thing Return(Pawn pawn, Job job, HashSet<Thing> candidates)
        {
            if (version != 1 || piece == null || units <= 0 || sourceBefore < units || (credited && !debited))
                return null;
            var inventory = pawn.inventory?.innerContainer;
            var hands = pawn.carryTracker?.innerContainer;
            if (piece.Destroyed || piece.Spawned || (piece.holdingOwner != null
                && !ReferenceEquals(piece.holdingOwner, inventory) && !ReferenceEquals(piece.holdingOwner, hands)))
                return null; // Already placed/transferred; never recover it from the world/another owner.
            if (piece.stackCount <= 0 || piece.stackCount > units)
                throw new InvalidOperationException("Nearby delivery remainder exceeds its exact receipt.");
            Exception primary = null;
            if (!Owns(inventory, piece) && !returnAttempted)
            {
                returnAttempted = true; // one operation, including throws after insertion
                try
                {
                    if (Owns(hands, piece))
                    {
                        // Whole known remainder: native SplitOff returns this same object. An unset out
                        // reference after NotifyAdded throws does not hide our independently retained identity.
                        hands.TryTransferToContainer(piece, inventory, piece.stackCount,
                            out _, canMergeWithExistingStacks: false);
                    }
                    else if (piece.holdingOwner == null && !piece.Spawned && inventory != null)
                        inventory.TryAdd(piece, canMergeWithExistingStacks: false);
                }
                catch (Exception error) { primary = error; }
            }
            Exception secondary = null;
            Thing returned = null;
            try
            {
                if (Owns(inventory, piece))
                {
                    returned = piece;
                    if (debited && !credited)
                    {
                        NearbyHaulDelivery.CreditReturn(job, piece, piece.stackCount);
                        credited = true; // before the possibly throwing tracking/compatibility notification
                        pawn.GetComp<CompHauledToInventory>().RegisterHauledItem(piece, piece.stackCount);
                    }
                    if (credited) candidates?.Add(piece);
                }
                else if (piece.holdingOwner == null && !piece.Destroyed && !piece.Spawned
                    && hands != null && hands.Count == 0)
                {
                    // Exact known detached portion only: restore native hands after a refused/throwing
                    // inventory add. This is one compensating attachment, never another inventory transfer.
                    if (!reattachAttempted)
                    {
                        reattachAttempted = true;
                        hands.TryAdd(piece, canMergeWithExistingStacks: false);
                    }
                    if (!Owns(hands, piece))
                        throw new InvalidOperationException("Nearby delivery could not retain its detached remainder.");
                }
            }
            catch (Exception error) { secondary = error; }
            Rethrow(primary, secondary);
            return returned;
        }

        internal bool CanReset(Pawn pawn) => piece == null || units == 0 || credited
            || piece.Destroyed || piece.Spawned
            || (piece.holdingOwner != null && !ReferenceEquals(piece.holdingOwner, pawn.inventory?.innerContainer)
                && !ReferenceEquals(piece.holdingOwner, pawn.carryTracker?.innerContainer));

        internal void Reset(Pawn pawn)
        {
            if (!CanReset(pawn))
                throw new InvalidOperationException("Nearby delivery still owns an unsettled portion.");
            source = null; piece = null; sourceBefore = units = 0;
            debited = returnAttempted = reattachAttempted = credited = false;
        }

        internal static void Rethrow(Exception primary, Exception secondary)
        {
            if (primary != null)
            {
                if (secondary != null)
                {
                    primary.Data["HaulersDream.NearbyDelivery.Recovery"] = secondary.ToString();
                    Log.Error("Nearby delivery recovery also failed: " + secondary);
                }
                ExceptionDispatchInfo.Capture(primary).Throw();
            }
            if (secondary != null) ExceptionDispatchInfo.Capture(secondary).Throw();
        }
    }
}
