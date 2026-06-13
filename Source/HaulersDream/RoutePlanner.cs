using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>The computed result of a route request — shared by the dialog (preview + time) and the executor (queueing).</summary>
    public sealed class RoutePlan
    {
        public readonly List<Thing> stops = new List<Thing>();        // ordered, distance-truncated targets
        public float totalTicks;        // estimated walk + work (+ return) ticks
        public float travelCells;       // the route's travel distance (the budget cost — same metric as "Max travel")
        public int selectedCount;       // candidates chosen before distance truncation
        public bool cappedByAmount;     // the selection itself hit the stop cap
        public bool cappedByDistance;   // the max-travel budget trimmed reachable stops off the route
        public bool cappedByReach;      // some chosen candidates can't be pathed to (e.g. across a river) and were dropped
        public bool fogCaution;         // Vein only: the visible cluster touches fogged same-kind cells (more hidden)
        public IntVec3? storageCell;    // smart-routing anchor, if any
        public bool Empty => stops.Count == 0;
    }

    /// <summary>
    /// The expensive, selection-dependent part of planning, cached by the dialog so the max-travel slider can
    /// re-truncate live without re-pathfinding. Holds the candidate stops in the chosen gather order — greedy
    /// cheapest-insertion by default (storage-anchored when smart), or nearest-to-the-clicked-anchor — plus
    /// each leg's real path distance and walk time.
    ///
    /// MONOTONICITY of the max-travel trim (a larger budget / more stops never shrinks the kept set) does NOT
    /// come from the gather order being append-only (it isn't — cheapest-insertion can reorder as stops are
    /// added). It rests on two real mechanisms, both load-bearing:
    /// (a) <see cref="prefixRouteCost"/> is non-decreasing BY CONSTRUCTION — RouteBudget clamps every per-stop
    ///     insertion delta to ≥ 0 and finishes with a running max — so "largest prefix within budget" is a
    ///     threshold on a non-decreasing sequence; and
    /// (b) the dialog pairs a FINITE max-travel budget ONLY with Chained mode, whose candidate set is
    ///     budget-independent; every other mode passes +infinity (Dialog_PlanRoute.MaxDistance), so the budget
    ///     can never interact with a selection that reshuffles.
    /// This invariant has regressed three times — keep both mechanisms true when touching the planner.
    /// </summary>
    public sealed class RouteLegs
    {
        public Pawn pawn;
        public RouteWorkKind kind;
        public readonly List<Thing> gatherOrdered = new List<Thing>(); // reachable candidates, route (greedy) order
        public readonly List<float> legWalkTicks = new List<float>();  // PawnPath.TotalCost of the leg arriving at gatherOrdered[i] (time estimate)
        public float[] prefixRouteCost;  // budget cost: cheapest-insertion route length through the first K stops (monotone)
        public int forcedCount = 1;      // leading must-include stops (anchor + picks) that the budget never trims
        public IntVec3? storage;
        public int selectedCount;
        public bool cappedByAmount;
        public bool fogCaution; // Vein only: visible cluster touches fogged same-kind cells (shown as a caution)
        public int Reachable => gatherOrdered.Count;
    }

    /// <summary>
    /// Plans a route in two stages: <see cref="ComputeLegs"/> (select + gather-order + pathfind every leg —
    /// expensive, depends only on mode/amount/smart) and <see cref="Truncate"/> (apply the max-travel budget,
    /// smart-reorder the kept stops, estimate time — cheap, depends on the budget). The dialog caches the legs
    /// and truncates live as the slider drags; the executor uses the dialog's frozen plan so the queued route
    /// matches the preview. The budget is decided on the stable gather order, then smart routing reorders only
    /// the kept stops to end near storage.
    /// </summary>
    public static class RoutePlanner
    {
        /// <summary>Max stops for which the "walking path" distance basis builds a real pathfound matrix; above
        /// this it falls back to straight-line (an N² pathfind matrix would be too slow on big routes).</summary>
        public const int WalkingPathStopCap = 20;

        /// <summary>Convenience for callers (e.g. the executor's fallback) that want a plan in one call.</summary>
        public static RoutePlan Plan(Pawn pawn, Thing clicked, RouteWorkKind kind, RouteMode mode, int amount,
            int radius, float maxDistance, bool smart, bool allowHarvest, int growthThreshold, IReadOnlyList<Thing> mustInclude = null,
            RouteSelectionMethod selectionMethod = RouteSelectionMethod.MostStopsPerTravel,
            RouteDistanceBasis distanceBasis = RouteDistanceBasis.StraightLine, int exactMax = RouteOrderPolicy.ExactMax,
            Thing startNode = null, Thing endNode = null, IReadOnlyList<IntVec3> roomAnchors = null)
            => Truncate(ComputeLegs(pawn, clicked, kind, mode, amount, radius, smart, allowHarvest, growthThreshold,
                mustInclude, selectionMethod, distanceBasis, roomAnchors), maxDistance, startNode, endNode, exactMax);

        /// <param name="mustInclude">player-picked targets (plus the clicked anchor) that must always be in the route.</param>
        /// <param name="selectionMethod">greedy cheapest-insertion (most stops per travel) vs nearest-to-the-clicked-target.</param>
        /// <param name="distanceBasis">straight-line, or real pathfound distances (small routes only).</param>
        /// <param name="roomAnchors">Rooms mode only: cells whose rooms bound the selection.</param>
        public static RouteLegs ComputeLegs(Pawn pawn, Thing clicked, RouteWorkKind kind, RouteMode mode, int amount,
            int radius, bool smart, bool allowHarvest, int growthThreshold, IReadOnlyList<Thing> mustInclude = null,
            RouteSelectionMethod selectionMethod = RouteSelectionMethod.MostStopsPerTravel,
            RouteDistanceBasis distanceBasis = RouteDistanceBasis.StraightLine, IReadOnlyList<IntVec3> roomAnchors = null)
        {
            var legs = new RouteLegs { pawn = pawn, kind = kind };
            if (pawn?.Map == null || clicked == null || kind == null)
                return legs;
            // No try/catch: a route-planning failure is a real bug to surface as a red error on the player's
            // "plan route" click, not a verbose-only debug line that swallows the error and returns an empty route.
            var selected = RouteSelection.Select(pawn, clicked, kind, mode, amount, radius, allowHarvest, growthThreshold,
                mustInclude, out bool cappedByAmount, out bool fogCaution, out int mustIncludeCount, roomAnchors);
            legs.cappedByAmount = cappedByAmount;
            legs.fogCaution = fogCaution;
            legs.selectedCount = selected.Count;
            if (selected.Count == 0)
                return legs;

            legs.storage = smart ? FindStorageCell(pawn, kind) : null;

            // 1. REACHABILITY filter — keep the reachable candidates (forced/must-include lead `selected`, so
            //    the survivors lead `reachable` too). SKIP an unreachable stop (don't end the route on it — a
            //    river/wall cutting one off must not drop everything after it). Cheap region-cache check only;
            //    the actual pathfind (for the time estimate) is done in the chosen route order below.
            var reachable = new List<Thing>(selected.Count);
            int forcedReachable = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                if (!pawn.CanReach(selected[i], PathEndMode.Touch, Danger.Deadly))
                    continue;
                reachable.Add(selected[i]);
                if (i < mustIncludeCount)
                    forcedReachable++;
            }
            if (reachable.Count == 0)
                return legs;
            legs.forcedCount = forcedReachable < 1 ? 1 : forcedReachable;

            // 2. DISTANCE matrix over [pawn, reachable stops, (storage when smart)]. Straight-line by default;
            //    for the "walking path" basis on a small enough route, real pathfound distances (so a target
            //    across a river counts as far, not near). Above the cap, walking-path falls back to straight-
            //    line to stay responsive. Smart routing adds STORAGE as a fixed END node (matrix index n+1) so
            //    the selection below values stops on the way back to storage — the cheap "quick wins" the pawn
            //    drives past on the return leg, which a storage-blind pawn→stops path treated as far/expensive.
            int n = reachable.Count;
            bool walking = distanceBasis == RouteDistanceBasis.WalkingPath && n <= WalkingPathStopCap;
            float[,] dist = BuildDistMatrix(pawn, reachable, walking, legs.storage);
            int endIdx = legs.storage.HasValue ? n + 1 : -1; // storage's matrix index, or -1 for an open (free-end) path

            // 3. Order + budget cost per the selection method.
            //    GREEDY (default): order the auto stops so each is the CHEAPEST remaining addition to the route,
            //      so the Max-travel budget keeps the stops giving the MOST coverage per cell walked (no far
            //      detours). NEAREST: take the auto stops simply nearest the clicked anchor, in order. Either
            //      way the forced base (anchor + picks) leads free, and `cost` is monotone → the budget trim
            //      (Truncate) stays monotone. This order becomes the prefix-stable gather order for the budget.
            //    When smart, both route THROUGH storage (endIdx) so "Max travel" measures the gather DETOUR
            //      beyond the haul-to-storage trip and way-back stops cost ~0 (picked first). When not smart,
            //      endIdx = -1 → the open pawn→stops path, byte-identical to the prior behaviour.
            int[] order;
            float[] cost;
            if (selectionMethod == RouteSelectionMethod.NearestToTarget)
            {
                order = NearestOrder(dist, legs.forcedCount, n);
                RouteBudget.PrefixRouteCostMatrixWithEnd(dist, order, legs.forcedCount, endIdx, out cost);
            }
            else
            {
                RouteBudget.GreedyPlanMatrixWithEnd(dist, legs.forcedCount, endIdx, out order, out cost);
            }
            legs.prefixRouteCost = cost;

            // 4. Materialise the gather order + pathfind each leg (in that order) for the in-game time estimate.
            //    Reachability was already confirmed in step 1, so a failed pathfind here is a rare transient —
            //    keep the stop aligned with `cost` and just take 0 ticks for that leg.
            IntVec3 from = pawn.Position;
            for (int k = 0; k < order.Length; k++)
            {
                var t = reachable[order[k]];
                TryLeg(pawn, from, t, PathEndMode.Touch, out float _, out float ticks);
                legs.gatherOrdered.Add(t);
                legs.legWalkTicks.Add(ticks);
                from = t.Position;
            }
            return legs;
        }

        public static RoutePlan Truncate(RouteLegs legs, float maxDistance, Thing startNode = null, Thing endNode = null,
            int exactMax = RouteOrderPolicy.ExactMax)
        {
            var plan = new RoutePlan();
            if (legs == null || legs.Reachable == 0)
                return plan;
            plan.selectedCount = legs.selectedCount;
            plan.cappedByAmount = legs.cappedByAmount;
            plan.fogCaution = legs.fogCaution;
            plan.storageCell = legs.storage;
            // No try/catch: a truncation failure is a real bug to surface as a red error, not a verbose-only
            // debug line that swallows it and returns an empty plan.
            int reachable = legs.Reachable;
            // Budget = how far the pawn ACTUALLY travels gathering the targets: the length of a real
            // cheapest-insertion route from the pawn through the kept stops (RouteBudget, computed in
            // ComputeLegs), harvest-only (the haul-back to storage is NOT charged to "Max travel" — folding it
            // in would let a far stockpile blow the budget on a tiny patch). The cost array is non-decreasing,
            // so this trim is monotonic: a larger Max travel never keeps fewer stops. (Radius/Vein pass
            // maxDistance = +inf → keeps everything.) The OLD span budget summed legs between distance-RANKED
            // stops, which zigzag spatially and over-counted the walk 2–4×, trimming obvious nearby targets.
            int kept = RouteBudget.LargestPrefixWithin(legs.prefixRouteCost, maxDistance, legs.forcedCount);
            if (kept < 1) kept = 1;             // always keep at least the first reachable stop (usually the clicked anchor)
            if (kept > reachable) kept = reachable;
            // Two distinct reasons the route can be smaller than the chosen candidate set, kept separate so the
            // dialog can say WHICH: the max-travel budget trimmed reachable stops (cappedByDistance), and/or some
            // chosen candidates simply can't be pathed to and were dropped in ComputeLegs (cappedByReach). The
            // latter fires even with an infinite budget, so it must NOT be reported as "trimmed to the travel limit".
            plan.cappedByDistance = kept < reachable;
            plan.cappedByReach = reachable < legs.selectedCount;
            // The route's travel distance = the budget cost at the kept count (the SAME metric as "Max travel",
            // so the two read on the same scale: straight-line or pathfound cells per the distance basis).
            plan.travelCells = (legs.prefixRouteCost != null && kept >= 1 && kept <= legs.prefixRouteCost.Length)
                ? legs.prefixRouteCost[kept - 1]
                : 0f;

            var keptStops = new List<Thing>(kept);
            for (int i = 0; i < kept; i++)
                keptStops.Add(legs.gatherOrdered[i]);

            // Route order: ALWAYS order the kept stops for the SHORTEST route (no left-right-left zigzag),
            // regardless of smart mode. With smart OFF, storage is null → OrderByEuclidean uses a FREE end =
            // the shortest open path from the pawn through the stops (take a side and sweep across, not bounce
            // back and forth). With smart ON, storage is the fixed end → the shortest path that ENDS next to
            // storage for a quick unload. Either way it reorders ONLY the kept set, never the budget decision
            // above (which was taken on the non-decreasing prefixRouteCost array, in gather order — see the
            // RouteLegs doc for the two mechanisms the monotonicity actually rests on).
            var routeStops = keptStops.Count > 1
                ? OrderByEuclidean(legs.pawn.Position, keptStops, legs.storage, startNode, endNode, exactMax)
                : keptStops;
            plan.stops.AddRange(routeStops);

            // Time: gather-order walk (≈ route walk over the same stops) + per-stop work + the haul back to
            // storage (counted but not drawn, so the preview matches the queued route).
            var stopWorkTicks = new List<float>(kept);
            for (int i = 0; i < kept; i++)
                stopWorkTicks.Add(WorkTicks(legs.pawn, plan.stops[i], legs.kind));
            float returnTicks = 0f;
            if (legs.storage.HasValue && plan.stops.Count > 0 &&
                TryLeg(legs.pawn, plan.stops[plan.stops.Count - 1].Position, legs.storage.Value,
                    PathEndMode.OnCell, out float _, out float rTicks))
                returnTicks = rTicks;

            plan.totalTicks = RouteEstimate.TotalTicks(legs.legWalkTicks, stopWorkTicks, kept, returnTicks);
            return plan;
        }

        // Distance matrix; index 0 = pawn, 1..n = stops[0..n-1], and (when smart) n+1 = the storage cell as a FIXED
        // END node so the selection can value stops on the way back to storage (see GreedyPlanMatrixWithEnd).
        // Straight-line, OR symmetric pathfound (the pawn as traverser) when `walking` — so a target across a
        // river/wall is measured by the real walk. A pathfind that fails for a pair falls back to straight-line.
        private static float[,] BuildDistMatrix(Pawn pawn, List<Thing> stops, bool walking, IntVec3? storage)
        {
            int n = stops.Count;
            int m = n + 1 + (storage.HasValue ? 1 : 0);
            var pos = new IntVec3[m];
            pos[0] = pawn.Position;
            for (int i = 0; i < n; i++)
                pos[i + 1] = stops[i].Position;
            if (storage.HasValue)
                pos[n + 1] = storage.Value;
            var d = new float[m, m];
            for (int i = 0; i < m; i++)
                for (int j = i + 1; j < m; j++)
                {
                    float v = (pos[i] - pos[j]).LengthHorizontal;
                    if (walking && TryLeg(pawn, pos[i], pos[j], PathEndMode.Touch, out float cells, out float _))
                        v = cells;
                    d[i, j] = v;
                    d[j, i] = v;
                }
            return d;
        }

        // "Nearest" gather order: the forced base leads (in its own order, matrix indices 0..forcedCount-1), then
        // the auto stops sorted by distance to the clicked anchor (the first forced stop = matrix index 1), with a
        // deterministic stop-index tiebreak so the budget prefix is stable.
        private static int[] NearestOrder(float[,] dist, int forcedCount, int n)
        {
            var order = new int[n];
            for (int i = 0; i < forcedCount && i < n; i++)
                order[i] = i;
            var auto = new List<int>(n);
            for (int s = forcedCount; s < n; s++)
                auto.Add(s);
            const int anchor = 1; // matrix index of the first forced stop (the clicked target)
            auto.Sort((a, b) =>
            {
                int c = dist[anchor, a + 1].CompareTo(dist[anchor, b + 1]);
                return c != 0 ? c : a.CompareTo(b);
            });
            for (int i = 0; i < auto.Count; i++)
                order[forcedCount + i] = auto[i];
            return order;
        }

        // Order the targets to minimize travel: the OPEN path runs from the pawn (or a pinned START stop) through
        // the stops to storage (or a pinned END stop). startNode/endNode (when present in `things`) PIN the
        // first/last stop the player chose; the rest is ordered optimally between them (pure math in
        // RouteOrderPolicy.OrderStops). With neither pinned this is the old behaviour: pawn → [TSP stops] → storage.
        private static List<Thing> OrderByEuclidean(IntVec3 pawnPos, List<Thing> things, IntVec3? storage,
            Thing startNode, Thing endNode, int exactMax)
        {
            int s = things.Count;
            if (s <= 1)
                return new List<Thing>(things);

            var xs = new int[s];
            var zs = new int[s];
            for (int i = 0; i < s; i++) { xs[i] = things[i].Position.x; zs[i] = things[i].Position.z; }

            int startStop = startNode != null ? things.IndexOf(startNode) : -1;
            int endStop = (endNode != null && endNode != startNode) ? things.IndexOf(endNode) : -1;
            bool hasStorage = storage.HasValue;
            int sx = hasStorage ? storage.Value.x : 0;
            int sz = hasStorage ? storage.Value.z : 0;

            int[] order = RouteOrderPolicy.OrderStops(pawnPos.x, pawnPos.z, xs, zs, hasStorage, sx, sz,
                startStop, endStop, exactMax);

            var result = new List<Thing>(s);
            for (int i = 0; i < order.Length; i++)
                result.Add(things[order[i]]);
            return result;
        }

        // Pathfind one leg; out the cell distance (≈ tiles walked, for the budget) and walk ticks
        // (PawnPath.TotalCost, for the time estimate). The pooled path is always released.
        private static bool TryLeg(Pawn pawn, IntVec3 from, LocalTargetInfo target, PathEndMode peMode,
            out float cells, out float ticks)
        {
            cells = 0f; ticks = 0f;
            PawnPath path = null;
            // No catch: a pathfind throw is a real bug to surface, not swallow. The try/finally stays ONLY to
            // release the pooled path on every exit (including an exception) — the "no path" case is already
            // handled by the (path == null || !path.Found) check, so the finally does not hide any error.
            try
            {
                path = pawn.Map.pathFinder.FindPathNow(from, target, pawn, peMode: peMode);
                if (path == null || !path.Found)
                    return false;
                cells = path.NodesReversed.Count;
                ticks = path.TotalCost;
                return true;
            }
            finally
            {
                path?.ReleaseToPool();
            }
        }

        private static float WorkTicks(Pawn pawn, Thing t, RouteWorkKind kind)
        {
            if (t is Plant plant)
            {
                float hw = plant.def.plant?.harvestWork ?? 0f;
                return RouteEstimate.PlantWorkTicks(hw, pawn.GetStatValue(StatDefOf.PlantWorkSpeed), plant.Growth);
            }
            if (t.def.mineable)
            {
                bool natural = t.def.building?.isNaturalRock ?? false;
                return RouteEstimate.MineWorkTicks(t.HitPoints, natural, pawn.GetStatValue(StatDefOf.MiningSpeed));
            }
            // Blueprint: count the build work (WorkToBuild / ConstructionSpeed), the precisely-computable part. The
            // material-hauling trips aren't modelled, so the estimate stays a lower bound for a construction route.
            if (t is Blueprint bp)
            {
                var toBuild = bp.EntityToBuild();
                if (toBuild != null)
                {
                    float work = toBuild.GetStatValueAbstract(StatDefOf.WorkToBuild, bp.EntityToBuildStuff());
                    float speed = pawn.GetStatValue(StatDefOf.ConstructionSpeed);
                    if (speed > 0f && work > 0f)
                        return work / speed;
                }
            }
            return 0f;
        }

        internal static IntVec3? FindStorageCell(Pawn pawn, RouteWorkKind kind)
        {
            var def = kind.yieldDef;
            if (def == null || def.MadeFromStuff)
                return null;
            // No try/catch: ThingMaker.MakeThing is a vanilla call — a throw is a real bug to surface, not hide.
            Thing rep = ThingMaker.MakeThing(def);
            if (rep == null)
                return null;
            bool found = StoreUtility.TryFindBestBetterStoreCellFor(
                rep, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 cell, needAccurateResult: false);
            return found ? cell : (IntVec3?)null;
        }
    }
}
