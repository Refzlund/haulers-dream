using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Picks the removable-floor CELLS a planned remove-floor route should cover, around the clicked cell. Cell-based
    /// by construction (removing floor targets terrain, not Things), so this is deliberately separate from both the
    /// Thing-based <see cref="RouteSelection"/> and the sow-cell <see cref="SowRouteSelection"/> — it never touches
    /// either pipeline.
    ///
    /// <para>Eligibility mirrors vanilla <see cref="Designator_RemoveFloor.CanDesignateCell"/> CONSERVATIVELY (right
    /// terrain removable, not under a solid wall, no building whose foundation-affordance would break) AND requires the
    /// cell already carry a <see cref="DesignationDefOf.RemoveFloor"/> designation (issue #110 — the route only
    /// prioritizes floors the player has already ordered removed). It is a SUPERSET-safe filter — the authoritative
    /// check runs again at job-build time (<c>scanner.HasJobOnCell(pawn, c, forced:true)</c>, which re-tests
    /// reservation + the same gates), so a slightly-too-permissive cell here just queues nothing, never a wrong job.</para>
    ///
    /// <para>DETERMINISTIC for Multiplayer: candidates are gathered from synced map state (terrain grid, buildings,
    /// designations) and ordered by cell index before any cap — so every client computes the same set. The Area
    /// flood visits neighbours in a fixed order (<see cref="GenAdj.AdjacentCells"/>) and its result is re-sorted by
    /// cell index before capping, so the flood order can never leak nondeterminism into the cap.</para>
    /// </summary>
    public static class RemoveFloorRouteSelection
    {
        // Window (in cells, radius) bounding the Chained candidate pool around the anchor — the sibling of
        // RouteSelection.VeinWindow / the sow Radius pool. The max-travel span trims it further downstream.
        private const int ChainedWindow = 60;

        // Slack over HardCap for the flood's visited budget, so the flood can find HardCap eligible cells even when a
        // few non-eligible cells (e.g. a wall tile) sit inside the cluster's bounding shape. The candidate list is
        // re-capped to HardCap after ordering, so the slack never inflates the route.
        private const int FloodSlack = 50;

        /// <param name="amount">stop-count cap for Radius (or <see cref="RouteSelection.AllAmount"/> for "All"); unused by Area/Chained, which take the HardCap.</param>
        /// <param name="radius">circle radius in cells for Radius mode; unused otherwise.</param>
        /// <param name="mustInclude">cells the player explicitly picked (plus the clicked anchor) that MUST be in the route — always kept, placed first.</param>
        /// <param name="capped">true when the natural selection was larger than the cap and got truncated.</param>
        /// <param name="mustIncludeCount">how many forced cells lead the result (the budget never trims these).</param>
        public static List<IntVec3> Select(Pawn pawn, IntVec3 anchor, RemoveFloorRouteMode mode, int amount,
            int radius, IReadOnlyList<IntVec3> mustInclude, out bool capped, out int mustIncludeCount)
        {
            capped = false;
            mustIncludeCount = 0;
            var result = new List<IntVec3>();
            if (pawn?.Map == null)
                return result;

            var map = pawn.Map;

            // MUST-INCLUDE prefix: the clicked anchor first, then each picked cell that is itself removable (deduped).
            var forced = new List<IntVec3>();
            var forcedSet = new HashSet<IntVec3>();
            if (IsRemovableFloorCell(pawn, anchor) && forcedSet.Add(anchor))
                forced.Add(anchor);
            if (mustInclude != null)
                for (int i = 0; i < mustInclude.Count; i++)
                {
                    var c = mustInclude[i];
                    if (forcedSet.Contains(c))
                        continue;
                    if (IsRemovableFloorCell(pawn, c) && forcedSet.Add(c))
                        forced.Add(c);
                }
            mustIncludeCount = forced.Count;

            // AUTO candidates by mode (Area flood / Chained window / Radius disc), minus the forced ones. Ordered by
            // cell index for a stable, MP-deterministic prefix BEFORE the cap is applied.
            var auto = BuildAuto(pawn, anchor, mode, radius, forcedSet);
            auto.Sort((a, b) => map.cellIndices.CellToIndex(a).CompareTo(map.cellIndices.CellToIndex(b)));

            // Cap the AUTO tail only (forced are extra). Area/Chained take the HardCap (Chained then trimmed by the
            // max-travel span downstream; Area is the whole cluster capped at HardCap); Radius takes the chosen
            // Amount. Leave room under HardCap for the forced.
            int autoCap = mode == RemoveFloorRouteMode.Radius ? RouteSelection.EffectiveAmount(amount) : RouteSelection.HardCap;
            int room = RouteSelection.HardCap - System.Math.Min(forced.Count, RouteSelection.HardCap);
            if (autoCap > room) autoCap = room;
            if (autoCap < 0) autoCap = 0;
            if (auto.Count > autoCap)
            {
                capped = true;
                auto.RemoveRange(autoCap, auto.Count - autoCap);
            }

            result.Capacity = forced.Count + auto.Count;
            result.AddRange(forced);
            result.AddRange(auto);
            return result;
        }

        // The removable-floor candidates for a mode, excluding the forced ones. Area floods the contiguous cluster;
        // Chained scans a bounded window; Radius scans the radius disc. Each returned cell passes IsRemovableFloorCell.
        private static List<IntVec3> BuildAuto(Pawn pawn, IntVec3 anchor, RemoveFloorRouteMode mode, int radius,
            HashSet<IntVec3> forcedSet)
        {
            switch (mode)
            {
                case RemoveFloorRouteMode.Area: return SelectArea(pawn, anchor, forcedSet);
                case RemoveFloorRouteMode.Radius: return SelectRadius(pawn, anchor, radius, forcedSet);
                default: return SelectChained(pawn, anchor, forcedSet); // Chained
            }
        }

        // AREA: the contiguous cluster of removable-floor cells reachable from the anchor by an 8-neighbour flood over
        // removable-floor cells (the "whole ruin floor"). Deterministic: the queue is seeded at the anchor and
        // neighbours are visited in the fixed GenAdj.AdjacentCells order; visited is bounded by HardCap+slack so a huge
        // connected floor can't run unbounded (the final list is re-sorted by cell index and re-capped in Select).
        private static List<IntVec3> SelectArea(Pawn pawn, IntVec3 anchor, HashSet<IntVec3> forcedSet)
        {
            var hits = new List<IntVec3>();
            var map = pawn.Map;
            // The anchor must itself be removable for a flood to make sense; if it isn't (e.g. the picker forced a
            // non-anchor start), there's no cluster to flood.
            if (!IsRemovableFloorCell(pawn, anchor))
                return hits;

            int budget = RouteSelection.HardCap + FloodSlack;
            var seen = new HashSet<IntVec3> { anchor };
            var queue = new Queue<IntVec3>();
            queue.Enqueue(anchor);
            var adj = GenAdj.AdjacentCells; // fixed 8-neighbour offsets → deterministic flood order
            while (queue.Count > 0 && seen.Count <= budget)
            {
                var cur = queue.Dequeue();
                if (!forcedSet.Contains(cur))
                    hits.Add(cur); // `cur` already passed IsRemovableFloorCell when enqueued
                for (int i = 0; i < adj.Length; i++)
                {
                    var next = cur + adj[i];
                    if (seen.Contains(next))
                        continue;
                    seen.Add(next);
                    if (IsRemovableFloorCell(pawn, next))
                        queue.Enqueue(next);
                }
            }
            return hits;
        }

        // CHAINED: every removable-floor cell within a bounded window of the anchor (the pool the max-travel span then
        // trims to the nearest reachable run). Scans CellRect.CenteredOn clamped to bounds — cheap and window-bounded.
        private static List<IntVec3> SelectChained(Pawn pawn, IntVec3 anchor, HashSet<IntVec3> forcedSet)
        {
            var hits = new List<IntVec3>();
            var map = pawn.Map;
            var rect = CellRect.CenteredOn(anchor, ChainedWindow).ClipInsideMap(map);
            foreach (var c in rect)
            {
                if (forcedSet.Contains(c))
                    continue;
                if (IsRemovableFloorCell(pawn, c))
                    hits.Add(c);
            }
            return hits;
        }

        // RADIUS: every removable-floor cell within `radius` cells of the anchor (capped at Amount in Select). Scans
        // the bounding square clamped to bounds and rejects cells outside the circle — mirrors the sow Radius filter.
        private static List<IntVec3> SelectRadius(Pawn pawn, IntVec3 anchor, int radius, HashSet<IntVec3> forcedSet)
        {
            var hits = new List<IntVec3>();
            var map = pawn.Map;
            float r = radius <= 0 ? 1 : radius;
            int box = radius <= 0 ? 1 : radius;
            var rect = CellRect.CenteredOn(anchor, box).ClipInsideMap(map);
            foreach (var c in rect)
            {
                if (forcedSet.Contains(c))
                    continue;
                if ((c - anchor).LengthHorizontal > r)
                    continue;
                if (IsRemovableFloorCell(pawn, c))
                    hits.Add(c);
            }
            return hits;
        }

        /// <summary>
        /// Whether this cell is a valid REMOVE-FLOOR route stop: its top-layer floor is removable (the removability
        /// gates mirror vanilla <see cref="Designator_RemoveFloor.CanDesignateCell"/> conservatively) AND it is already
        /// MARKED for removal (carries a <see cref="DesignationDefOf.RemoveFloor"/> designation). Note the designation
        /// clause is the OPPOSITE of the vanilla designator, which REJECTS an already-marked cell — here an
        /// already-marked cell is exactly what the route prioritizes. Used both to build the candidate set and to
        /// validate a player's manual cell pick. Reachability is NOT checked here (the planner does that); the
        /// authoritative recheck is <c>scanner.HasJobOnCell</c> at job-build time.
        ///
        /// <para>Issue #110: the route only covers floors the player has ALREADY ordered removed — so the "Plan
        /// prioritized removing floor" right-click option appears only over a marked floor (it used to appear over
        /// every built floor, which cluttered the menu when hauling / cleaning), and the route prioritizes what was
        /// marked rather than designating extra floors on its own. Mark floors with the vanilla Remove-floor order
        /// first, then prioritize their removal here.</para>
        /// </summary>
        public static bool IsRemovableFloorCell(Pawn pawn, IntVec3 c)
        {
            if (pawn?.Map == null || !c.IsValid)
                return false;
            var map = pawn.Map;
            if (!c.InBounds(map) || c.Fogged(map))
                return false;

            // Issue #110: only floors the player has already MARKED for removal are route stops. Cheap per-cell lookup
            // (DesignationManager indexes designations by cell), placed first so an unmarked cell short-circuits before
            // the terrain / edifice / building checks below.
            if (map.designationManager.DesignationAt(c, DesignationDefOf.RemoveFloor) == null)
                return false;

            // The load-bearing vanilla gate: the terrain grid's top layer must be removable here.
            if (!map.terrainGrid.CanRemoveTopLayerAt(c))
                return false;

            // Not under a solid wall (a full-fillage impassable edifice) — vanilla forbids designating those.
            if (c.GetEdifice(map) is Building e && e.def.Fillage == FillCategory.Full && e.def.passability == Traversability.Impassable)
                return false;

            // A building whose required foundation-affordance the under-terrain wouldn't satisfy blocks removal
            // (removing the floor would leave the building on unsupported ground). Exactly vanilla's check.
            if (WorkGiver_ConstructRemoveFloor.AnyBuildingBlockingFloorRemoval(c, map))
                return false;

            if (c.IsForbidden(pawn))
                return false;

            return true;
        }
    }
}
