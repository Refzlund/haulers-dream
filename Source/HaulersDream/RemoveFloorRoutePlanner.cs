using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>How a planned REMOVE-FLOOR route gathers the removable-floor cells around the clicked cell.
    /// A strict, remove-floor-specific subset of <see cref="RouteMode"/> — removing floor has no harvest gate, no
    /// product to store, and no zone concept; the only meaningful shapes are "the whole connected floor", "the
    /// nearest few", and "within a radius".</summary>
    public enum RemoveFloorRouteMode
    {
        Area,    // the contiguous connected cluster of removable-floor cells reached by 8-neighbour flood (the "whole ruin floor"), capped at HardCap
        Chained, // the nearest removable-floor cells within a bounded window, bounded by the max-travel span
        Radius,  // every removable-floor cell within a radius of the clicked cell, capped at Amount
    }

    /// <summary>
    /// The computed result of a remove-floor-route request — the ordered list of CELLS to remove flooring from, plus
    /// the caps that fired, shared by the dialog (preview + estimate) and the executor (queueing). Cell-based by
    /// construction (removing floor targets terrain, not Things), so this deliberately never touches the Thing-based
    /// <see cref="RoutePlan"/> pipeline. Unlike <see cref="SowRoutePlan"/> it has NO plant/storage/smart semantics —
    /// removing a floor produces no haulable product to circle back to.
    /// </summary>
    public sealed class RemoveFloorRoutePlan
    {
        public readonly List<IntVec3> cells = new List<IntVec3>(); // ordered, distance-truncated remove-floor cells
        public float totalTicks;       // estimated walk + per-cell remove-floor work ticks
        public float travelCells;      // the route's travel distance (the budget metric, == "Max travel")
        public int selectedCount;      // removable cells chosen before distance truncation
        public bool cappedByAmount;    // the selection itself hit the stop cap
        public bool cappedByDistance;  // the max-travel budget trimmed reachable cells off the route
        public bool Empty => cells.Count == 0;
    }

    /// <summary>
    /// Plans a REMOVE-FLOOR route over the removable-floor cells around a clicked cell, isolated from the Thing-based
    /// and sow-cell route systems. Selection (<see cref="RemoveFloorRouteSelection"/>) finds the removable cells; this
    /// class orders them for the shortest route (reusing the pure, unit-tested <see cref="RouteOrderPolicy.OrderStops"/>),
    /// applies the max-travel budget, and estimates the time. Deterministic for Multiplayer: the candidate set is a
    /// stable function of synced map state (terrain, buildings, designations), ordered by a fixed cell key before any
    /// cap, and the visiting order is pure math over those cells — so every client computes the same route.
    /// </summary>
    public static class RemoveFloorRoutePlanner
    {
        // Per-cell work-ticks proxy for the remove-floor job's UI estimate only. The vanilla RemoveFloor job runs the
        // generic AffectFloor toil with WorkAmount ~200 ticks of construction work; we use a flat constant here (it's
        // a lower-bound UI hint, not a scheduler input — the real job accrues by the pawn's construction speed, which
        // we don't fold in, exactly as the Thing route's estimates are approximations). Not scaled by anything.
        private const float RemoveFloorWorkTicksProxy = 200f;

        public static RemoveFloorRoutePlan Plan(Pawn pawn, IntVec3 anchor, RemoveFloorRouteMode mode, int amount,
            int radius, float maxDistance, IReadOnlyList<IntVec3> mustInclude = null,
            RouteSelectionMethod selectionMethod = RouteSelectionMethod.MostStopsPerTravel,
            int exactMax = RouteOrderPolicy.ExactMax)
        {
            var plan = new RemoveFloorRoutePlan();
            if (pawn?.Map == null || !anchor.IsValid)
                return plan;

            // 1. Candidate removable cells (eligible; reachable filtered below). Forced/must-include lead the list.
            bool cappedByAmount;
            int mustIncludeCount;
            var selected = RemoveFloorRouteSelection.Select(pawn, anchor, mode, amount, radius, mustInclude,
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

            // 3. Order the cells for the shortest route via the pure math (pawn → [cells] → free end). No storage:
            //    removing a floor produces no product, so there's no haul-back leg to circle toward.
            var ordered = OrderCells(pawn, reachable, forcedReachable, selectionMethod, exactMax);

            // 4. Apply the max-travel budget: keep the longest prefix of the ordered route whose cumulative
            //    straight-line travel (pawn → c0 → c1 → …) stays within maxDistance. Forced cells are always kept.
            int kept = ApplyBudget(pawn.Position, ordered, forcedCells, maxDistance, out float travel);
            plan.travelCells = travel;
            plan.cappedByDistance = kept < ordered.Count;

            for (int i = 0; i < kept; i++)
                plan.cells.Add(ordered[i]);

            // 5. Time estimate: per-leg walk (straight-line ticks proxy) + a per-cell remove-floor work constant. A
            //    lower bound, sufficient for the UI. No return-to-storage leg.
            plan.totalTicks = EstimateTicks(pawn, plan);
            return plan;
        }

        // Order the cells for the shortest open path pawn → [cells] → free end, reusing RouteOrderPolicy. There is no
        // storage anchor (removing a floor stores nothing), so the end is always free; nearest/most-stops otherwise
        // mirror SowRoutePlanner.OrderCells exactly.
        private static List<IntVec3> OrderCells(Pawn pawn, List<IntVec3> cells, int forcedCount,
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
            // hasStorage:false — no product-storage end for a remove-floor route.
            int[] order = RouteOrderPolicy.OrderStops(pawn.Position.x, pawn.Position.z, xs, zs, hasStorage: false, 0, 0,
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

        private static float EstimateTicks(Pawn pawn, RemoveFloorRoutePlan plan)
        {
            if (plan.cells.Count == 0)
                return 0f;

            // Walk ticks ≈ straight-line cells × the pawn's per-cell move ticks (a UI proxy). A contiguous floor is
            // near-uniform, so straight-line is close (the Thing route uses real pathfinds; a floor doesn't warrant it).
            float ticksPerCell = TicksPerCell(pawn);
            var legWalk = new List<float>(plan.cells.Count);
            var stopWork = new List<float>(plan.cells.Count);
            IntVec3 from = pawn.Position;
            for (int i = 0; i < plan.cells.Count; i++)
            {
                legWalk.Add((plan.cells[i] - from).LengthHorizontal * ticksPerCell);
                stopWork.Add(RemoveFloorWorkTicksProxy);
                from = plan.cells[i];
            }
            // No return leg (returnLegTicks:0) — nothing to haul back to.
            return RouteEstimate.TotalTicks(legWalk, stopWork, plan.cells.Count, returnLegTicks: 0f);
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
    }
}
