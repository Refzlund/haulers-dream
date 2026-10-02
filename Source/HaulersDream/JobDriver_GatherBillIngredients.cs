using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace HaulersDream
{
    /// <summary>
    /// Gathers a retained ordinary recipe selection, then starts native DoBill with that same selection.
    /// New def/driver: the legacy prep's saved numeric toil indices and floor-only payload stay untouched.
    /// </summary>
    public class JobDriver_GatherBillIngredients : JobDriver
    {
        // Invoke the real owner seam, including installed Harmony postfixes. CE refreshes its weight/bulk
        // caches here; HoldTracker notification alone does not refresh those pickup-limit caches.
        private static readonly MethodInfo NotifyMerge = AccessTools.Method(typeof(ThingOwner),
            "NotifyAddedAndMergedWith", new[] { typeof(Thing), typeof(int) })
            ?? throw new MissingMethodException(typeof(ThingOwner).FullName, "NotifyAddedAndMergedWith");

        private int scanLimit = -1;
        private int cursor;
        private bool loadedAnything;
        private bool handoffStarted;
        private bool invalidSelection;
        private Toil choose;
        private Toil walk;

        private ThingOwner<Thing> Inventory => pawn.inventory?.innerContainer;

        public override string GetReport() => "HaulersDream.PrepGather.Report".Translate();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref scanLimit, "hdBillGatherScanLimit", -1);
            Scribe_Values.Look(ref cursor, "hdBillGatherCursor", 0);
            Scribe_Values.Look(ref loadedAnything, "hdBillGatherLoaded", false);
            Scribe_Values.Look(ref handoffStarted, "hdBillGatherHandoff", false);
            Scribe_Values.Look(ref invalidSelection, "hdBillGatherInvalid", false);
            // Base rebuilds toils at PostLoadInit. Their layout never depends on live settings/stock.
            base.ExposeData();
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
            var bench = job.targetA.Thing;
            if (bench != null && bench.def.hasInteractionCell
                && !pawn.ReserveSittableOrSpot(bench.InteractionCell, job, errorOnFailed)) return false;
            if (job.targetQueueB != null) pawn.ReserveAsManyAsPossible(job.targetQueueB, job);
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => pawn.Drafted || pawn.Downed || pawn.InMentalState
                || job.bill == null || job.bill.billStack == null || job.bill.DeletedOrDereferenced || job.bill.suspended);
            AddFinishAction(condition =>
            {
                if (handoffStarted) return;
                if (invalidSelection || !loadedAnything) BillPrepTracker.NoteEmptyRun(pawn);
                if (loadedAnything && !pawn.Dead && !pawn.Downed && !pawn.InMentalState)
                    PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true, behindQueuedWork: true);
            });

            var initialize = ToilMaker.MakeToil("HD_BillGather_Initialize");
            initialize.initAction = () =>
            {
                if (scanLimit >= 0) return;
                if (!BillGatherContract.BillMayStart(pawn, job)
                    || !BillGatherContract.TryValidateSelection(pawn, job, out var targets, out var counts))
                { RejectSelection("start-invalid"); return; }
                ReplaceSelection(targets, counts);
                scanLimit = targets.Count;
            };
            initialize.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return initialize;

            var goBench = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            choose = ToilMaker.MakeToil("HD_BillGather_Choose");
            choose.initAction = () =>
            {
                if (invalidSelection || scanLimit < 0 || job.targetQueueB == null
                    || job.countQueue == null || job.targetQueueB.Count != job.countQueue.Count
                    || scanLimit > job.targetQueueB.Count || cursor < 0 || cursor > scanLimit)
                { RejectSelection("invalid-cursor"); return; }
                while (cursor < scanLimit)
                {
                    var t = job.targetQueueB[cursor].Thing;
                    // Existing inventory and capacity-limited floor remainders stay in the native contract.
                    if (t != null && t.Spawned && BillGatherContract.UsableTarget(pawn, job, t)
                        && AllowedTake(t, job.countQueue[cursor]) > 0)
                    { job.targetB = t; return; }
                    cursor++;
                }
                job.targetB = LocalTargetInfo.Invalid;
                JumpToToil(goBench);
            };
            choose.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return choose;
            walk = SweepWalk.MakeToil(this, TargetIndex.B, "HD_BillGather_Walk", choose,
                () => cursor++, () => false);
            yield return walk;

            var take = ToilMaker.MakeToil("HD_BillGather_Transfer");
            take.initAction = () =>
            {
                var source = job.targetB.Thing;
                if (cursor >= scanLimit || source != job.targetQueueB[cursor].Thing)
                { RejectSelection("retargeted-source"); return; }
                if (source != null && source.Spawned && BillGatherContract.UsableTarget(pawn, job, source)
                    && ReachabilityImmediate.CanReachImmediate(pawn.Position, source, pawn.Map,
                        PathEndMode.ClosestTouch, pawn))
                    TransferSelected(source, AllowedTake(source, job.countQueue[cursor]));
                // Transfer, remap, tagging and cursor advancement are one simulation action.
                cursor++;
                job.targetB = LocalTargetInfo.Invalid;
            };
            take.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return take;
            yield return Toils_Jump.Jump(choose);
            yield return goBench;

            var handoff = ToilMaker.MakeToil("HD_BillGather_Handoff");
            handoff.initAction = Handoff;
            handoff.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return handoff;
        }

        public override void Notify_PatherFailed()
        {
            if (CurToil == walk && choose != null) { cursor++; JumpToToil(choose); }
            else base.Notify_PatherFailed();
        }

        private int AllowedTake(Thing source, int selected)
        {
            if (Inventory == null || selected <= 0) return 0;
            int count = Math.Min(selected, source.stackCount);
            var settings = HaulersDreamMod.Settings;
            if (settings == null) return 0;
            if (OverloadGate.NoOverload(settings))
                count = Math.Min(count, OverloadGate.CountToPickUp(pawn, source, settings));
            return count;
        }

        private void TransferSelected(Thing source, int requested)
        {
            if (requested <= 0) return;
            var inventory = Inventory;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (inventory == null || comp == null) { invalidSelection = true; return; }
            if (!inventory.CanAcceptAnyOf(source, canMergeWithExistingStacks: false)) return;
            var receipts = new List<BillGatherReceipt<Thing>>();
            var recovery = new CargoSplitRecovery(pawn, source, requested);
            Exception failure = null;
            try
            {
                Thing split = source.SplitOff(requested);
                recovery.Record(split);
                // Merge only into explicitly tracked cargo, preserving personal untagged stack ownership.
                // Snapshot references because callbacks may alter the inventory or tracked set.
                var recipients = new List<Thing>();
                foreach (var t in comp.PeekHashSet()) if (inventory.Contains(t)) recipients.Add(t);
                recipients.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
                foreach (var recipient in recipients)
                {
                    if (split.Destroyed || split.stackCount <= 0) break;
                    if (!inventory.Contains(recipient) || !recipient.CanStackWith(split)
                        || recipient.stackCount >= recipient.def.stackLimit
                        || !pawn.Reserve(recipient, job, errorOnFailed: false)) continue;
                    int before = recipient.stackCount;
                    int splitBefore = split.stackCount;
                    try { recipient.TryAbsorbStack(split, respectStackLimit: true); }
                    finally
                    {
                        int moved = recipient.stackCount - before;
                        int removed = splitBefore - (split.Destroyed ? 0 : split.stackCount);
                        if (moved < 0 || moved != removed || !inventory.Contains(recipient))
                            invalidSelection = true;
                        else if (moved > 0)
                        {
                            // Record physical ownership before callbacks can fail. Even an absorb callback
                            // throwing after a proven move must leave a remapped, recoverable receipt.
                            RecordReceipt(receipts, recipient, moved);
                            try { NotifyMerge.Invoke(inventory, new object[] { recipient, moved }); }
                            finally { comp.RegisterHauledItem(recipient, moved); }
                        }
                    }
                    if (invalidSelection) return;
                }
                if (!split.Destroyed && split.stackCount > 0)
                {
                    int remainder = split.stackCount;
                    bool accepted = false;
                    try { accepted = inventory.TryAdd(split, canMergeWithExistingStacks: false); }
                    finally
                    {
                        // TryAdd calls NotifyAdded after inserting the actual stack. A notification failure
                        // does not undo that insertion, so preserve its real receipt even when TryAdd throws.
                        if (!split.Destroyed && inventory.Contains(split) && split.stackCount == remainder)
                        {
                            RecordReceipt(receipts, split, remainder);
                            comp.RegisterHauledItem(split);
                            split.Position = pawn.Position;
                        }
                        else if (inventory.Contains(split) || split.Destroyed)
                            invalidSelection = true;
                    }
                    if (accepted != inventory.Contains(split))
                        invalidSelection = true; // foreign transfer behavior supplied no proven identity mapping
                }
                if (receipts.Count > 0)
                {
                    comp.NotifyYieldPicked();
                    foreach (var receipt in receipts)
                        if (!pawn.Reserve(receipt.Thing, job, errorOnFailed: false)) invalidSelection = true;
                    HDLog.Dbg($"[BillGather] job {job.loadID} row {cursor}: {requested} requested, "
                        + $"{receipts.Count} verified recipient(s), invalid={invalidSelection}.");
                    if (source.def.ingestible != null && (int)source.def.ingestible.preferability <= 5)
                        pawn.mindState.lastInventoryRawFoodUseTick = Find.TickManager.TicksGame;
                    source.def.soundPickup?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
                }
            }
            catch (Exception error)
            {
                invalidSelection = true;
                failure = error;
            }
            finally
            {
                // The base receipt survives PostSplitOff failures. Do not retry the
                // merge/placement callback that failed or retake the original remainder.
                recovery.Finish(ref failure);
                invalidSelection |= recovery.RecoveredDetached || failure != null;
                loadedAnything |= recovery.HasInventoryCargo;
                if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private void RecordReceipt(List<BillGatherReceipt<Thing>> receipts, Thing recipient, int moved)
        {
            var receipt = new BillGatherReceipt<Thing>(recipient, moved);
            receipts.Add(receipt);
            loadedAnything = true;
            var targets = new List<Thing>(job.targetQueueB.Count);
            foreach (var target in job.targetQueueB) targets.Add(target.Thing);
            // Each receipt consumes the remaining original row. Full transfers replace it; partial transfers
            // append held stock and retain the exact floor shortfall. The sweep cursor stays stable either way.
            if (!BillGatherSelection.TryRemap(targets, job.countQueue, cursor, new[] { receipt }))
                invalidSelection = true;
            else
            {
                job.targetQueueB = new List<LocalTargetInfo>(targets.Count);
                foreach (var target in targets) job.targetQueueB.Add(target);
            }
        }

        private void ReplaceSelection(List<Thing> targets, List<int> counts)
        {
            job.targetQueueB = new List<LocalTargetInfo>(targets.Count);
            foreach (var t in targets) job.targetQueueB.Add(t);
            job.countQueue = counts;
        }

        private void RejectSelection(string reason)
        {
            invalidSelection = true;
            HDLog.Dbg($"[BillGather] job {job.loadID} abandoned: {reason}; native continuation not created.");
            EndJobWith(JobCondition.Incompletable);
        }

        private void Handoff()
        {
            if (handoffStarted) { RejectSelection("replayed-handoff"); return; }
            // Explicitly queued work wins over an automatic continuation. Never clear/replace that queue.
            if (job.playerInterruptedForced || pawn.jobs.jobQueue.Count > 0)
                return;
            if (invalidSelection || !BillGatherContract.BillMayStart(pawn, job)
                || !BillGatherContract.TryValidateSelection(pawn, job, out var targets, out var counts))
            { RejectSelection("handoff-invalid"); return; }
            var native = JobMaker.MakeJob(JobDefOf.DoBill, job.targetA);
            BillGatherContract.CopyContext(job, native);
            native.targetQueueB = new List<LocalTargetInfo>(targets.Count);
            foreach (var t in targets) native.targetQueueB.Add(t);
            native.countQueue = new List<int>(counts);
            // Do not clone the old Job or copy its ID: reservation and scribe identity belong to this new job.
            int oldId = job.loadID;
            var giver = job.jobGiver;
            var tree = job.jobGiverThinkTree;
            var tag = pawn.mindState.lastJobTag;
            handoffStarted = true;
            HDLog.Dbg($"[BillGather] job {oldId} -> native DoBill {native.loadID}, {targets.Count} retained target(s).");
            pawn.jobs.StartJob(native, JobCondition.Succeeded, giver, resumeCurJobAfterwards: false,
                cancelBusyStances: false, thinkTree: tree, tag: tag, canReturnCurJobToPool: true,
                preToilReservationsCanFail: true);
        }
    }
}
