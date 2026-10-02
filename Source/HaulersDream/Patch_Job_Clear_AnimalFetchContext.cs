using HarmonyLib;
using Verse.AI;

namespace HaulersDream
{
	[HarmonyPatch(typeof(Job), "Clear")]
	internal static class Patch_Job_Clear_AnimalFetchContext
	{
		private static void Prefix(Job __instance)
		{
			AnimalFetchContexts.Forget(__instance);
		}
	}
}
