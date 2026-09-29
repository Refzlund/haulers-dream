using HarmonyLib;
using Verse.AI;

namespace HaulersDream
{
	[HarmonyPatch(typeof(Job), "ExposeData")]
	internal static class Patch_Job_ExposeData_AnimalFetchContext
	{
		private static void Postfix(Job __instance)
		{
			AnimalFetchContexts.Expose(__instance);
		}
	}
}
