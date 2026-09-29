using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
	[HarmonyPatch(typeof(Pawn), "GetGizmos")]
	internal static class Patch_Pawn_RefuelRecoveryGizmo
	{
		private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
		{
			foreach (Gizmo item in __result)
			{
				yield return item;
			}
			if (!__instance.Dead)
			{
				Command_Action command_Action = RefuelRecoveryCommand.Gizmo(__instance);
				if (command_Action != null)
				{
					yield return command_Action;
				}
			}
		}
	}
}
