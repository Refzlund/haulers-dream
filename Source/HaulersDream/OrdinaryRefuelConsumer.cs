using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	internal static class OrdinaryRefuelConsumer
	{
		internal static bool TryReadLive(Pawn pawn, Job job, Thing target, out CompRefuelable comp, out CompHauledToInventory tracking, out ThingFilter filter, out int demand)
		{
			comp = null;
			tracking = null;
			filter = null;
			demand = 0;
			string refusal;
			if (pawn != null && job != null && pawn.CurJob == job)
			{
				return BulkRefuel.TryReadContext(BulkRefuel.OrdinaryWorkGiver(), pawn, target, job.playerForced, out tracking, out comp, out filter, out demand, out refusal, continuing: true);
			}
			return false;
		}

        private static bool TryReadLive(RefuelActivation activation, Thing target,
            out CompRefuelable comp, out CompHauledToInventory tracking,
            out ThingFilter filter, out int demand)
        {
            comp = null;
            tracking = null;
            filter = null;
            demand = 0;
            if (!activation.IntentCurrent || !ReferenceEquals(target, activation.TargetA.Thing)) return false;
            bool permitted = TryReadLive(activation.Pawn, activation.Job, target,
                out comp, out tracking, out filter, out demand);
            return activation.IntentCurrent && permitted;
        }

        internal static void Deposit(RefuelActivation activation, Thing target)
        {
            Pawn pawn = activation.Pawn;
            Job job = activation.Job;
			if (!TryReadLive(activation, target, out var _, out var _, out var _, out var demand) || !CanTouchReserved(pawn, job, target) || !activation.IntentCurrent)
			{
				return;
			}
			ThingOwner<Thing> innerContainer = pawn.inventory.innerContainer;
			List<Thing> list = new List<Thing>(innerContainer.Count);
			for (int i = 0; i < innerContainer.Count; i++)
			{
				list.Add(innerContainer[i]);
			}
			for (int j = 0; j < list.Count; j++)
			{
				if (!TryReadLive(activation, target, out var comp2, out var tracking2, out var filter2, out var demand2))
				{
					break;
				}
				if (!CanTouchReserved(pawn, job, target) || !activation.IntentCurrent)
				{
					break;
				}
				Thing thing = list[j];
				if (!RefuelAttempt.IsMember(pawn.inventory.innerContainer, thing) || !BulkRefuel.AllowsOrdinaryFuel(filter2, thing) || thing.IsForbidden(pawn))
				{
					continue;
				}
				if (!InventorySurplusAllocation.TryCreate(pawn, tracking2, out var allocation, out var invalidReason) || !allocation.TryAllocate(thing, demand2, out var allocatedUnits, out invalidReason))
				{
					break;
				}
				if (allocatedUnits <= 0)
				{
					continue;
				}
				if (!allocation.TryAllocate(thing, 0, out demand, out invalidReason))
				{
					break;
				}
                if (!activation.IntentCurrent) break;
                RefuelAttempt refuelAttempt = RefuelAttempt.Begin(activation, comp2, thing, allocatedUnits);
				Exception primary = null;
				bool flag = false;
				try
				{
					Thing thing2 = refuelAttempt.PrepareOwnedPortion();
					if (TryReadLive(activation, target, out var comp3, out var tracking3, out var filter3, out var demand3) && comp3 == comp2 && CanTouchReserved(pawn, job, target) && BulkRefuel.AllowsOrdinaryFuel(filter3, thing2) && !thing2.IsForbidden(pawn) && InventorySurplusAllocation.TryCreate(pawn, tracking3, out var allocation2, out invalidReason) && allocation2.TryAllocate(thing2, demand3, out var allocatedUnits2, out invalidReason) && allocatedUnits2 == thing2.stackCount && allocatedUnits2 > 0 && allocatedUnits2 <= allocatedUnits && allocation2.TryAllocate(thing2, 0, out demand, out invalidReason) && activation.IntentCurrent)
					{
						refuelAttempt.InvokeNativeRefuel();
						flag = true;
					}
				}
				catch (Exception ex)
				{
					primary = ex;
				}
				refuelAttempt.FinishAndRethrow(primary);
				if (!activation.IntentCurrent || !flag)
				{
					break;
				}
			}
		}

        internal static int Pickup(RefuelActivation activation, Thing target, Thing source, int planned)
        {
            Pawn pawn = activation.Pawn;
            Job job = activation.Job;
			if (planned <= 0 || !TryReadLive(activation, target, out var comp, out var tracking, out var filter, out var demand) || !GroundUsable(pawn, job, source, filter) || !activation.IntentCurrent)
			{
				return 0;
			}
			if (!RefuelCeCarryBudget.TryRefreshForDriver(pawn, out var refusal))
			{
				throw new InvalidOperationException(refusal);
			}
			Pawn_InventoryTracker inventory = pawn.inventory;
			ThingOwner<Thing> innerContainer = inventory.innerContainer;
			if (!InventorySurplusAllocation.TryCreate(pawn, tracking, out var allocation, out var invalidReason))
			{
				return 0;
			}
			int num = 0;
			for (int i = 0; i < allocation.Sources.Count; i++)
			{
				if (num >= demand)
				{
					break;
				}
				Thing thing = allocation.Sources[i];
				if (BulkRefuel.AllowsOrdinaryFuel(filter, thing) && !thing.IsForbidden(pawn))
				{
					if (!allocation.TryAllocate(thing, demand - num, out var allocatedUnits, out invalidReason))
					{
						return 0;
					}
					num = checked(num + allocatedUnits);
				}
			}
			int num2 = demand - num;
			if (num2 <= 0 || !BulkRefuel.TryGroundMass(pawn, out var mass, out var ceiling) || !BulkRefuel.TryFuelMass(source, out var unitMass))
			{
				return 0;
			}
			int count = RefuelPlan.TakeFromStack(Math.Min(planned, num2), ceiling, mass, unitMass, source.stackCount);
			if (count <= 0)
			{
				return 0;
			}
			if (!RefuelCeCarryBudget.TryCreate(pawn, out var budget, out invalidReason) || !budget.TryLimitAndAppend(source, count, out count, out invalidReason) || count <= 0 || !budget.TryValidate(out invalidReason))
			{
				return 0;
			}
			if (!TryReadLive(activation, target, out var comp2, out var _, out var filter2, out var demand2) || comp2 != comp || filter2 != filter || demand2 != demand || !GroundUsable(pawn, job, source, filter) || !activation.IntentCurrent)
			{
				return 0;
			}
			if (pawn.inventory != inventory || inventory.innerContainer != innerContainer || !BulkRefuel.TryGroundMass(pawn, out var mass2, out var ceiling2) || mass2 != mass || ceiling2 != ceiling || !BulkRefuel.TryFuelMass(source, out var unitMass2) || unitMass2 != unitMass || !budget.TryValidate(out invalidReason))
			{
				return 0;
			}
			if (allocation.Sources.Count > 0 && !allocation.TryAllocate(allocation.Sources[0], 0, out var _, out invalidReason))
			{
				return 0;
			}
			if (allocation.Sources.Count == 0 && pawn.inventory.innerContainer.Count != 0)
			{
				return 0;
			}
            if (!activation.IntentCurrent) return 0;
            RefuelAttempt refuelAttempt = RefuelAttempt.Begin(activation, comp, source, count, pickupOnly: true);
			Exception primary = null;
			int result = 0;
			try
			{
				Thing thing2 = refuelAttempt.PrepareOwnedPortion();
				if (!RefuelAttempt.IsMember(pawn.inventory.innerContainer, thing2) || thing2.stackCount != count)
				{
					throw new InvalidOperationException("Refuel pickup did not retain the selected quantity.");
				}
				result = count;
			}
			catch (Exception ex)
			{
				primary = ex;
			}
			refuelAttempt.FinishAndRethrow(primary);
			return activation.IntentCurrent ? result : 0;
		}

		private static bool GroundUsable(Pawn pawn, Job job, Thing source, ThingFilter filter)
		{
			if (source != null && source.Spawned && source.Map == pawn.Map && !source.IsForbidden(pawn) && !source.Fogged() && BulkRefuel.AllowsOrdinaryFuel(filter, source) && pawn.Map.reservationManager.ReservedBy(source, pawn, job))
			{
				return ReachabilityImmediate.CanReachImmediate(pawn.Position, source, pawn.Map, PathEndMode.ClosestTouch, pawn);
			}
			return false;
		}

		private static bool CanTouchReserved(Pawn pawn, Job job, Thing target)
		{
			if (pawn.Map.reservationManager.ReservedBy(target, pawn, job))
			{
				return ReachabilityImmediate.CanReachImmediate(pawn.Position, target, pawn.Map, PathEndMode.Touch, pawn);
			}
			return false;
		}
	}
}
