using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
	[HarmonyPatch(typeof(Thing), "SplitOff", new Type[] { typeof(int) })]
	internal static class Patch_RefuelSplitWitness
	{
		[HarmonyPriority(0)]
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			RefuelNativeWitness.SplitAnchored = false;
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			MethodInfo method = AccessTools.Method(typeof(ThingMaker), "MakeThing", new Type[2]
			{
				typeof(ThingDef),
				typeof(ThingDef)
			});
			int num = RefuelNativeWitness.UniqueCall(list, method);
			FieldInfo stackCount = AccessTools.Field(typeof(Thing), "stackCount");
			int assignment = num + 2;
			// CommonSense clears the new Thing's ingredients after MakeThing/store,
			// before vanilla assigns its quantity. Keep that callback in place, and
			// still require the original same-local assignment and source debit.
			if (HasIngredientCleanup(list, assignment))
				assignment += 2;
			if (num < 0 || assignment + 8 >= list.Count || list[num + 1].opcode != OpCodes.Stloc_0
				|| list[assignment].opcode != OpCodes.Ldloc_0
				|| list[assignment + 1].opcode != OpCodes.Ldarg_1
				|| list[assignment + 2].opcode != OpCodes.Stfld || !object.Equals(list[assignment + 2].operand, stackCount)
				|| list[assignment + 3].opcode != OpCodes.Ldarg_0
				|| list[assignment + 4].opcode != OpCodes.Ldarg_0
				|| list[assignment + 5].opcode != OpCodes.Ldfld || !object.Equals(list[assignment + 5].operand, stackCount)
				|| list[assignment + 6].opcode != OpCodes.Ldarg_1
				|| list[assignment + 7].opcode != OpCodes.Sub
				|| list[assignment + 8].opcode != OpCodes.Stfld || !object.Equals(list[assignment + 8].operand, stackCount))
			{
				return RefuelNativeWitness.UnsupportedBody(list, "Thing.SplitOff(int)");
			}
			// No branch or exception boundary may enter the custody window after
			// creation, skip either witness, or change the proven straight-line debit.
			for (int i = num + 1; i <= assignment + 8; i++)
				if (list[i].labels.Count != 0 || list[i].blocks.Count != 0)
					return RefuelNativeWitness.UnsupportedBody(list, "Thing.SplitOff(int)");
			list.InsertRange(assignment + 9, new CodeInstruction[3]
			{
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldloc_0),
				RefuelNativeWitness.Call("Debited")
			});
			// Observe immediately after MakeThing/store, before any provider code.
			// If cleanup throws, RunSplit retains the actual unfunded piece and has
			// no debit observation; existing ambiguous settlement remains in charge.
			list.InsertRange(num + 2, new CodeInstruction[3]
			{
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldloc_0),
				RefuelNativeWitness.Call("Created")
			});
			RefuelNativeWitness.SplitAnchored = true;
			return list;
		}

		private static bool HasIngredientCleanup(List<CodeInstruction> code, int at)
		{
			if (at + 1 >= code.Count || code[at].opcode != OpCodes.Ldloc_0
				|| code[at + 1].opcode != OpCodes.Call || !(code[at + 1].operand is MethodInfo cleanup))
				return false;
			// This is the one supported provider insertion, not permission for an
			// arbitrary callback or rewritten split. There is no hard CS dependency.
			ParameterInfo[] parameters = cleanup.GetParameters();
			return cleanup.DeclaringType?.FullName == "CommonSense.Thing_SplitOff_CommonSensePatch"
				&& cleanup.DeclaringType.Assembly.GetName().Name == "CommonSense"
				&& cleanup.Name == "ClearIngs" && cleanup.IsStatic && !cleanup.IsGenericMethod
				&& cleanup.ReturnType == typeof(void) && parameters.Length == 1
				&& parameters[0].ParameterType == typeof(Thing);
		}
	}
}
