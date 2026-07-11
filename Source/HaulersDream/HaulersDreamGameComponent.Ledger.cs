using System.Collections.Generic;
using HaulersDream.Core;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    public partial class HaulersDreamGameComponent
    {
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

        /// <summary>Pick the ledger bucket for a loadable: a Vehicle-kind target keys the separate
        /// <see cref="loadVehicleTasks"/> dict (raw thingIDNumber, disjoint from transporter/portal keys because it
        /// is a DIFFERENT dictionary); transporters and portals share the flat <see cref="loadTasks"/> dict (their
        /// keys are already namespaced disjoint there). The SINGLE selector every ledger method routes through.</summary>
        private Dictionary<int, LoadLedgerEntry> BucketFor(IManagedLoadable l)
            => l != null && l.Kind == LoadableKind.Vehicle ? loadVehicleTasks : loadTasks;

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

        /// <summary>Record a DEPOSIT-ONLY pawn's claim from the tagged SURPLUS it already carries — there is no sweep
        /// queue to derive a plan from (the opportunistic divert + the boarding-passenger recovery build an
        /// empty-<c>targetQueueB</c> job). Per def the claim is <c>min(carriedSurplus, availableToClaim)</c>, so the
        /// target's remaining need reflects this incoming cargo and other carrying pawns don't all pile onto the same
        /// small remainder (#188). Registers the task if needed; an empty plan safely releases any prior claim this
        /// pawn held and records nothing. Uses <see cref="InventorySurplus.SurplusByDef"/> — the identical surplus math
        /// the divert scan reads — and integer per-def sums (no floats / Rand), so it is multiplayer-deterministic.</summary>
        public void LoadClaimCarriedSurplus(Pawn pawn, IManagedLoadable loadable)
        {
            if (pawn == null || loadable == null)
                return;
            var entry = LoadRegisterOrUpdate(loadable);
            if (entry == null)
                return;
            var avail = entry.AvailableToClaim(pawn);
            var plan = new Dictionary<ThingDef, int>();
            if (avail.Count > 0)
                foreach (var kv in InventorySurplus.SurplusByDef(pawn))
                {
                    int availForDef = avail.TryGetValue(kv.Key, out int a) ? a : 0;
                    int n = OpportunisticLoadPolicy.DepositCount(kv.Value, availForDef); // min(carried, available)
                    if (n > 0)
                        plan[kv.Key] = n;
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

        /// <summary>True when the loadable still needs something AND every remaining unit is already covered by
        /// SOME live pawn claim (see <see cref="LoadLedgerEntry.FullyClaimed"/>). Used to stop a vanilla-fallback
        /// gate (issue #164) from handing a further pawn a job that would duplicate cargo other pawns are already
        /// carrying toward this same loadable but haven't delivered yet (vanilla's own loader only tracks physical
        /// deliveries, so it can't see an HD claim as "already spoken for").</summary>
        public bool LoadFullyClaimedByOthers(IManagedLoadable loadable)
        {
            if (loadable == null)
                return false;
            return BucketFor(loadable).TryGetValue(loadable.GetUniqueLoadID(), out var entry) && entry != null
                   && entry.FullyClaimed();
        }

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

        /// <summary>
        /// fix/mix RECOVERY — release PHANTOM load-claims left in a save written by the old pre-save cleanup. That
        /// cleanup Wait-swapped a pawn's in-flight load job DURING <c>ScribeSaver.InitSaving</c>, which could mutate
        /// this ledger mid-serialization and persist a claim for a pawn that no longer runs the job. A phantom claim
        /// counts against <c>AvailableToClaim</c> forever, so the load/bulk planners read "already fully claimed" and
        /// stop offering work — the reported colony-wide hauling/cleanup stall (recovered before only by reloading
        /// WITHOUT HD and re-saving). The root cause is now fixed in <see cref="Patch_ScribeSaver_InitSaving"/> (no
        /// more save-time job swap); this SELF-HEALS saves written before that fix.
        ///
        /// Run once at load (<see cref="FinalizeInit"/>, after pawn jobs are restored). For every spawned PLAYER
        /// pawn that is NOT currently running an HD job, release its load-claims: such a pawn cannot legitimately
        /// hold one. A pawn whose HD load job genuinely RESUMED has <see cref="IsRunningHdJob"/> true, so its real
        /// in-flight claim is preserved (the resumed driver relies on the scribed claim surviving). Idempotent and a
        /// no-op on a clean / new game (empty buckets short-circuit). NOT gated on a setting: an orphaned claim must
        /// be cleared regardless of toggles, or the stall persists.
        /// </summary>
        internal void ValidateLoadLedgerAfterLoad()
        {
            if ((loadTasks == null || loadTasks.Count == 0)
                && (loadVehicleTasks == null || loadVehicleTasks.Count == 0))
                return; // clean / new game — nothing to validate

            int repaired = 0;
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m]?.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                    continue;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    // A resumed HD job (load or otherwise) is allowed to keep its claim; only a pawn doing NOTHING
                    // HD-related can hold a phantom one. (A pawn mid-non-load HD job that holds a stale claim self-
                    // heals when that job ends → LoadReleaseClaimsForPawn — so skipping it here is the safe side.)
                    if (p == null || IsRunningHdJob(p) || !LoadPawnHoldsAnyClaim(p))
                        continue;
                    LoadReleaseClaimsForPawn(p);
                    repaired++;
                }
            }
            if (repaired > 0)
                HDLog.Warn($"Released {repaired} orphaned bulk-load claim(s) on load — pawns that held a claim but "
                           + "are no longer running their load job (a save written by an older version). If hauling "
                           + "or cleanup had stalled colony-wide, it should now resume.");
        }

        /// <summary>True if <paramref name="p"/> holds a live claim on ANY bulk-load task (either bucket). Reads the
        /// public <c>pawnClaims</c> map directly; used only by the once-at-load <see cref="ValidateLoadLedgerAfterLoad"/>.</summary>
        private bool LoadPawnHoldsAnyClaim(Pawn p)
        {
            if (p == null)
                return false;
            if (loadTasks.Count > 0)
                foreach (var e in loadTasks.Values)
                    if (e?.pawnClaims != null && e.pawnClaims.ContainsKey(p))
                        return true;
            if (loadVehicleTasks.Count > 0)
                foreach (var e in loadVehicleTasks.Values)
                    if (e?.pawnClaims != null && e.pawnClaims.ContainsKey(p))
                        return true;
            return false;
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

        // The bulk-load claim-ledger scribing (additive to base.ExposeData via ExposeData() -> ExposeLedger()).
        // Order: loadTasks first, then loadVehicleTasks (verbatim from the original ExposeData).
        private void ExposeLedger()
        {
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
