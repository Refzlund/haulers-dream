using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	public class FloatMenuOptionProvider_BulkRefuel : FloatMenuOptionProvider
	{
		public override bool Drafted
		{
			get
			{
				return true;
			}
		}

		public override bool Undrafted
		{
			get
			{
				return true;
			}
		}

		public override bool Multiselect
		{
			get
			{
				return false;
			}
		}

		public override bool MechanoidCanDo
		{
			get
			{
				return false;
			}
		}

		public override bool CanSelfTarget
		{
			get
			{
				return false;
			}
		}

		public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
		{
			Pawn pawn = context?.FirstSelectedPawn;
			List<Thing> list = context?.ClickedThings;
			if (pawn == null || list == null || !BulkRefuel.FeatureEnabled)
			{
				yield break;
			}
			WorkGiver_Refuel workGiver_Refuel = BulkRefuel.OrdinaryWorkGiver();
			if (workGiver_Refuel == null)
			{
				yield break;
			}
			for (int i = 0; i < list.Count; i++)
			{
				Thing thing = list[i];
				if ((pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_BulkRefuel && pawn.CurJob.GetTarget(TargetIndex.A).Thing == thing) || !BulkRefuel.TryPlan(workGiver_Refuel, pawn, thing, playerOrder: true, out var _, out var _))
				{
					continue;
				}
				Pawn pawnLocal = pawn;
				Thing clickedLocal = thing;
				FloatMenuOption option = new FloatMenuOption("HaulersDream.BulkRefuel.Option".Translate(thing.LabelShort), delegate
				{
					Job job = BulkRefuel.TryGiveBulkRefuelJob(pawnLocal, clickedLocal, playerOrder: true);
					if (job == null)
					{
						Messages.Message("HaulersDream.BulkRefuel.CouldNotStart".Translate(), clickedLocal, MessageTypeDefOf.RejectInput, historical: false);
					}
					else
					{
						pawnLocal.jobs.TryTakeOrderedJob(job, JobTag.Misc);
					}
				})
				{
					iconThing = thing
				};
				yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, thing);
				break;
			}
		}
	}
}
