using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
	[HarmonyPatch(typeof(Corpse), "GetGizmos")]
	internal static class Patch_Corpse_RefuelRecoveryGizmo
	{
		private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Corpse __instance)
		{
			foreach (Gizmo item in __result)
			{
				yield return item;
			}
			Command_Action command_Action = RefuelRecoveryCommand.Gizmo(__instance.InnerPawn);
			if (command_Action != null)
			{
				yield return command_Action;
			}
		}
	}
}
