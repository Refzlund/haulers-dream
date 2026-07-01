using HaulersDream.Core;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// A sow route's settings saved as the explicit "remembered" template for one plant type (keyed by the growing
    /// zone's plant <see cref="ThingDef"/> in <see cref="HaulersDreamSettings.rememberedSowRoutesByDef"/>). Written
    /// ONLY when the player presses "Remember" in <see cref="Dialog_PlanSowRoute"/>; its mere existence is what makes
    /// that plant's right-click option read "(remembered)" and run in one click (while the interface toggle is on).
    ///
    /// <para>Distinct from the per-instance settings the dialog auto-restores every time it closes — this is a
    /// SEPARATE, opt-in layer that exists solely so the one-click has an explicit template to replay. The raw dialog
    /// values are stored (with the portable -1 sentinels for "no limit" / "All"); the effective route args are
    /// re-derived at replay time, exactly like <see cref="Dialog_PlanRoute.ExecuteRemembered"/> does. Persisted in
    /// the mod config (survives restarts).</para>
    /// </summary>
    public class SowRouteTemplate : IExposable
    {
        public SowRouteMode mode = SowRouteMode.Zone;
        public int maxTravel = 100;   // Chained span (cells); -1 = "no limit" (portable sentinel)
        public int radius = 8;        // Radius: circle size (cells)
        public int amount = -1;       // Radius stop cap; -1 = "All" (portable across the configurable max)
        public bool smart = true;
        public RouteSelectionMethod selectionMethod = RouteSelectionMethod.MostStopsPerTravel;

        public void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", SowRouteMode.Zone);
            Scribe_Values.Look(ref maxTravel, "maxTravel", 100);
            Scribe_Values.Look(ref radius, "radius", 8);
            Scribe_Values.Look(ref amount, "amount", -1);
            Scribe_Values.Look(ref smart, "smart", true);
            Scribe_Values.Look(ref selectionMethod, "selectionMethod", RouteSelectionMethod.MostStopsPerTravel);
        }
    }
}
