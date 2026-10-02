using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;

namespace HaulersDream
{
	[HarmonyPatch(typeof(CompRefuelable), "Refuel", new Type[] { typeof(float) })]
	internal static class Patch_RefuelFloatWitness
	{
		[HarmonyPriority(800)]
		private static void Prefix(CompRefuelable __instance, out RefuelAttempt __state)
		{
			__state = RefuelNativeWitness.Active;
			if (__state == null || __state.Target != __instance)
			{
				__state = null;
				return;
			}
			__state.FloatDepth++;
			if (__state.FloatDepth != 1 || !__state.ExpectedFloat)
			{
				__state.Ambiguous = true;
			}
		}

		[HarmonyPriority(0)]
		private static Exception Finalizer(Exception __exception, RefuelAttempt __state)
		{
			if (__state != null)
			{
				__state.FloatDepth--;
			}
			return __exception;
		}

		[HarmonyPriority(0)]
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			RefuelNativeWitness.FloatAnchored = false;
			List<CodeInstruction> list = new List<CodeInstruction>(instructions);
			FieldInfo fieldInfo = AccessTools.Field(typeof(CompRefuelable), "fuel");
			int num = -1;
			int num2 = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].opcode == OpCodes.Stfld && object.Equals(list[i].operand, fieldInfo))
				{
					if (num < 0)
					{
						num = i;
					}
					num2++;
				}
			}
			if (num2 != 2 || num < 2 || list[num - 1].opcode != OpCodes.Add || list[num - 2].opcode != OpCodes.Mul)
			{
				throw new InvalidOperationException("Ordinary refuel native fuel-write anchor changed.");
			}
			list.InsertRange(num + 1, new CodeInstruction[5]
			{
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldarg_1),
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldfld, fieldInfo),
				RefuelNativeWitness.Call("FuelWritten")
			});
			RefuelNativeWitness.FloatAnchored = true;
			return list;
		}
	}
}
