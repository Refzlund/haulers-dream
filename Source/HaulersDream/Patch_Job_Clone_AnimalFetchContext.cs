using HarmonyLib;
using Verse.AI;

namespace HaulersDream
{
	[HarmonyPatch(typeof(Job), "Clone")]
	internal static class Patch_Job_Clone_AnimalFetchContext
	{
		private static void Postfix(Job __instance, Job __result)
		{
			AnimalFetchContexts.Copy(__instance, __result);
		}
	}
}
