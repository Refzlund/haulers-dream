using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>How a planned route picks the nearby same-kind targets around the clicked thing.
    /// Not every mode fits every kind of work — <see cref="RouteSelection.AllowedModes"/> decides per kind
    /// (chained/touching make no sense for scattered filth; rooms make no sense for an ore vein).</summary>
    public enum RouteMode
    {
        Radius,  // every same-kind target within a radius, capped at Amount
        Chained, // the nearest same-kind targets, bounded by the max-travel span
        Vein,    // the contiguous touching cluster (flood-fill), capped at Amount
        Rooms,   // every same-kind target inside the picked room(s), capped at Amount (cleaning's default)
        Zone,    // every same-kind target inside the growing zone the clicked thing is in, capped at Amount
    }

    /// <summary>
    /// Picks the set of same-kind targets a planned route should cover, around the clicked thing. "Same kind"
    /// is same <see cref="ThingDef"/> (a berry patch, a steel vein); harvest targets pass the ripeness gate.
    /// <list type="bullet">
    /// <item><b>Chained</b> takes the nearest targets up to <see cref="HardCap"/>; the max-travel span (applied
    /// later, in the planner) is what really bounds it.</item>
    /// <item><b>Radius</b> takes same-kind targets within a radius, capped at <paramref name="amount"/>.</item>
    /// <item><b>Vein</b> floods the contiguous cluster, capped at <paramref name="amount"/>.</item>
    /// </list>
    /// The result is in a prefix-stable order (sorted by distance to the clicked anchor, anchor first; or flood
    /// order for veins), which keeps the downstream max-travel truncation monotonic.
    /// </summary>
    public static class RouteSelection
    {
        /// <summary>Absolute ceiling on a route's length (keeps ordering + pathfinding + the job queue bounded), independent of the configurable Amount.</summary>
        public const int HardCap = 100;

        /// <summary>Amount sentinel for the slider's "All" step.</summary>
        public const int AllAmount = int.MaxValue;

        // Window (in cells, radius) used to bound the candidate set for Vein flood-fill.
        private const int VeinWindow = 60;

        /// <summary>
        /// Which selection modes make sense for this kind of work, default FIRST. The WORK decides the menu,
        /// not the thing:
        /// <list type="bullet">
        /// <item><b>Cleaning</b> (any-filth): Rooms (default) + Radius. "Chained"/"Touching" are meaningless for
        /// scattered stains — you clean a place, not a cluster.</item>
        /// <item><b>Deconstruct / uninstall</b> (designated-only): Chained + Radius. Vein is EXCLUDED structurally:
        /// its def-flood ignores the designated-only scope, and the executor designates every stop — flooding
        /// would auto-mark unmarked same-def buildings for deconstruction (destructive).</item>
        /// <item><b>Construction</b> (blueprints/frames): Chained + Radius. Vein's same-def flood would skip the
        /// gates/corners of a mixed-def run — the exact thing the Constructible scope exists to include.</item>
        /// <item><b>Plants</b> (harvest / cut): Chained, Vein, Radius — plus Zone when the clicked plant stands
        /// in a growing zone ("harvest this whole field").</item>
        /// <item><b>Mining and other same-def work</b>: Chained, Vein, Radius.</item>
        /// </list>
        /// </summary>
        public static List<RouteMode> AllowedModes(RouteWorkKind kind, Thing clicked, Map map)
        {
            var modes = new List<RouteMode>(4);
            switch (kind?.scope ?? RouteTargetScope.SameDef)
            {
                case RouteTargetScope.AnyFilth:
                    modes.Add(RouteMode.Rooms);
                    modes.Add(RouteMode.Radius);
                    return modes;
                case RouteTargetScope.DesignatedOnly:
                case RouteTargetScope.Constructible:
                    modes.Add(RouteMode.Chained);
                    modes.Add(RouteMode.Radius);
                    return modes;
                default:
                    modes.Add(RouteMode.Chained);
                    modes.Add(RouteMode.Vein);
                    modes.Add(RouteMode.Radius);
                    if (GrowingZoneFor(clicked, map) != null)
                        modes.Add(RouteMode.Zone);
                    return modes;
            }
        }

        /// <summary>The growing zone the clicked plant stands in, or null. Zone mode is only offered for a plant
        /// inside a growing zone (a wild bush has no zone; a mineable never does).</summary>
        public static Zone_Growing GrowingZoneFor(Thing clicked, Map map)
        {
            if (map == null || !(clicked is Plant) || !clicked.Spawned)
                return null;
            return map.zoneManager.ZoneAt(clicked.Position) as Zone_Growing;
        }

        /// <param name="amount">stop-count cap for Radius / Vein (or <see cref="AllAmount"/> for "All"); unused by Chained.</param>
        /// <param name="radius">circle radius in cells for Radius mode; unused otherwise.</param>
        /// <param name="allowHarvest">harvest mode only: also include nearby UNMARKED plants (which get designated for harvest).</param>
        /// <param name="growthThreshold">harvest mode only: an unmarked plant must be at least this % grown to be included.</param>
        /// <param name="capped">true when the natural selection was larger than the cap and got truncated.</param>
        /// <param name="fogCaution">Vein only: true when the visible cluster touches a hidden (fogged) same-kind cell — the
        /// vein continues into unexplored fog, so not all of it is shown (and the deferred-reveal tracker may extend it later).</param>
        /// <param name="mustInclude">targets the player explicitly picked (plus the clicked anchor) that MUST be in
        /// the route — always kept (never trimmed by amount/travel), bypass the growth threshold, placed first.</param>
        /// <param name="mustIncludeCount">how many forced targets lead the result (the planner frees these from the budget).</param>
        /// <param name="roomAnchors">Rooms mode only: cells whose rooms bound the selection (the clicked target's
        /// own room is always counted, so null/empty = just that room).</param>
        public static List<Thing> Select(Pawn pawn, Thing clicked, RouteWorkKind kind, RouteMode mode, int amount,
            int radius, bool allowHarvest, int growthThreshold, IReadOnlyList<Thing> mustInclude,
            out bool capped, out bool fogCaution, out int mustIncludeCount,
            IReadOnlyList<IntVec3> roomAnchors = null)
        {
            capped = false;
            fogCaution = false;
            mustIncludeCount = 0;
            var result = new List<Thing>();
            if (pawn?.Map == null || clicked == null || !clicked.Spawned || kind == null)
                return result;

            // GUARD: never run a mode the kind doesn't allow (a stale per-def pref, or a caller bug). Vein on a
            // designated-only kind would def-flood past the scope and the executor would then DESIGNATE the flood —
            // auto-marking unmarked buildings for deconstruction. Coerce to the kind's default instead.
            var allowed = AllowedModes(kind, clicked, pawn.Map);
            if (!allowed.Contains(mode))
                mode = allowed[0];

            // MUST-INCLUDE prefix: the clicked anchor + any picked targets. Always kept, anchor first.
            var forced = BuildForced(pawn, clicked, kind, mustInclude);
            mustIncludeCount = forced.Count;
            var forcedSet = new HashSet<Thing>(forced);

            List<Thing> auto;
            switch (mode)
            {
                case RouteMode.Radius: auto = SelectRadius(pawn, clicked, kind, allowHarvest, growthThreshold, radius); break;
                case RouteMode.Chained: auto = SelectChained(pawn, clicked, kind, allowHarvest, growthThreshold); break;
                case RouteMode.Vein: auto = SelectVein(pawn, clicked, kind, allowHarvest, growthThreshold, out fogCaution); break;
                case RouteMode.Rooms: auto = SelectRooms(pawn, clicked, kind, allowHarvest, growthThreshold, roomAnchors); break;
                case RouteMode.Zone: auto = SelectZone(pawn, clicked, kind, allowHarvest, growthThreshold); break;
                default: auto = new List<Thing>(); break;
            }
            auto.RemoveAll(t => forcedSet.Contains(t)); // a picked target is already in the forced prefix — no dup

            // Don't poach a resource another colony pawn is already assigned to (it's the target of their current
            // or queued job — e.g. a route a different pawn already planned). Two pawns walking to the same bush
            // wastes a trip. A shared harvest DESIGNATION can't tell the difference (a marked-but-unassigned plant
            // is fair game; an already-queued one is not), so we key off the actual JOB targets — robust regardless
            // of reservation timing (our routes EnqueueLast a job per stop, so each claimed stop is a queued target).
            var claimedByOthers = ClaimedByOtherPawns(pawn);
            if (claimedByOthers.Count > 0)
                auto.RemoveAll(t => claimedByOthers.Contains(t));

            // Cap the AUTO tail only (forced targets are extra). Chained takes the absolute HardCap (the max-travel
            // span bounds it downstream); Radius/Vein/Rooms/Zone take the chosen Amount. Leave room under HardCap
            // for the forced.
            // NOTE (smart-routing dense-map limitation): this pool is the HardCap nearest to the CLICKED ANCHOR. The
            // storage-aware selection downstream (RoutePlanner: route THROUGH storage so way-back stops are cheap)
            // can only value stops that are IN this pool. On a DENSE field (>HardCap same-kind plants packed around
            // the anchor), a "quick win" bush on the way back to storage but farther from the anchor than the
            // HardCap-th plant is cut here before the planner ever sees it. Sparse fields (the common case) keep the
            // whole field, so it's fully covered. Widening this to an anchor+storage-aware ranking is a follow-on.
            int autoCap = mode == RouteMode.Chained ? HardCap : EffectiveAmount(amount);
            int room = HardCap - Math.Min(forced.Count, HardCap);
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

        // The must-include prefix: clicked anchor first, then each player-picked target that is a valid same-kind
        // target (deduped). Reachability is NOT filtered here — ComputeLegs does that, so an unreachable pick is
        // surfaced as "can't be reached" like any other unreachable target rather than silently vanishing.
        private static List<Thing> BuildForced(Pawn pawn, Thing clicked, RouteWorkKind kind, IReadOnlyList<Thing> mustInclude)
        {
            var forced = new List<Thing>();
            var seen = new HashSet<Thing>();
            if (clicked != null && clicked.Spawned) { forced.Add(clicked); seen.Add(clicked); }
            if (mustInclude != null)
                for (int i = 0; i < mustInclude.Count; i++)
                {
                    var t = mustInclude[i];
                    if (t == null || seen.Contains(t))
                        continue;
                    if (IsValidRouteTarget(pawn, clicked, kind, t)) { forced.Add(t); seen.Add(t); }
                }
            return forced;
        }

        /// <summary>
        /// Whether <paramref name="t"/> is a valid same-kind target for a route around <paramref name="clicked"/>:
        /// same def, spawned, not forbidden, and (for a harvest route) currently harvestable. Used to validate a
        /// player's manual "must include" pick. Does NOT apply the growth threshold (an explicit pick bypasses it,
        /// exactly like the clicked anchor) and does NOT check reachability (the planner does).
        /// </summary>
        public static bool IsValidRouteTarget(Pawn pawn, Thing clicked, RouteWorkKind kind, Thing t)
        {
            if (t == null || !t.Spawned || clicked == null)
                return false;
            if (!MatchesScope(pawn?.Map ?? clicked.Map, clicked, kind, t))
                return false;
            if (pawn != null && t.IsForbidden(pawn))
                return false;
            if (kind?.designation == DesignationDefOf.HarvestPlant && t is Plant p && !p.HarvestableNow)
                return false;
            if (pawn != null && IsClaimedByOtherPawn(pawn, t))
                return false; // already an order for another pawn — picking it would poach their route
            return true;
        }

        /// <summary>Is <paramref name="t"/> "the same kind of target" as the clicked thing under the kind's scope?
        /// (See <see cref="RouteTargetScope"/> — the WORK decides the grouping, not the ThingDef.)</summary>
        private static bool MatchesScope(Map map, Thing clicked, RouteWorkKind kind, Thing t)
        {
            switch (kind?.scope ?? RouteTargetScope.SameDef)
            {
                case RouteTargetScope.AnyFilth:
                    return t is Filth;
                case RouteTargetScope.Constructible:
                    return (t is Blueprint || t is Frame) && t.Faction == clicked.Faction;
                case RouteTargetScope.DesignatedOnly:
                    return kind.designation != null && map != null
                           && map.designationManager.DesignationOn(t, kind.designation) != null;
                case RouteTargetScope.SameDefOrDesignated:
                    if (t.def == clicked.def)
                        return true;
                    if (kind.designation == null || map == null)
                        return false;
                    if (kind.designation == DesignationDefOf.Mine)
                        return map.designationManager.DesignationAt(t.Position, DesignationDefOf.Mine) != null
                               || map.designationManager.DesignationAt(t.Position, DesignationDefOf.MineVein) != null;
                    return map.designationManager.DesignationOn(t, kind.designation) != null;
                default:
                    return t.def == clicked.def;
            }
        }

        // Per-(pawn, tick) memo for ClaimedByOtherPawns. The bulk-haul snowball, the work-spot nearby-sweep, the
        // transporter sweep, and the route picker all build this same set, and on a busy tick several of them run
        // for the SAME pawn — each scan walks every colony pawn's whole job queue, so recomputing per call was
        // pure waste. One entry per thread (matches BulkHaul.planCache); a different pawn or a new tick recomputes.
        [System.ThreadStatic] private static Pawn claimedCachePawn;
        [System.ThreadStatic] private static int claimedCacheTick;
        [System.ThreadStatic] private static HashSet<Thing> claimedCacheSet;

        // Self-register the per-(pawn,tick) claimed-by-others memo clear with the game-load hygiene sweep (see
        // CacheRegistry), so it is cleared DIRECTLY on load — previously it was only cleared transitively (via
        // BulkHaul.ClearPlanCache calling it), a fragile coupling. The static ctor runs once, the first time any
        // RouteSelection member is touched (the only way the memo can hold cross-session data); ClearClaimedCache
        // resets the FinalizeInit (main) thread's slot, and the pawn-identity check is the actual correctness
        // safeguard (a deserialized pawn is a fresh instance that can't match a stale entry).
        static RouteSelection() => CacheRegistry.Register(ClearClaimedCache);

        // Shared read-only empty result for the null-guard early return. NO CALLER MUTATES the handed-out
        // set (every use is Contains/Count — verified across BulkHaul, YieldRouter, TransportLoad, and the
        // route picker), so one shared instance is safe and avoids a per-scan throwaway allocation.
        private static readonly HashSet<Thing> EmptyClaimSet = new HashSet<Thing>();

        /// <summary>Drop the main thread's per-(pawn,tick) memo on game load (sibling of BulkHaul.ClearPlanCache),
        /// so it never retains a dead session's Thing references. Correctness is already guaranteed by the
        /// pawn-identity check (a deserialized pawn is a fresh instance that can't match a stale entry); this is
        /// hygiene only — release the references promptly.</summary>
        internal static void ClearClaimedCache()
        {
            claimedCachePawn = null;
            claimedCacheSet = null;
            claimedCacheTick = -1;
        }

        // Things that are the target of another colony pawn's current or queued job — i.e. already an "order" for
        // someone else, which a new route must not include. We scan job targets (targetA/B/C plus the targetQueues
        // that a multi-target vanilla job uses) rather than the designation manager, because a designation is global
        // (any pawn may take it) while a job target is a specific pawn's commitment. Our own routes queue one job
        // per stop, so every stop another pawn's route claimed is a queued target here.
        //
        // NO CALLER MUTATES the returned set (every use is Contains/Count — verified across BulkHaul, YieldRouter,
        // TransportLoad, and the route picker), so a same-tick re-request safely shares one instance. The memo is
        // keyed on TicksGame, which FREEZES while paused; the route picker runs per-frame WHILE PAUSED and the
        // player can queue an order mid-pause (changing a job queue with no tick advance), so paused requests
        // bypass the cache and recompute fresh — exactly the BulkHaul plan-cache paused-bypass rationale.
        internal static HashSet<Thing> ClaimedByOtherPawns(Pawn pawn)
        {
            if (pawn?.Map == null || pawn.Faction == null)
                return EmptyClaimSet;

            bool paused = Find.TickManager?.Paused ?? false;
            int tick = Find.TickManager?.TicksGame ?? -1;
            if (!paused && claimedCacheSet != null && claimedCachePawn == pawn && claimedCacheTick == tick)
                return claimedCacheSet;

            var set = new HashSet<Thing>();
            var pawns = pawn.Map.mapPawns.SpawnedPawnsInFaction(pawn.Faction);
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == pawn || p.jobs == null)
                    continue;
                // ANIMALS' jobs are not work claims: a tame deer Ingest-ing a berry bush would otherwise exclude
                // that bush from every route AND make the must-include picker refuse the player's explicit click.
                if (p.RaceProps != null && p.RaceProps.Animal)
                    continue;
                CollectJobTargets(set, p.CurJob);
                var q = p.jobs.jobQueue;
                if (q != null)
                    for (int k = 0; k < q.Count; k++)
                        CollectJobTargets(set, q[k]?.job);
            }

            if (!paused)
            {
                claimedCachePawn = pawn;
                claimedCacheTick = tick;
                claimedCacheSet = set;
            }
            return set;
        }

        // Whether one specific thing is claimed by another pawn (per-call; used by the must-include validator, which
        // runs per-frame while the picker is open). Builds the same set ClaimedByOtherPawns does — cheap (bounded by
        // the colony's total queued work) and only live while picking.
        private static bool IsClaimedByOtherPawn(Pawn pawn, Thing t)
            => t != null && ClaimedByOtherPawns(pawn).Contains(t);

        private static void CollectJobTargets(HashSet<Thing> set, Job job)
        {
            if (job == null)
                return;
            if (job.targetA.Thing != null) set.Add(job.targetA.Thing);
            if (job.targetB.Thing != null) set.Add(job.targetB.Thing);
            if (job.targetC.Thing != null) set.Add(job.targetC.Thing);
            AddQueueTargets(set, job.targetQueueA);
            AddQueueTargets(set, job.targetQueueB);
        }

        private static void AddQueueTargets(HashSet<Thing> set, List<LocalTargetInfo> q)
        {
            if (q == null)
                return;
            for (int i = 0; i < q.Count; i++)
                if (q[i].Thing != null) set.Add(q[i].Thing);
        }

        /// <summary>The Amount slider value resolved to a concrete cap (All → <see cref="HardCap"/>; clamped to HardCap).</summary>
        public static int EffectiveAmount(int amount)
            => amount == AllAmount || amount <= 0 ? HardCap : Math.Min(amount, HardCap);

        private static List<Thing> SelectRadius(Pawn pawn, Thing clicked, RouteWorkKind kind, bool allowHarvest, int threshold, int radius)
        {
            float r = radius <= 0 ? 1 : radius;
            var hits = new List<Thing>();
            foreach (var t in SameKind(pawn, clicked, kind, allowHarvest, threshold))
                if ((t.Position - clicked.Position).LengthHorizontal <= r)
                    hits.Add(t);
            SortByDistanceTo(hits, clicked.Position);
            EnsureAnchorFirst(hits, clicked);
            return hits;
        }

        private static List<Thing> SelectChained(Pawn pawn, Thing clicked, RouteWorkKind kind, bool allowHarvest, int threshold)
        {
            var hits = new List<Thing>(SameKind(pawn, clicked, kind, allowHarvest, threshold));
            SortByDistanceTo(hits, clicked.Position);
            EnsureAnchorFirst(hits, clicked);
            return hits; // bounded to HardCap (and then by the max-travel span) in Select / the planner
        }

        private static List<Thing> SelectVein(Pawn pawn, Thing clicked, RouteWorkKind kind, bool allowHarvest, int threshold, out bool fogCaution)
        {
            // Flood one past the hard cap so Select can tell whether the vein overflowed (→ capped). Only VISIBLE
            // (non-fogged) cells count; the clicked thing is always eligible, others pass the work/harvest gate.
            var hits = FloodVisibleVein(pawn.Map, pawn, clicked.def, clicked.Position, HardCap + 1,
                t => t == clicked || IncludeForHarvest(pawn.Map, t, kind, allowHarvest, threshold), out fogCaution);
            EnsureAnchorFirst(hits, clicked);
            return hits;
        }

        // Rooms mode: same-kind targets standing in any of the picked rooms. Rooms are resolved from ANCHOR
        // CELLS at call time (Room objects regenerate whenever regions rebuild — a wall built mid-preview must
        // not leave the dialog holding dead references), deduped by Room.ID; the clicked target's own room is
        // always included even when the caller passes no anchors. NOTE: the open outdoors is one big map-edge
        // room — picking it makes the room filter near-meaningless there, so the Amount cap (nearest-first) is
        // what bounds an outdoor selection.
        private static List<Thing> SelectRooms(Pawn pawn, Thing clicked, RouteWorkKind kind, bool allowHarvest,
            int threshold, IReadOnlyList<IntVec3> roomAnchors)
        {
            var map = pawn.Map;
            var roomIds = new HashSet<int>();
            var clickedRoom = clicked.Position.GetRoom(map);
            if (clickedRoom != null)
                roomIds.Add(clickedRoom.ID);
            if (roomAnchors != null)
                for (int i = 0; i < roomAnchors.Count; i++)
                {
                    var c = roomAnchors[i];
                    if (!c.IsValid || !c.InBounds(map))
                        continue;
                    var r = c.GetRoom(map);
                    if (r != null)
                        roomIds.Add(r.ID);
                }
            var hits = new List<Thing>();
            if (roomIds.Count == 0)
                return hits; // doorless void (shouldn't happen for spawned filth) — nothing to bound by
            foreach (var t in SameKind(pawn, clicked, kind, allowHarvest, threshold))
            {
                var r = t.Position.GetRoom(map);
                if (r != null && roomIds.Contains(r.ID))
                    hits.Add(t);
            }
            SortByDistanceTo(hits, clicked.Position);
            EnsureAnchorFirst(hits, clicked);
            return hits;
        }

        // Zone mode: same-kind targets standing in the growing zone the clicked plant is in ("this whole field").
        // The harvest ripeness / allow-unmarked gate still applies via SameKind, so an unripe corner of the zone
        // isn't routed.
        private static List<Thing> SelectZone(Pawn pawn, Thing clicked, RouteWorkKind kind, bool allowHarvest, int threshold)
        {
            var hits = new List<Thing>();
            var zone = GrowingZoneFor(clicked, pawn.Map);
            if (zone == null)
                return hits;
            var zm = pawn.Map.zoneManager;
            foreach (var t in SameKind(pawn, clicked, kind, allowHarvest, threshold))
                if (zm.ZoneAt(t.Position) == zone)
                    hits.Add(t);
            SortByDistanceTo(hits, clicked.Position);
            EnsureAnchorFirst(hits, clicked);
            return hits;
        }

        /// <summary>
        /// Re-floods the VISIBLE part of a mining vein from <paramref name="seed"/> — used by the deferred-reveal
        /// tracker as fog clears while a pawn mines. Returns the contiguous visible cluster as mineable Things in
        /// flood order (capped), and reports whether it still touches fogged same-def cells (more may yet reveal).
        /// </summary>
        public static List<Thing> ReFloodVisibleVein(Map map, Pawn pawn, ThingDef def, IntVec3 seed, int cap,
            IEnumerable<IntVec3> routeFootprint, out bool fogCaution)
        {
            fogCaution = false;
            if (map == null || def == null)
                return new List<Thing>();
            // The route's OWN covered cells act as VIRTUAL connectors: mined cells despawn (vanishing from
            // same-def membership) yet they are exactly the path the flood must cross to reach the rocks the
            // mining just revealed — without them, mining the seed empties the flood and mining any in-between
            // cell disconnects it, silently killing the deferred reveal on every snake-shaped vein.
            var connectors = new HashSet<RouteCell> { new RouteCell(seed.x, seed.z) };
            if (routeFootprint != null)
                foreach (var c in routeFootprint)
                    connectors.Add(new RouteCell(c.x, c.z));
            return FloodVisibleVein(map, pawn, def, seed, cap <= 0 ? HardCap : cap, _ => true, out fogCaution, connectors);
        }

        // Builds same-def vein membership within the window around the seed, splitting it into VISIBLE cells (the
        // route candidates) and FOGGED cells (hidden — excluded so the count can't be abused to peek into an
        // unexplored vein), floods the visible cluster from the seed, and flags caution when the visible cluster
        // touches a fogged cell (the vein keeps going into the dark). The clicked seed passes via the gate.
        // Optional `connectors` are membership-only cells (the deferred tracker's mined route footprint + seed):
        // they keep the flood CONNECTED across despawned cells but never become route candidates themselves.
        private static List<Thing> FloodVisibleVein(Map map, Pawn pawn, ThingDef def, IntVec3 seed, int cap,
            Func<Thing, bool> candidateGate, out bool fogCaution, HashSet<RouteCell> connectors = null)
        {
            fogCaution = false;
            var byCell = new Dictionary<RouteCell, Thing>();
            var visible = new HashSet<RouteCell>();
            var fogged = new HashSet<RouteCell>();
            var all = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || !t.Spawned)
                    continue;
                if ((t.Position - seed).LengthHorizontal > VeinWindow)
                    continue;
                var cell = new RouteCell(t.Position.x, t.Position.z);
                if (map.fogGrid.IsFogged(t.Position))
                {
                    fogged.Add(cell); // hidden same-kind cell — never counted, only used for the caution flag
                    continue;
                }
                if (t.IsForbidden(pawn) || !candidateGate(t))
                    continue;
                if (!byCell.ContainsKey(cell))
                {
                    byCell[cell] = t;
                    visible.Add(cell);
                }
            }
            var seedCell = new RouteCell(seed.x, seed.z);
            var members = visible;
            int floodCap = cap;
            if (connectors != null && connectors.Count > 0)
            {
                members = new HashSet<RouteCell>(visible);
                members.UnionWith(connectors);
                // Connectors occupy flood slots but aren't candidates — widen the flood cap so they can't
                // starve the real cells out of the budget (the candidate cap is re-applied on `hits` below).
                floodCap = cap > 0 ? cap + connectors.Count : cap;
            }
            var flood = VeinFloodMath.FloodOrder(members, seedCell, floodCap);
            fogCaution = VeinFloodMath.AnyAdjacent(flood, fogged);

            var hits = new List<Thing>(flood.Count);
            foreach (var c in flood)
            {
                if (byCell.TryGetValue(c, out var t))
                    hits.Add(t);
                if (cap > 0 && hits.Count >= cap)
                    break;
            }
            return hits;
        }

        // Every spawned, non-forbidden candidate IN THE KIND'S SCOPE that is a valid target for this work (and,
        // for harvesting, passes the ripeness / allow-unmarked gate). "Same kind" is scope-dependent — same def
        // for harvest, ANY filth for cleaning, ANY marked thing for deconstruct, ANY blueprint/frame for
        // construction (see RouteTargetScope). Reachability is NOT filtered here — the planner (ComputeLegs)
        // does that per-stop so unreachable candidates still count toward selectedCount for the UI.
        private static IEnumerable<Thing> SameKind(Pawn pawn, Thing clicked, RouteWorkKind kind, bool allowHarvest, int threshold)
        {
            var seen = new HashSet<Thing>();
            foreach (var t in ScopeCandidates(pawn.Map, clicked, kind))
            {
                if (t == null || !t.Spawned || t.IsForbidden(pawn) || !seen.Add(t))
                    continue;
                if (t != clicked && !IncludeForHarvest(pawn.Map, t, kind, allowHarvest, threshold))
                    continue;
                yield return t;
            }
        }

        // Raw candidate enumeration per scope. Same-def scopes use the def lister; designation scopes walk the
        // designation manager (so a marked door joins a marked wall's deconstruct route); filth/blueprints use
        // their thing-request groups.
        private static IEnumerable<Thing> ScopeCandidates(Map map, Thing clicked, RouteWorkKind kind)
        {
            switch (kind.scope)
            {
                case RouteTargetScope.AnyFilth:
                    foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Filth))
                        yield return t;
                    yield break;

                case RouteTargetScope.Constructible:
                    foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
                        if (t.Faction == clicked.Faction)
                            yield return t;
                    foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                        if (t.Faction == clicked.Faction)
                            yield return t;
                    yield break;

                case RouteTargetScope.DesignatedOnly:
                    if (kind.designation != null)
                        foreach (var d in map.designationManager.SpawnedDesignationsOfDef(kind.designation))
                            if (d.target.HasThing)
                                yield return d.target.Thing;
                    yield break;

                case RouteTargetScope.SameDefOrDesignated:
                    foreach (var t in map.listerThings.ThingsOfDef(clicked.def))
                        yield return t;
                    if (kind.designation == DesignationDefOf.Mine)
                    {
                        // Mining designations live on CELLS — surface the mineable at each designated cell.
                        // Mine ∪ MineVein, mirroring vanilla MineAIUtility.PotentialMineables.
                        foreach (var d in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine))
                        {
                            var rock = d.target.Cell.GetFirstMineable(map);
                            if (rock != null)
                                yield return rock;
                        }
                        foreach (var d in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.MineVein))
                        {
                            var rock = d.target.Cell.GetFirstMineable(map);
                            if (rock != null)
                                yield return rock;
                        }
                    }
                    else if (kind.designation != null)
                    {
                        foreach (var d in map.designationManager.SpawnedDesignationsOfDef(kind.designation))
                            if (d.target.HasThing)
                                yield return d.target.Thing;
                    }
                    yield break;

                default: // SameDef
                    foreach (var t in map.listerThings.ThingsOfDef(clicked.def))
                        yield return t;
                    yield break;
            }
        }

        // Harvest mode: a plant must be harvestable now; an already-marked plant is always included, while an
        // UNMARKED plant is included only when "allow harvest" is on and it's at least the growth threshold grown
        // (so the route can pull in a whole patch of ripe-enough bushes, not just the ones you'd hand-marked).
        // Cut / mine / deconstruct accept any same-def thing (no growth concept).
        private static bool IncludeForHarvest(Map map, Thing t, RouteWorkKind kind, bool allowHarvest, int threshold)
        {
            if (kind.designation != DesignationDefOf.HarvestPlant || !(t is Plant p))
                return true;
            if (!p.HarvestableNow)
                return false; // can't route-harvest a plant that isn't ripe yet
            if (map.designationManager.DesignationOn(t, DesignationDefOf.HarvestPlant) != null)
                return true;  // already marked for harvest → always include
            return allowHarvest && p.Growth >= threshold / 100f;
        }

        // Sort purely by distance to the clicked anchor — MARKED and UNMARKED targets ranked EQUALLY, so
        // "Chained — the nearest few" really takes the nearest few. (This used to put marked plants FIRST to keep
        // the allow-unmarked toggle strictly monotone; but that ranked a NEARBY unmarked bush behind FARTHER
        // marked ones, so the travel budget trimmed it — the exact opposite of what "allow harvesting unmarked
        // plants" promises. With the route-length budget [RouteBudget], turning allow-unmarked on now adds the
        // nearby unmarked stops by proximity rather than dropping a marked one. The thingIDNumber tiebreak keeps
        // the order deterministic — List.Sort is unstable — so the budget's gather-order prefix stays stable.)
        private static void SortByDistanceTo(List<Thing> things, IntVec3 anchor)
        {
            // Struct IComparer holding the anchor in a field — no capturing closure / delegate alloc per
            // sort (the picker paths run this per-frame while a route dialog is open).
            things.Sort(new ByDistanceToComparer(anchor));
        }

        /// <summary>Allocation-free distance comparator: ranks two Things by squared distance to the
        /// anchor, then by thingIDNumber, via the pure <see cref="RouteOrdering.CompareMarkedFirst"/>
        /// with both "marked" flags false (so it degrades to distance-then-id). Holds the anchor in a
        /// field, so a sort allocates no closure.</summary>
        private struct ByDistanceToComparer : IComparer<Thing>
        {
            private readonly IntVec3 anchor;
            public ByDistanceToComparer(IntVec3 anchor) { this.anchor = anchor; }

            public int Compare(Thing a, Thing b)
            {
                long ad = (a.Position - anchor).LengthHorizontalSquared;
                long bd = (b.Position - anchor).LengthHorizontalSquared;
                // marked flags both false → CompareMarkedFirst sorts by squared distance, then by id.
                return RouteOrdering.CompareMarkedFirst(false, ad, a.thingIDNumber, false, bd, b.thingIDNumber);
            }
        }

        private static void EnsureAnchorFirst(List<Thing> things, Thing anchor)
        {
            int idx = things.IndexOf(anchor);
            if (idx > 0) { things.RemoveAt(idx); things.Insert(0, anchor); }
            else if (idx < 0) things.Insert(0, anchor);
        }
    }
}
