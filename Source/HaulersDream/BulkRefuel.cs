using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	public static class BulkRefuel
	{
		public static bool FeatureEnabled
		{
			get
			{
				HaulersDreamSettings settings = HaulersDreamMod.Settings;
				if (settings != null && settings.enableBulkRefuel)
				{
					return MasterEnable.Active;
				}
				return false;
			}
		}

		public static Job TryGiveBulkRefuelJob(Pawn pawn, Thing refuelable, bool playerOrder)
		{
			if (!FeatureEnabled)
			{
				return null;
			}
			if (!TryPlan(OrdinaryWorkGiver(), pawn, refuelable, playerOrder, out var plan, out var _))
			{
				return null;
			}
			return plan.CreateJob();
		}

		public static bool HasPotentialBulkRefuel(Pawn pawn, Thing refuelable)
		{
			if (!FeatureEnabled || refuelable == null)
			{
				return false;
			}
			CompRefuelable compRefuelable = refuelable.TryGetComp<CompRefuelable>();
			if (compRefuelable != null && compRefuelable.GetType() == typeof(CompRefuelable) && compRefuelable.Props != null && !compRefuelable.Props.atomicFueling && !compRefuelable.IsFull)
			{
				return compRefuelable.GetFuelCountToFullyRefuel() > 0;
			}
			return false;
		}

		internal static WorkGiver_Refuel OrdinaryWorkGiver()
		{
			WorkGiverDef namedSilentFail = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Refuel");
			if (namedSilentFail == null || namedSilentFail.giverClass != typeof(WorkGiver_Refuel))
			{
				return null;
			}
			WorkGiver workGiver = namedSilentFail?.Worker;
			if (workGiver == null || !(workGiver.GetType() == typeof(WorkGiver_Refuel)))
			{
				return null;
			}
			return (WorkGiver_Refuel)workGiver;
		}

		internal static bool TryPlan(WorkGiver_Refuel giver, Pawn pawn, Thing target, bool playerOrder, out BulkRefuelPlan plan, out string refusal, bool includeGround = true)
		{
			plan = null;
			if (!TryReadContext(giver, pawn, target, playerOrder, out var tracking, out var comp, out var filter, out var demand, out refusal))
			{
				return false;
			}
			Pawn_InventoryTracker inventory = pawn.inventory;
			ThingOwner<Thing> innerContainer = inventory.innerContainer;
			if (!InventorySurplusAllocation.TryCreate(pawn, tracking, out var allocation, out refusal))
			{
				return false;
			}
			List<BulkRefuelPlan.Portion> list = new List<BulkRefuelPlan.Portion>();
			int num = 0;
			for (int i = 0; i < allocation.Sources.Count; i++)
			{
				if (num >= demand)
				{
					break;
				}
				Thing thing = allocation.Sources[i];
				if (AllowsOrdinaryFuel(filter, thing) && !thing.IsForbidden(pawn))
				{
					if (!allocation.TryAllocate(thing, demand - num, out var allocatedUnits, out refusal))
					{
						return false;
					}
					if (allocatedUnits > 0)
					{
						list.Add(new BulkRefuelPlan.Portion(thing, allocatedUnits, 0f));
						num = checked(num + allocatedUnits);
					}
				}
			}
			int num2 = demand - num;
			List<BulkRefuelPlan.Portion> list2 = new List<BulkRefuelPlan.Portion>();
			int num3 = 0;
			float mass = 0f;
			float ceiling = 0f;
			bool flag = false;
			RefuelCeCarryBudget budget = null;
			if (num2 > 0 && includeGround)
			{
				IntVec3 position = pawn.Position;
				if (position.InBounds(pawn.Map) && position.GetRegion(pawn.Map) != null)
				{
					List<Thing> list3 = RefuelWorkGiverUtility.FindEnoughReservableThings(pawn, position, new IntRange(1, num2), (Thing source) => AllowsOrdinaryFuel(filter, source));
					if (list3 != null && list3.Count > 0)
					{
						if (num == 0 && list3.Count < 2)
						{
							return Decline("A lone ground stack remains on the native refuel route.", out refusal);
						}
						if (!TryGroundMass(pawn, out mass, out ceiling))
						{
							return Decline("Ground carry inputs are not finite usable values.", out refusal);
						}
						flag = true;
						float num4 = mass;
						for (int num5 = 0; num5 < list3.Count; num5++)
						{
							if (num3 >= num2)
							{
								break;
							}
							Thing thing2 = list3[num5];
							if (!GroundUsable(pawn, thing2, filter) || Contains(list2, thing2))
							{
								continue;
							}
							if (!TryFuelMass(thing2, out var unitMass))
							{
								return Decline("Ground fuel mass is not finite.", out refusal);
							}
							int count = RefuelPlan.TakeFromStack(num2 - num3, ceiling, num4, unitMass, thing2.stackCount);
							if (count <= 0)
							{
								continue;
							}
							if (budget == null && !RefuelCeCarryBudget.TryCreate(pawn, out budget, out refusal))
							{
								return false;
							}
							if (!budget.TryLimitAndAppend(thing2, count, out count, out refusal))
							{
								return false;
							}
							if (count > 0)
							{
								list2.Add(new BulkRefuelPlan.Portion(thing2, count, unitMass));
								num3 = checked(num3 + count);
								num4 += (float)count * unitMass;
								if (float.IsNaN(num4) || float.IsInfinity(num4))
								{
									return Decline("Planned ground mass overflowed.", out refusal);
								}
							}
						}
					}
				}
			}
			if (num == 0 && list2.Count < 2)
			{
				return Decline("No held contribution or multi-stack ground sweep is available.", out refusal);
			}
			if (!TryReadContext(giver, pawn, target, playerOrder, out var tracking2, out var comp2, out var filter2, out var demand2, out refusal))
			{
				return false;
			}
			if (tracking2 != tracking || comp2 != comp || filter2 != filter || demand2 != demand || pawn.inventory != inventory || inventory.innerContainer != innerContainer)
			{
				return Decline("Refuel channel, demand or inventory owner changed during planning.", out refusal);
			}
			for (int num6 = 0; num6 < list.Count; num6++)
			{
				if (!AllowsOrdinaryFuel(filter, list[num6].Source) || list[num6].Source.IsForbidden(pawn))
				{
					return Decline("Held fuel eligibility changed during planning.", out refusal);
				}
			}
			for (int num7 = 0; num7 < list2.Count; num7++)
			{
				BulkRefuelPlan.Portion portion = list2[num7];
				if (!GroundUsable(pawn, portion.Source, filter) || portion.Source.stackCount < portion.Count || !TryFuelMass(portion.Source, out var unitMass2) || unitMass2 != portion.UnitMass)
				{
					return Decline("Ground fuel changed during planning.", out refusal);
				}
			}
			if (flag && (!TryGroundMass(pawn, out var mass2, out var ceiling2) || mass2 != mass || ceiling2 != ceiling))
			{
				return Decline("Carry inputs changed during planning.", out refusal);
			}
			if (budget != null && !budget.TryValidate(out refusal))
			{
				return false;
			}
			if (allocation.Sources.Count > 0)
			{
				if (!allocation.TryAllocate(allocation.Sources[0], 0, out var _, out refusal))
				{
					return false;
				}
			}
			else if (innerContainer.Count != 0 || innerContainer.Owner != inventory || inventory.pawn != pawn)
			{
				return Decline("Empty inventory changed during planning.", out refusal);
			}
			plan = new BulkRefuelPlan(pawn, target, comp, innerContainer, playerOrder, demand, list, num, list2, num3);
			refusal = null;
			return true;
		}

		internal static bool TryReadContext(WorkGiver_Refuel giver, Pawn pawn, Thing target, bool playerOrder, out CompHauledToInventory tracking, out CompRefuelable comp, out ThingFilter filter, out int demand, out string refusal, bool continuing = false)
		{
			tracking = null;
			comp = null;
			filter = null;
			demand = 0;
			if (!FeatureEnabled || !RefuelNativeWitness.Ready || giver == null || giver.GetType() != typeof(WorkGiver_Refuel))
			{
				return Decline("Bulk refuel is disabled or this is not the ordinary work giver.", out refusal);
			}
			if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Map == null || target == null || target.Destroyed || !target.Spawned || target.Map != pawn.Map || !MapGate.HdActiveOnMap(pawn.Map))
			{
				return Decline("Pawn and target must be live on the same enabled map.", out refusal);
			}
			tracking = pawn.GetComp<CompHauledToInventory>();
			if (tracking == null || tracking.HasPendingRefuelRecovery || pawn.inventory?.innerContainer == null || HaulOrderGate.Blocks(pawn) || (!playerOrder && !YieldRouter.IsEligible(pawn)))
			{
				return Decline("Pawn lacks the inventory, capability or automatic eligibility for bulk refuel.", out refusal);
			}
			if (!giver.CanRefuelThing(target))
			{
				return Decline("The ordinary work giver rejects this target.", out refusal);
			}
			comp = target.TryGetComp<CompRefuelable>();
			if (comp == null || comp.GetType() != typeof(CompRefuelable) || comp.Props == null || comp.Props.atomicFueling || HasMedievalFuelStore(target))
			{
				return Decline("This refuel channel requires a separate adapter.", out refusal);
			}
			filter = comp.Props.fuelFilter;
			if (filter == null || comp.IsFull || target.Fogged() || target.IsForbidden(pawn) || target.Faction != pawn.Faction || (comp.FuelPercentOfMax > 0f && !comp.Props.allowRefuelIfNotEmpty) || (!playerOrder && (!comp.allowAutoRefuel || !(continuing ? comp.ShouldAutoRefuelNowIgnoringFuelPct : comp.ShouldAutoRefuelNow))))
			{
				return Decline("The target's refuel conditions are not satisfied.", out refusal);
			}
			if (target.TryGetComp(out CompInteractable comp2) && comp2.Props.cooldownPreventsRefuel && comp2.OnCooldown)
			{
				return Decline("The target cannot be refuelled during its interaction cooldown.", out refusal);
			}
			if (!pawn.CanReserve(target, 1, -1, null, playerOrder) || !pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly))
			{
				return Decline("The target is not reachable and reservable.", out refusal);
			}
			demand = comp.GetFuelCountToFullyRefuel();
			if (demand <= 0)
			{
				return Decline("The target has no positive item demand.", out refusal);
			}
			refusal = null;
			return true;
		}

		internal static bool AllowsOrdinaryFuel(ThingFilter filter, Thing source)
		{
			if (source == null || source.Destroyed || source is Pawn || source.def == null || source.stackCount <= 0 || !filter.Allows(source))
			{
				return false;
			}
			List<DefModExtension> modExtensions = source.def.modExtensions;
			if (modExtensions != null)
			{
				for (int i = 0; i < modExtensions.Count; i++)
				{
					if (IsNamedType(modExtensions[i], "MedievalOverhaul.FuelValueProperty"))
					{
						return false;
					}
				}
			}
			return true;
		}

		private static bool HasMedievalFuelStore(Thing target)
		{
			if (!(target is ThingWithComps thingWithComps))
			{
				return false;
			}
			List<ThingComp> allComps = thingWithComps.AllComps;
			for (int i = 0; i < allComps.Count; i++)
			{
				if (IsNamedType(allComps[i], "MedievalOverhaul.CompStoreFuelThing"))
				{
					return true;
				}
			}
			return false;
		}

		private static bool IsNamedType(object value, string fullName)
		{
			Type type = value?.GetType();
			while (type != null)
			{
				if (type.FullName == fullName)
				{
					return true;
				}
				type = type.BaseType;
			}
			return false;
		}

		private static bool GroundUsable(Pawn pawn, Thing source, ThingFilter filter)
		{
			if (source != null && source.Spawned && source.Map == pawn.Map && !source.Fogged() && !source.IsForbidden(pawn) && AllowsOrdinaryFuel(filter, source) && pawn.CanReserve(source))
			{
				return pawn.CanReach(source, PathEndMode.ClosestTouch, Danger.Deadly);
			}
			return false;
		}

		private static bool Contains(List<BulkRefuelPlan.Portion> portions, Thing source)
		{
			for (int i = 0; i < portions.Count; i++)
			{
				if (portions[i].Source == source)
				{
					return true;
				}
			}
			return false;
		}

		internal static bool TryGroundMass(Pawn pawn, out float mass, out float ceiling)
		{
			HaulersDreamSettings settings = HaulersDreamMod.Settings;
			mass = MassUtility.GearAndInventoryMass(pawn);
			float baseCapKg = CarryMath.EffectiveCapacity(CarryCapacity.Of(pawn), settings.carryLimitFraction);
			ceiling = BulkHaulPolicy.CeilingKg(settings.overloadLevel, OverloadGate.NoOverloadFor(pawn, settings), baseCapKg, OverloadGate.MaxCeilingKg(settings));
			if (!float.IsNaN(mass) && !float.IsInfinity(mass) && !float.IsNaN(ceiling) && !float.IsNegativeInfinity(ceiling))
			{
				return ceiling >= 0f;
			}
			return false;
		}

		internal static bool TryFuelMass(Thing source, out float unitMass)
		{
			unitMass = source.GetStatValue(StatDefOf.Mass);
			if (float.IsNaN(unitMass) || float.IsInfinity(unitMass))
			{
				return false;
			}
			unitMass = Math.Max(0f, unitMass);
			return true;
		}

		private static bool Decline(string reason, out string refusal)
		{
			refusal = reason;
			return false;
		}
	}
}
