using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Picks the empty growable CELLS a planned sow route should cover, inside the clicked <see cref="Zone_Growing"/>.
    /// Cell-based by construction (sowing has no Thing to anchor on), so this is deliberately separate from the
    /// Thing-based <see cref="RouteSelection"/> — it never touches that pipeline.
    ///
    /// <para>Eligibility mirrors the gates of vanilla <see cref="WorkGiver_GrowerSow.JobOnCell"/> CONSERVATIVELY:
    /// a cell is offered only when the zone's wanted plant can be sown there now (right terrain/fertility, growth
    /// season, not already sown, not blocked). It is intentionally a SUPERSET-safe filter — the authoritative
    /// check runs again at job-build time (<c>scanner.HasJobOnCell(pawn, c, forced:true)</c>), which drops any cell
    /// that turns out unworkable, so a slightly-too-permissive cell here just queues nothing, never a wrong job.</para>
    ///
    /// <para>DETERMINISTIC for Multiplayer: candidates come from the zone's RAW <c>cells</c> list (NOT
    /// <see cref="Zone.Cells"/>, which SHUFFLES), are filtered by synced map state, and are ordered by cell index
    /// before any cap — so every client computes the same set.</para>
    /// </summary>
    public static class SowRouteSelection
    {
        /// <param name="amount">stop-count cap for Radius (or <see cref="RouteSelection.AllAmount"/> for "All"); unused by Chained/Zone, which take the HardCap.</param>
        /// <param name="radius">circle radius in cells for Radius mode; unused otherwise.</param>
        /// <param name="mustInclude">cells the player explicitly picked (plus the clicked anchor) that MUST be in the route — always kept, placed first.</param>
        /// <param name="capped">true when the natural selection was larger than the cap and got truncated.</param>
        /// <param name="mustIncludeCount">how many forced cells lead the result (the budget never trims these).</param>
        public static List<IntVec3> Select(Pawn pawn, IntVec3 anchor, Zone_Growing zone, SowRouteMode mode, int amount,
            int radius, IReadOnlyList<IntVec3> mustInclude, out bool capped, out int mustIncludeCount)
        {
            capped = false;
            mustIncludeCount = 0;
            var result = new List<IntVec3>();
            if (pawn?.Map == null || zone == null || zone.Map != pawn.Map)
                return result;

            var map = pawn.Map;
            ThingDef plantDef = zone.GetPlantDefToGrow();
            if (plantDef?.plant == null)
                return result;

            // MUST-INCLUDE prefix: the clicked anchor first, then each picked cell that is itself sowable (deduped).
            var forced = new List<IntVec3>();
            var forcedSet = new HashSet<IntVec3>();
            if (IsSowableCell(pawn, anchor, zone, plantDef) && forcedSet.Add(anchor))
                forced.Add(anchor);
            if (mustInclude != null)
                for (int i = 0; i < mustInclude.Count; i++)
                {
                    var c = mustInclude[i];
                    if (forcedSet.Contains(c))
                        continue;
                    if (IsSowableCell(pawn, c, zone, plantDef) && forcedSet.Add(c))
                        forced.Add(c);
                }
            mustIncludeCount = forced.Count;

            // AUTO candidates: the sowable cells of the zone (Chained/Zone) or within the radius (Radius), minus the
            // forced ones. Ordered by cell index for a stable, MP-deterministic prefix BEFORE the cap is applied.
            var auto = new List<IntVec3>();
            float r = radius <= 0 ? 1 : radius;
            var cells = zone.cells; // RAW list — never Zone.Cells (it shuffles)
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (forcedSet.Contains(c))
                    continue;
                if (mode == SowRouteMode.Radius && (c - anchor).LengthHorizontal > r)
                    continue;
                if (IsSowableCell(pawn, c, zone, plantDef))
                    auto.Add(c);
            }
            auto.Sort((a, b) => map.cellIndices.CellToIndex(a).CompareTo(map.cellIndices.CellToIndex(b)));

            // Cap the AUTO tail only (forced are extra). Chained/Zone take the HardCap (Chained then trimmed by the
            // max-travel span downstream); Radius takes the chosen Amount. Leave room under HardCap for the forced.
            int autoCap = mode == SowRouteMode.Radius ? RouteSelection.EffectiveAmount(amount) : RouteSelection.HardCap;
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

        /// <summary>
        /// Whether the zone's wanted plant can be SOWN in this cell now, mirroring the gates of vanilla
        /// <see cref="WorkGiver_GrowerSow.JobOnCell"/> conservatively. Used both to build the candidate set and to
        /// validate a player's manual cell pick. Reachability is NOT checked here (the planner does that); the
        /// authoritative recheck is <c>scanner.HasJobOnCell</c> at job-build time.
        /// </summary>
        public static bool IsSowableCell(Pawn pawn, IntVec3 c, Zone_Growing zone, ThingDef plantDef)
        {
            if (pawn?.Map == null || plantDef?.plant == null || !c.IsValid)
                return false;
            var map = pawn.Map;
            if (!c.InBounds(map) || map.fogGrid.IsFogged(c))
                return false;
            // Must still be a cell of THIS growing zone (a pick outside it, or a zone that changed, is rejected).
            if ((c.GetZone(map) as Zone_Growing) != zone || !zone.allowSow)
                return false;
            if (c.IsForbidden(pawn))
                return false;

            // Growth season + plantable terrain/fertility for the wanted plant (the load-bearing vanilla gates).
            if (!PlantUtility.GrowthSeasonNow(c, map, plantDef))
                return false;
            if (!plantDef.CanNowPlantAt(c, map))
                return false;

            // Already sown with the wanted plant? Then there's nothing to sow here. (A wrong-def or immature plant
            // blocks sowing too, but vanilla's JobOnCell would issue a CutPlant/haul-aside instead — out of scope
            // for a sow ROUTE, so we simply don't offer those cells; they fall to normal Growing work.)
            var existing = c.GetPlant(map);
            if (existing != null)
                return false;

            // A blueprint/frame on fertile soil still allows sowing in vanilla, but anything that BlocksPlanting
            // (rocks, items, hostile structures) means vanilla would cut/haul-aside first — skip those cells.
            var things = c.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                if (t.def == plantDef)
                    return false; // already the wanted plant (covers a non-Plant edge case)
                if (t.def.BlocksPlanting())
                    return false;
            }

            // An adjacent sow blocker (e.g. a tree that would shade this cell) means vanilla sows elsewhere first;
            // be conservative and skip the cell — the route is for the cleanly-sowable bulk of the field.
            if (PlantUtility.AdjacentSowBlocker(plantDef, c, map) != null)
                return false;

            return true;
        }
    }
}
