using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
	[HarmonyPatch(typeof(WorkGiver_Refuel), "HasJobOnThing")]
	public static class Patch_WorkGiver_Refuel_Admission
	{
		private static void Postfix(WorkGiver_Refuel __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
		{
			// The work scanner asks this for many pawn/building pairs. Native ground
			// eligibility already covers a bulk ground sweep; only held fuel can
			// add a job here. Search for multiple ground stacks once, when creating
			// the selected job, rather than while ranking every possible building.
			if (!__result && pawn?.inventory?.innerContainer?.Count > 0)
				__result = BulkRefuel.TryPlan(__instance, pawn, t, forced, out _, out _, includeGround: false);
		}
	}
}
