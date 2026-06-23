using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>How a planned SOW route gathers the empty growable cells of a growing zone around the clicked cell.
    /// A strict, sow-specific subset of <see cref="RouteMode"/> — sowing has no harvest gate, no vein flood, and no
    /// rooms concept; the only meaningful shapes are "the whole field", "the nearest few", and "within a radius".</summary>
    public enum SowRouteMode
    {
        Zone,    // every sowable cell in the clicked zone (default — "sow this whole field")
        Chained, // the nearest sowable cells, bounded by the max-travel span
        Radius,  // every sowable cell within a radius of the clicked cell, capped at Amount
    }

    /// <summary>
    /// The computed result of a sow-route request — the ordered list of CELLS to sow, plus the caps that fired,
    /// shared by the dialog (preview + estimate) and the executor (queueing). Cell-based by construction: sowing
    /// targets empty cells, not Things, so this deliberately never touches the Thing-based <see cref="RoutePlan"/>
    /// pipeline (harvest/mine/clean/construct), keeping that path byte-identical.
    /// </summary>
    public sealed class SowRoutePlan
    {
        public readonly List<IntVec3> cells = new List<IntVec3>(); // ordered, distance-truncated sow cells
        public float totalTicks;       // estimated walk + sow ticks (+ return when smart)
        public float travelCells;      // the route's travel distance (the budget metric, == "Max travel")
        public int selectedCount;      // sowable cells chosen before distance truncation
        public bool cappedByAmount;    // the selection itself hit the stop cap
        public bool cappedByDistance;  // the max-travel budget trimmed reachable cells off the route
        public IntVec3? storageCell;   // smart-routing anchor (the wanted plant's harvested-product storage), if any
        public ThingDef plantDef;      // the zone's plant being sown (for the estimate + storage anchor)
        public bool Empty => cells.Count == 0;
    }

    /// <summary>
    /// Plans a SOW route over the empty growable cells of a <see cref="Zone_Growing"/>, isolated from the
    /// Thing-based route system. Selection (<see cref="SowRouteSelection"/>) finds the sowable cells; this class
    /// orders them for the shortest route (reusing the pure, unit-tested <see cref="RouteOrderPolicy.OrderStops"/>),
    /// applies the max-travel budget, and estimates the time. Deterministic for Multiplayer: the candidate set is a
    /// stable function of synced map state (zone cells, terrain, plants), ordered by a fixed cell key before any
    /// cap, and the visiting order is pure math over those cells — so every client computes the same route.
    /// </summary>
    public static class SowRoutePlanner
    {
        public static SowRoutePlan Plan(Pawn pawn, IntVec3 anchor, Zone_Growing zone, SowRouteMode mode, int amount,
            int radius, float maxDistance, bool smart, IReadOnlyList<IntVec3> mustInclude = null,
            RouteSelectionMethod selectionMethod = RouteSelectionMethod.MostStopsPerTravel,
            int exactMax = RouteOrderPolicy.ExactMax)
        {
            var plan = new SowRoutePlan();
            if (pawn?.Map == null || zone == null || zone.Map != pawn.Map)
                return plan;

            ThingDef plantDef = zone.GetPlantDefToGrow();
            plan.plantDef = plantDef;

            // 1. Candidate sow cells (sow-eligible, reachable filtered below). Forced/must-include lead the list.
            bool cappedByAmount;
            int mustIncludeCount;
            var selected = SowRouteSelection.Select(pawn, anchor, zone, mode, amount, radius, mustInclude,
                out cappedByAmount, out mustIncludeCount);
            plan.cappedByAmount = cappedByAmount;
            plan.selectedCount = selected.Count;
            if (selected.Count == 0)
                return plan;

            // 2. Reachability filter (cheap region check; keeps the forced prefix leading the survivors).
            var reachable = new List<IntVec3>(selected.Count);
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
                return plan;
            if (forcedReachable < 1) forcedReachable = 1;
            // The actual forced CELLS (the leading must-include of `reachable`), tracked by VALUE so the budget can
            // honour them after greedy ordering reshuffles their positions — `i < forcedCount` would be wrong once
            // OrderStops reorders them. (`reachable[0]` is the clicked anchor, always present.)
            var forcedCells = new HashSet<IntVec3>();
            for (int i = 0; i < forcedReachable && i < reachable.Count; i++)
                forcedCells.Add(reachable[i]);

            // 3. Smart routing anchor: where the wanted plant's HARVESTED product would be stored, so the route can
            //    end near it. Sowing produces no haulable yield itself, but the field it plants will be harvested to
            //    that storage, so circling back toward it keeps the grower near where the future haul-back goes.
            plan.storageCell = smart ? FindStorageCell(pawn, plantDef) : null;

            // 4. Order the cells for the shortest route via the pure math (pawn → [cells] → storage/free-end). The
            //    forced prefix is preserved as the route start when "nearest" selection is chosen; greedy ordering
            //    reorders all of them between the pawn and storage. Max-travel is applied AFTER ordering by walking
            //    the prefix until the budget is exceeded.
            var ordered = OrderCells(pawn, reachable, forcedReachable, plan.storageCell, selectionMethod, exactMax);

            // 5. Apply the max-travel budget: keep the longest prefix of the ordered route whose cumulative
            //    straight-line travel (pawn → c0 → c1 → …) stays within maxDistance. Forced cells are always kept.
            int kept = ApplyBudget(pawn.Position, ordered, forcedCells, maxDistance, out float travel);
            plan.travelCells = travel;
            plan.cappedByDistance = kept < ordered.Count;

            for (int i = 0; i < kept; i++)
                plan.cells.Add(ordered[i]);

            // 6. Time estimate: per-leg walk (straight-line ticks proxy) + per-cell sow work + the return-to-storage
            //    leg when smart. A lower bound, sufficient for the UI.
            plan.totalTicks = EstimateTicks(pawn, plan, plantDef);
            return plan;
        }

        // Order the cells for the shortest open path pawn → [cells] → (storage|free end), reusing RouteOrderPolicy.
        // Greedy/most-stops and nearest both collapse to the same TSP order here (there is no per-leg "coverage"
        // difference for uniform sow cells); we always solve the shortest visiting order, pinning nothing.
        private static List<IntVec3> OrderCells(Pawn pawn, List<IntVec3> cells, int forcedCount, IntVec3? storage,
            RouteSelectionMethod selectionMethod, int exactMax)
        {
            int s = cells.Count;
            if (s <= 1)
                return new List<IntVec3>(cells);

            // NEAREST selection: keep the forced prefix, then the remaining cells nearest the clicked anchor (the
            // first forced cell), so the route hugs the click point. Deterministic id tiebreak via the cell index.
            if (selectionMethod == RouteSelectionMethod.NearestToTarget)
            {
                var result = new List<IntVec3>(s);
                var map = pawn.Map;
                IntVec3 anchor = cells[0];
                for (int i = 0; i < forcedCount && i < s; i++)
                    result.Add(cells[i]);
                var rest = new List<IntVec3>(s);
                for (int i = forcedCount; i < s; i++)
                    rest.Add(cells[i]);
                rest.Sort((a, b) =>
                {
                    long da = (a - anchor).LengthHorizontalSquared;
                    long db = (b - anchor).LengthHorizontalSquared;
                    int c = da.CompareTo(db);
                    if (c != 0) return c;
                    return map.cellIndices.CellToIndex(a).CompareTo(map.cellIndices.CellToIndex(b));
                });
                result.AddRange(rest);
                return result;
            }

            var xs = new int[s];
            var zs = new int[s];
            for (int i = 0; i < s; i++) { xs[i] = cells[i].x; zs[i] = cells[i].z; }
            bool hasStorage = storage.HasValue;
            int sx = hasStorage ? storage.Value.x : 0;
            int sz = hasStorage ? storage.Value.z : 0;
            int[] order = RouteOrderPolicy.OrderStops(pawn.Position.x, pawn.Position.z, xs, zs, hasStorage, sx, sz,
                startStop: -1, endStop: -1, exactMax: exactMax);
            var ordered = new List<IntVec3>(s);
            for (int i = 0; i < order.Length; i++)
                ordered.Add(cells[order[i]]);
            return ordered;
        }

        // Largest prefix of the ordered route within the straight-line travel budget (pawn → c0 → c1 → …). Forced
        // cells (the must-include set, by VALUE) are always kept regardless of budget; at least the first cell is kept.
        private static int ApplyBudget(IntVec3 pawnPos, List<IntVec3> ordered, HashSet<IntVec3> forcedCells,
            float maxDistance, out float travel)
        {
            travel = 0f;
            if (ordered.Count == 0)
                return 0;
            bool noLimit = maxDistance <= 0f || float.IsPositiveInfinity(maxDistance);
            IntVec3 from = pawnPos;
            int kept = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                float leg = (ordered[i] - from).LengthHorizontal;
                float next = travel + leg;
                bool forced = forcedCells != null && forcedCells.Contains(ordered[i]);
                if (!noLimit && !forced && i > 0 && next > maxDistance)
                    break;
                travel = next;
                from = ordered[i];
                kept = i + 1;
            }
            if (kept < 1) kept = 1;
            // Recompute the kept travel exactly (a forced cell may have been kept past the budget above).
            travel = 0f;
            from = pawnPos;
            for (int i = 0; i < kept; i++)
            {
                travel += (ordered[i] - from).LengthHorizontal;
                from = ordered[i];
            }
            return kept;
        }

        private static float EstimateTicks(Pawn pawn, SowRoutePlan plan, ThingDef plantDef)
        {
            if (plan.cells.Count == 0)
                return 0f;
            float plantWorkSpeed = pawn.GetStatValue(StatDefOf.PlantWorkSpeed);
            float sowWork = plantDef?.plant?.sowWork ?? 0f;
            float perCellWork = RouteEstimate.SowWorkTicks(sowWork, plantWorkSpeed);

            // Walk ticks ≈ straight-line cells × the pawn's per-cell move ticks (a UI proxy; the route planner for
            // Things uses real pathfinds, but a sow field is contiguous and uniform, so straight-line is close).
            float ticksPerCell = TicksPerCell(pawn);
            var legWalk = new List<float>(plan.cells.Count);
            var stopWork = new List<float>(plan.cells.Count);
            IntVec3 from = pawn.Position;
            for (int i = 0; i < plan.cells.Count; i++)
            {
                legWalk.Add((plan.cells[i] - from).LengthHorizontal * ticksPerCell);
                stopWork.Add(perCellWork);
                from = plan.cells[i];
            }
            float returnTicks = 0f;
            if (plan.storageCell.HasValue)
                returnTicks = (plan.storageCell.Value - from).LengthHorizontal * ticksPerCell;
            return RouteEstimate.TotalTicks(legWalk, stopWork, plan.cells.Count, returnTicks);
        }

        // Approximate ticks to walk one cell at the pawn's current move speed. MoveSpeed is cells/second; a tick is
        // 1/60 s. Clamp to a sane floor so a 0/edge case never divides by zero.
        private static float TicksPerCell(Pawn pawn)
        {
            float cellsPerSecond = pawn.GetStatValue(StatDefOf.MoveSpeed);
            if (cellsPerSecond <= 0.01f)
                cellsPerSecond = 1f;
            return 60f / cellsPerSecond;
        }

        // The storage cell the wanted plant's HARVESTED product would go to (so smart routing can end near it).
        // Mirrors RoutePlanner.FindStorageCell: only for a non-stuff yield, via the player-explicit Unload context.
        internal static IntVec3? FindStorageCell(Pawn pawn, ThingDef plantDef)
        {
            var harvested = plantDef?.plant?.harvestedThingDef;
            if (harvested == null || harvested.MadeFromStuff)
                return null;
            Thing rep = ThingMaker.MakeThing(harvested);
            if (rep == null)
                return null;
            bool found;
            IntVec3 cell;
            using (StorageBuildingFilter.PushContext(StorageFilterContext.Unload))
            {
                found = StoreUtility.TryFindBestBetterStoreCellFor(
                    rep, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out cell, needAccurateResult: false);
            }
            return found ? cell : (IntVec3?)null;
        }
    }
}
