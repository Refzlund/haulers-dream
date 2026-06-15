using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Two periodic jobs, run for every loaded save (GameComponents with a Game-ctor are added automatically):
    /// <list type="bullet">
    /// <item>Interval unloading — every <c>intervalUnloadHours</c> game-hours, ask each player pawn carrying
    /// scooped yields to make its consolidated unload trip.</item>
    /// <item>Deferred vein reveal — for each tracked vein route that ran into fog, append newly-uncovered cells
    /// as the pawn mines (only while the route's tail is still the pawn's last task).</item>
    /// </list>
    /// </summary>
    public class HaulersDreamGameComponent : GameComponent
    {
        private List<VeinRevealTracker> veinTrackers = new List<VeinRevealTracker>();
        private const int VeinTickInterval = 60;   // re-scan tracked veins ~once a second
        private const int IdleTickInterval = 250;  // idle-backstop scan ~4x per in-game minute

        // --- per-bill BATCH config (the Batch-Y bill mode) ---
        // Key = bill.GetUniqueLoadID() (stable across save/load; bill loadIDs are monotonic and never reused
        // within a game, so a stale entry left by a deleted bill is INERT — it can never mis-apply to a future
        // bill — which is why no active pruning is needed). Presence in the map = batching ON; value = batch
        // size (>= 1). Scribed WITH THE GAME (bills are game-scoped, not global like the def-keyed settings),
        // via a plain Scribe_Collections — no core-serialization Harmony patching.
        private Dictionary<string, int> batchBills = new Dictionary<string, int>();
        public const int BatchSizeMax = 1000;

        // --- transport/portal bulk-load concurrency CLAIM-LEDGER ---
        // Keyed by the task's save-unique load id: transporters by CompTransporter.groupID (≥0), portals by
        // MapPortalBulkTarget.LedgerKey = -(MapPortal.thingIDNumber + 1) (strictly <0). groupID and thingIDNumber
        // come from two INDEPENDENT UniqueIDsManager counters (both ≥0) so a RAW thingIDNumber could collide with a
        // transporter groupID — the negative-namespacing in LedgerKey keeps the two id spaces disjoint in this single
        // FLAT map. Each entry carries its own Map, so map removal is a one-pass drop. Scribed WITH THE GAME (live
        // claims survive a save/load round-trip; the entry's own ExposeData recomputes totalClaimed from the surviving
        // pawnClaims). Self-prunes inert entries (needed AND claimed both empty), same tolerance as batchBills.
        private Dictionary<int, LoadLedgerEntry> loadTasks = new Dictionary<int, LoadLedgerEntry>();

        // --- Vehicle Framework bulk-load CLAIM-LEDGER (a SEPARATE dict, addendum SF1) ---
        // Keyed by the vehicle's RAW thingIDNumber (≥0). A SECOND dictionary (not the flat loadTasks above) so a
        // vehicle's thingIDNumber can NEVER collide with a transporter groupID or a portal -(id+1) key — they live
        // in different maps, so the routing is zero-arithmetic and provably disjoint. Same self-pruning, same
        // map-removal drop, same additive ExposeData scribing as loadTasks. A Vehicle-kind IManagedLoadable is
        // routed here by BucketFor; everything else stays in loadTasks.
        private Dictionary<int, LoadLedgerEntry> loadVehicleTasks = new Dictionary<int, LoadLedgerEntry>();

        /// <summary>The component for the running game (null at the main menu / before a game loads).</summary>
        public static HaulersDreamGameComponent Instance => Current.Game?.GetComponent<HaulersDreamGameComponent>();

        /// <summary>Pick the ledger bucket for a loadable: a Vehicle-kind target keys the separate
        /// <see cref="loadVehicleTasks"/> dict (raw thingIDNumber, disjoint from transporter/portal keys because it
        /// is a DIFFERENT dictionary); transporters and portals share the flat <see cref="loadTasks"/> dict (their
        /// keys are already namespaced disjoint there). The SINGLE selector every ledger method routes through.</summary>
        private Dictionary<int, LoadLedgerEntry> BucketFor(IManagedLoadable l)
            => l != null && l.Kind == LoadableKind.Vehicle ? loadVehicleTasks : loadTasks;

        private static string BatchKey(Bill bill) => bill?.recipe == null ? null : bill.GetUniqueLoadID();

        /// <summary>Is this bill set to batch?</summary>
        public bool IsBatchBill(Bill bill)
        {
            var k = BatchKey(bill);
            return k != null && batchBills.ContainsKey(k);
        }

        /// <summary>The bill's batch size, or 0 if it isn't batching.</summary>
        public int BatchSizeOf(Bill bill)
        {
            var k = BatchKey(bill);
            return (k != null && batchBills.TryGetValue(k, out int n)) ? n : 0;
        }

        /// <summary>Turn batching on (size clamped to [1, BatchSizeMax]) or off for a bill.</summary>
        public void SetBatch(Bill bill, bool on, int size)
        {
            var k = BatchKey(bill);
            if (k == null)
                return;
            if (on)
                batchBills[k] = Mathf.Clamp(size, 1, BatchSizeMax);
            else
                batchBills.Remove(k);
        }

        // ============ transport/portal bulk-load CLAIM-LEDGER API (thin adapters over Core.LoadLedger) ============

        /// <summary>Register a task (idempotent) and refresh its <c>totalNeeded</c> from the live manifest. Called
        /// by the planner before computing the claimable slice, so a manifest change between trips is reflected.</summary>
        // HD-LOADSCAN: a reused per-thread scratch dict for the manifest rebuild on the per-pawn-scan PROBE path.
        // It is COPIED into the entry's own dict via SetNeededFrom (never aliased — the entry must own its dict),
        // so the refresh allocates nothing after first use. [ThreadStatic] to match the assembly's hook-reachable
        // scratch convention (the work-scan that drives this can run off a [ThreadStatic] worker).
        [System.ThreadStatic] private static Dictionary<ThingDef, int> tmpNeeded;

        public LoadLedgerEntry LoadRegisterOrUpdate(IManagedLoadable loadable)
        {
            if (loadable == null)
                return null;
            var bucket = BucketFor(loadable);
            int id = loadable.GetUniqueLoadID();
            if (!bucket.TryGetValue(id, out var entry) || entry == null)
            {
                entry = new LoadLedgerEntry(loadable.GetMap());
                bucket[id] = entry;
            }
            if (entry.map == null)
                entry.map = loadable.GetMap();

            // Cheap allocation-free emptiness pre-gate: nothing left on the manifest -> empty totalNeeded in place
            // (so HasWork reads false) and bail BEFORE materialising GetTransferables' List + a needed dict. This is
            // the common per-pawn-scan case once a load finishes / before one is queued.
            if (!loadable.AnythingToLoad())
            {
                entry.ClearNeeded();
                return entry;
            }

            // Rebuild totalNeeded from the manifest (def -> Σ CountToTransfer across same-def entries) into a reused
            // [ThreadStatic] scratch dict, then COPY it into the entry's own dict (SetNeededFrom — never alias the
            // scratch, which SetNeeded would; the entry must own its dict). No fresh dict allocated per refresh.
            var needed = tmpNeeded ?? (tmpNeeded = new Dictionary<ThingDef, int>());
            needed.Clear();
            var transferables = loadable.GetTransferables();
            if (transferables != null)
                for (int i = 0; i < transferables.Count; i++)
                {
                    var tr = transferables[i];
                    if (tr == null || !tr.HasAnyThing || tr.CountToTransfer <= 0)
                        continue;
                    var def = tr.ThingDef;
                    if (def == null)
                        continue;
                    needed[def] = (needed.TryGetValue(def, out int cur) ? cur : 0) + tr.CountToTransfer;
                }
            entry.SetNeededFrom(needed);
            needed.Clear();
            return entry;
        }

        /// <summary>The per-def map this pawn may newly claim on the task (needed − others' claims; the asker's own
        /// claim excluded so a re-plan is stable). Empty if the task is unknown.</summary>
        public Dictionary<ThingDef, int> LoadAvailableToClaim(IManagedLoadable loadable, Pawn pawn)
        {
            if (loadable == null)
                return new Dictionary<ThingDef, int>();
            return BucketFor(loadable).TryGetValue(loadable.GetUniqueLoadID(), out var entry) && entry != null
                ? entry.AvailableToClaim(pawn)
                : new Dictionary<ThingDef, int>();
        }

        /// <summary>True if the pawn can newly claim anything on the task right now.</summary>
        public bool LoadHasWork(IManagedLoadable loadable, Pawn pawn)
        {
            if (loadable == null)
                return false;
            return BucketFor(loadable).TryGetValue(loadable.GetUniqueLoadID(), out var entry) && entry != null
                   && entry.HasWork(pawn);
        }

        /// <summary>Record a pawn's claim from a running bulk-load job's sweep queue (def → Σ countQueue over
        /// targetQueueB stacks of that def). Called from the driver's <c>Notify_Starting</c> so a built-but-never-
        /// started menu probe never claims. Registers the task if needed.</summary>
        public void LoadClaim(Pawn pawn, Job job, IManagedLoadable loadable)
        {
            if (pawn == null || job == null || loadable == null)
                return;
            var entry = LoadRegisterOrUpdate(loadable);
            if (entry == null)
                return;
            var plan = new Dictionary<ThingDef, int>();
            var queue = job.targetQueueB;
            var counts = job.countQueue;
            if (queue != null && counts != null)
                for (int i = 0; i < queue.Count && i < counts.Count; i++)
                {
                    var thing = queue[i].Thing;
                    if (thing?.def == null || counts[i] <= 0)
                        continue;
                    plan[thing.def] = (plan.TryGetValue(thing.def, out int cur) ? cur : 0) + counts[i];
                }
            entry.ApplyClaim(pawn, plan);
        }

        /// <summary>Settle a deposit (the Thing survives in the container — transporters): shrinks needed/claimed/the
        /// pawn's claim by the moved count.</summary>
        public void LoadNotifyDeposited(Pawn pawn, IManagedLoadable loadable, Thing moved)
        {
            if (pawn == null || loadable == null || moved?.def == null)
                return;
            LoadNotifyDeposited(pawn, loadable, moved.def, moved.stackCount);
        }

        /// <summary>Settle a deposit thing-lessly (the Thing was consumed/teleported — portals): the (def, count) MUST
        /// be captured BEFORE the transfer by the caller.</summary>
        public void LoadNotifyDeposited(Pawn pawn, IManagedLoadable loadable, ThingDef def, int count)
        {
            if (pawn == null || loadable == null || def == null || count <= 0)
                return;
            if (BucketFor(loadable).TryGetValue(loadable.GetUniqueLoadID(), out var entry) && entry != null)
                entry.Settle(pawn, def, count);
        }

        /// <summary>Return every claim this pawn holds across ALL tasks to the pool (idempotent / null-safe). Called
        /// on every interrupt path (job-end / despawn / map-removal). Does NOT touch needed.</summary>
        public void LoadReleaseClaimsForPawn(Pawn pawn)
        {
            if (pawn == null)
                return;
            // BOTH dicts — a pawn's claim can be on a transporter/portal (loadTasks) or a vehicle (loadVehicleTasks).
            if (loadTasks.Count > 0)
                foreach (var entry in loadTasks.Values)
                    entry?.Release(pawn);
            if (loadVehicleTasks.Count > 0)
                foreach (var entry in loadVehicleTasks.Values)
                    entry?.Release(pawn);
        }

        /// <summary>True if any pawn still holds a live claim on a TRANSPORTER/PORTAL task (gates premature
        /// board/launch + autoload). Keyed by the transporter <c>groupID</c> / portal <c>-(id+1)</c> key in
        /// <see cref="loadTasks"/>.</summary>
        public bool LoadAnyClaimsInProgress(int taskId)
            => loadTasks.TryGetValue(taskId, out var entry) && entry != null && entry.AnyClaimed();

        /// <summary>Drop every ledger entry tied to a removed map (clears its Pawn/Map refs, so an orphaned entry
        /// can't keep <c>AnyClaimsInProgress</c> true and block boarding forever).</summary>
        public void Notify_LoadMapRemoved(Map map)
        {
            if (map == null)
                return;
            // BOTH dicts — a removed map's tasks may be transporters/portals (loadTasks) or vehicles (loadVehicleTasks).
            DropFromBucket(loadTasks, kv => kv.Value == null || kv.Value.map == map);
            DropFromBucket(loadVehicleTasks, kv => kv.Value == null || kv.Value.map == map);
        }

        // Self-prune fully-inert entries (no needed AND no claimed) — called from the same periodic tick the
        // veins/idle use. Keeps both maps small without active per-tick scanning of live tasks.
        private void PruneInertLoadTasks()
        {
            DropFromBucket(loadTasks, kv => kv.Value == null || kv.Value.IsInert);
            DropFromBucket(loadVehicleTasks, kv => kv.Value == null || kv.Value.IsInert);
        }

        // Remove every entry of a ledger bucket matching the predicate (two-pass so the dict isn't mutated mid-enum).
        private static void DropFromBucket(Dictionary<int, LoadLedgerEntry> bucket,
            System.Func<KeyValuePair<int, LoadLedgerEntry>, bool> drop)
        {
            if (bucket == null || bucket.Count == 0)
                return;
            List<int> toDrop = null;
            foreach (var kv in bucket)
                if (drop(kv))
                    (toDrop ?? (toDrop = new List<int>())).Add(kv.Key);
            if (toDrop != null)
                for (int i = 0; i < toDrop.Count; i++)
                    bucket.Remove(toDrop[i]);
        }

        public HaulersDreamGameComponent(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // BulkHaul's per-tick plan cache is STATIC state, so it survives a quickload into the freshly
            // loaded game — where its cached plans reference the previous game's (now-stale) things. Clear it
            // whenever a game finishes initialising (new game and load alike). Same hygiene for the batch
            // handoff map (entries whose ordered job never started).
            BulkHaul.ClearPlanCache();
            BatchCraftHandoff.Clear();
            // The per-(pawn,tick) mass memo is likewise static and survives a quickload — drop it so an equal
            // tick number can't serve a previous session's pawn-id mass (decision-neutral hygiene; the memo is
            // per-tick self-clearing anyway, this just avoids a one-tick cross-session read after load).
            PawnMassCache.Clear();
            // Same hygiene for the per-(pawn,tick) inspect-pane surplus-boolean memo (HD-GIZMO).
            SurplusCache.Clear();
            // Same hygiene for the per-(pawn,tick) opportunistic-unload tracked-mass memo (HD-OPPUNLOAD).
            TrackedMassCache.Clear();
            // Same hygiene for the per-(worker,def,tick) availability-count memos (tagged / organic / carried)
            // and the per-(thing,carrier,cell,tick) haul-to-stack cell memo. These are [ThreadStatic]/static and
            // survive a quickload, and their keys use thingIDNumber (collidable across saves). Each clear touches
            // only the main (FinalizeInit) thread's slot — the `tick != -1` populate guard in each is the actual
            // cross-session safeguard; this keeps them consistent with the list above.
            InventoryShare.Clear();
            OrganicInventoryShare.Clear();
            CarriedHaulShare.Clear();
            HaulToStack.Clear();
        }

        /// <summary>Register (or replace, per pawn) a deferred-reveal tracker for a vein route that ran into fog.</summary>
        public void RegisterVeinTracker(VeinRevealTracker tracker)
        {
            if (tracker?.pawn == null)
                return;
            // Bind the tracker to the map the route was planned on (registration runs synchronously right
            // after queueing, so the pawn is still there) — TryExtend drops it if the pawn changes maps.
            tracker.mapId = tracker.pawn.Map?.uniqueID ?? -1;
            if (veinTrackers == null)
                veinTrackers = new List<VeinRevealTracker>();
            veinTrackers.RemoveAll(t => t.pawn == tracker.pawn); // a new route supersedes the pawn's old one
            veinTrackers.Add(tracker);
        }

        public override void GameComponentTick()
        {
            int tick = Find.TickManager.TicksGame;
            if (veinTrackers != null && veinTrackers.Count > 0 && tick % VeinTickInterval == 0)
                ProcessVeinTrackers();

            // Idle/checkpoint backstop: when a colonist is idle — or in a between-runs job like a meal or
            // recreation — (a) scoop any pending fresh drops it still owes itself (DropThenHaul) and
            // (b) run the unload pass for tracked stock. This used to be a Harmony postfix on
            // JobGiver_Idle.TryGiveJob — DEAD in 1.6, where ordinary colonists never execute that node
            // (it lives only in gathering/ritual duty trees). Driven from here instead.
            if (tick % IdleTickInterval == 0)
            {
                RunIdleBackstop();
                PruneInertLoadTasks(); // drop fully-settled/released bulk-load ledger entries (cheap when empty)
            }

            var s = HaulersDreamMod.Settings;
            if (s == null || !s.markForUnload
                || !SchedulePolicy.IntervalDueNow(tick, s.intervalUnloadHours))
                return;

            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m].mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < pawns.Count; i++)
                    PawnUnloadChecker.CheckIfShouldUnload(pawns[i]);
            }
        }

        private static void RunIdleBackstop()
        {
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return;
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m].mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (!IsUnloadCheckpoint(p))
                        continue;
                    // Unload check FIRST, then the self-pickup: both EnqueueFirst, so this order makes the
                    // queue [SelfPickup, Unload] — the pawn scoops its pending drops and unloads everything
                    // in ONE trip. (The reverse order ran the unload before the scoop, so freshly scooped
                    // stock waited for the next idle cycle: a second trip.) Safe either way for the strict-
                    // mode livelock: HasPendingRealWork excludes housekeeping jobs by defName, not by order.
                    if (s.markForUnload)
                        PawnUnloadChecker.CheckIfShouldUnload(p);
                    // Same eligibility gate as scoop time (FindWorker uses IsCandidate): a pawn that turned
                    // ineligible since its drops were recorded (drafted with pauseWhileDrafted, a settings
                    // flip, a non-home map) must not walk off to scoop into an inventory the unload side
                    // refuses to serve.
                    if (YieldRouter.IsCandidate(p))
                        YieldRouter.EnsureSelfPickupJob(p);
                }
            }
        }

        // A moment the backstop may act on: genuinely idle (no job, or a wander/wait filler), OR in a
        // between-runs job — eating or recreation — that an unload can be queued BEHIND. The queued
        // unload starts the instant the meal/joy job ends (queued jobs precede every think-tree scan),
        // so a pawn that drifted to dinner or horseshoes with a full backpack puts its stuff away right
        // after, instead of carrying it for the rest of the day; the current job itself is never
        // interrupted. (A queued job means the pawn is between tasks of a real run — the backstop must
        // not jump in; HasPendingRealWork in the checker also guards that, but skipping here avoids the
        // scan entirely.)
        private static bool IsUnloadCheckpoint(Pawn p)
        {
            if (p?.jobs == null || p.Drafted)
                return false;
            if (p.jobs.jobQueue != null && p.jobs.jobQueue.Count > 0)
                return false;
            if (p.CurJob == null)
                return true;
            var def = p.CurJobDef;
            if (def == JobDefOf.Wait || def == JobDefOf.Wait_Wander
                || def == JobDefOf.GotoWander || def == JobDefOf.Wait_MaintainPosture)
                return true;
            // Eating and joy jobs: between work runs by definition. Sleep is deliberately NOT included —
            // a queued job fires before everything on wake (even urgent breakfast), and the morning work
            // scan / end-of-run trigger covers it anyway.
            return def == JobDefOf.Ingest || def.joyKind != null;
        }

        private void ProcessVeinTrackers()
        {
            // Master-switch gate (the F25 idiom): with the route planner off, in-flight trackers must not keep
            // designating/queueing newly revealed cells. The trackers are kept — re-enabling resumes them.
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.planRoutes)
                return;
            for (int i = veinTrackers.Count - 1; i >= 0; i--)
            {
                // No try/catch: a vein-tracker failure is a real bug to surface as a red error, not a verbose-only
                // debug line that swallows it and silently drops the tracker.
                if (!TryExtend(veinTrackers[i]))
                    veinTrackers.RemoveAt(i);
            }
        }

        // Extend one vein route as fog clears. Returns false to DROP the tracker (pawn gone, route superseded,
        // cap reached, or the vein is fully revealed with nothing left hidden).
        private bool TryExtend(VeinRevealTracker tr)
        {
            var pawn = tr?.pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null || tr.veinDef == null || tr.included == null)
                return false;
            // Map identity: the tracker's seed/lastCell/included are coordinates ON THE MAP the route was
            // planned on. A pawn that has changed maps since (caravan, drop pod, …) must never re-flood the
            // NEW map from them — the final-attempt branch below could otherwise designate + queue mining
            // there. Drop the tracker (matching the self-drop idiom; -1 = a pre-fix save, also dropped).
            if (pawn.Map.uniqueID != tr.mapId)
                return false;
            if (tr.included.Count >= tr.cap)
                return false; // route already at its chosen Amount — never grow past it

            // Only extend while the route's last task is STILL the pawn's last task — if the player has queued
            // other work after it (so the tail moved), leave the route alone, exactly as requested. The tail must
            // be the SAME cell AND a Mine job: once the final cell is mined its ore drops there, and a later haul
            // of that ore would coincidentally match the cell — the def check rejects that false positive.
            // EXCEPTION (the flagship 1-2-visible-cell stub): when the FINAL queued stop IS the fog-boundary cell,
            // the reveal happens at the exact moment the route completes — the tail check fails right then. If the
            // tail cell was MINED (not superseded) and the pawn has nothing else queued, make one last extend
            // attempt instead of dropping the tracker at the moment it was about to pay off.
            bool finalAttempt = false;
            if (!RouteExecutor.TryGetQueueTailCell(pawn, out IntVec3 tail, out Verse.AI.Job tailJob)
                || tail != tr.lastCell || tailJob.def != JobDefOf.Mine)
            {
                bool lastCellMined = !AnyVeinThingAt(pawn.Map, tr.veinDef, tr.lastCell);
                // "Nothing else queued" must ignore the mod's OWN housekeeping (the yield hook queues a
                // self-pickup — and possibly an unload — at the exact moment the final cell is mined);
                // only REAL queued work means the route was superseded. Same idiom as PawnUnloadChecker.
                var queue = pawn.jobs?.jobQueue;
                // Allocation-free queue scan (indexed for — the JobQueue enumerator boxes; the indexer does not)
                // + a reference compare against the two housekeeping defs. No List<string>/params allocation.
                bool nothingElseQueued = true;
                if (queue != null)
                {
                    var selfPickup = HaulersDreamDefOf.HaulersDream_SelfPickup;
                    var unload = HaulersDreamDefOf.HaulersDream_UnloadInventory;
                    for (int i = 0; i < queue.Count; i++)
                        if (UnloadPolicy.IsPendingRealWork(queue[i]?.job?.def, selfPickup, unload))
                        {
                            nothingElseQueued = false;
                            break;
                        }
                }
                if (!lastCellMined || !nothingElseQueued)
                    return false; // genuinely superseded / diverted — leave the route alone
                finalAttempt = true;
            }

            var kind = WorkKindResolver.MiningKind(tr.veinDef);
            if (kind?.scanner == null)
                return false;

            // Re-flood the now-visible vein from the seed, with the route's own (mined) footprint as virtual
            // connectors; new stops are visible cells we haven't queued yet.
            var visible = RouteSelection.ReFloodVisibleVein(pawn.Map, pawn, tr.veinDef, tr.seed, tr.cap, tr.included, out bool stillFog);
            var newStops = new List<Thing>();
            for (int i = 0; i < visible.Count; i++)
            {
                var t = visible[i];
                if (t == null || tr.included.Contains(t.Position))
                    continue;
                if (tr.included.Count + newStops.Count >= tr.cap)
                    break;
                newStops.Add(t);
            }
            if (newStops.Count == 0)
                return !finalAttempt && stillFog; // a fruitless FINAL attempt drops the tracker (no route jobs remain to mine more)

            // Continue the route from the current tail: order the new cells nearest-first from there.
            newStops.Sort((a, b) =>
                (a.Position - tr.lastCell).LengthHorizontalSquared.CompareTo((b.Position - tr.lastCell).LengthHorizontalSquared));

            int appended = RouteExecutor.AppendStops(pawn, kind, newStops, out IntVec3 newLast);
            // Mark every revealed cell as covered (all were designated; any that didn't queue fall to normal mining).
            for (int i = 0; i < newStops.Count; i++)
                tr.included.Add(newStops[i].Position);
            if (appended > 0)
                tr.lastCell = newLast;
            return true;
        }

        /// <summary>Is a spawned thing of the vein's def still at <paramref name="cell"/>? (False = it was mined.)</summary>
        private static bool AnyVeinThingAt(Map map, ThingDef def, IntVec3 cell)
        {
            if (map == null || def == null || !cell.InBounds(map))
                return false;
            var things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
                if (things[i]?.def == def && things[i].Spawned)
                    return true;
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref veinTrackers, "haulersDreamVeinTrackers", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && veinTrackers == null)
                veinTrackers = new List<VeinRevealTracker>();
            Scribe_Collections.Look(ref batchBills, "haulersDreamBatchBills", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && batchBills == null)
                batchBills = new Dictionary<string, int>();
            // The bulk-load claim-ledger: keyed by int task id, values are Deep-scribed LoadLedgerEntry (each
            // recomputes totalClaimed from its surviving pawnClaims in PostLoadInit — the quota-leak fix).
            Scribe_Collections.Look(ref loadTasks, "haulersDreamLoadTasks", LookMode.Value, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && loadTasks == null)
                loadTasks = new Dictionary<int, LoadLedgerEntry>();
            // The VEHICLE bulk-load claim-ledger (additive — absent in pre-VF-compat saves, so null-init to empty
            // on load; same Deep-scribed LoadLedgerEntry recompute-on-load as loadTasks). Keyed by raw thingIDNumber.
            Scribe_Collections.Look(ref loadVehicleTasks, "haulersDreamLoadVehicleTasks", LookMode.Value, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && loadVehicleTasks == null)
                loadVehicleTasks = new Dictionary<int, LoadLedgerEntry>();
        }
    }
}
