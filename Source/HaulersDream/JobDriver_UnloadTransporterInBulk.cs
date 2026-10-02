using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    // Feature adapted from nullpat's GH267. Unlike the proposal, storage is part of this job:
    // queueing a delivery never earns permission to return for another load.
    public sealed class JobDriver_UnloadTransporterInBulk : JobDriver
    {
        private List<TransporterUnloadCargo> cargo = new List<TransporterUnloadCargo>();
        private TransporterUnloadProgress progress = new TransporterUnloadProgress();
        private bool pendingToHands;
        private int pullLoops;
        private Thing handTail;
        // A busy store can change while we walk. Permit three alternative delivery plans without
        // depositing anything, then leave the held cargo to normal recovery rather than circling.
        // Save/load preserves this budget; only a real credited placement earns another budget.
        private const int MaxCellReplansWithoutDelivery = 3;
        private int cellReplansWithoutDelivery;
        private CompTransporter Hold => job.targetA.Thing?.TryGetComp<CompTransporter>();
        internal int StorageBoundHandCount => OwnedHands(pawn?.carryTracker?.CarriedThing)
            ? pawn.carryTracker.CarriedThing.stackCount : 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref cargo, "hdUtibCargo", LookMode.Deep);
            Scribe_References.Look(ref handTail, "hdUtibHandTail");
            Scribe_Values.Look(ref pendingToHands, "hdUtibPendingToHands");
            Scribe_Values.Look(ref pullLoops, "hdUtibPullLoops");
            Scribe_Values.Look(ref cellReplansWithoutDelivery, "hdUtibCellReplansWithoutDelivery");
            int pulled = progress.Pulled, delivered = progress.Delivered;
            Scribe_Values.Look(ref pulled, "hdUtibPulled");
            Scribe_Values.Look(ref delivered, "hdUtibDelivered");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            { progress = new TransporterUnloadProgress(pulled, delivered); cargo ??= new List<TransporterUnloadCargo>(); }
        }

        // Multiple haulers may visit a hold. Each atomic transfer rereads its real current owner/count.
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => BulkUnloadTransporterGate.StartBlock(pawn, Hold, job.playerForced) == TransporterUnloadBlock.None;

        public override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(_ =>
            {
                ReleaseDestination();
                var held = new List<Thing>();
                foreach (var item in cargo) if (item.remaining > 0) held.Add(item.thing);
                int ownedHands = cargo.Find(item => item.thing == handTail && item.remaining > 0)?.remaining ?? 0;
                BulkUnloadRecovery.Queue(pawn, held, handTail, ownedHands, job.playerForced);
            });
            this.FailOn(() => !BulkUnloadTransporterGate.Enabled || pawn.Drafted || pawn.Dead || pawn.Downed
                || !pawn.Spawned || pawn.inventory == null || pawn.carryTracker == null);

            var begin = ToilMaker.MakeToil("HD_Utib_BeginTrip");
            var select = ToilMaker.MakeToil("HD_Utib_SelectPull");
            var deliver = ToilMaker.MakeToil("HD_Utib_SelectDelivery");
            var deposited = ToilMaker.MakeToil("HD_Utib_AfterDelivery");
            begin.initAction = () =>
            {
                if (BulkUnloadTransporterGate.StartBlock(pawn, Hold, job.playerForced) != TransporterUnloadBlock.None)
                    EndJobWith(JobCondition.Incompletable);
            };
            begin.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return begin;
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            select.initAction = () =>
            {
                if (++pullLoops > 256 || BulkUnloadTransporterGate.ActorAndTargetBlock(pawn, Hold, job.playerForced) != TransporterUnloadBlock.None
                    || !BulkUnloadPull.TrySelect(pawn, Hold?.innerContainer, !job.playerForced, job.playerForced,
                        out Thing selected, out int count, out bool toHands, thing => BulkUnloadTransporterGate.HasStorage(pawn, thing)))
                { JumpToToil(deliver); return; }
                job.targetC = selected; job.count = count; pendingToHands = toHands;
            };
            select.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return select;
            int delay = HaulersDreamMod.Settings?.visualUnloadDelay ?? 0;
            yield return delay > 0 ? Toils_General.Wait(delay) : Toils_General.Label();
            var pull = ToilMaker.MakeToil("HD_Utib_Pull");
            pull.initAction = () =>
            {
                if (!BulkUnloadTransporterGate.HasStorage(pawn, job.targetC.Thing)) { JumpToToil(deliver); return; }
                int moved = BulkUnloadTransporterGate.Transfer(pawn, Hold, job.targetC.Thing, job.count,
                    pendingToHands, job.playerForced, out Thing item, out bool toHands);
                if (moved > 0)
                {
                    cargo.Add(new TransporterUnloadCargo(item, moved)); progress.RecordPull(moved);
                    if (toHands) handTail = item;
                    HaulersDreamGameComponent.Instance?.BulkUnloadAllClearIfNothingPullable(Hold);
                }
                JumpToToil(moved <= 0 || toHands ? deliver : select);
            };
            pull.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return pull;

            deliver.initAction = () =>
            {
                cargo.RemoveAll(entry => entry.remaining <= 0);
                if (cargo.Count == 0)
                {
                    bool repeat = progress.Complete && job.playerForced && pawn.jobs.jobQueue.Count == 0
                        && BulkUnloadTransporterGate.StartBlock(pawn, Hold, true) == TransporterUnloadBlock.None;
                    if (repeat && progress.BeginNextTrip())
                    { pullLoops = 0; JumpToToil(begin); }
                    else EndJobWith(progress.Complete ? JobCondition.Succeeded : JobCondition.Incompletable);
                    return;
                }
                var entry = FindHeldCargo();
                if (entry == null || !PlanDelivery(entry)) { EndJobWith(JobCondition.Incompletable); return; }
            };
            deliver.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return deliver;

            var toCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);
            yield return Toils_Jump.JumpIf(toCell, () => !job.targetB.HasThing);
            yield return Toils_Haul.CarryHauledThingToContainer();
            var containerDeposit = Toils_Haul.DepositHauledThingInContainer(TargetIndex.B, TargetIndex.None);
            var nativeDeposit = containerDeposit.initAction;
            containerDeposit.initAction = () =>
            {
                var item = pawn.carryTracker.CarriedThing;
                var target = job.targetB.Thing;
                var owner = target?.TryGetInnerInteractableThingOwner();
                if (!OwnedHands(item) || target == null || !target.Spawned || target.IsForbidden(pawn)
                    || !(target is IHaulDestination storage) || !storage.HaulDestinationEnabled || !storage.Accepts(item)
                    || owner == null || !owner.CanAcceptAnyOf(item))
                { EndJobWith(JobCondition.Incompletable); return; }
                int before = item.stackCount, storedBefore = CountDef(owner, item.def);
                var def = item.def;
                nativeDeposit();
                CreditDelivery(item, before, CountDef(owner, def) - storedBefore);
            };
            yield return containerDeposit;
            yield return Toils_Jump.Jump(deposited);

            yield return toCell;
            var place = ToilMaker.MakeToil("HD_Utib_StoreCell");
            place.initAction = () =>
            {
                var item = pawn.carryTracker.CarriedThing;
                var cell = job.targetB.Cell;
                if (!OwnedHands(item))
                { EndJobWith(JobCondition.Incompletable); return; }
                if (!cell.IsValid || !StoreUtility.IsGoodStoreCell(cell, pawn.Map, item, pawn, pawn.Faction))
                {
                    if (!ReplanCellDelivery(deliver)) EndJobWith(JobCondition.Incompletable);
                    return;
                }
                int before = item.stackCount, stored = 0;
                // Native direct placement only. Its general hauling toil can redirect/drop aside/destroy on
                // full storage; none of those outcomes completes this operation's storage trip.
                pawn.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out _, (placed, count) =>
                { if (placed != null && placed.Spawned && placed.Position == cell) stored += count; });
                CreditDelivery(item, before, stored);
            };
            place.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return place;
            deposited.initAction = () => { ReleaseDestination(); JumpToToil(deliver); };
            deposited.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return deposited;
        }

        private TransporterUnloadCargo FindHeldCargo()
        {
            var hands = pawn.carryTracker.CarriedThing;
            if (hands != null) return cargo.Find(x => x.thing == hands && x.remaining > 0);
            foreach (var entry in cargo)
                if (entry.thing != null && !entry.thing.Destroyed && pawn.inventory.innerContainer.Contains(entry.thing)
                    && InventorySurplus.SurplusOf(pawn, entry.thing) > 0) return entry;
            return null; // missing ownership or a new keep order ends the operation, never claims delivery
        }

        private bool PlanDelivery(TransporterUnloadCargo entry)
        {
            var item = entry.thing;
            if (!StoreUtility.TryFindBestBetterStorageFor(item, pawn, pawn.Map, StoragePriority.Unstored,
                pawn.Faction, out var cell, out var destination)) return false;
            int count = Math.Min(entry.remaining, item.stackCount);
            bool inInventory = pawn.inventory.innerContainer.Contains(item);
            if (inInventory) count = Math.Min(count, InventorySurplus.SurplusOf(pawn, item));
            if (count <= 0) return false;
            if (cell.IsValid) job.targetB = cell;
            else if (destination is Thing building && building.TryGetInnerInteractableThingOwner() != null) job.targetB = building;
            else return false;
            var group = cell.IsValid ? BulkHaul.BudgetGroupOf(pawn.Map.haulDestinationManager.SlotGroupAt(cell)) : null;
            bool arbitrated = StorageCommitments.TryCommit(pawn, group, item.def, count, "transporter-unload");
            if (!arbitrated && !pawn.Reserve(job.targetB, job)) return false;
            if (inInventory)
            {
                int moved = pawn.inventory.innerContainer.TryTransferToContainer(item, pawn.carryTracker.innerContainer,
                    count, out Thing held, canMergeWithExistingStacks: false);
                if (moved <= 0) return false;
                entry.remaining -= moved;
                cargo.Add(new TransporterUnloadCargo(held, moved));
                if (!pawn.inventory.innerContainer.Contains(item)) pawn.GetComp<CompHauledToInventory>()?.Deregister(item);
                handTail = held;
            }
            else handTail = item;
            job.count = count;
            return true;
        }

        private bool OwnedHands(Thing item) => item != null && !item.Destroyed
            && cargo.Exists(x => x.thing == item && x.remaining >= item.stackCount)
            && pawn.carryTracker.innerContainer.Contains(item);

        private bool ReplanCellDelivery(Toil deliver)
        {
            if (!OwnedHands(pawn.carryTracker.CarriedThing)
                || cellReplansWithoutDelivery >= MaxCellReplansWithoutDelivery) return false;
            cellReplansWithoutDelivery++;
            ReleaseDestination();
            // PlanDelivery replaces our group/def claim and retains the same actual hand stack.
            // No new pull, queue operation or progress reset occurs before this cargo is stored.
            JumpToToil(deliver);
            return true;
        }

        private void CreditDelivery(Thing item, int before, int actuallyStored)
        {
            int remains = pawn.carryTracker.innerContainer.Contains(item) ? item.stackCount : 0;
            int credited = progress.RecordDelivery(before - remains, actuallyStored);
            var entry = cargo.Find(x => x.thing == item && x.remaining > 0);
            if (entry != null) entry.remaining -= credited;
            if (remains == 0) handTail = null;
            if (credited <= 0) EndJobWith(JobCondition.Incompletable);
            else cellReplansWithoutDelivery = 0;
        }

        private static int CountDef(ThingOwner owner, ThingDef def)
        {
            int count = 0;
            for (int i = 0; i < owner.Count; i++) if (owner[i].def == def) count += owner[i].stackCount;
            return count;
        }

        private void ReleaseDestination()
        {
            if (pawn?.Map?.reservationManager == null) return;
            // Storage commitments may admit this delivery without an exclusive native reservation.
            // Release only this job's actual claim, including after native cleanup already released it.
            if (job.targetB.IsValid && pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, job))
                pawn.Map.reservationManager.Release(job.targetB, pawn, job);
            job.targetB = LocalTargetInfo.Invalid;
        }
    }

    public sealed class TransporterUnloadCargo : IExposable
    {
        public Thing thing;
        public int remaining;
        public TransporterUnloadCargo() { }
        public TransporterUnloadCargo(Thing thing, int remaining) { this.thing = thing; this.remaining = remaining; }
        public void ExposeData()
        { Scribe_References.Look(ref thing, "thing"); Scribe_Values.Look(ref remaining, "remaining"); }
    }
}
