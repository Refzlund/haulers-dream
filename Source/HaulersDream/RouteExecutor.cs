using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Turns a planned-route request into queued work: takes the ordered, distance-truncated stops from
    /// <see cref="RoutePlanner"/>, designates each (additively, so the vanilla WorkGiver produces a job for it),
    /// builds the forced job per stop, then either REPLACES current work with the route (interrupt + clear queue,
    /// start now) or APPENDS it to the pawn's existing manually-prioritized queue (never clearing it). The pawn
    /// then scoops + hauls each stop exactly as if you'd shift-prioritized them by hand.
    /// </summary>
    public static class RouteExecutor
    {
        /// <summary>
        /// Multiplayer entry point for the Plan-Route dialog's Append/Replace buttons. <see cref="Execute"/> performs
        /// world/save mutations the MP auto-sync does NOT cover — <c>DesignationManager.AddDesignation</c>,
        /// <c>jobQueue.EnqueueLast</c> / <c>ClearQueuedJobs</c> / <c>EndCurrentJob</c>, the lead
        /// <c>TryTakeOrderedJobPrioritizedWork</c>, and the scribed <c>RegisterVeinTracker</c> write — so the dialog
        /// must NOT call <see cref="Execute"/> directly (that runs only on the clicking client → desync). Instead the
        /// button calls THIS, which the <c>[SyncMethod]</c> attribute turns into a COMMAND replayed identically on
        /// every client; the designation/job-queue/tracker writes then all run inside synced execution everywhere.
        ///
        /// <para>Args are MP-serializable only (the wire form must be unambiguous): a <see cref="Pawn"/>, a
        /// <see cref="Thing"/>, primitives/enums, and <c>List&lt;Thing&gt;</c>/<c>List&lt;IntVec3&gt;</c>. The live
        /// <see cref="RouteWorkKind"/> (which holds a WorkGiver_Scanner) can't cross the wire, so it travels as its
        /// stable <see cref="WorkKindResolver.WorkKindId"/> (the scanner's WorkGiverDef.defName) and is re-derived per
        /// client via <see cref="WorkKindResolver.ResolveById"/>. The cached preview <see cref="RoutePlan"/> is NOT
        /// shipped either (not serializable, and shipping a single client's plan would defeat lockstep); we pass
        /// <c>precomputed: null</c> so every client RECOMPUTES the plan deterministically from the same synced state
        /// (<see cref="RoutePlanner"/> reads only synced inputs — verified deterministic). When MP is absent the
        /// attribute is inert and this just runs <see cref="Execute"/> directly, so single-player is unchanged.</para>
        ///
        /// <para>The method BODY references NO Multiplayer.API type, and it carries NO <c>[SyncMethod]</c> attribute
        /// (which would bake a Multiplayer.API reference into HD's metadata and crash any reflection in a non-MP game —
        /// issue #6). It is registered by name from the MP-gated <see cref="MultiplayerCompat"/> shim instead, so a
        /// non-MP game never resolves the unshipped API assembly.</para>
        /// </summary>
        public static void ExecuteRouteSynced(Pawn pawn, Thing clicked, string workGiverDefName, RouteMode mode,
            int amount, int radius, float maxDistance, bool smart, bool allowHarvest, int growthThreshold, bool replace,
            List<Thing> mustInclude, HaulersDream.Core.RouteSelectionMethod selectionMethod,
            HaulersDream.Core.RouteDistanceBasis distanceBasis, int exactMax, Thing startNode, Thing endNode,
            bool alsoBuild, List<IntVec3> roomAnchors, List<ThingDef> extraDefs)
        {
            // Re-derive the live work kind from its portable id on THIS client (deterministic — same synced state →
            // same scanner). A null means the thing is no longer routable / the id didn't reproduce (the world
            // diverged between plan and execute): no-op rather than queue a mismatched route. Execute's own
            // null-guards would also catch this, but bailing here keeps the intent explicit.
            var kind = WorkKindResolver.ResolveById(pawn, clicked, workGiverDefName);
            if (kind == null)
                return;
            // precomputed: null → recompute the plan on every client (recompute-on-all-clients is the correct MP
            // model; the dialog's cached plan isn't serializable and is one client's view anyway).
            Execute(pawn, clicked, kind, mode, amount, radius, maxDistance, smart, allowHarvest, growthThreshold,
                replace, precomputed: null, mustInclude: mustInclude, selectionMethod: selectionMethod,
                distanceBasis: distanceBasis, exactMax: exactMax, startNode: startNode, endNode: endNode,
                alsoBuild: alsoBuild, roomAnchors: roomAnchors, extraDefs: extraDefs);
        }

        public static void Execute(Pawn pawn, Thing clicked, RouteWorkKind kind, RouteMode mode, int amount, int radius,
            float maxDistance, bool smart, bool allowHarvest, int growthThreshold, bool replace, RoutePlan precomputed = null,
            IReadOnlyList<Thing> mustInclude = null,
            HaulersDream.Core.RouteSelectionMethod selectionMethod = HaulersDream.Core.RouteSelectionMethod.MostStopsPerTravel,
            HaulersDream.Core.RouteDistanceBasis distanceBasis = HaulersDream.Core.RouteDistanceBasis.StraightLine,
            int exactMax = HaulersDream.Core.RouteOrderPolicy.ExactMax,
            Thing startNode = null, Thing endNode = null, bool alsoBuild = false,
            IReadOnlyList<IntVec3> roomAnchors = null, IReadOnlyList<ThingDef> extraDefs = null)
        {
            if (pawn?.Map == null || clicked == null || kind?.scanner == null)
                return;

            // Prefer the dialog's already-computed plan so the queued route matches the previewed one exactly
            // (the dialog runs unpaused, so recomputing here against a mutated world could diverge). Fall back
            // to a fresh plan only if none was supplied.
            var plan = (precomputed != null && precomputed.stops.Count > 0)
                ? precomputed
                : RoutePlanner.Plan(pawn, clicked, kind, mode, amount, radius, maxDistance, smart, allowHarvest, growthThreshold,
                    mustInclude, selectionMethod, distanceBasis, exactMax, startNode, endNode, roomAnchors, extraDefs);
            if (plan.stops.Count == 0)
            {
                // Player-facing toast: only on the issuing client (in MP this command replays on every client, but
                // the feedback should appear once, for whoever clicked — see MultiplayerCompat.ShouldShowLocalFeedback).
                if (MultiplayerCompat.ShouldShowLocalFeedback)
                    Messages.Message("HaulersDream.PlanRoute.NoTargets".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            int selected = plan.stops.Count;

            // 1. Designate every still-valid stop so the scanner produces a job (additive; existing designations
            // kept). A stop that turns out unworkable now (step 2) or unreservable (step 3) stays designated and
            // is picked up by normal work priorities later — same outcome as drag-designating the area by hand.
            for (int i = 0; i < plan.stops.Count; i++)
                if (plan.stops[i] != null && plan.stops[i].Spawned)
                    EnsureDesignated(pawn.Map, plan.stops[i], kind);

            // 2. Build the forced job per stop, in route order; drop the ones that can't be worked right now
            // (including any that despawned since the plan was previewed). For construction routes the intent
            // flag tells the deliver-job conversion whether stops should HAUL-ONLY (fill sites with materials)
            // or HAUL+BUILD (each stop's delivery tethers its own build before the next stop runs).
            var jobs = new List<RouteJob>(plan.stops.Count);
            // The thread-static assignments live INSIDE the try: the demand aggregation below calls
            // TotalMaterialCost on arbitrary modded IConstructibles (can throw), and a throw outside the
            // try would leave RouteIntent stuck for the whole session.
            try
            {
                InventoryConstructDelivery.RouteIntent = alsoBuild ? ConstructRouteIntent.HaulBuild : ConstructRouteIntent.HaulOnly;
                if (alsoBuild)
                {
                    // Publish the route's TOTAL per-def demand so the first stop's gather sweeps material for the
                    // whole run (the gather ceiling still mass-bounds it; a too-heavy total just means a mid-route top-up).
                    var demand = new Dictionary<ThingDef, int>();
                    for (int i = 0; i < plan.stops.Count; i++)
                    {
                        if (!(plan.stops[i] is IConstructible ic))
                            continue;
                        var costs = ic.TotalMaterialCost();
                        if (costs == null)
                            continue;
                        for (int k = 0; k < costs.Count; k++)
                        {
                            var d = costs[k]?.thingDef;
                            if (d == null)
                                continue;
                            int n = ic.ThingCountNeeded(d);
                            if (n <= 0)
                                continue;
                            demand.TryGetValue(d, out int cur);
                            demand[d] = cur + n;
                        }
                    }
                    InventoryConstructDelivery.RouteDemandByDef = demand;
                }
                for (int i = 0; i < plan.stops.Count; i++)
                {
                    var t = plan.stops[i];
                    if (t == null || !t.Spawned || StopLostDesignation(pawn.Map, t, kind))
                        continue;
                    var job = BuildJobForStop(pawn, t, kind);
                    if (job != null)
                        jobs.Add(new RouteJob { job = job, cell = t.Position, stop = t });
                }
            }
            finally
            {
                InventoryConstructDelivery.RouteIntent = ConstructRouteIntent.None;
                InventoryConstructDelivery.RouteDemandByDef = null;
            }

            // Haul+build: each stop's job loads the demand of ALL remaining same-material stops in one gather,
            // so the pawn keeps the wood in inventory and builds down the line without re-fetching. (Suffix sums,
            // so an interrupted route still loads the right amount from any later stop.)
            if (alsoBuild)
            {
                var remainingByDef = new Dictionary<ThingDef, int>();
                for (int i = jobs.Count - 1; i >= 0; i--)
                {
                    var rj = jobs[i];
                    var def = rj.job.targetA.Thing?.def;
                    if (def == null || !(rj.stop is IConstructible ic))
                        continue;
                    remainingByDef.TryGetValue(def, out int sum);
                    sum += System.Math.Max(0, ic.ThingCountNeeded(def));
                    remainingByDef[def] = sum;
                    // Assign unconditionally: TryBuild stamped the WHOLE route's demand into every stop's
                    // count, so later stops must be LOWERED to their suffix sum or they re-gather material
                    // the earlier stops already delivered.
                    if (rj.job.def == HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild)
                        rj.job.count = sum;
                }
            }
            if (jobs.Count == 0)
            {
                // A construction route whose blueprints have no reachable materials yet queues nothing right now —
                // but the blueprints persist and build under normal Construction priority as materials arrive, so
                // don't report an outright failure (which would contradict the route preview the player just saw).
                // Toast on the issuing client only (MP); the early return is unconditional so all clients agree.
                if (MultiplayerCompat.ShouldShowLocalFeedback)
                {
                    if (kind.scanner is WorkGiver_ConstructDeliverResourcesToBlueprints)
                        Messages.Message("HaulersDream.PlanRoute.WaitingForMaterials".Translate(), pawn, MessageTypeDefOf.CautionInput, historical: false);
                    else
                        Messages.Message("HaulersDream.PlanRoute.NoTargets".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                }
                return;
            }

            // 3. Queue the route — replace current work or append to the existing manual queue.
            int queued = replace ? QueueReplace(pawn, kind, jobs) : QueueAppend(pawn, kind, jobs);

            HDLog.Dbg($"{pawn} planned a {mode} route ({(replace ? "replace" : "append")}): {queued}/{selected} stop(s) " +
                      $"({kind.gerund}), smart={smart}, ~{RouteEstimateHours(plan)}h, cappedAmount={plan.cappedByAmount}, cappedDist={plan.cappedByDistance}.");

            // Deferred reveal: a vein that runs into fog keeps growing as the pawn uncovers it. Register a tracker
            // that appends newly-revealed cells to the route — but only while the route's tail is still the pawn's
            // last task (handled by the tracker). Mining veins only; harvest/cut patches aren't hidden by fog.
            if (queued > 0 && mode == RouteMode.Vein && plan.fogCaution && kind.designation == DesignationDefOf.Mine)
                RegisterVeinTracker(pawn, clicked, amount, plan);

            // Outcome toast — player-facing feedback, so only on the issuing client (in MP the command replays on
            // every client; the actual world writes above already ran on all of them, but the toast is shown once
            // for whoever clicked). The queueing/designation/tracker writes above are NOT gated — they must run on
            // every client to stay in lockstep.
            if (MultiplayerCompat.ShouldShowLocalFeedback)
            {
                if (queued == 0)
                    Messages.Message("HaulersDream.PlanRoute.NoTargets".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                else if (plan.cappedByDistance)
                    Messages.Message("HaulersDream.PlanRoute.DistanceLimited".Translate(queued), pawn, MessageTypeDefOf.CautionInput, historical: false);
                else if (plan.cappedByAmount)
                    Messages.Message("HaulersDream.PlanRoute.Capped".Translate(queued), pawn, MessageTypeDefOf.CautionInput, historical: false);
                else if (queued < selected)
                    Messages.Message("HaulersDream.PlanRoute.Partial".Translate(queued, selected - queued), pawn, MessageTypeDefOf.CautionInput, historical: false);
                else
                    Messages.Message("HaulersDream.PlanRoute.Planned".Translate(queued), pawn, MessageTypeDefOf.SilentInput, historical: false);
            }
        }

        private sealed class RouteJob
        {
            public Job job;
            public IntVec3 cell;
            public Thing stop;
        }

        /// <summary>
        /// The job for one route stop: the scanner's normal job, plus — for a player-forced CONSTRUCTION route —
        /// a fallback that delivers from the pawn's OWN carried inventory when no map/shared materials are found.
        /// The pawn is standing there with the materials, so a forced order should use them (vanilla + the shared-
        /// inventory feature only see reachable floor stock and tagged/scooped surplus, not a pawn's own untagged
        /// hauled stack — which is exactly what was carried in the reported case). Returns null when nothing works.
        /// </summary>
        public static Job BuildJobForStop(Pawn pawn, Thing t, RouteWorkKind kind)
        {
            if (pawn?.Map == null || t == null || kind?.scanner == null)
                return null;
            // No try/catch: the resolved scanner throwing is a real bug to surface (once per route action), not hide.
            Job job = kind.scanner.JobOnThing(pawn, t, forced: true);
            if (job != null)
                return job;
            if (kind.scanner is WorkGiver_ConstructDeliverResourcesToBlueprints)
            {
                // A FRAME stop (the route scope includes frames — a half-built fence line): the blueprints
                // scanner can't serve it. A materials-complete frame in haul+build mode is built directly;
                // otherwise the FRAMES deliverer supplies it.
                if (t is Frame f)
                {
                    if (f.IsCompleted()
                        && InventoryConstructDelivery.RouteIntent == ConstructRouteIntent.HaulBuild
                        && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction))
                    {
                        // No try/catch: GenConstruct.CanConstruct is a vanilla call — a throw is a real bug to surface.
                        if (GenConstruct.CanConstruct(f, pawn, checkSkills: true, forced: true))
                        {
                            var build = JobMaker.MakeJob(JobDefOf.FinishFrame, f);
                            build.playerForced = true;
                            return build;
                        }
                    }
                    foreach (var s in ConstructTether.DeliverScanners())
                    {
                        if (s == kind.scanner)
                            continue; // already tried above
                        job = s.JobOnThing(pawn, t, forced: true);
                        if (job != null)
                            return job;
                    }
                }
                return TryDeliverFromOwnStock(pawn, t);
            }
            return null;
        }

        // Forced construction fallback: if the pawn is CARRYING (in hands, e.g. mid-unload) or has in its INVENTORY
        // enough of a material the blueprint still needs, deliver it straight from the pawn's own stock. We build a
        // plain HaulToContainer targeting that stack (same shape as the shipped shared-inventory delivery): for a
        // hands stack, a REPLACE drops it to the ground as the route interrupts the current job and the job then
        // picks it up; for an inventory stack the vanilla driver pulls it via StartCarryThing(canTakeFromInventory).
        // Any stack qualifies — scooped or not. The map/floor + shared-tagged-inventory sources are tried first
        // (above, via JobOnThing); this only fires when those find nothing, which is exactly the reported case.
        private static Job TryDeliverFromOwnStock(Pawn pawn, Thing blueprint)
        {
            if (!(blueprint is IConstructible c))
                return null;
            var carried = pawn.carryTracker?.CarriedThing;
            var inv = pawn.inventory?.innerContainer;
            foreach (var need in c.TotalMaterialCost())
            {
                int needed = c.ThingCountNeeded(need.thingDef);
                if (needed <= 0)
                    continue;
                Thing stack = null;
                if (carried != null && carried.def == need.thingDef && carried.stackCount > 0)
                    stack = carried; // prefer the hands stack — it's right there and a REPLACE frees it to the ground
                else if (inv != null)
                    for (int i = 0; i < inv.Count; i++)
                    {
                        var it = inv[i];
                        if (it != null && it.def == need.thingDef && it.stackCount > 0) { stack = it; break; }
                    }
                if (stack == null)
                    continue;
                var deliver = JobMaker.MakeJob(JobDefOf.HaulToContainer);
                deliver.targetA = stack;
                deliver.targetB = blueprint;
                deliver.targetC = blueprint;
                deliver.count = needed < stack.stackCount ? needed : stack.stackCount;
                deliver.haulMode = HaulMode.ToContainer;
                return deliver;
            }
            return null;
        }

        // REPLACE: interrupt current work + clear any existing queue (the lead does this synchronously), then
        // append the rest of the route. Mirrors a manual prioritize followed by shift-queues.
        private static int QueueReplace(Pawn pawn, RouteWorkKind kind, List<RouteJob> jobs)
        {
            // Discard any existing manual queue up front. TryTakeOrderedJobPrioritizedWork normally clears it on
            // its interrupt path, but when the pawn's CURRENT job already equals the lead route job it early-
            // returns at JobIsSameAs BEFORE clearing — so do it explicitly here to guarantee a true replace.
            pawn.jobs.ClearQueuedJobs();

            int queued = 0;
            bool leadStarted = false;
            for (int i = 0; i < jobs.Count; i++)
            {
                var rj = jobs[i];
                rj.job.workGiverDef = kind.scanner.def;
                if (!leadStarted)
                {
                    if (pawn.jobs.TryTakeOrderedJobPrioritizedWork(rj.job, kind.scanner, rj.cell))
                    {
                        leadStarted = true;
                        queued++;
                    }
                    continue;
                }
                rj.job.playerForced = true;
                if (rj.job.TryMakePreToilReservations(pawn, errorOnFailed: false))
                {
                    pawn.jobs.jobQueue.EnqueueLast(rj.job, kind.scanner.def.tagToGive);
                    queued++;
                }
            }
            return queued;
        }

        // APPEND: never clear the queue (so the pawn's existing manual route survives). Reserve + EnqueueLast
        // every stop, then — only if the pawn is idle — nudge it to start. Deliberately NOT via TryTakeOrderedJob
        // (requestQueueing:true), whose idle branch calls ClearQueuedJobs() and would wipe the existing queue.
        private static int QueueAppend(Pawn pawn, RouteWorkKind kind, List<RouteJob> jobs)
        {
            int queued = 0;
            for (int i = 0; i < jobs.Count; i++)
            {
                var rj = jobs[i];
                rj.job.workGiverDef = kind.scanner.def;
                rj.job.playerForced = true;
                if (rj.job.TryMakePreToilReservations(pawn, errorOnFailed: false))
                {
                    pawn.jobs.jobQueue.EnqueueLast(rj.job, kind.scanner.def.tagToGive);
                    queued++;
                }
            }
            if (queued > 0)
            {
                // Start the queue if the pawn is otherwise idle; if it's doing real work (or has earlier queued
                // work), the appended route just waits its turn behind it.
                var cur = pawn.jobs.curJob;
                if (cur == null)
                    pawn.jobs.CheckForJobOverride(0f, ignoreQueue: false); // must consider the queue (default ignores it)
                else if (cur.def.isIdle)
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            return queued;
        }

        // Register a deferred-reveal tracker for an in-progress vein route whose visible cluster touches fog.
        private static void RegisterVeinTracker(Pawn pawn, Thing clicked, int amount, RoutePlan plan)
        {
            var comp = Current.Game?.GetComponent<HaulersDreamGameComponent>();
            if (comp == null || clicked?.def == null)
                return;
            if (!TryGetQueueTailCell(pawn, out IntVec3 tail, out _))
                return;

            int cap = RouteSelection.EffectiveAmount(amount);
            var tracker = new VeinRevealTracker
            {
                pawn = pawn,
                veinDef = clicked.def,
                seed = clicked.Position,
                cap = cap,
                lastCell = tail,
            };
            for (int i = 0; i < plan.stops.Count; i++)
                if (plan.stops[i] != null)
                    tracker.included.Add(plan.stops[i].Position);
            if (tracker.included.Count >= cap)
                return; // route already at its amount cap — no room for revealed cells
            comp.RegisterVeinTracker(tracker);
        }

        /// <summary>
        /// Designates and APPENDS the given stops to the pawn's existing job queue, in order, WITHOUT clearing it
        /// or nudging the pawn — used by the deferred-reveal tracker to extend an in-progress vein route. Returns
        /// how many queued and out the last successfully-queued cell (the route's new tail).
        /// </summary>
        public static int AppendStops(Pawn pawn, RouteWorkKind kind, List<Thing> stops, out IntVec3 lastCell)
        {
            lastCell = IntVec3.Invalid;
            if (pawn?.Map == null || kind?.scanner == null || stops == null || stops.Count == 0)
                return 0;

            for (int i = 0; i < stops.Count; i++)
                if (stops[i] != null && stops[i].Spawned)
                    EnsureDesignated(pawn.Map, stops[i], kind);

            int queued = 0;
            for (int i = 0; i < stops.Count; i++)
            {
                var t = stops[i];
                if (t == null || !t.Spawned || StopLostDesignation(pawn.Map, t, kind))
                    continue;
                // No try/catch: the resolved scanner throwing is a real bug to surface, not silently skip.
                Job job = kind.scanner.JobOnThing(pawn, t, forced: true);
                if (job == null)
                    continue;
                job.workGiverDef = kind.scanner.def;
                job.playerForced = true;
                if (job.TryMakePreToilReservations(pawn, errorOnFailed: false))
                {
                    pawn.jobs.jobQueue.EnqueueLast(job, kind.scanner.def.tagToGive);
                    // Track the job's OWN target cell (matches what TryGetQueueTailCell reads back); for a clean
                    // mine job this == t.Position, but a rare MineAIUtility haul-aside targets a blocking chunk.
                    lastCell = job.targetA.IsValid ? job.targetA.Cell : t.Position;
                    queued++;
                }
            }
            return queued;
        }

        /// <summary>
        /// The pawn's FINAL planned task — the last queued job, or the current job if the queue is empty — out its
        /// target cell and the job itself (so callers can verify it's the kind of job they expect, not just a
        /// coincidental cell match, e.g. a haul of the ore that just dropped where the last vein cell was).
        /// </summary>
        public static bool TryGetQueueTailCell(Pawn pawn, out IntVec3 cell, out Job tailJob)
        {
            cell = IntVec3.Invalid;
            tailJob = null;
            var jobs = pawn?.jobs;
            if (jobs == null)
                return false;
            // Skip the mod's OWN housekeeping jobs (self-pickup / unload) when picking the tail: the
            // yield hook EnqueueFirst's them at the exact moment a route cell is mined, and reading one
            // as "the final planned task" (its targetA isn't a route cell) would make the vein-reveal
            // tracker drop the route exactly when it's about to pay off.
            Job j = null;
            var q = jobs.jobQueue;
            if (q != null)
            {
                for (int i = q.Count - 1; i >= 0; i--)
                {
                    var qj = q[i]?.job;
                    if (qj == null || qj.def == HaulersDreamDefOf.HaulersDream_SelfPickup
                        || qj.def == HaulersDreamDefOf.HaulersDream_UnloadInventory)
                        continue;
                    j = qj;
                    break;
                }
            }
            if (j == null)
                j = jobs.curJob;
            if (j == null || !j.targetA.IsValid)
                return false;
            cell = j.targetA.Cell;
            tailJob = j;
            return true;
        }

        private static string RouteEstimateHours(RoutePlan plan)
            => Core.RouteEstimate.HoursFromTicks(plan.totalTicks).ToString("0.0");

        // DesignatedOnly kinds (deconstruct/uninstall) work ONLY what is still marked: a designation the
        // player cancelled while the dialog was open must not be resurrected by the route. The scanner's
        // JobOnThing (WorkGiver_RemoveBuilding) is unconditional, so the stop must be skipped outright.
        private static bool StopLostDesignation(Map map, Thing t, RouteWorkKind kind)
            => kind.scope == RouteTargetScope.DesignatedOnly
               && kind.designation != null
               && map.designationManager.DesignationOn(t, kind.designation) == null;

        private static void EnsureDesignated(Map map, Thing t, RouteWorkKind kind)
        {
            if (kind.designation == null)
                return;
            if (kind.scope == RouteTargetScope.DesignatedOnly)
                return; // deconstruct/uninstall never (re-)mark — the live designation IS the player's consent
            var dm = map.designationManager;
            if (kind.designation == DesignationDefOf.Mine || kind.designation == DesignationDefOf.MineVein)
            {
                if (dm.DesignationAt(t.Position, DesignationDefOf.Mine) == null &&
                    dm.DesignationAt(t.Position, DesignationDefOf.MineVein) == null)
                    dm.AddDesignation(new Designation(t.Position, DesignationDefOf.Mine));
            }
            else if (dm.DesignationOn(t, kind.designation) == null)
            {
                dm.AddDesignation(new Designation(t, kind.designation));
            }
        }
    }
}
