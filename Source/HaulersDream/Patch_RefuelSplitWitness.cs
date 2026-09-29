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
			FieldInfo objB = AccessTools.Field(typeof(Thing), "stackCount");
			if (num + 10 >= list.Count || list[num + 1].opcode != OpCodes.Stloc_0 || list[num + 2].opcode != OpCodes.Ldloc_0 || list[num + 3].opcode != OpCodes.Ldarg_1 || list[num + 4].opcode != OpCodes.Stfld || !object.Equals(list[num + 4].operand, objB) || list[num + 5].opcode != OpCodes.Ldarg_0 || list[num + 6].opcode != OpCodes.Ldarg_0 || list[num + 7].opcode != OpCodes.Ldfld || !object.Equals(list[num + 7].operand, objB) || list[num + 8].opcode != OpCodes.Ldarg_1 || list[num + 9].opcode != OpCodes.Sub || list[num + 10].opcode != OpCodes.Stfld || !object.Equals(list[num + 10].operand, objB))
			{
				throw new InvalidOperationException("Ordinary refuel split-custody anchors changed.");
			}
			list.InsertRange(num + 11, new CodeInstruction[3]
			{
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldloc_0),
				RefuelNativeWitness.Call("Debited")
			});
			list.InsertRange(num + 2, new CodeInstruction[3]
			{
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldloc_0),
				RefuelNativeWitness.Call("Created")
			});
			RefuelNativeWitness.SplitAnchored = true;
			return list;
		}
	}
}
