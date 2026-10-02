using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    // Pawn enumerates its native trackers explicitly and does not expose ThingComp
    // holders. Link our existing component so both saved recovery owners belong to
    // native pawn-root traversal. Never replace or duplicate existing child holders.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetChildHolders))]
    internal static class Patch_Pawn_RecoveryHolders
    {
        private static void Postfix(Pawn __instance, List<IThingHolder> outChildren)
        {
            var comp = __instance?.GetComp<CompHauledToInventory>();
            if (comp != null && outChildren != null && !outChildren.Contains(comp))
                outChildren.Add(comp);
        }
    }
}
