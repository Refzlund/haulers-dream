using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    // Durable physical receipt for one ordinary unload activation. Neither a destination nor
    // a same-def hand passenger can create this receipt: it names the actual withdrawn object.
    internal sealed class OrdinaryUnloadTransit : IExposable
    {
        private int version = 1, jobId, units, sourceBefore;
        private Thing source, piece;
        public OrdinaryUnloadTransit() { }
        internal OrdinaryUnloadTransit(Job job, Thing source, int units)
        { jobId = job.loadID; this.source = source; sourceBefore = source.stackCount; this.units = units; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref version, "version", 0);
            Scribe_Values.Look(ref jobId, "jobId", -1);
            Scribe_Values.Look(ref units, "units");
            Scribe_Values.Look(ref sourceBefore, "sourceBefore");
            Scribe_References.Look(ref source, "source");
            Scribe_References.Look(ref piece, "piece");
        }
        internal Thing Held(JobDriver_UnloadHauledInventory driver)
            => version == 1 && units > 0 && units <= sourceBefore
                && driver?.job?.loadID == jobId && driver.pawn?.CurJob == driver.job
                && driver.pawn.jobs.curDriver == driver && Owns(driver.pawn.carryTracker?.innerContainer, piece)
                && ReferenceEquals(driver.pawn.carryTracker.CarriedThing, piece) && piece.stackCount <= units
                ? piece : null;
        internal int HeldUnits(JobDriver_UnloadHauledInventory driver)
            => Math.Min(units, Held(driver)?.stackCount ?? 0);
        internal Thing Retained(JobDriver_UnloadHauledInventory driver)
            => version == 1 && units > 0 && units <= sourceBefore && driver?.job?.loadID == jobId
                && (Owns(driver.pawn?.carryTracker?.innerContainer, piece)
                    || Owns(driver.pawn?.inventory?.innerContainer, piece)) ? piece : null;
        internal static OrdinaryUnloadTransit Migrate(Job job, Thing held, int maximum)
            => new OrdinaryUnloadTransit(job, held, maximum) { piece = held, sourceBefore = maximum };
        internal static bool Owns(ThingOwner owner, Thing item) => owner != null && item != null
            && !item.Destroyed && !item.Spawned && item.stackCount > 0
            && ReferenceEquals(item.holdingOwner, owner) && owner.Contains(item);

        internal Thing Withdraw(JobDriver_UnloadHauledInventory driver, int count,
            StorageCommitments.UnloadResourceTransfer responsibility)
        {
            var pawn = driver.pawn;
            var inventory = pawn.inventory?.innerContainer;
            var hands = pawn.carryTracker?.innerContainer;
            if (!responsibility.Current || !Owns(inventory, source) || hands == null || hands.Count != 0
                || pawn.CurJob != driver.job || pawn.jobs.curDriver != driver || driver.job.loadID != jobId
                || count <= 0 || count > units || count > source.stackCount) return null;
            var scope = new TransferScope(source, count);
            Exception failure = null;
            try
            {
                try
                {
                    // Preserve native/provider transfer interception. Empty hands plus no merging
                    // prevents a callback-inserted passenger from becoming indistinguishable cargo.
                    inventory.TryTransferToContainer(source, hands, count, out Thing returned,
                        canMergeWithExistingStacks: false);
                    // A successful provider override need not call base SplitOff. Its actual out
                    // object, empty-hands precondition and observed source debit give the same
                    // receipt; the base hook additionally captures descendants on a thrown return.
                    int sourceLeft = Owns(inventory, source) ? source.stackCount : 0;
                    if (Owns(hands, returned) && returned.stackCount <= count
                        && sourceBefore - sourceLeft >= returned.stackCount) scope.Record(returned);
                }
                catch (Exception error) { failure = error; }

                Thing held = pawn.carryTracker.CarriedThing;
                if (scope.Contains(held) && Owns(hands, held) && held.stackCount <= units) piece = held;
                bool sameActivation = pawn.CurJob == driver.job && pawn.jobs.curDriver == driver
                    && driver.job.loadID == jobId;
                if (failure != null || !sameActivation)
                {
                    // Only the witnessed piece can return from hands. Never move a replacement
                    // passenger, spawned descendant, or object another holder has acquired.
                    if (Owns(hands, piece))
                        try { hands.TryTransferToContainer(piece, inventory, piece.stackCount, out _,
                            canMergeWithExistingStacks: false); }
                        catch (Exception error) { Preserve(ref failure, error, "return-hands"); }
                }
                scope.RetainDetached(inventory, ref failure);
                if (Held(driver) != null && sameActivation)
                {
                    driver.job.SetTarget(TargetIndex.A, piece);
                    driver.job.count = HeldUnits(driver);
                    pawn.GetComp<CompHauledToInventory>()?.PeekHashSet().Remove(piece);
                }
                RepairTags(pawn, source, scope.Fragments, ref failure);
                try { responsibility.Complete(scope.Fragments, Held(driver), HeldUnits(driver)); }
                catch (Exception error) { Preserve(ref failure, error, "claims"); }
            }
            finally { scope.Dispose(); }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            return Held(driver);
        }

        internal Thing Return(JobDriver_UnloadHauledInventory driver, ISlotGroup retiredDestination = null)
            => ReturnForLoad(driver, null, retiredDestination);

        internal Thing ReturnForLoad(JobDriver_UnloadHauledInventory driver,
            StorageCommitments.LoadRecoveryTicket ticket, ISlotGroup retiredDestination = null)
        {
            Thing held = Held(driver);
            if (held == null) return null;
            var pawn = driver.pawn;
            var responsibility = StorageCommitments.UnloadResourceTransfer.CaptureReturnForLoad(pawn, held,
                retiredDestination, ticket);
            if (responsibility == null || !responsibility.Current || !ReferenceEquals(Held(driver), held)) return null;
            var scope = new TransferScope(held, held.stackCount);
            Exception failure = null;
            try
            {
                try { pawn.carryTracker.innerContainer.TryTransferToContainer(held, pawn.inventory.innerContainer,
                    held.stackCount, out _, canMergeWithExistingStacks: false); }
                catch (Exception error) { failure = error; }
                scope.RetainDetached(pawn.inventory.innerContainer, ref failure);
                RepairTags(pawn, held, scope.Fragments, ref failure);
                try
                {
                    // Custody remains exact even if return fails. Normally the retained piece
                    // keeps its old responsibility for retry; a confirmed rejected destination
                    // is retired without discarding the receipt or the actual inventory tag.
                    responsibility.Complete(scope.Fragments, null, 0);
                }
                catch (Exception error) { Preserve(ref failure, error, "return-claims"); }
            }
            finally { scope.Dispose(); }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            return Owns(pawn.inventory.innerContainer, held) ? held : null;
        }

        private static void RepairTags(Pawn pawn, Thing source, IReadOnlyList<Thing> fragments, ref Exception failure)
        {
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null) return;
            // Register mutates the tag before notifying CE. Attempt every exact recipient even
            // if one provider notification throws; all observation remains behind the barrier.
            var candidates = new List<Thing> { source };
            foreach (Thing fragment in fragments) if (!candidates.Contains(fragment)) candidates.Add(fragment);
            foreach (Thing candidate in candidates)
                if (Owns(pawn.inventory?.innerContainer, candidate))
                    try { comp.RegisterHauledItem(candidate); }
                    catch (Exception error) { Preserve(ref failure, error, "tag." + candidate.thingIDNumber); }
        }

        private static void Preserve(ref Exception primary, Exception secondary, string operation)
        {
            if (primary == null) primary = secondary;
            else primary.Data["HaulersDream.OrdinaryUnload." + operation] = secondary.ToString();
        }

        internal sealed class TransferScope : StorageSplitScope
        {
            internal TransferScope(Thing source, int requested) : base(source, requested) { }
            internal void RetainDetached(ThingOwner inventory, ref Exception failure)
            {
                for (int i = 0; i < Fragments.Count; i++)
                {
                    Thing item = Fragments[i];
                    if (item == null || item.Destroyed || item.Spawned || item.stackCount <= 0
                        || item.holdingOwner != null) continue;
                    try
                    {
                        if (inventory == null || (!inventory.TryAdd(item, canMergeWithExistingStacks: false)
                            && !Owns(inventory, item)))
                            throw new InvalidOperationException("Could not retain the exact ordinary-unload fragment.");
                    }
                    catch (Exception error) { Preserve(ref failure, error, "fragment." + item.thingIDNumber); }
                }
            }
        }
    }

}
