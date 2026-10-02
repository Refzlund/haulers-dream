using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	[HarmonyPatch]
	internal static class Patch_WorkGiver_InteractAnimal_FetchContext
	{
		private static MethodBase TargetMethod()
		{
			return AccessTools.DeclaredMethod(typeof(WorkGiver_InteractAnimal), "TakeFoodForAnimalInteractJob", new Type[2]
			{
				typeof(Pawn),
				typeof(Pawn)
			});
		}

		private static void Postfix(Pawn __0, Pawn __1, Job __result)
		{
			AnimalFetchContexts.Remember(__result, __0, __1);
		}
	}
}
