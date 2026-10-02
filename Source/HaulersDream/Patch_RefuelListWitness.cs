using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
	[HarmonyPatch(typeof(CompRefuelable), "Refuel", new Type[] { typeof(List<Thing>) })]
	internal static class Patch_RefuelListWitness
	{
		[HarmonyPriority(0)]
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			RefuelNativeWitness.ListAnchored = false;
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			int num = RefuelNativeWitness.UniqueCall(list, RefuelNativeWitness.FloatMethod);
			int num2 = RefuelNativeWitness.UniqueCall(list, RefuelNativeWitness.SplitMethod);
			int num3 = RefuelNativeWitness.UniqueCall(list, RefuelNativeWitness.DestroyMethod);
			if (num < 3 || num + 5 != num3 || num + 3 != num2 || list[num - 3].opcode != OpCodes.Ldarg_0 || list[num - 2].opcode != OpCodes.Ldloc_2 || list[num - 1].opcode != OpCodes.Conv_R4 || list[num + 1].opcode != OpCodes.Ldloc_1 || list[num + 2].opcode != OpCodes.Ldloc_2 || list[num3 - 1].opcode != OpCodes.Ldc_I4_0)
			{
				return RefuelNativeWitness.UnsupportedBody(list, "CompRefuelable.Refuel(List<Thing>)");
			}
			list[num2].opcode = OpCodes.Call;
			list[num2].operand = AccessTools.Method(typeof(RefuelNativeWitness), "Split");
			list[num3].opcode = OpCodes.Call;
			list[num3].operand = AccessTools.Method(typeof(RefuelNativeWitness), "Destroy");
			CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldloc_1);
			RefuelNativeWitness.MoveEntry(list[num], codeInstruction);
			list[num].opcode = OpCodes.Call;
			list[num].operand = AccessTools.Method(typeof(RefuelNativeWitness), "Credit");
			list.InsertRange(num, new CodeInstruction[2]
			{
				codeInstruction,
				new CodeInstruction(OpCodes.Ldloc_2)
			});
			RefuelNativeWitness.ListAnchored = true;
			return list;
		}
	}
}
