using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	public class JobDriver_BulkRefuel : JobDriver
	{
		private const TargetIndex RefuelableInd = TargetIndex.A;

		private const TargetIndex FuelInd = TargetIndex.B;

		private int loadIndex;

		private Toil sweepGotoToil;

		private Toil sweepDecideToil;

		private Thing Refuelable => job.GetTarget(TargetIndex.A).Thing;

		private CompRefuelable Comp => Refuelable?.TryGetComp<CompRefuelable>();

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref loadIndex, "hdBrefLoadIndex", 0);
		}

		public override string GetReport()
		{
			return "HaulersDream.BulkRefuel.Report".Translate();
		}

		public override void Notify_PatherFailed()
		{
			if (base.CurToil != sweepGotoToil)
			{
				base.Notify_PatherFailed();
				return;
			}
			loadIndex++;
			JumpToToil(sweepDecideToil);
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			if (RefuelNativeWitness.Ready)
			{
				CompHauledToInventory comp = pawn.GetComp<CompHauledToInventory>();
				if (comp != null && !comp.HasPendingRefuelRecovery && pawn.Reserve(Refuelable, job, 1, -1, null, errorOnFailed))
				{
					List<LocalTargetInfo> targetQueue = job.GetTargetQueue(TargetIndex.B);
					if (targetQueue != null && targetQueue.Count > 0)
					{
						pawn.ReserveAsManyAsPossible(targetQueue, job);
					}
					return true;
				}
			}
			return false;
		}

        private void RunRefuelAction(Action<RefuelActivation> action)
        {
            var activation = new RefuelActivation(this);
            if (!activation.IntentCurrent) return;
            try { action(activation); }
            catch (Exception failure) { activation.HandleFailure(failure); }
        }

		public override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
			AddEndCondition(() => (Comp == null || !Comp.IsFull) ? JobCondition.Ongoing : JobCondition.Succeeded);
			this.FailOn(() => Comp == null || (!Comp.IsFull && !OrdinaryRefuelConsumer.TryReadLive(pawn, job, Refuelable, out var _, out var _, out var _, out var _)));
			Toil depositStart = Toils_General.Label();
			Toil sweepDecide = ToilMaker.MakeToil("HD_Bref_SweepDecide");
			sweepDecideToil = sweepDecide;
			sweepDecide.initAction = delegate
			{
				List<LocalTargetInfo> targetQueueB = job.targetQueueB;
				List<int> countQueue = job.countQueue;
				if (targetQueueB == null || loadIndex >= targetQueueB.Count)
				{
					JumpToToil(depositStart);
				}
				else
				{
					while (loadIndex < targetQueueB.Count)
					{
						Thing thing = targetQueueB[loadIndex].Thing;
						bool flag = thing != null && thing.Spawned && thing.Map == pawn.Map && !thing.IsForbidden(pawn) && countQueue != null && loadIndex < countQueue.Count && countQueue[loadIndex] > 0;
						if (flag && !pawn.Map.reservationManager.ReservedBy(thing, pawn, job) && (!pawn.CanReserve(thing) || !pawn.Reserve(thing, job, 1, -1, null, errorOnFailed: false)))
						{
							flag = false;
						}
						if (flag)
						{
							break;
						}
						loadIndex++;
					}
					if (loadIndex >= targetQueueB.Count)
					{
						JumpToToil(depositStart);
					}
					else
					{
						job.SetTarget(TargetIndex.B, targetQueueB[loadIndex].Thing);
					}
				}
			};
			sweepDecide.defaultCompleteMode = ToilCompleteMode.Instant;
			yield return sweepDecide;
			yield return sweepGotoToil = SweepWalk.MakeToil(this, TargetIndex.B, "HD_Bref_SweepGoto", sweepDecide, delegate
			{
				loadIndex++;
			}, () => false);
			yield return PickupPause.MakeToil(TargetIndex.B, PickupDelayContext.AutoHaul);
			Toil toil = ToilMaker.MakeToil("HD_Bref_SweepTake");
            toil.initAction = () => RunRefuelAction(activation =>
            {
                int selectedIndex = loadIndex;
                Thing source = activation.Job.GetTarget(TargetIndex.B).Thing;
                List<int> counts = activation.Job.countQueue;
                List<LocalTargetInfo> targets = activation.Job.targetQueueB;
                if (selectedIndex < 0 || targets == null || selectedIndex >= targets.Count) return;
                LocalTargetInfo selectedTarget = targets[selectedIndex];
                if (!ReferenceEquals(selectedTarget.Thing, source)) return;
                int planned = counts != null && selectedIndex < counts.Count ? counts[selectedIndex] : 0;
                int taken = OrdinaryRefuelConsumer.Pickup(activation, Refuelable, source, planned);
                if (!activation.IntentCurrent) return;
                // A returning callback may also redirect this same driver's queue.
                // Never apply the old invocation's debit or jump to newer work.
                if (loadIndex != selectedIndex || !ReferenceEquals(activation.Job.countQueue, counts)
                    || !ReferenceEquals(activation.Job.targetQueueB, targets)
                    || selectedIndex >= targets.Count || targets[selectedIndex] != selectedTarget
                    || (counts != null && (selectedIndex >= counts.Count || counts[selectedIndex] != planned))
                    || !ReferenceEquals(activation.Job.GetTarget(TargetIndex.B).Thing, source)) return;
                if (counts != null && selectedIndex < counts.Count)
                    counts[selectedIndex] = Math.Max(0, planned - taken);
                loadIndex++;
                JumpToToil(sweepDecide);
            });
			toil.defaultCompleteMode = ToilCompleteMode.Instant;
			yield return toil;
			yield return depositStart;
			Toil toil2 = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
			toil2.FailOnDespawnedNullOrForbidden(TargetIndex.A);
			yield return toil2;
			Toil toil3 = ToilMaker.MakeToil("HD_Bref_Deposit");
            toil3.initAction = () => RunRefuelAction(activation =>
                OrdinaryRefuelConsumer.Deposit(activation, Refuelable));
			toil3.defaultCompleteMode = ToilCompleteMode.Instant;
			yield return toil3;
		}
	}
}
