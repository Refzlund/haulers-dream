using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
	[HarmonyPatch(typeof(WorkGiver_Refuel), "HasJobOnThing")]
	public static class Patch_WorkGiver_Refuel_Admission
	{
		private static bool Prefix(WorkGiver_Refuel __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
		{
			if (!BulkRefuel.TryPlan(__instance, pawn, t, forced, out var _, out var _))
			{
				return true;
			}
			__result = true;
			return false;
		}
	}
}
