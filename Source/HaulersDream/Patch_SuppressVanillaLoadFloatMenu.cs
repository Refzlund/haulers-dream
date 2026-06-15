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
    /// The two branches are INDEPENDENTLY gated (transporters → <c>WorkGiver_LoadTransporters</c>; portals →
    /// <c>WorkGiver_HaulToPortal</c>) so enabling one while disabling the other leaves the still-vanilla option intact.
    /// Returning false makes the method produce no option; HD's own auto-discovered providers
    /// (<see cref="FloatMenuOptionProvider_BulkLoadTransporter"/> / <see cref="FloatMenuOptionProvider_BulkLoadPortal"/>)
    /// add the bulk order in its place.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), "GetWorkGiverOption")]
    public static class Patch_SuppressVanillaLoadFloatMenu
    {
        // Apply when EITHER sub-feature is on; the per-branch gates inside decide which option to suppress.
        static bool Prepare()
        {
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return true;
            return s.enableBulkLoadTransporters || s.enableBulkLoadPortal;
        }

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
            if (s.enableBulkLoadPortal && workGiver.giverClass == typeof(WorkGiver_HaulToPortal))
            {
                __result = null; // HD's bulk-portal-load float-menu provider supplies the order instead
                return false;
            }
            return true;
        }
    }
}
