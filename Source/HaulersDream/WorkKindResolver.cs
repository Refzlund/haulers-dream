using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The kind of work a clicked thing represents, for the "plan prioritized route" feature: the vanilla
    /// WorkGiver that produces the job (so the route reuses the exact prioritize path the float menu uses),
    /// the designation that makes a same-kind target eligible (so the route can designate nearby objects),
    /// the gerund for the menu label, and the harvested/mined product (for storage anchoring).
    /// </summary>
    /// <summary>
    /// What counts as "the same kind of target" for a route. The route planner originally grouped by ThingDef —
    /// right for a berry patch or a steel vein, WRONG wherever the WORK is the unit rather than the thing:
    /// cleaning one bloodstain must route ALL filth (dirt AND blood), deconstructing one marked wall must route
    /// every marked thing (and never auto-mark unmarked same-def buildings!), a construction route over a fence
    /// line must include its gates/corners (different blueprint defs).
    /// </summary>
    public enum RouteTargetScope
    {
        SameDef,            // same ThingDef (harvest: group by crop)
        SameDefOrDesignated,// same def (expandable) OR already carrying the work designation (mine, cut)
        DesignatedOnly,     // ONLY things already carrying the designation — never marks new ones (deconstruct, uninstall)
        AnyFilth,           // any filth whatsoever (cleaning)
        Constructible,      // any player blueprint or frame (construction)
    }

    public sealed class RouteWorkKind
    {
        public WorkGiver_Scanner scanner;
        public DesignationDef designation; // null = no designation needed (already-eligible work)
        public string gerund;
        public ThingDef yieldDef;          // the product, for smart-routing storage anchor; may be null
        public RouteTargetScope scope = RouteTargetScope.SameDef;
    }

    /// <summary>
    /// Resolves a clicked Thing to its <see cref="RouteWorkKind"/> the same way vanilla's float menu does:
    /// walk every directly-orderable WorkGiver_Scanner in work-type priority order and take the first that
    /// has a forced job on the thing (harvest/cut a designated plant, mine a designated vein, deconstruct,
    /// …). Returns null when the thing isn't routable. No designation is added here — only inspected.
    /// </summary>
    public static class WorkKindResolver
    {
        // #138 perf — per-tick memo of the resolve result. Resolve walks EVERY directly-orderable WorkGiver_Scanner
        // (all work types × their givers, each doing a validity/reach probe) for a clicked thing, and the route
        // planner's float-menu provider resolves this for every thing under the cursor when a menu is built. A
        // right-click that re-opens the menu on the same tick, or a second planner provider probing the same thing,
        // otherwise re-runs the whole scanner walk. Memoize (result, keyed on TicksGame + pawn + thing) so a given
        // (pawn, thing) is resolved at most once per tick. RouteWorkKind holds only Def singletons + strings (no Thing
        // refs — verified), so a cached value is safe to reuse; the tick stamp self-clears each tick and the
        // `tick != -1` populate guard is the cross-session safeguard (registered with CacheRegistry on load).
        [System.ThreadStatic] private static int resolveCacheTick;
        [System.ThreadStatic] private static System.Collections.Generic.Dictionary<long, RouteWorkKind> resolveCache;

        static WorkKindResolver() => CacheRegistry.Register(ClearResolveCache);

        /// <summary>Drop the per-tick resolve memo (FinalizeInit hygiene; the tick-stamp + <c>tick != -1</c> guard is
        /// the real cross-session safeguard). Clears only this thread's slot.</summary>
        public static void ClearResolveCache()
        {
            resolveCache?.Clear();
            resolveCacheTick = -1;
        }

        /// <summary>Resolve a clicked Thing to its <see cref="RouteWorkKind"/>, memoized per (tick, pawn, thing) — see
        /// the class remarks. Behaviour-identical to <see cref="ResolveUncached"/>; only repeated same-tick resolves
        /// of the same (pawn, thing) are short-circuited.</summary>
        public static RouteWorkKind Resolve(Pawn pawn, Thing clicked)
        {
            if (pawn == null || clicked == null)
                return null;
            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick == -1)
                return ResolveUncached(pawn, clicked);
            var cache = resolveCache ?? (resolveCache = new System.Collections.Generic.Dictionary<long, RouteWorkKind>());
            if (tick != resolveCacheTick)
            {
                cache.Clear();
                resolveCacheTick = tick;
            }
            long key = ((long)pawn.thingIDNumber << 32) | (uint)clicked.thingIDNumber;
            if (cache.TryGetValue(key, out var cached))
                return cached;
            var result = ResolveUncached(pawn, clicked);
            cache[key] = result;
            return result;
        }

        private static RouteWorkKind ResolveUncached(Pawn pawn, Thing clicked)
        {
            if (pawn?.Map == null || clicked == null || !clicked.Spawned || clicked.Map != pawn.Map)
                return null;
            if (pawn.thinker?.TryGetMainTreeThinkNode<JobGiver_Work>() == null)
                return null;

            // Construction targets (blueprints/frames) resolve up front, BEFORE the workable-now WorkGiver loop:
            // they're their own work markers (no designation to add), and a route over them should be plannable
            // even when materials aren't deliverable this instant. Vanilla hides "Prioritize constructing" then
            // (HasJobOnThing needs reachable resources), but a player wants to queue the build of a whole run
            // regardless — the route then queues each stop as materials become available.
            var construct = TryResolveConstruction(pawn, clicked);
            if (construct != null)
                return construct;

            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int w = 0; w < workTypes.Count; w++)
            {
                var givers = workTypes[w].workGiversByPriority;
                for (int g = 0; g < givers.Count; g++)
                {
                    var wgDef = givers[g];
                    if (!wgDef.directOrderable)
                        continue;
                    if (pawn.Drafted && !wgDef.canBeDoneWhileDrafted)
                        continue;
                    // Respect work incapability ("incapable of dumb labor" etc.) exactly like vanilla's own
                    // "Prioritize…" does: a pawn that won't do cleaning gets no "Plan prioritized cleaning".
                    // The "all pawns can haul/clean/cut plants" overrides flow through automatically — they
                    // make WorkTypeIsDisabled itself return false (see WorkOverride).
                    if (wgDef.workType != null && WorkCapabilityProbe.IsDisabled(pawn, wgDef.workType))
                        continue;
                    if (!(wgDef.Worker is WorkGiver_Scanner scanner))
                        continue;
                    // A workbench/bill station is a DoBill scanner target — but a "route" over a stationary bench is
                    // nonsensical (you stand at it and craft). Those stations get the dedicated crafting planner
                    // (Dialog_PlanCraft / FloatMenuOptionProvider_PlanCraft) instead, so never offer a route for them.
                    // WorkGiver_Researcher is skipped for the same "not a route" reason plus vanilla parity: research
                    // is non-prioritizable in vanilla, and a research "route" is degenerate (a single research bench).
                    // Excluding it at this single resolver chokepoint also blocks remembered-route replay of research
                    // (ResolveById re-runs Resolve), so no Intellectual-capable pawn ever gets research force-queued.
                    if (scanner is WorkGiver_DoBill || scanner is WorkGiver_Researcher)
                        continue;

                    // This loop probes ARBITRARY third-party WorkGivers (an open, mod-extensible contract), so
                    // this is the one place a catch is justified — but it must NOT hide the fault. A throw here
                    // is another mod's WorkGiver bug; surface it LOUDLY as a red error naming the culprit (never
                    // a Warning/swallow), then skip only that giver so one broken mod can't abort the whole
                    // route resolve (or break the vanilla right-click menu this provider feeds).
                    // The gerund is also read from this same arbitrary scanner, so it lives INSIDE the boundary
                    // too — otherwise a third-party scanner throwing from PostProcessedGerund would escape the
                    // per-giver guard and abort the whole resolve.
                    Job job;
                    string gerund;
                    try
                    {
                        if (ScannerSkips(pawn, scanner, clicked))
                            continue;
                        if (!scanner.HasJobOnThing(pawn, clicked, forced: true))
                            continue;
                        job = scanner.JobOnThing(pawn, clicked, forced: true);
                        if (job == null)
                            continue;
                        gerund = scanner.PostProcessedGerund(job);
                    }
                    catch (Exception e)
                    {
                        HDLog.Err($"WorkGiver '{wgDef.defName}' threw while resolving a route "
                                  + $"for {clicked} — skipping it (report to that mod's author): {e}");
                        continue;
                    }

                    var designation = DetermineDesignation(pawn.Map, clicked);
                    return new RouteWorkKind
                    {
                        scanner = scanner,
                        designation = designation,
                        gerund = gerund,
                        yieldDef = ResolveYield(clicked),
                        scope = ScopeFor(scanner, designation),
                    };
                }
            }

            // Fallback: a ripe plant the player hasn't marked for harvest yet. No WorkGiver produces a job for it
            // (there's no designation), so the loop above finds nothing — but you should still be able to plan a
            // route from a harvestable patch without hand-marking it first. Synthesize the harvest kind; the dialog
            // designates the clicked plant (and, with "allow harvest" on, nearby ripe ones) before queueing.
            return TryResolveUnmarkedHarvest(pawn, clicked);
        }

        // ── Stable id ↔ RouteWorkKind (for Multiplayer command replay) ───────────────────────────────────────
        // The route-execute command must cross the MP wire, but RouteWorkKind holds a live WorkGiver_Scanner (not
        // serializable). The portable stand-in is the resolved scanner's WorkGiverDef.defName: a def name is a
        // stable, identical token on every client (defs load in the same order from the same content), and the
        // scanner — plus the designation/scope/gerund/yield the kind also carries — is fully RE-DERIVABLE from the
        // pawn + clicked thing via Resolve(), whose every input (work priorities, designations, reachability) is
        // synced game state. So we ship just the defName and reconstruct the whole kind per client. This mirrors
        // MiningKind(ThingDef), which already reconstructs the mining kind from a def alone for the (un-serializable)
        // vein tracker — same "rebuild the live kind from a portable key" pattern.

        /// <summary>
        /// A portable, MP-serializable id for a resolved <see cref="RouteWorkKind"/> — the scanner's
        /// <see cref="WorkGiverDef.defName"/>. Identical on every client (defs are content, loaded in the same
        /// order), and round-trips through <see cref="ResolveById"/>. Returns null for a null/incomplete kind.
        /// </summary>
        public static string WorkKindId(RouteWorkKind kind) => kind?.scanner?.def?.defName;

        /// <summary>
        /// Reconstructs the full <see cref="RouteWorkKind"/> for a previously-resolved work-giver id against the
        /// clicked thing's CURRENT state — the MP-replay counterpart of <see cref="WorkKindId"/>. We re-run the
        /// normal <see cref="Resolve"/> (so the designation/scope/gerund/yield are derived by the exact same logic,
        /// not a duplicated copy) and confirm it produced the SAME scanner the issuing client sent. The id check is
        /// a determinism guard: every client re-resolving the same synced state yields the same scanner, so a
        /// mismatch means the world diverged between plan and execute (e.g. the designation was cancelled), in which
        /// case we return null and the executor no-ops rather than queue a different route than was planned. Returns
        /// null when the id is empty or the thing is no longer routable.
        /// </summary>
        public static RouteWorkKind ResolveById(Pawn pawn, Thing clicked, string workGiverDefName)
        {
            if (string.IsNullOrEmpty(workGiverDefName))
                return null;
            var kind = Resolve(pawn, clicked);
            // Re-resolve must reproduce the same scanner the planning client used; otherwise the world changed
            // under the command and we must not silently queue a different kind of route.
            return WorkKindId(kind) == workGiverDefName ? kind : null;
        }

        // A plant that is harvestable RIGHT NOW but carries no HarvestPlant/CutPlant designation. Mirrors the gates
        // WorkGiver_PlantsCut.JobOnThing will apply once we designate it, so the menu option only appears when the
        // pawn could actually do it.
        private static RouteWorkKind TryResolveUnmarkedHarvest(Pawn pawn, Thing clicked)
        {
            if (!(clicked is Plant plant) || plant.def.plant == null)
                return null;
            if (plant.def.plant.harvestedThingDef == null || !plant.HarvestableNow)
                return null;

            var wgDef = PlantsCutDef();
            if (wgDef == null || !(wgDef.Worker is WorkGiver_Scanner scanner))
                return null;
            if (WorkCapabilityProbe.IsDisabled(pawn, wgDef.workType))
                return null;

            // ignoreOtherReservations: true mirrors the forced (player-prioritized) job path the route uses.
            // No try/catch: these are vanilla calls — a throw is a real bug to surface, not hide.
            if (plant.IsBurning() ||
                !PlantUtility.PawnWillingToCutPlant_Job(plant, pawn) ||
                !pawn.CanReserve(plant, 1, -1, null, ignoreOtherReservations: true) ||
                !pawn.CanReach(plant, PathEndMode.Touch, Danger.Deadly))
                return null;

            return new RouteWorkKind
            {
                scanner = scanner,
                designation = DesignationDefOf.HarvestPlant,
                gerund = "HarvestGerund".Translate(),   // matches WorkGiver_PlantsCut.PostProcessedGerund(HarvestDesignated)
                yieldDef = plant.def.plant.harvestedThingDef,
            };
        }

        // Reconstructs the mining RouteWorkKind for a given vein def — used by the deferred-reveal tracker, which
        // runs without the original kind object (it can't be serialized). Scanner = WorkGiver_Miner, designation = Mine.
        public static RouteWorkKind MiningKind(ThingDef veinDef)
        {
            var wgDef = MinerDef();
            if (wgDef == null || !(wgDef.Worker is WorkGiver_Scanner scanner))
                return null;
            return new RouteWorkKind
            {
                scanner = scanner,
                designation = DesignationDefOf.Mine,
                gerund = wgDef.gerund ?? (string)"HaulersDream.PlanRoute.MiningGerundFallback".Translate(),
                yieldDef = veinDef?.building?.mineableThing,
                scope = RouteTargetScope.SameDefOrDesignated,
            };
        }

        private static WorkGiverDef minerDef;
        private static bool minerResolved;

        private static WorkGiverDef MinerDef()
        {
            if (minerResolved)
                return minerDef;
            minerResolved = true;
            var defs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                if (typeof(WorkGiver_Miner).IsAssignableFrom(defs[i].giverClass))
                {
                    minerDef = defs[i];
                    break;
                }
            }
            return minerDef;
        }

        // A BLUEPRINT the pawn could build: capable of Construction, same faction, reachable. No designation — the
        // blueprint IS the work marker. We resolve blueprints up front (material-independently) because a freshly
        // placed run — a fence, a wall line — should be route-plannable BEFORE materials are deliverable, and the
        // deliver-to-blueprints WorkGiver is always the right scanner for one (it delivers resources, then makes
        // the frame). Frames (mid-construction) are deliberately left to the workable-now loop, which picks the
        // correct deliver-vs-finish scanner for the frame's current state.
        private static RouteWorkKind TryResolveConstruction(Pawn pawn, Thing clicked)
        {
            // Frames resolve here too (not just blueprints): clicking a half-built piece of a fence run must
            // still yield the Constructible scope spanning the whole mixed-def run. The per-stop job builder
            // already dispatches frames to the frames-deliverer / FinishFrame as appropriate.
            if (!clicked.def.IsBlueprint && !(clicked is Frame))
                return null;
            if (clicked.Faction != pawn.Faction)
                return null;
            if (WorkCapabilityProbe.IsDisabled(pawn, WorkTypeDefOf.Construction))
                return null;

            // No try/catch: pawn.CanReach is a vanilla call — a throw is a real bug to surface, not hide.
            if (!pawn.CanReach(clicked, PathEndMode.Touch, Danger.Deadly))
                return null;

            var wgDef = WorkGiverOfClass(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints));
            if (wgDef == null || !(wgDef.Worker is WorkGiver_Scanner scanner))
                return null;

            return new RouteWorkKind
            {
                scanner = scanner,
                designation = null, // construction targets are their own markers — nothing to designate
                gerund = "HaulersDream.PlanRoute.ConstructGerund".Translate(),
                yieldDef = null,    // construction consumes materials, produces no haulable yield → no storage anchor
                scope = RouteTargetScope.Constructible, // a fence LINE mixes defs (fences, gates, corners) + frames
            };
        }

        private static readonly Dictionary<Type, WorkGiverDef> wgByClass = new Dictionary<Type, WorkGiverDef>();

        // The first WorkGiverDef whose Worker is (or derives from) the given WorkGiver class. Cached.
        private static WorkGiverDef WorkGiverOfClass(Type workGiverClass)
        {
            if (wgByClass.TryGetValue(workGiverClass, out var cached))
                return cached;
            WorkGiverDef found = null;
            var defs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                if (workGiverClass.IsAssignableFrom(defs[i].giverClass))
                {
                    found = defs[i];
                    break;
                }
            }
            wgByClass[workGiverClass] = found;
            return found;
        }

        private static WorkGiverDef plantsCutDef;
        private static bool plantsCutResolved;

        // The WorkGiverDef whose Worker is WorkGiver_PlantsCut (handles HarvestPlant/CutPlant designations).
        private static WorkGiverDef PlantsCutDef()
        {
            if (plantsCutResolved)
                return plantsCutDef;
            plantsCutResolved = true;
            var defs = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                if (typeof(WorkGiver_PlantsCut).IsAssignableFrom(defs[i].giverClass))
                {
                    plantsCutDef = defs[i];
                    break;
                }
            }
            return plantsCutDef;
        }

        // The route-target grouping for a resolved work kind. The work decides the grouping, not the thing:
        // cleaning covers ALL filth; deconstruct/uninstall cover ONLY already-marked things of any def (auto-
        // marking unmarked same-def buildings for deconstruction would be destructive); mining/cutting expand by
        // def (designating more of the same is the feature) and also adopt already-designated things of OTHER
        // defs; everything else (harvest) groups by def.
        private static RouteTargetScope ScopeFor(WorkGiver_Scanner scanner, DesignationDef designation)
        {
            if (scanner is WorkGiver_CleanFilth)
                return RouteTargetScope.AnyFilth;
            if (designation == DesignationDefOf.Deconstruct || designation == DesignationDefOf.Uninstall)
                return RouteTargetScope.DesignatedOnly;
            if (designation == DesignationDefOf.Mine || designation == DesignationDefOf.CutPlant)
                return RouteTargetScope.SameDefOrDesignated;
            return RouteTargetScope.SameDef;
        }

        // Mirror of vanilla FloatMenuOptionProvider_WorkGivers.ScannerShouldSkip.
        private static bool ScannerSkips(Pawn pawn, WorkGiver_Scanner s, Thing t)
        {
            if (s.PotentialWorkThingRequest.Accepts(t))
                return s.ShouldSkip(pawn, forced: true);
            var global = s.PotentialWorkThingsGlobal(pawn);
            if (global != null)
            {
                foreach (var thing in global)
                    if (thing == t)
                        return s.ShouldSkip(pawn, forced: true);
            }
            return true;
        }

        // The designation we replicate onto expansion targets so the same scanner produces jobs for them.
        // Plant/building designations live on the Thing; mining designations live on the cell.
        private static DesignationDef DetermineDesignation(Map map, Thing clicked)
        {
            var dm = map.designationManager;
            var on = dm.AllDesignationsOn(clicked);
            for (int i = 0; i < on.Count; i++)
            {
                var def = on[i].def;
                if (def == DesignationDefOf.HarvestPlant || def == DesignationDefOf.CutPlant ||
                    def == DesignationDefOf.Deconstruct || def == DesignationDefOf.Uninstall)
                    return def;
            }
            if (dm.DesignationAt(clicked.Position, DesignationDefOf.Mine) != null ||
                dm.DesignationAt(clicked.Position, DesignationDefOf.MineVein) != null)
                return DesignationDefOf.Mine;
            return null;
        }

        private static ThingDef ResolveYield(Thing clicked)
        {
            if (clicked is Plant plant)
                return plant.def.plant?.harvestedThingDef;
            if (clicked.def.mineable)
                return clicked.def.building?.mineableThing;
            return null;
        }

    }
}
