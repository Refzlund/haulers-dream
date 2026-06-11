namespace HaulersDream.Core
{
    /// <summary>How a planned route decides WHICH same-kind targets to include within the travel budget.</summary>
    public enum RouteSelectionMethod
    {
        /// <summary>Greedy cheapest-insertion — add whichever target lengthens the route the LEAST, so the budget
        /// fits the most stops for the least travel (avoids far detours). The default.</summary>
        MostStopsPerTravel = 0,

        /// <summary>Take the targets simply NEAREST the clicked one, in order, until the budget runs out. More
        /// predictable ("grab the closest bushes") but can spend the budget on a detour.</summary>
        NearestToTarget = 1,
    }

    /// <summary>How a planned route MEASURES distance when selecting/budgeting targets.</summary>
    public enum RouteDistanceBasis
    {
        /// <summary>Straight-line (Euclidean) — fast; ignores rivers/walls between targets. The default.</summary>
        StraightLine = 0,

        /// <summary>Real walking path — accounts for terrain (a bush across a river is treated as far, not near).
        /// Needs pathfinding between targets, so it is applied only to small routes (it falls back to straight-line
        /// above a stop cap to stay responsive).</summary>
        WalkingPath = 1,
    }
}
