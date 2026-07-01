using HaulersDream.Core;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The "Plan prioritized route" dialog options, remembered PER target type (keyed by the clicked thing's
    /// <see cref="ThingDef"/> in <see cref="HaulersDreamSettings.routePrefsByDef"/>). So berries remember the
    /// settings you last used on berries, cotton remembers its own, ambrosia its own — reopen the dialog on the
    /// same kind of thing and it comes back the way you left it. Persisted in the mod's config (survives restarts).
    /// </summary>
    public class RouteDialogPrefs : IExposable
    {
        public RouteMode mode = RouteMode.Chained;
        public int maxTravel = 100;        // Chained: span first→last stop (cells); -1 = "no limit" (portable sentinel)
        public int radius = 8;             // Radius: circle size (cells)
        public int amount = -1;            // Radius/Vein stop cap; -1 = "All" (portable across the configurable max)
        public bool smart = true;
        public bool allowHarvest = true;   // harvest only
        public int growthThreshold = 80;   // harvest only
        // Route-calc overrides (seeded from the global mod-settings defaults the first time a type is opened).
        public RouteSelectionMethod selectionMethod = RouteSelectionMethod.MostStopsPerTravel;
        public RouteDistanceBasis distanceBasis = RouteDistanceBasis.StraightLine;
        // The last confirm button used on this target type (true = Replace/start-now, false = Append). Remembered so
        // the "Remember plan" one-click reuse (see FloatMenuOptionProvider_PlanRoute) replays the same action the
        // player last chose. Defaults to Replace, matching the "prioritize now" feel of the right-click option.
        public bool lastReplace = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", RouteMode.Chained);
            Scribe_Values.Look(ref maxTravel, "maxTravel", 100);
            Scribe_Values.Look(ref radius, "radius", 8);
            Scribe_Values.Look(ref amount, "amount", -1);
            Scribe_Values.Look(ref smart, "smart", true);
            Scribe_Values.Look(ref allowHarvest, "allowHarvest", true);
            Scribe_Values.Look(ref growthThreshold, "growthThreshold", 80);
            Scribe_Values.Look(ref selectionMethod, "selectionMethod", RouteSelectionMethod.MostStopsPerTravel);
            Scribe_Values.Look(ref distanceBasis, "distanceBasis", RouteDistanceBasis.StraightLine);
            Scribe_Values.Look(ref lastReplace, "lastReplace", true);
        }
    }
}
