using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
	internal static class RefuelNativeWitness
	{
		[ThreadStatic]
		internal static RefuelAttempt Active;

		internal static bool ListAnchored;

		internal static bool FloatAnchored;

		internal static bool SplitAnchored;

		internal static readonly MethodInfo ListMethod = AccessTools.Method(typeof(CompRefuelable), "Refuel", new Type[1] { typeof(List<Thing>) });

		internal static readonly MethodInfo FloatMethod = AccessTools.Method(typeof(CompRefuelable), "Refuel", new Type[1] { typeof(float) });

		internal static readonly MethodInfo SplitMethod = AccessTools.Method(typeof(Thing), "SplitOff", new Type[1] { typeof(int) });

		internal static readonly MethodInfo DestroyMethod = AccessTools.Method(typeof(Thing), "Destroy", new Type[1] { typeof(DestroyMode) });

		internal static bool Ready
		{
			get
			{
				if (ListAnchored && FloatAnchored && SplitAnchored && Owns(ListMethod, typeof(Patch_RefuelListWitness), "Transpiler", 0) && Owns(FloatMethod, typeof(Patch_RefuelFloatWitness), "Transpiler", 0) && Owns(FloatMethod, typeof(Patch_RefuelFloatWitness), "Prefix", 1) && Owns(FloatMethod, typeof(Patch_RefuelFloatWitness), "Finalizer", 2))
				{
					return Owns(SplitMethod, typeof(Patch_RefuelSplitWitness), "Transpiler", 0);
				}
				return false;
			}
		}

		private static bool Owns(MethodInfo target, Type owner, string name, int kind)
		{
			if (target == null)
			{
				return false;
			}
			Patches patchInfo = Harmony.GetPatchInfo(target);
			if (patchInfo == null)
			{
				return false;
			}
			ReadOnlyCollection<Patch> readOnlyCollection;
			switch (kind)
			{
			default:
				readOnlyCollection = patchInfo.Finalizers;
				break;
			case 1:
				readOnlyCollection = patchInfo.Prefixes;
				break;
			case 0:
				readOnlyCollection = patchInfo.Transpilers;
				break;
			}
			MethodInfo methodInfo = AccessTools.Method(owner, name);
			foreach (Patch item in readOnlyCollection)
			{
				if (item.owner == "giwaffed.HaulersDream" && item.PatchMethod == methodInfo)
				{
					return true;
				}
			}
			return false;
		}

		internal static void Credit(CompRefuelable target, float amount, Thing source, int selected)
		{
			RefuelAttempt active = Active;
			if (active == null || target != active.Target)
			{
				target.Refuel(amount);
				return;
			}
			if (target != active.Target || source != active.Prepared || !active.InvokingList || active.CreditCallStarted || selected <= 0 || selected > active.Bound || selected > source.stackCount)
			{
				active.Ambiguous = true;
				throw new InvalidOperationException("Unexpected ordinary refuel credit call.");
			}
			active.CreditCallStarted = true;
			active.Selected = selected;
			active.ExpectedFloat = true;
			try
			{
				target.Refuel(amount);
			}
			finally
			{
				active.ExpectedFloat = false;
			}
		}

		internal static Thing Split(Thing source, int count)
		{
			RefuelAttempt active = Active;
			if (active == null || source != active.Prepared)
			{
				return source.SplitOff(count);
			}
			if (!active.InvokingList || source != active.Prepared || count != active.Selected || !active.CreditObserved || active.Ambiguous || active.ConsumptionSplit != null)
			{
				active.Ambiguous = true;
				throw new InvalidOperationException("Unexpected ordinary refuel consumption split.");
			}
			return active.RunSplit(active.ConsumptionSplit = new RefuelSplit(source, count));
		}

		internal static void Destroy(Thing item, DestroyMode mode)
		{
			RefuelAttempt active = Active;
			if (active == null || item != active.ConsumptionSplit?.Piece)
			{
				item.Destroy(mode);
				return;
			}
			if (!active.InvokingList || active.ConsumptionSplit == null || item != active.ConsumptionSplit.Piece || !active.ConsumptionSplit.HasFundedPiece || !active.CreditObserved || active.Ambiguous)
			{
				active.Ambiguous = true;
				throw new InvalidOperationException("Unexpected ordinary refuel destruction.");
			}
			active.DestroyOnce(item, mode);
		}

		internal static void Created(Thing source, Thing piece)
		{
			RefuelSplit refuelSplit = Active?.SplitWindow;
			if (refuelSplit == null || refuelSplit.Source != source)
			{
				return;
			}
			if (refuelSplit.Piece != null && refuelSplit.Piece != source)
			{
				Active.Ambiguous = true;
				return;
			}
			if (refuelSplit.Piece != null || source.stackCount != refuelSplit.BeforeCount)
			{
				Active.Ambiguous = true;
			}
			refuelSplit.Piece = piece;
			refuelSplit.NativeBeforeCount = source.stackCount;
		}

		internal static void Debited(Thing source, Thing piece)
		{
			RefuelSplit refuelSplit = Active?.SplitWindow;
			if (refuelSplit != null && refuelSplit.Source == source)
			{
				if (refuelSplit.Piece != piece || source.stackCount != refuelSplit.NativeBeforeCount - refuelSplit.Requested || piece.stackCount != refuelSplit.Requested)
				{
					Active.Ambiguous = true;
				}
				else
				{
					refuelSplit.DebitObserved = true;
				}
			}
		}

		internal static void FuelWritten(CompRefuelable target, float actualArgument, float actualFuel)
		{
			RefuelAttempt active = Active;
			if (active != null && active.Target == target)
			{
				if (!active.ExpectedFloat || active.FloatDepth != 1 || !active.CreditCallStarted || active.CreditObserved || actualArgument != (float)active.Selected || float.IsNaN(actualFuel) || float.IsInfinity(actualFuel))
				{
					active.Ambiguous = true;
				}
				else
				{
					active.CreditObserved = true;
				}
			}
		}

		internal static bool Calls(CodeInstruction code, MethodInfo method)
		{
			if (code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt)
			{
				return object.Equals(code.operand, method);
			}
			return false;
		}

		internal static int UniqueCall(List<CodeInstruction> code, MethodInfo method)
		{
			int num = -1;
			for (int i = 0; i < code.Count; i++)
			{
				if (Calls(code[i], method))
				{
					if (num >= 0)
					{
						throw new InvalidOperationException("Duplicate refuel IL call anchor.");
					}
					num = i;
				}
			}
			if (num < 0)
			{
				throw new InvalidOperationException("Missing refuel IL call anchor.");
			}
			return num;
		}

		internal static CodeInstruction Call(string name)
		{
			return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RefuelNativeWitness), name));
		}

		internal static void MoveEntry(CodeInstruction from, CodeInstruction to)
		{
			to.labels.AddRange(from.labels);
			from.labels.Clear();
			to.blocks.AddRange(from.blocks);
			from.blocks.Clear();
		}
	}
}
