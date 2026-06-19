using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The transporter/shuttle bulk-load planner — the <see cref="PackAnimalLoad"/> analogue for a
    /// <see cref="LoadTransportersAdapter"/>. Reuses HD's nearest-first SWEEP (<see cref="BulkHaul.BuildPool"/> +
    /// <see cref="BulkHaulPolicy.CountWithinCeiling"/> + CE clamp) to scoop the pawn's CLAIMED slice of the group
    /// manifest into inventory under the smart-overload ceiling, then walks to the transporter once and deposits
    /// (see <see cref="JobDriver_LoadTransportersInBulk"/>). Each per-stack pull is clamped by
    /// <see cref="TransportLoadPlan.DeliverableUnits"/> (stack / manifest-remaining / ledger-available / carry) under
    /// the destination-mass <see cref="TransportLoadPlan.TripMassBudget"/> — the one genuinely new term over plain
    /// bulk-haul. The CLAIM is recorded in the driver's <c>Notify_Starting</c> (not here), so a speculative menu/
    /// work-scan probe that builds-but-never-starts never reserves quota.
    /// </summary>
    public static class TransportLoad
    {
        private const float MinSearchRadius = 12f;
        private const int MaxStacks = 24;
        private const float PoolRadiusHops = 4f;

        // Reused snowball working sets, copied into the FRESH job-owned targetQueueB/countQueue at the end (the Job
        // pool owns + scribes those). [ThreadStatic] + lazy-init per the repo's hook-reachable scratch convention;
        // Cleared at the point of use, never trusted empty. SAFETY: a single TryGiveBulkJob runs to completion (no
        // nested re-entry into this builder) before the next reuse, so sharing on one thread is sound.
        [System.ThreadStatic] private static List<Thing> scratchThings;
        [System.ThreadStatic] private static List<int> scratchCounts;
        // Storage Network opt-in path only: a snapshot of the claimable defs taken before committing (claimLeft is
        // decremented as stacks commit, so it can't be iterated live). [ThreadStatic] + lazy-init like the scratch
        // above; Cleared at use, never trusted empty. Untouched unless the SN bulk-load opt-in is on AND SN is active.
        [System.ThreadStatic] private static List<ThingDef> scratchNetworkDefs;

        // B2 — per-frame "is there bulk-load work?" memo for the AUTOMATIC work-scan path. The transporter/portal/
        // vehicle work-scan calls HasPotentialBulkWork for EVERY pawn × EVERY group per scan, and each call would
        // otherwise re-run LoadRegisterOrUpdate + the ledger's LoadHasWork need-scan — pure repeated cost when the
        // (pawn, group) pair already answered this same tick. One generation per tick: the SAME (pawn, groupId)
        // within a TicksGame returns the cached boolean (both true and false results — the "no work" reject is the
        // common case on a scan). Mirrors BulkHaul.planCache exactly: [ThreadStatic] + lazy-init (ThreadStatic field
        // initializers only run on the static-ctor thread, so a worker-thread scan gets its own slot), self-clearing
        // by tick-stamp.
        //
        // CACHES ONLY THE BOOLEAN — never a ledger snapshot, a claimable map, or anything that feeds a CLAIM. The
        // ledger and every live game read stay LIVE: TryGiveBulkJob (the path that actually reserves quota in the
        // driver's Notify_Starting) does NOT consult this memo at all — it always re-reads LoadAvailableToClaim
        // fresh. So this can never flip a claim/quota decision; it only short-circuits the same-tick repeat of the
        // cheap AVAILABILITY probe (HasJobOnThing). A stale-across-ticks read is impossible because the key includes
        // TicksGame (a new tick clears the dict), and a cross-session collision is impossible because the populate is
        // guarded on tick != -1 (TicksGame is never -1 in play; -1 is only the uninitialized stamp), the same
        // cross-session safeguard PawnMassCache/InventoryShare/etc. rely on. ClearLoadWorkCache() is the FinalizeInit
        // hygiene clear (sibling of BulkHaul.ClearPlanCache) for the orchestrator to wire into the GameComponent.
        [System.ThreadStatic] private static int workCacheTick;
        [System.ThreadStatic] private static Dictionary<long, bool> workCache;

        // Self-register the per-session load-work memo clear with the game-load hygiene sweep (see CacheRegistry), so
        // it can never be forgotten. The static ctor runs once, the first time any member is touched (the only way
        // the memo can hold cross-session data); ClearLoadWorkCache resets the FinalizeInit (main) thread's slot —
        // the `tick != -1` populate guard is the actual cross-session safeguard.
        static TransportLoad() => CacheRegistry.Register(ClearLoadWorkCache);

        /// <summary>Drop the per-frame load-work memo and reset its tick stamp. The FinalizeInit hygiene sibling of
        /// <see cref="BulkHaul.ClearPlanCache"/>: the [ThreadStatic] memo is static state that survives a quickload,
        /// so an equal TicksGame across a load could otherwise serve a previous session's (pawn-id, groupId) boolean.
        /// (The <c>tick != -1</c> populate guard is the actual cross-session safeguard — TicksGame is never -1 in
        /// play — so this is decision-neutral consistency with the existing FinalizeInit clear list; the orchestrator
        /// wires it alongside <c>BulkHaul.ClearPlanCache()</c> in <c>HaulersDreamGameComponent.FinalizeInit</c>.)
        /// Clears only the main (FinalizeInit) thread's slot — other threads' memos are per-tick self-clearing.</summary>
        public static void ClearLoadWorkCache()
        {
            workCache?.Clear();
            workCacheTick = -1;
        }

        /// <summary>Is there bulk-load work for this pawn on the TRANSPORTER loadable? Feature on, not drafted,
        /// eligible (auto path), the comp present, and the ledger says the pawn can claim something.</summary>
        public static bool HasPotentialBulkWork(Pawn pawn, IManagedLoadable loadable)
            => HasPotentialBulkWork(pawn, loadable, FeatureEnabled(loadable));

        /// <summary>The shared "potential bulk work" gate, with the feature flag resolved by the caller (so the portal
        /// path can gate on <c>enableBulkLoadPortal</c> while the transporter path gates on
        /// <c>enableBulkLoadTransporters</c>). Everything else is identical.</summary>
        private static bool HasPotentialBulkWork(Pawn pawn, IManagedLoadable loadable, bool featureEnabled)
        {
            if (!featureEnabled || loadable == null)
                return false;
            if (pawn?.Map == null || pawn.Drafted)
                return false;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return false;
            // Auto-path eligibility (the work-scan / utility takeover route). Player orders skip this in the menu
            // provider (deposit goes into a container → nothing strands), so this gate is for the automatic path.
            if (!YieldRouter.IsEligible(pawn))
                return false;
            var ledger = HaulersDreamGameComponent.Instance;
            if (ledger == null)
                return false;

            // B2 — per-frame availability memo (mirror BulkHaul.planCache). The work scan re-probes this same
            // (pawn, group) many times per tick; the LoadRegisterOrUpdate + LoadHasWork need-scan below is the
            // dominant per-scan cost, so cache its BOOLEAN result keyed on (TicksGame, pawn, groupId). The ledger
            // itself stays LIVE — only the repeated same-tick availability answer is memoized; nothing here feeds a
            // claim (TryGiveBulkJob never reads this cache and always re-reads LoadAvailableToClaim fresh). The key
            // includes the tick (a new tick clears the dict), so it can never serve a stale cross-tick decision.
            int tick = Find.TickManager?.TicksGame ?? -1;
            var cache = workCache ?? (workCache = new Dictionary<long, bool>());
            if (tick != workCacheTick)
            {
                cache.Clear();
                workCacheTick = tick;
            }
            long key = ((long)pawn.thingIDNumber << 32) | (uint)loadable.GetUniqueLoadID();
            if (tick != -1 && cache.TryGetValue(key, out bool cachedHasWork))
                return cachedHasWork;

            // LIVE reads (never cached): refresh the ledger task from the live manifest, then ask whether THIS pawn
            // can still claim something. Same calls as before — only their repeat within one tick is short-circuited.
            ledger.LoadRegisterOrUpdate(loadable);
            bool hasWork = ledger.LoadHasWork(loadable, pawn);
            // Only populate on a real in-play tick (TicksGame is never -1 in play; -1 is the uninitialized stamp).
            // This is the cross-session safeguard — a quickload landing on an equal tick number can never serve a
            // previous session's (pawn-id, groupId) entry because a -1 read is never stored (matches the
            // PawnMassCache / InventoryShare convention this assembly already uses).
            if (tick != -1)
                cache[key] = hasWork;
            return hasWork;
        }

        /// <summary>Is there bulk-load work for this pawn on the PORTAL loadable? Same gate as the transporter path,
        /// only the feature flag differs (<c>enableBulkLoadPortal</c>).</summary>
        public static bool HasPotentialBulkWorkPortal(Pawn pawn, IManagedLoadable loadable)
            => HasPotentialBulkWork(pawn, loadable, HaulersDreamMod.Settings?.enableBulkLoadPortal ?? false);

        /// <summary>The feature flag for a loadable — the explicit 3-way on <see cref="IManagedLoadable.Kind"/>
        /// (addendum SF2): transporters/shuttles gate on <c>enableBulkLoadTransporters</c>, portals on
        /// <c>enableBulkLoadPortal</c>, and vehicles on the master <c>enableVehicleFramework</c> AND the sub
        /// <c>enableBulkLoadVehicles</c>.</summary>
        private static bool FeatureEnabled(IManagedLoadable loadable)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || loadable == null)
                return false;
            switch (loadable.Kind)
            {
                case LoadableKind.Portal: return s.enableBulkLoadPortal;
                case LoadableKind.Vehicle: return s.enableVehicleFramework && s.enableBulkLoadVehicles;
                default: return s.enableBulkLoadTransporters;
            }
        }

        /// <summary>
        /// Build the bulk-load job: (1) refresh the task's <c>totalNeeded</c> + read the pawn's claimable per-def
        /// map; (2) run the sweep to pick nearest source stacks of those defs into a (targetQueueB, countQueue)
        /// pickup chain, clamping each pull via <see cref="TransportLoadPlan.DeliverableUnits"/> under the trip-mass
        /// budget; (3) make the <c>HaulersDream_LoadTransportersInBulk</c> job (targetA = the parent transporter).
        /// PURE planning — no reservations, no claim (the driver claims on start). Null when nothing is claimable /
        /// nothing reachable to sweep. <paramref name="playerOrder"/> skips the auto eligibility gate.
        /// </summary>
        public static Job TryGiveBulkJob(Pawn pawn, IManagedLoadable loadable, bool playerOrder = false)
        {
            return TryGiveBulkJob(pawn, loadable, JobDefFor(loadable), FeatureEnabled(loadable), playerOrder);
        }

        /// <summary>The bulk-load JobDef for a loadable — the explicit 3-way on <see cref="IManagedLoadable.Kind"/>
        /// (addendum SF2): transporter, portal, or vehicle.</summary>
        private static JobDef JobDefFor(IManagedLoadable loadable)
        {
            if (loadable != null)
                switch (loadable.Kind)
                {
                    case LoadableKind.Portal: return HaulersDreamDefOf.HaulersDream_LoadPortalInBulk;
                    case LoadableKind.Vehicle: return HaulersDreamDefOf.HaulersDream_LoadVehicleInBulk;
                }
            return HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk;
        }

        private static Job TryGiveBulkJob(Pawn pawn, IManagedLoadable loadable, JobDef jobDef, bool featureEnabled, bool playerOrder)
        {
            var s = HaulersDreamMod.Settings;
            var map = pawn?.Map;
            if (s == null || !featureEnabled || map == null || loadable == null)
                return null;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return null;
            if (!playerOrder && !YieldRouter.IsEligible(pawn))
                return null;

            var ledger = HaulersDreamGameComponent.Instance;
            if (ledger == null)
                return null;
            ledger.LoadRegisterOrUpdate(loadable);
            var claimable = ledger.LoadAvailableToClaim(loadable, pawn);
            if (claimable.Count == 0)
                return null;

            // The per-def remaining budget the sweep decrements as it commits stacks. Previously TWO dictionaries
            // (ledgerLeft + manifestLeft) were cloned from the SAME `claimable` and decremented IDENTICALLY by the
            // same per-stack take, so they were always equal — DeliverableUnits saw manifestRem == ledgerAvail every
            // call. Collapsed to ONE dict passed as both args (HD-JOBLIST): one fewer Dictionary clone per probe,
            // behavior-identical (claimable ≤ the live manifest, so it's the binding per-def cap either way).
            var claimLeft = new Dictionary<ThingDef, int>(claimable);

            // Carry ceiling (smart overload) + the trip-mass budget (pawn free space AND group headroom).
            float maxCap = MassUtility.Capacity(pawn);
            float baseCap = CarryMath.EffectiveCapacity(maxCap, s.carryLimitFraction);
            float ceiling = BulkHaulPolicy.CeilingKg(s.overloadLevel, OverloadGate.NoOverloadFor(pawn, s), baseCap);
            float running = MassUtility.GearAndInventoryMass(pawn);
            float bulkRoom = CECompat.AvailableBulk(pawn);

            float pawnFree = float.IsPositiveInfinity(ceiling) ? float.MaxValue : Math.Max(0f, ceiling - running);
            float tripMass = TransportLoadPlan.TripMassBudget(pawnFree,
                loadable.GetMassCapacity(), loadable.GetMassUsage(), loadable.HasMassCap);
            float massLeft = tripMass; // shrinks as stacks are committed (destination + pawn mass headroom)

            var pool = BulkHaul.BuildPool(pawn, loadable.GetParentThing(), map, MinSearchRadius * PoolRadiusHops);
            // SPAWNED-STORAGE COMPATIBILITY (shelves, deep storage, plain stockpiles — any storage that keeps its
            // contents as spawned on-map stacks): BuildPool draws ONLY from the loose-haulables lister (things NOT in
            // valid storage), so when the manifest goods sit in storage the sweep finds nothing and the pawn falls
            // back to vanilla's one-stack-per-trip loading ("takes 1 pack instead of everything they need"). Add the
            // spawned, in-storage stacks of the CLAIMABLE manifest defs so HD bulk-loads them too — exactly the defs
            // vanilla's loader would pull from storage anyway. Everything downstream stays bounded: the per-stack
            // DeliverableUnits / claim-ledger clamps and the existing per-candidate gate (reachable / reservable /
            // not-forbidden, via TryQualify) still apply, and the deposit goes INTO the transporter, so this can only
            // let the pawn load manifest items it is already allowed to load — never more, never stranded.
            // NOTE: SPAWNED-only. A VIRTUAL / digital storage like Storage Network keeps its items DESPAWNED inside
            // server ThingOwners (absent from ThingsOfDef), so they are NOT picked up here — the opt-in
            // AppendNetworkClaimables / StorageNetworkCompat path below handles the network case separately.
            AddStoredClaimables(pool, claimable, map);
            var claimedByOthers = RouteSelection.ClaimedByOtherPawns(pawn);

            // Reused working sets (Cleared at use), copied into the fresh job-owned queues below.
            var things = scratchThings ?? (scratchThings = new List<Thing>());
            var counts = scratchCounts ?? (scratchCounts = new List<int>());
            things.Clear();
            counts.Clear();
            var from = loadable.GetParentThing()?.Position ?? pawn.Position;

            while (things.Count < MaxStacks && running < ceiling - 0.0001f && massLeft > 0.0001f)
            {
                var next = NearestEligible(pawn, pool, from, claimedByOthers, claimLeft,
                    ceiling, running, bulkRoom, massLeft, out int take);
                if (next == null)
                    break;
                things.Add(next);
                counts.Add(take);
                float unit = next.GetStatValue(StatDefOf.Mass);
                running += take * unit;
                bulkRoom -= take * CECompat.BulkPerUnit(next);
                massLeft -= take * unit;
                claimLeft[next.def] = Math.Max(0, (claimLeft.TryGetValue(next.def, out int l) ? l : 0) - take);
                from = next.Position;
            }

            // Storage Network (virtual servers): its items live DESPAWNED in the network, invisible to BuildPool +
            // AddStoredClaimables (both spawned-only). When the experimental opt-in is on AND SN is installed,
            // supplement the plan with the network's despawned stacks of the still-claimable defs — staged in
            // targetQueueB and materialised at a usable terminal by SN's OWN StartJob auto-spawn when the job runs.
            // Bounded by the SAME claim/carry/mass/bulk budget as the sweep above; a stack SN can't materialise is
            // skipped by the driver's sweep toil (it requires Spawned), so this can never strand cargo or over-pull.
            if (s.enableStorageNetworkBulkLoad && StorageNetworkCompat.IsActive)
                AppendNetworkClaimables(pawn, map, claimLeft, claimedByOthers, things, counts,
                    ref running, ceiling, ref bulkRoom, ref massLeft);

            if (things.Count == 0)
                return null; // nothing reachable to sweep of the claimable defs

            var job = JobMaker.MakeJob(jobDef, loadable.GetParentThing());
            job.targetQueueB = new List<LocalTargetInfo>(things.Count);
            for (int i = 0; i < things.Count; i++)
                job.targetQueueB.Add(things[i]);
            job.countQueue = new List<int>(counts);
            job.count = 1; // sentinel: a -1 Job.count reads as "broken" in some vanilla checks
            if (s.verboseLogging)
                HDLog.Dbg($"TransportLoad: {pawn} sweeping {things.Count} stacks for group {loadable.GetUniqueLoadID()} (~{running:0.#}kg).");
            return job;
        }

        /// <summary>
        /// Append the spawned, on-map stacks of the CLAIMABLE manifest defs that <see cref="BulkHaul.BuildPool"/>
        /// omits because they sit in valid storage (a stockpile, shelf, deep-storage cell, or a storage-mod building
        /// such as Storage Network). Dedups against the loose pool already built. PURE — no reservations; the
        /// eligibility + per-stack clamp run later in <see cref="NearestEligible"/>/<see cref="TryQualify"/>, exactly
        /// as for the loose candidates. Only adds Spawned, on-map things (the driver picks up via <c>SplitOff</c>,
        /// which needs a spawned stack); items a storage holds off-map are correctly skipped (safe no-op).
        /// </summary>
        private static void AddStoredClaimables(List<Thing> pool, Dictionary<ThingDef, int> claimable, Map map)
        {
            if (pool == null || claimable == null || map == null)
                return;
            // Dedup against the loose pool in one pass (the loose pool of a claimable def overlaps ThingsOfDef).
            var seen = new HashSet<Thing>(pool);
            foreach (var kv in claimable)
            {
                var def = kv.Key;
                if (def == null || kv.Value <= 0)
                    continue;
                var things = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < things.Count; i++)
                {
                    var t = things[i];
                    if (t == null || !t.Spawned || t.Map != map)
                        continue;
                    // Mirror BulkHaul.BuildPoolInto's loose-pool filters so this stored supplement stays consistent
                    // with the pool it extends: corpse hauling keeps its own vanilla carry-in-hands flow (corpses
                    // don't belong in pockets), and non-haulable defs are never swept. A transporter/portal manifest
                    // CAN list a corpse, so without this a stored (or loose) manifest corpse would be scooped into
                    // inventory and deposited, instead of vanilla carrying it in hands.
                    if (t is Corpse || !t.def.EverHaulable)
                        continue;
                    if (t.ParentHolder is Pawn_InventoryTracker)
                        continue; // a pawn's carried inventory is not a load source on this sweep
                    if (seen.Add(t))
                        pool.Add(t);
                }
            }
        }

        /// <summary>
        /// EXPERIMENTAL Storage Network supplement (opt-in; only reached when the setting is on AND SN is active).
        /// Adds the network's DESPAWNED stacks of the still-claimable manifest defs to the plan (things/counts),
        /// bounded by the SAME claim / carry-ceiling / trip-mass / CE-bulk budget as the loose+spawned sweep above.
        /// They are staged into job.targetQueueB and materialised at a usable terminal by Storage Network's OWN
        /// <c>Pawn_JobTracker.StartJob</c> auto-spawn when the job runs; HD's sweep toil skips any queue entry that
        /// fails to materialise (it requires Spawned), so this can never strand cargo or over-pull. PURE planning —
        /// no reservations and no spawning here (the materialisation is SN's, at job start, not during this probe).
        /// </summary>
        private static void AppendNetworkClaimables(Pawn pawn, Map map, Dictionary<ThingDef, int> claimLeft,
            HashSet<Thing> claimedByOthers, List<Thing> things, List<int> counts,
            ref float running, float ceiling, ref float bulkRoom, ref float massLeft)
        {
            var terminals = StorageNetworkCompat.UsableTerminals(pawn, map);
            if (terminals.Count == 0)
                return; // no reachable/usable terminal — the network is inaccessible to this pawn right now

            // Snapshot the claimable defs that still have budget: we decrement claimLeft as we commit, so we can't
            // iterate it live. Dedup network stacks against the already-planned set AND across terminals that share a
            // network (each terminal returns the whole network's stacks).
            var defs = scratchNetworkDefs ?? (scratchNetworkDefs = new List<ThingDef>());
            defs.Clear();
            foreach (var kv in claimLeft)
                if (kv.Key != null && kv.Value > 0)
                    defs.Add(kv.Key);
            if (defs.Count == 0)
                return;
            var seen = new HashSet<Thing>(things);

            for (int di = 0; di < defs.Count; di++)
            {
                if (things.Count >= MaxStacks || running >= ceiling - 0.0001f || massLeft <= 0.0001f)
                    break;
                var def = defs[di];
                for (int ti = 0; ti < terminals.Count; ti++)
                {
                    if (things.Count >= MaxStacks || running >= ceiling - 0.0001f || massLeft <= 0.0001f)
                        break;
                    var stacks = StorageNetworkCompat.NetworkStacksOfDef(terminals[ti], def);
                    for (int si = 0; si < stacks.Count; si++)
                    {
                        int claimAvail = claimLeft.TryGetValue(def, out int la) ? la : 0;
                        if (claimAvail <= 0)
                            break; // this def's claim budget is spent
                        if (things.Count >= MaxStacks || running >= ceiling - 0.0001f || massLeft <= 0.0001f)
                            break;
                        var t = stacks[si];
                        if (t == null || t.def != def || t.Destroyed || !seen.Add(t))
                            continue; // wrong def / gone / already planned (or seen via another terminal on this network)
                        if (claimedByOthers.Contains(t))
                            continue; // another pawn already has this exact network stack queued
                        int take = ClampNetworkTake(pawn, t, claimAvail, ceiling, running, bulkRoom, massLeft);
                        if (take <= 0)
                            continue; // too heavy/bulky for the remaining budget — a lighter neighbour may still fit
                        things.Add(t);
                        counts.Add(take);
                        float unit = t.GetStatValue(StatDefOf.Mass);
                        running += take * unit;
                        bulkRoom -= take * CECompat.BulkPerUnit(t);
                        massLeft -= take * unit;
                        claimLeft[def] = Math.Max(0, claimAvail - take);
                    }
                }
            }
        }

        // The per-stack deliverable clamp for a Storage Network candidate — the SAME budget math as TryQualify
        // (DeliverableUnits over the claim budget, the carry-ceiling CountWithinCeiling, the CE bulk cap, and the
        // trip-mass UnitsWithinMassBudget), MINUS the spawned-thing gates (IsForbidden / PawnCanAutomaticallyHaulFast
        // / ExtraSweepReach) — those don't apply to a despawned network stack, whose reach/usability is gated once at
        // the TERMINAL in StorageNetworkCompat.UsableTerminals.
        private static int ClampNetworkTake(Pawn pawn, Thing chosen, int claimAvail,
            float ceiling, float running, float bulkRoom, float massLeft)
        {
            float unit = chosen.GetStatValue(StatDefOf.Mass);
            int carryAffordable = BulkHaulPolicy.CountWithinCeiling(ceiling, running, unit, chosen.stackCount);
            carryAffordable = Math.Min(carryAffordable, CECompat.MaxFitCount(pawn, chosen));
            float bulkPer = CECompat.BulkPerUnit(chosen);
            if (bulkPer > 0f && !float.IsPositiveInfinity(bulkRoom))
                carryAffordable = Math.Min(carryAffordable, (int)Math.Floor(bulkRoom / bulkPer));
            int massAffordable = TransportLoadPlan.UnitsWithinMassBudget(massLeft, unit, chosen.stackCount);
            return TransportLoadPlan.DeliverableUnits(chosen.stackCount, claimAvail, claimAvail,
                Math.Min(carryAffordable, massAffordable));
        }

        // Nearest pool candidate of a CLAIMABLE def within reach, clamped per-stack via DeliverableUnits under the
        // trip-mass budget. Removes chosen/rejected candidates from the pool as it scans (like BulkHaul).
        //
        // B3 — when loadHybridPathing is ON, the FINAL ranking switches from straight-line to real A* path cost over
        // the top-N straight-line candidates (the true-nearest by walkable distance, not crow-flies). The flag is OFF
        // by default, and the off-path below is byte-IDENTICAL to before (the on-path is a separate early-return
        // branch that runs no pathfinding when off — see the single `if (loadHybridPathing)` gate).
        private static Thing NearestEligible(Pawn pawn, List<Thing> pool, IntVec3 from, HashSet<Thing> claimedByOthers,
            Dictionary<ThingDef, int> claimLeft,
            float ceiling, float running, float bulkRoom, float massLeft, out int take)
        {
            // B3 ON: re-rank the top-N straight-line-nearest QUALIFYING candidates by real path cost. Gated on the
            // OFF-by-default setting so this whole branch is skipped (zero pathfinding, zero allocation) on the
            // default path — when it's off, execution falls straight through to the unchanged straight-line loop.
            if (HaulersDreamMod.Settings?.loadHybridPathing == true)
            {
                var ranked = NearestEligibleHybrid(pawn, pool, from, claimedByOthers, claimLeft,
                    ceiling, running, bulkRoom, massLeft, out take);
                return ranked;
            }

            take = 0;
            while (true)
            {
                int bestIdx = -1;
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < pool.Count; i++)
                {
                    var t = pool[i];
                    if (t?.def == null || !claimLeft.TryGetValue(t.def, out int la) || la <= 0)
                        continue; // not a claimable def, or its claim budget is spent
                    float d = (t.Position - from).LengthHorizontalSquared;
                    if (d < bestDistSq) { bestDistSq = d; bestIdx = i; }
                }
                if (bestIdx < 0)
                    return null;
                var chosen = pool[bestIdx];
                pool.RemoveAt(bestIdx);

                int deliverable = TryQualify(pawn, chosen, claimedByOthers, claimLeft,
                    ceiling, running, bulkRoom, massLeft);
                if (deliverable <= 0)
                    continue; // forbidden / claimed / too heavy / no claim budget — a neighbor may still fit
                take = deliverable;
                return chosen;
            }
        }

        // How many candidates' real path cost we'll evaluate per NearestEligible call when loadHybridPathing is on.
        // Clamped from the loadPathfindingCandidates setting (default 8, slider 2..24) — keeps the pathfinding budget
        // bounded (at most N FindPathNow calls per pool pick, only the top-N straight-line candidates).
        private static int HybridCandidateBudget()
        {
            int n = HaulersDreamMod.Settings?.loadPathfindingCandidates ?? 8;
            return n < 2 ? 2 : (n > 24 ? 24 : n);
        }

        // B3 on-path: take the top-N straight-line-nearest QUALIFYING pool candidates (same eligibility + per-stack
        // clamp as the off-path), then return the one with the lowest REAL A* path cost from `from` — the genuine
        // nearest by walkable distance, which a straight-line pick can get wrong across walls/rivers. Pool-mutation
        // contract matches the off-path: the CHOSEN candidate is removed; candidates that fail TryQualify during the
        // scan are removed (permanently ineligible this sweep); the other qualifying-but-not-chosen candidates STAY
        // in the pool for the next NearestEligible call (so a single sweep still considers them). Returns null (and
        // take 0) when nothing qualifies — identical outcome to the off-path's bestIdx < 0 / loop-exhaustion.
        private static Thing NearestEligibleHybrid(Pawn pawn, List<Thing> pool, IntVec3 from, HashSet<Thing> claimedByOthers,
            Dictionary<ThingDef, int> claimLeft,
            float ceiling, float running, float bulkRoom, float massLeft, out int take)
        {
            take = 0;
            int budget = HybridCandidateBudget();
            var cands = hybridCands ?? (hybridCands = new List<Thing>());
            var takes = hybridTakes ?? (hybridTakes = new List<int>());
            cands.Clear();
            takes.Clear();

            // Gather up to `budget` straight-line-nearest QUALIFYING candidates, removing rejected ones from the pool
            // (the off-path's "rejected leaves the pool" contract). The straight-line gather is the cheap pre-filter
            // that bounds how many expensive FindPathNow calls run — only these top-N get a real path.
            while (cands.Count < budget)
            {
                int bestIdx = -1;
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < pool.Count; i++)
                {
                    var t = pool[i];
                    if (t?.def == null || !claimLeft.TryGetValue(t.def, out int la) || la <= 0)
                        continue; // not a claimable def, or its claim budget is spent
                    float d = (t.Position - from).LengthHorizontalSquared;
                    if (d < bestDistSq) { bestDistSq = d; bestIdx = i; }
                }
                if (bestIdx < 0)
                    break; // no more claimable-def candidates straight-line
                var chosen = pool[bestIdx];
                pool.RemoveAt(bestIdx);

                int deliverable = TryQualify(pawn, chosen, claimedByOthers, claimLeft,
                    ceiling, running, bulkRoom, massLeft);
                if (deliverable <= 0)
                    continue; // permanently ineligible this sweep — stays out of the pool (off-path parity)
                cands.Add(chosen);
                takes.Add(deliverable);
            }

            if (cands.Count == 0)
                return null;
            if (cands.Count == 1)
            {
                // Only one qualifier — no re-rank needed; return it exactly as the off-path would (no pathfinding).
                take = takes[0];
                return cands[0];
            }

            // Re-rank by REAL path cost. The candidate with the lowest walkable cost from `from` wins; an unreachable
            // candidate (no path) falls back to straight-line distance so it can never be picked over a reachable
            // one but is still usable if it's the only option. The map's pathFinder is the same entry point
            // EnRoutePickup uses (Perfect Pathfinding's accuracy is inherited automatically when present).
            var map = pawn.Map;
            int bestCand = 0;
            float bestCost = float.MaxValue;
            for (int i = 0; i < cands.Count; i++)
            {
                var t = cands[i];
                float cost = PathCostTo(map, pawn, from, t);
                if (cost < 0f) // unreachable (no path) — rank it behind any reachable candidate by straight-line.
                    cost = float.MaxValue - (t.Position - from).LengthHorizontalSquared; // (a genuine 0-cost path —
                    // pawn standing on the pickup — is reachable and now correctly ranks BEST, not last.)
                if (cost < bestCost) { bestCost = cost; bestCand = i; }
            }

            var winner = cands[bestCand];
            take = takes[bestCand];
            // Put the NON-chosen qualifiers back into the pool so this sweep still considers them on the next call —
            // only the winner is consumed (matches the off-path, which removes only the one it returns).
            for (int i = 0; i < cands.Count; i++)
                if (i != bestCand)
                    pool.Add(cands[i]);
            cands.Clear();
            takes.Clear();
            return winner;
        }

        // Scratch for the B3 hybrid gather (the top-N candidates + their clamped takes), reused per-thread so a pool
        // pick allocates nothing. [ThreadStatic] + lazy-init matches this file's scratchThings/scratchCounts and the
        // BulkHaul.scratchPool convention; Cleared at the point of use, never trusted empty. SAFETY: one
        // NearestEligibleHybrid call runs to completion (no nested re-entry) before the next reuse, so sharing on one
        // thread is sound. Only ever touched on the loadHybridPathing-ON path.
        [System.ThreadStatic] private static List<Thing> hybridCands;
        [System.ThreadStatic] private static List<int> hybridTakes;

        // Full per-candidate eligibility + per-stack DeliverableUnits clamp, factored out of the straight-line loop
        // so the B3 hybrid path applies the IDENTICAL qualification (the re-rank only changes which qualifier is
        // picked FIRST, never WHICH things are eligible or HOW MANY units are taken). Returns the deliverable count,
        // or 0 when the candidate is forbidden / claimed by another pawn / too heavy/bulky / out of claim budget.
        // PURE — no pool mutation (the caller owns removal), no reservations.
        private static int TryQualify(Pawn pawn, Thing chosen, HashSet<Thing> claimedByOthers,
            Dictionary<ThingDef, int> claimLeft, float ceiling, float running, float bulkRoom, float massLeft)
        {
            if (chosen.IsForbidden(pawn) || claimedByOthers.Contains(chosen))
                return 0;

            float unit = chosen.GetStatValue(StatDefOf.Mass);
            // Carry-affordable (smart overload) + CE.
            int carryAffordable = BulkHaulPolicy.CountWithinCeiling(ceiling, running, unit, chosen.stackCount);
            carryAffordable = Math.Min(carryAffordable, CECompat.MaxFitCount(pawn, chosen));
            float bulkPer = CECompat.BulkPerUnit(chosen);
            if (bulkPer > 0f && !float.IsPositiveInfinity(bulkRoom))
                carryAffordable = Math.Min(carryAffordable, (int)Math.Floor(bulkRoom / bulkPer));
            // Trip-mass budget (destination + pawn mass headroom for THIS stack).
            int massAffordable = TransportLoadPlan.UnitsWithinMassBudget(massLeft, unit, chosen.stackCount);

            // ledgerLeft and manifestLeft were always equal (one source, identical decrements) — now one dict, so
            // DeliverableUnits's manifestRem and ledgerAvail args take the same value, exactly as before.
            int claimAvail = claimLeft.TryGetValue(chosen.def, out int l) ? l : 0;
            int deliverable = TransportLoadPlan.DeliverableUnits(
                chosen.stackCount, claimAvail, claimAvail, Math.Min(carryAffordable, massAffordable));
            if (deliverable <= 0)
                return 0; // too heavy / no claim budget left — a lighter/other-def neighbor may still fit
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, chosen, forced: false))
                return 0;
            if (!ExtraSweepReach.Allows(pawn, chosen))
                return 0; // bonus extra: cap reach at Some (don't load transport cargo out of vacuum/fire)
            return deliverable;
        }

        // One A* leg cost via the MC1.6 pathfinder, pooling the path (the EXACT idiom EnRoutePickup.PathCost uses:
        // FindPathNow(start, destThing, pawn, tuning=null, peMode) -> PawnPath with .Found/.TotalCost, ALWAYS
        // released to the pool in a finally). Returns a NEGATIVE sentinel (-1f) when no path exists so the caller
        // can distinguish "unreachable" from a genuine 0-cost found path (pawn already standing on the pickup).
        // ClosestTouch matches a haul pickup's "walk adjacent to the stack" end mode.
        private static float PathCostTo(Map map, Pawn pawn, IntVec3 start, Thing destThing)
        {
            if (map?.pathFinder == null)
                return -1f;
            PawnPath path = map.pathFinder.FindPathNow(start, destThing, pawn, null, PathEndMode.ClosestTouch);
            try
            {
                return path != null && path.Found ? path.TotalCost : -1f;
            }
            finally
            {
                path?.ReleaseToPool();
            }
        }
    }
}
