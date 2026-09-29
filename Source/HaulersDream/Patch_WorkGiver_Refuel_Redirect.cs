using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	[HarmonyPatch(typeof(WorkGiver_Refuel), "JobOnThing")]
	public static class Patch_WorkGiver_Refuel_Redirect
	{
		private static bool Prefix(WorkGiver_Refuel __instance, Pawn pawn, Thing t, bool forced, ref Job __result)
		{
			if (!BulkRefuel.TryPlan(__instance, pawn, t, forced, out var plan, out var _))
			{
				return true;
			}
			__result = plan.CreateJob();
			return false;
		}
	}
}
