using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
	internal sealed class RefuelCeCarryBudget
	{
		private static class Members
		{
			internal static readonly MethodInfo CurrentWeight = Getter("currentWeight");

			internal static readonly MethodInfo CurrentBulk = Getter("currentBulk");

			internal static readonly MethodInfo CapacityWeight = Getter("capacityWeight");

			internal static readonly MethodInfo CapacityBulk = Getter("capacityBulk");

			internal static readonly MethodInfo CanFit = FitMethod();

			internal static readonly FieldInfo BulkStat = BulkField();

			internal static bool Available
			{
				get
				{
					if (CurrentWeight != null && CurrentBulk != null && CapacityWeight != null && CapacityBulk != null && CanFit != null)
					{
						return BulkStat != null;
					}
					return false;
				}
			}

			private static MethodInfo Getter(string name)
			{
				PropertyInfo obj = InventoryType?.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
				MethodInfo methodInfo = obj?.GetGetMethod();
				if (!(obj?.PropertyType == typeof(float)) || !(methodInfo != null) || methodInfo.IsStatic || methodInfo.GetParameters().Length != 0)
				{
					return null;
				}
				return methodInfo;
			}

			private static MethodInfo FitMethod()
			{
				MethodInfo methodInfo = InventoryType?.GetMethod("CanFitInInventory", BindingFlags.Instance | BindingFlags.Public, null, new Type[4]
				{
					typeof(Thing),
					typeof(int).MakeByRefType(),
					typeof(bool),
					typeof(bool)
				}, null);
				if (!(methodInfo != null) || !(methodInfo.ReturnType == typeof(bool)) || methodInfo.IsStatic || !methodInfo.GetParameters()[1].IsOut)
				{
					return null;
				}
				return methodInfo;
			}

			private static FieldInfo BulkField()
			{
				FieldInfo fieldInfo = (InventoryType?.Assembly.GetType("CombatExtended.CE_StatDefOf", throwOnError: false))?.GetField("Bulk", BindingFlags.Static | BindingFlags.Public);
				if (!(fieldInfo != null) || !fieldInfo.IsStatic || !(fieldInfo.FieldType == typeof(StatDef)))
				{
					return null;
				}
				return fieldInfo;
			}
		}

		private readonly struct Totals
		{
			internal readonly float Weight;

			internal readonly float Bulk;

			internal readonly float CapacityWeight;

			internal readonly float CapacityBulk;

			internal Totals(float weight, float bulk, float capacityWeight, float capacityBulk)
			{
				Weight = weight;
				Bulk = bulk;
				CapacityWeight = capacityWeight;
				CapacityBulk = capacityBulk;
			}

			internal bool SameAs(Totals other)
			{
				if (Weight == other.Weight && Bulk == other.Bulk && CapacityWeight == other.CapacityWeight)
				{
					return CapacityBulk == other.CapacityBulk;
				}
				return false;
			}
		}

		private sealed class Candidate
		{
			internal readonly Thing Source;

			private readonly ThingDef def;

			private readonly ThingDef stuff;

			internal readonly int StackCount;

			internal readonly int CanonicalFit;

			internal readonly float UnitWeight;

			internal readonly float UnitBulk;

			internal Candidate(Thing source, ThingDef def, ThingDef stuff, int count, float weight, float bulk, int fit)
			{
				Source = source;
				this.def = def;
				this.stuff = stuff;
				StackCount = count;
				UnitWeight = weight;
				UnitBulk = bulk;
				CanonicalFit = fit;
			}

			internal bool SameAs(Candidate other)
			{
				if (Source == other.Source && def == other.def && stuff == other.stuff && StackCount == other.StackCount && UnitWeight == other.UnitWeight && UnitBulk == other.UnitBulk)
				{
					return CanonicalFit == other.CanonicalFit;
				}
				return false;
			}
		}

		private static readonly Type InventoryType = AccessTools.TypeByName("CombatExtended.CompInventory");

		private readonly Pawn pawn;

		private readonly ThingComp inventory;

		private readonly StatDef bulkStat;

		private readonly Totals initial;

		private readonly List<Candidate> reached = new List<Candidate>();

		private float plannedWeight;

		private float plannedBulk;

		private RefuelCeCarryBudget(Pawn pawn, ThingComp inventory, StatDef bulkStat, Totals totals)
		{
			this.pawn = pawn;
			this.inventory = inventory;
			this.bulkStat = bulkStat;
			initial = totals;
			plannedWeight = totals.Weight;
			plannedBulk = totals.Bulk;
		}

		internal static bool TryCreate(Pawn pawn, out RefuelCeCarryBudget budget, out string refusal)
		{
			budget = null;
			ThingComp thingComp = FindInventory(pawn);
			if (thingComp == null)
			{
				budget = new RefuelCeCarryBudget(pawn, null, null, default(Totals));
				refusal = null;
				return true;
			}
			if (!Members.Available)
			{
				return Decline("CE inventory planning members are unavailable.", out refusal);
			}
			if (!(Members.BulkStat.GetValue(null) is StatDef statDef) || thingComp.parent != pawn)
			{
				return Decline("CE inventory parent or Bulk definition is unavailable.", out refusal);
			}
			if (!TryReadTotals(pawn, thingComp, statDef, out var totals, out refusal))
			{
				return false;
			}
			budget = new RefuelCeCarryBudget(pawn, thingComp, statDef, totals);
			return true;
		}

		internal static bool TryRefreshForDriver(Pawn pawn, out string refusal)
		{
			ThingComp thingComp = FindInventory(pawn);
			if (thingComp == null)
			{
				refusal = null;
				return true;
			}
			MethodInfo method = InventoryType.GetMethod("UpdateInventory", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
			if (method == null || method.IsStatic || method.ReturnType != typeof(void))
			{
				return Decline("The pawn's CE inventory refresh method is unavailable.", out refusal);
			}
			method.Invoke(thingComp, null);
			refusal = null;
			return true;
		}

		internal bool TryLimitAndAppend(Thing source, int requested, out int count, out string refusal)
		{
			count = 0;
			if (requested < 0)
			{
				return Decline("A CE ground request cannot be negative.", out refusal);
			}
			if (inventory == null)
			{
				count = requested;
				refusal = null;
				return true;
			}
			if (!TryReadCandidate(source, out var candidate, out refusal) || !TryReadTotals(pawn, inventory, bulkStat, out var totals, out refusal))
			{
				return false;
			}
			if (!initial.SameAs(totals))
			{
				return Decline("CE carry totals changed during ground planning.", out refusal);
			}
			int num = Math.Min(requested, Math.Min(candidate.StackCount, candidate.CanonicalFit));
			float a = ((candidate.UnitWeight <= 0f) ? ((float)candidate.StackCount) : ((initial.CapacityWeight - plannedWeight + 0f) / candidate.UnitWeight));
			float num2 = Mathf.Min((candidate.UnitBulk <= 0f) ? ((float)candidate.StackCount) : ((initial.CapacityBulk - plannedBulk + 0f) / candidate.UnitBulk), Mathf.Min(a, candidate.StackCount));
			if (float.IsNaN(num2))
			{
				return Decline("CE virtual item fit is not a number.", out refusal);
			}
			int num3 = ((num > 0 && !(num2 <= 0f)) ? (((double)num2 >= (double)num) ? num : Math.Max(0, Mathf.FloorToInt(num2))) : 0);
			float value = plannedWeight + candidate.UnitWeight * (float)num3;
			float value2 = plannedBulk + candidate.UnitBulk * (float)num3;
			if (!Finite(value) || !Finite(value2))
			{
				return Decline("CE planned inventory totals overflowed.", out refusal);
			}
			reached.Add(candidate);
			plannedWeight = value;
			plannedBulk = value2;
			count = num3;
			refusal = null;
			return true;
		}

		internal bool TryValidate(out string refusal)
		{
			if (FindInventory(pawn) != inventory)
			{
				return Decline("The pawn's CE inventory comp changed during planning.", out refusal);
			}
			if (inventory == null)
			{
				refusal = null;
				return true;
			}
			for (int i = 0; i < reached.Count; i++)
			{
				Candidate candidate = reached[i];
				if (!TryReadCandidate(candidate.Source, out var candidate2, out refusal))
				{
					return false;
				}
				if (!candidate.SameAs(candidate2))
				{
					return Decline("Reached CE ground fit inputs changed during planning.", out refusal);
				}
			}
			if (!TryReadTotals(pawn, inventory, bulkStat, out var totals, out refusal))
			{
				return false;
			}
			if (!initial.SameAs(totals))
			{
				return Decline("CE carry totals changed before publishing the refuel plan.", out refusal);
			}
			refusal = null;
			return true;
		}

		private bool TryReadCandidate(Thing source, out Candidate candidate, out string refusal)
		{
			candidate = null;
			if (source == null || source.Destroyed || source.def == null || source.stackCount <= 0)
			{
				return Decline("CE ground source is no longer a live positive stack.", out refusal);
			}
			ThingDef def = source.def;
			ThingDef stuff = source.Stuff;
			int stackCount = source.stackCount;
			float statValue = source.GetStatValue(StatDefOf.Mass);
			float statValue2 = source.GetStatValue(bulkStat);
			if (!Finite(statValue) || !Finite(statValue2))
			{
				return Decline("CE ground Mass or Bulk is not finite.", out refusal);
			}
			object[] array = new object[4] { source, 0, false, false };
			Members.CanFit.Invoke(inventory, array);
			int fit = Math.Max(0, (int)array[1]);
			float statValue3 = source.GetStatValue(StatDefOf.Mass);
			float statValue4 = source.GetStatValue(bulkStat);
			if (source.Destroyed || source.def != def || source.Stuff != stuff || source.stackCount != stackCount || statValue3 != statValue || statValue4 != statValue2)
			{
				return Decline("CE ground source changed while reading its native fit.", out refusal);
			}
			candidate = new Candidate(source, def, stuff, stackCount, statValue, statValue2, fit);
			refusal = null;
			return true;
		}

		private static bool TryReadTotals(Pawn pawn, ThingComp inventory, StatDef bulkStat, out Totals totals, out string refusal)
		{
			totals = default(Totals);
			if (FindInventory(pawn) != inventory || inventory.parent != pawn || Members.BulkStat.GetValue(null) != bulkStat)
			{
				return Decline("CE inventory identity or Bulk definition changed during planning.", out refusal);
			}
			float num = (float)Members.CurrentWeight.Invoke(inventory, null);
			float num2 = (float)Members.CurrentBulk.Invoke(inventory, null);
			float num3 = (float)Members.CapacityWeight.Invoke(inventory, null);
			float num4 = (float)Members.CapacityBulk.Invoke(inventory, null);
			if (!Finite(num) || !Finite(num2) || !Finite(num3) || !Finite(num4))
			{
				return Decline("CE current or capacity totals are not finite.", out refusal);
			}
			totals = new Totals(num, num2, num3, num4);
			refusal = null;
			return true;
		}

		private static ThingComp FindInventory(Pawn pawn)
		{
			if (InventoryType == null)
			{
				return null;
			}
			List<ThingComp> list = pawn?.AllComps;
			if (list == null)
			{
				return null;
			}
			for (int i = 0; i < list.Count; i++)
			{
				if (InventoryType.IsInstanceOfType(list[i]))
				{
					return list[i];
				}
			}
			return null;
		}

		private static bool Finite(float value)
		{
			if (!float.IsNaN(value))
			{
				return !float.IsInfinity(value);
			}
			return false;
		}

		private static bool Decline(string reason, out string refusal)
		{
			refusal = reason;
			return false;
		}
	}
}
