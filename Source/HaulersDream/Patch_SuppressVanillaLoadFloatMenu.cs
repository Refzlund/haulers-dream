using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Suppress the vanilla single-item "Load X into transporter" right-click float-menu option when HD's bulk-load
    /// replacement is on, so the player sees HD's bulk order instead of fighting the vanilla one-stack option. A
    /// prefix on the private <c>FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption(Pawn, WorkGiverDef,
    /// LocalTargetInfo, FloatMenuContext)</c> returns false (suppress the vanilla option) when the work giver's
    /// <c>giverClass</c> is <see cref="WorkGiver_LoadTransporters"/>.
    ///
    /// The branch is independently gated (transporters here; the portal branch is Stage 3) so enabling transporters
    /// while disabling portals would leave the still-vanilla portal option intact. Returning false makes the method
    /// produce no option; HD's own <see cref="FloatMenuOptionProvider_BulkLoadTransporter"/> (auto-discovered) adds
    /// the bulk order in its place.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), "GetWorkGiverOption")]
    public static class Patch_SuppressVanillaLoadFloatMenu
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        // Returning false from a prefix that sets __result=null suppresses the vanilla option entirely.
        static bool Prefix(WorkGiverDef workGiver, ref FloatMenuOption __result)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || workGiver?.giverClass == null)
                return true;
            if (s.enableBulkLoadTransporters && workGiver.giverClass == typeof(WorkGiver_LoadTransporters))
            {
                __result = null; // HD's bulk-load float-menu provider supplies the order instead
                return false;
            }
            return true;
        }
    }
}
