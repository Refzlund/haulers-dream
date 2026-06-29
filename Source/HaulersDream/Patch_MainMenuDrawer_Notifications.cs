using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Draws Hauler's Dream's report notifications (and pumps their once-per-launch poll) in the bottom-right
    /// of the MAIN MENU only. <see cref="MainMenuDrawer.MainMenuOnGUI"/> is the per-frame main-menu OnGUI entry;
    /// vanilla also calls it for the in-game ESC overlay, so we gate on <see cref="ProgramState.Entry"/> (the
    /// title screen, no game loaded). Auto-discovered + wrapped by the resilient patch loader in HaulersDreamMod;
    /// the heavy lifting (and its own try/catch) lives in <see cref="ReportNotifications"/>.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class Patch_MainMenuDrawer_Notifications
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (Current.ProgramState != ProgramState.Entry) return; // title screen only
            ReportNotifications.OnMainMenuGUI();
        }
    }
}
