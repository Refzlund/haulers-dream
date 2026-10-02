using System.Collections.Generic;
using Verse;

namespace HaulersDream
{
    // Plain data only: no Multiplayer API types/attributes enter non-MP loading.
    internal struct RouteCommandArgs
    {
        internal string workGiverDefName;
        internal RouteMode mode;
        internal int amount;
        internal int radius;
        internal float maxDistance;
        internal bool smart;
        internal bool allowHarvest;
        internal int growthThreshold;
        internal bool replace;
        internal List<Thing> mustInclude;
        internal HaulersDream.Core.RouteSelectionMethod selectionMethod;
        internal HaulersDream.Core.RouteDistanceBasis distanceBasis;
        internal int exactMax;
        internal Thing startNode;
        internal Thing endNode;
        internal bool alsoBuild;
        internal List<IntVec3> roomAnchors;
        internal List<ThingDef> extraDefs;
        internal bool blightedOnly;
    }

    internal struct SowRouteCommandArgs
    {
        internal IntVec3 anchor;
        internal SowRouteMode mode;
        internal int amount;
        internal int radius;
        internal float maxDistance;
        internal bool smart;
        internal bool replace;
        internal List<IntVec3> mustInclude;
        internal HaulersDream.Core.RouteSelectionMethod selectionMethod;
        internal int exactMax;
    }

    internal struct RemoveFloorRouteCommandArgs
    {
        internal IntVec3 anchor;
        internal RemoveFloorRouteMode mode;
        internal int amount;
        internal int radius;
        internal float maxDistance;
        internal bool replace;
        internal List<IntVec3> mustInclude;
        internal HaulersDream.Core.RouteSelectionMethod selectionMethod;
        internal int exactMax;
    }

}
