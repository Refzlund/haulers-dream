using HaulersDream.Core;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// A remove-floor route's settings saved as the explicit "remembered" template for one floor type (keyed by the
    /// clicked cell's floor <see cref="TerrainDef"/> in <see cref="HaulersDreamSettings.rememberedRemoveFloorRoutesByDef"/>).
    /// Written ONLY when the player presses "Remember" in <see cref="Dialog_PlanRemoveFloorRoute"/>; its existence is
    /// what makes that floor's right-click option read "(remembered)" and run in one click (while the interface toggle
    /// is on).
    ///
    /// <para>The remove-floor route has no "Smart" routing (it produces no haulable to circle back to), so — unlike
    /// <see cref="SowRouteTemplate"/> — there is no smart field. Raw dialog values are stored (with the portable -1
    /// sentinels for "no limit" / "All"); effective args are re-derived at replay time. Persisted in the mod config.</para>
    /// </summary>
    public class RemoveFloorRouteTemplate : IExposable
    {
        public RemoveFloorRouteMode mode = RemoveFloorRouteMode.Area;
        public int maxTravel = 100;   // Chained span (cells); -1 = "no limit" (portable sentinel)
        public int radius = 8;        // Radius: circle size (cells)
        public int amount = -1;       // Radius stop cap; -1 = "All" (portable across the configurable max)
        public RouteSelectionMethod selectionMethod = RouteSelectionMethod.MostStopsPerTravel;

        public void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", RemoveFloorRouteMode.Area);
            Scribe_Values.Look(ref maxTravel, "maxTravel", 100);
            Scribe_Values.Look(ref radius, "radius", 8);
            Scribe_Values.Look(ref amount, "amount", -1);
            Scribe_Values.Look(ref selectionMethod, "selectionMethod", RouteSelectionMethod.MostStopsPerTravel);
        }

        /// <summary>Field-by-field value equality against another template (see
        /// <see cref="RouteDialogPrefs.ValueEquals"/> — used to show "Already remembered" on the button).</summary>
        public bool ValueEquals(RemoveFloorRouteTemplate o) => o != null
            && mode == o.mode && maxTravel == o.maxTravel && radius == o.radius && amount == o.amount
            && selectionMethod == o.selectionMethod;
    }
}
