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
        /// <summary>
        /// A pawn that is a BOARDING PASSENGER of THIS loadable (its boarding-lord duty targets this exact
        /// transporter group / portal). Such a pawn is Lord-driven, so <see cref="YieldRouter.IsEligible"/> stands it
        /// down for every autonomous HD action — but loading the very shuttle/portal it is about to board IS its
        /// directed task, not an interruption of one. Admitting it here (and ONLY here, for ITS OWN loadable) lets a
        /// selected passenger bulk-load instead of one-stack vanilla loading, without loosening the global eligibility
        /// gate that protects ritual/caravan/quest inventories. Scoped tightly: only the two boarding duties, only when
        /// the duty's group/focus matches this loadable; any other duty (ritual, caravan, …) and any other loadable
        /// fall through to the normal IsEligible reject. The board/launch timing is still governed by the anti-conflict
        /// board gate (it releases on the goods manifest emptying), so this can't cause a premature launch.
        /// </summary>
        private static bool IsBoardingPassengerFor(Pawn pawn, IManagedLoadable loadable)
        {
            var duty = pawn?.mindState?.duty;
            if (duty == null || loadable == null)
                return false;
            if (loadable.Kind == LoadableKind.Transporter && duty.def == DutyDefOf.LoadAndEnterTransporters)
                return duty.transportersGroup >= 0 && duty.transportersGroup == loadable.GetUniqueLoadID();
            if (loadable.Kind == LoadableKind.Portal && duty.def == DutyDefOf.LoadAndEnterPortal)
                return duty.focus.Thing != null && duty.focus.Thing == loadable.GetParentThing();
            return false;
        }

        /// <summary>True if the pawn is carrying a tagged inventory stack that the reused deposit driver would
        /// actually MOVE into <paramref name="loadable"/> — i.e. the stack is SURPLUS above the pawn's keep-stock
        /// (<see cref="InventorySurplus.SurplusOf"/> &gt; 0) AND the manifest still wants this EXACT thing (a 3-tier
        /// <see cref="TransferableUtility.TransferableMatchingDesperate"/> match with CountToTransfer &gt; 0,
        /// variant-aware). This MIRRORS the driver's own <c>HasDepositableForGroup</c> predicate, so the deposit-only
        /// recovery only fires when the driver will deposit ≥1 unit — keying on claimable-by-def instead would
        /// re-issue a NO-OP deposit-only job forever (a keep-stock livelock for kept-but-wanted stock, or an
        /// off-variant the manifest can't accept). Read-only (PeekHashSet). Rare path (a stranded passenger whose
        /// ground sweep is empty), so materialising the manifest here is fine.</summary>
        private static bool HoldsCargoLoadableWants(Pawn pawn, IManagedLoadable loadable)
        {
            var comp = pawn?.GetComp<CompHauledToInventory>();
            var inner = pawn?.inventory?.innerContainer;
            if (comp == null || inner == null || loadable == null)
                return false;
            var manifest = loadable.GetTransferables();
            if (manifest == null || manifest.Count == 0)
                return false;
            foreach (var t in comp.PeekHashSet())
            {
                if (t == null || t.Destroyed || t.def == null || !inner.Contains(t))
                    continue;
                if (InventorySurplus.SurplusOf(pawn, t) <= 0)
                    continue; // entirely within keep-stock → the deposit loop would move 0
                var match = TransferableUtility.TransferableMatchingDesperate(t, manifest, TransferAsOneMode.PodsOrCaravanPacking);
                if (match != null && match.CountToTransfer > 0)
                    return true;
            }
            return false;
        }

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
            // A boarding PASSENGER of this loadable is admitted: loading the shuttle/portal it's about to board is its
            // directed task (it's otherwise IsEligible-ineligible only because it's Lord-driven). MUST stay in lockstep
            // with the same carve-out in TryGiveBulkJob, or HasJob/JobOn diverge into the "10 jobs in one tick" loop.
            if (!IsBoardingPassengerFor(pawn, loadable) && !YieldRouter.IsEligible(pawn))
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
            // Admit a boarding PASSENGER of this loadable (see IsBoardingPassengerFor) past the autonomous-eligibility
            // gate — loading the shuttle/portal it's about to board is its directed task. Lockstep with the identical
            // carve-out in HasPotentialBulkWork (a HasJob/JobOn divergence would loop). Player orders already bypass.
            if (!playerOrder && !IsBoardingPassengerFor(pawn, loadable) && !YieldRouter.IsEligible(pawn))
                return null;

            var ledger = HaulersDreamGameComponent.Instance;
            if (ledger == null)
                return null;
            // Capture the entry: the fair-share divisor below reads its pawnClaims to skip co-loaders that already
            // carry a slice. Never null here (loadable was null-checked above; register always yields an entry).
            var entry = ledger.LoadRegisterOrUpdate(loadable);
            var claimable = ledger.LoadAvailableToClaim(loadable, pawn);
            if (claimable.Count == 0)
                return null;

            // The per-def remaining budget the sweep decrements as it commits stacks. Previously TWO dictionaries
            // (ledgerLeft + manifestLeft) were cloned from the SAME `claimable` and decremented IDENTICALLY by the
            // same per-stack take, so they were always equal — DeliverableUnits saw manifestRem == ledgerAvail every
            // call. Collapsed to ONE dict passed as both args (HD-JOBLIST): one fewer Dictionary clone per probe,
            // behavior-identical (claimable ≤ the live manifest, so it's the binding per-def cap either way).
            var claimLeft = new Dictionary<ThingDef, int>(claimable);

            // Per-VARIANT demand gate (issue #156). The claim/ledger accounting above is per ThingDef, correct for
            // the concurrency split, but BLIND to quality/stuff/hitpoints. So the def-keyed sweep below would happily
            // scoop an EXCELLENT-quality jacket for a manifest that asked for a NORMAL one (the reported shuttle bug),
            // because both are the same def. Vanilla's own loader only ever picks things belonging to a WANTED
            // transferable, so it never does this. This budget restores that: a candidate must match a wanted
            // manifest variant (quality/stuff/hitpoints, via the SAME TransferAsOne vanilla uses) and may take at most
            // that variant's remaining count. Built once from the live manifest; consulted in TryQualify/ClampNetworkTake
            // and committed at each real pick. See VariantBudget.
            var variants = new VariantBudget(loadable.GetTransferables());

            // Carry ceiling (smart overload) + the trip-mass budget (pawn free space AND group headroom).
            float maxCap = CarryCapacity.Of(pawn);
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

            // FAIR SHARE across ready co-loaders (the "only one pawn gathers the dungeon loot" fix). Every bound
            // above is per-PAWN (claim units, carry ceiling, trip mass, CE bulk); nothing bounded the claim per-PEER.
            // That was the previously-overlooked term: the first asker's plan swallowed the whole manifest up to its
            // smart-overload ceiling (275 percent of capacity by default, unbounded at level 0), the other ordered
            // pawns saw nothing claimable, and the portal board gate then held them from entering, so they wandered.
            // Clamp this plan's mass to an even split of the claimable pool across the asker plus every other READY
            // co-loader (boarding passengers of this loadable with no live claim; claim holders' slices are already
            // excluded from `claimable`). A lone loader is never clamped (ShareMassBudget returns its no-clamp
            // sentinel for a divisor of 1), and a player order means "this pawn loads, as much as it can", so it
            // keeps the full trip budget.
            if (!playerOrder)
            {
                int coLoaders = CountClaimlessCoLoaders(pawn, loadable, entry);
                if (coLoaders > 0)
                {
                    // Multiplayer determinism: the pool arrives in HashSet order (per-client), and the float mass
                    // sum below is order-sensitive in its low bits, so an unsorted sum could nudge the share across
                    // a unit boundary on one client only and desync the committed claim. Normalize to thingIDNumber
                    // order first. This is behavior-neutral for item selection: the sweep's NearestEligible picks
                    // min(distance, thingIDNumber) over the WHOLE pool each step, which is order-independent.
                    pool.Sort(ByThingId);
                    float claimableMass = ClaimablePoolMass(pawn, pool, claimable, claimedByOthers, out float heaviestUnit);
                    float share = LoadFairShare.ShareMassBudget(claimableMass, heaviestUnit, 1 + coLoaders);
                    if (share < massLeft)
                        massLeft = share;
                }
            }

            // Reused working sets (Cleared at use), copied into the fresh job-owned queues below.
            var things = scratchThings ?? (scratchThings = new List<Thing>());
            var counts = scratchCounts ?? (scratchCounts = new List<int>());
            things.Clear();
            counts.Clear();
            var from = loadable.GetParentThing()?.Position ?? pawn.Position;

            while (things.Count < MaxStacks && running < ceiling - 0.0001f && massLeft > 0.0001f)
            {
                var next = NearestEligible(pawn, pool, from, claimedByOthers, claimLeft, variants,
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
                variants.Commit(next, take); // decrement the picked variant's remaining demand (issue #156)
                from = next.Position;
            }

            // Storage Network (virtual servers): its items live DESPAWNED in the network, invisible to BuildPool +
            // AddStoredClaimables (both spawned-only). When the experimental opt-in is on AND SN is installed,
            // supplement the plan with the network's despawned stacks of the still-claimable defs — staged in
            // targetQueueB and materialised at a usable terminal by SN's OWN StartJob auto-spawn when the job runs.
            // Bounded by the SAME claim/carry/mass/bulk budget as the sweep above; a stack SN can't materialise is
            // skipped by the driver's sweep toil (it requires Spawned), so this can never strand cargo or over-pull.
            if (s.enableStorageNetworkBulkLoad && StorageNetworkCompat.IsActive)
                AppendNetworkClaimables(pawn, map, claimLeft, variants, claimedByOthers, things, counts,
                    ref running, ceiling, ref bulkRoom, ref massLeft);

            if (things.Count == 0)
            {
                // DEPOSIT-ONLY recovery for a boarding PASSENGER that is already carrying tagged cargo this loadable
                // still wants but has nothing left to sweep from the ground. This happens when the passenger was
                // INTERRUPTED mid bulk-load (urgent need / draft / mental break) with swept cargo stranded in its
                // inventory: a Lord-driven passenger never reaches the Work-tree deposit/unload path a free hauler
                // uses, so without this the cargo sits in its pack, the manifest never empties, and the board gate
                // blocks it forever ("stuck waiting"). Returning a deposit-only job (empty sweep queue) lets the
                // boarding tree's JobGiver_LoadTransporters node shed that cargo INTO the transporter before it falls
                // through to the Enter node. Scoped to the passenger case (a free hauler's stranded cargo is handled
                // by its Work-tree opportunistic deposit/unload); HoldsCargoLoadableWants mirrors the driver's own
                // deposit predicate (surplus-above-keep AND a manifest variant-match), so the recovery job is issued
                // ONLY when the deposit will actually move ≥1 unit — never a no-op that would re-fire forever.
                if (!playerOrder && IsBoardingPassengerFor(pawn, loadable) && HoldsCargoLoadableWants(pawn, loadable))
                    return Patch_OpportunisticLoadDeposit.BuildDepositOnlyJob(pawn, loadable);
                return null; // nothing reachable to sweep of the claimable defs
            }

            var job = JobMaker.MakeJob(jobDef, loadable.GetParentThing());
            job.targetQueueB = new List<LocalTargetInfo>(things.Count);
            for (int i = 0; i < things.Count; i++)
                job.targetQueueB.Add(things[i]);
            job.countQueue = new List<int>(counts);
            job.count = 1; // sentinel: a -1 Job.count reads as "broken" in some vanilla checks
            HDLog.Dbg($"TransportLoad: {pawn} sweeping {things.Count} stacks for group {loadable.GetUniqueLoadID()} (~{running:0.#}kg).");
            return job;
        }

        /// <summary>
        /// How many OTHER ready co-loaders the fair-share split divides across: spawned boarding passengers of THIS
        /// loadable (their whole duty is "load this, then enter", so they are guaranteed idle until the manifest
        /// empties) that could actually run a bulk-load job (not downed/drafted/mentally broken, manipulation
        /// capable per vanilla's own load gate, carrier comp + inventory present) and hold NO live claim on this
        /// task. Claim holders are excluded because their slice is already subtracted from the asker's claimable
        /// map; counting them again would over-divide. Free haulers are deliberately NOT counted: they have a colony
        /// of other work and no board gate holds them, so dividing for pawns that may never come would shrink every
        /// share for nothing (a lone-hauler plan stays exactly as big as before). Deterministic: a plain count over
        /// the spawned-pawn list (Multiplayer runs this in sim on every client; order does not matter for a count).
        /// </summary>
        private static int CountClaimlessCoLoaders(Pawn asker, IManagedLoadable loadable, LoadLedgerEntry entry)
        {
            var map = asker?.Map;
            if (map?.mapPawns == null || loadable == null)
                return 0;
            var claims = entry?.pawnClaims;
            var spawned = map.mapPawns.AllPawnsSpawned;
            int count = 0;
            for (int i = 0; i < spawned.Count; i++)
            {
                var p = spawned[i];
                if (p == null || p == asker)
                    continue;
                // Cheapest reject first: most spawned pawns hold no boarding duty at all.
                if (!IsBoardingPassengerFor(p, loadable))
                    continue;
                if (p.Downed || p.Drafted || p.InMentalState)
                    continue; // won't run its load duty right now
                if (p.health?.capacities == null || !p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                    continue; // vanilla's HasJobOnPortal/loading gate: no manipulation, no hauling
                if (p.GetComp<CompHauledToInventory>() == null || p.inventory == null)
                    continue; // can't run the bulk driver at all
                if (claims != null && claims.ContainsKey(p))
                    continue; // already carries its slice (excluded from `claimable` too)
                count++;
            }
            return count;
        }

        // Deterministic pool order for the fair-share mass sum (see the sort at the TryGiveBulkJob call site): the
        // float total in ClaimablePoolMass is order-sensitive in its low bits while the pool arrives in per-client
        // HashSet order, so summing unsorted could nudge one client's share across a unit boundary and desync the
        // committed claim. Static so the sort never allocates a delegate per plan. Null-tolerant (a null entry sorts
        // first) because the sweep's own scans tolerate null pool entries.
        private static readonly System.Comparison<Thing> ByThingId =
            (a, b) => (a?.thingIDNumber ?? int.MinValue).CompareTo(b?.thingIDNumber ?? int.MinValue);

        // Scratch for the fair-share pre-pass (per-def remaining claimable units, decremented as pool stacks are
        // counted so over-supplied defs don't inflate the mass). [ThreadStatic] + lazy-init per this file's scratch
        // convention; Cleared at use, never trusted empty. SAFETY: one ClaimablePoolMass call runs to completion
        // (no nested re-entry) before the next reuse on a thread.
        [System.ThreadStatic] private static Dictionary<ThingDef, int> scratchShareLeft;

        /// <summary>
        /// Total mass of what THIS asker could claim from the already-built pool: stacks of claimable defs, each
        /// counted up to the def's remaining claimable units, skipping stacks other pawns' jobs already queued and
        /// stacks forbidden to the asker. Also reports the HEAVIEST counted unit mass, the no-starvation floor for
        /// <see cref="LoadFairShare.ShareMassBudget"/> (heaviest, not lightest, so EVERY claimable stack stays
        /// unit-affordable within one share and the fairness clamp alone can never mass-starve a pick; a lightest
        /// floor could leave a heavy item unclaimable by the whole crew). The expensive per-stack gates
        /// (reachability, CE bulk) are deliberately NOT mirrored here: including an unsweepable stack inflates the
        /// claimable pool mass (the share's numerator; the loader count is untouched) equally for every asker, which
        /// OVER-sizes shares. The excess only dilutes evenness (an asker may take somewhat more than its fair
        /// fraction of the actually sweepable goods, a diluted form of the concentration this clamp exists to stop)
        /// and self-heals through the re-offer when a job ends; it can never starve a pawn or over-claim the ledger.
        /// PURE read (no pool mutation, no reservations). Stuff-aware: mass comes from each stack's own
        /// <c>GetStatValue(Mass)</c>, not the def's base mass. The float sum runs in pool order and feeds a claim
        /// decision, so the caller MUST hand over a deterministically ordered pool (TryGiveBulkJob sorts it by
        /// thingIDNumber first).
        /// </summary>
        /// <param name="pawn">The asker (forbidden-ness is evaluated against its allowed areas).</param>
        /// <param name="pool">The sweep candidate pool (loose haulables plus stored claimables), already built and
        /// already in deterministic order.</param>
        /// <param name="claimable">The asker's per-def claimable map from the ledger; not mutated (copied into the
        /// scratch counter).</param>
        /// <param name="claimedByOthers">Stacks already queued by other pawns' jobs (per-thing claims).</param>
        /// <param name="heaviestUnitMass">Unit mass of the heaviest counted item, or 0 when none had positive mass.</param>
        private static float ClaimablePoolMass(Pawn pawn, List<Thing> pool, Dictionary<ThingDef, int> claimable,
            HashSet<Thing> claimedByOthers, out float heaviestUnitMass)
        {
            heaviestUnitMass = 0f;
            var left = scratchShareLeft ?? (scratchShareLeft = new Dictionary<ThingDef, int>());
            left.Clear();
            foreach (var kv in claimable)
                if (kv.Key != null && kv.Value > 0)
                    left[kv.Key] = kv.Value;
            if (left.Count == 0)
                return 0f;

            float total = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                var t = pool[i];
                if (t?.def == null || !left.TryGetValue(t.def, out int remaining) || remaining <= 0)
                    continue;
                if (claimedByOthers.Contains(t) || t.IsForbidden(pawn))
                    continue;
                int units = Math.Min(t.stackCount, remaining);
                if (units <= 0)
                    continue;
                float unit = t.GetStatValue(StatDefOf.Mass);
                total += units * unit;
                left[t.def] = remaining - units;
                if (unit > heaviestUnitMass)
                    heaviestUnitMass = unit;
            }
            left.Clear();
            return total;
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
            VariantBudget variants, HashSet<Thing> claimedByOthers, List<Thing> things, List<int> counts,
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
                        int take = ClampNetworkTake(pawn, t, claimAvail, variants, ceiling, running, bulkRoom, massLeft);
                        if (take <= 0)
                            continue; // too heavy/bulky, or not a wanted variant; a lighter or right-variant neighbour may still fit
                        things.Add(t);
                        counts.Add(take);
                        float unit = t.GetStatValue(StatDefOf.Mass);
                        running += take * unit;
                        bulkRoom -= take * CECompat.BulkPerUnit(t);
                        massLeft -= take * unit;
                        claimLeft[def] = Math.Max(0, claimAvail - take);
                        variants.Commit(t, take); // decrement the picked variant's remaining demand (issue #156)
                    }
                }
            }
        }

        // The per-stack deliverable clamp for a Storage Network candidate — the SAME budget math as TryQualify
        // (DeliverableUnits over the claim budget, the carry-ceiling CountWithinCeiling, the CE bulk cap, and the
        // trip-mass UnitsWithinMassBudget), MINUS the spawned-thing gates (IsForbidden / PawnCanAutomaticallyHaulFast
        // / ExtraSweepReach) — those don't apply to a despawned network stack, whose reach/usability is gated once at
        // the TERMINAL in StorageNetworkCompat.UsableTerminals.
        private static int ClampNetworkTake(Pawn pawn, Thing chosen, int claimAvail, VariantBudget variants,
            float ceiling, float running, float bulkRoom, float massLeft)
        {
            float unit = chosen.GetStatValue(StatDefOf.Mass);
            int carryAffordable = BulkHaulPolicy.CountWithinCeiling(ceiling, running, unit, chosen.stackCount);
            carryAffordable = Math.Min(carryAffordable, CECompat.MaxFitCount(pawn, chosen));
            float bulkPer = CECompat.BulkPerUnit(chosen);
            if (bulkPer > 0f && !float.IsPositiveInfinity(bulkRoom))
                carryAffordable = Math.Min(carryAffordable, (int)Math.Floor(bulkRoom / bulkPer));
            int massAffordable = TransportLoadPlan.UnitsWithinMassBudget(massLeft, unit, chosen.stackCount);
            int deliverable = TransportLoadPlan.DeliverableUnits(chosen.stackCount, claimAvail, claimAvail,
                Math.Min(carryAffordable, massAffordable));
            // Per-variant demand gate (issue #156): a network stack of a wanted DEF must still match a wanted
            // manifest VARIANT (quality/stuff/hitpoints) and fit its remaining count, same rule as the ground sweep.
            return variants.Cap(chosen, deliverable);
        }

        // Nearest pool candidate of a CLAIMABLE def within reach, clamped per-stack via DeliverableUnits under the
        // trip-mass budget. Removes chosen/rejected candidates from the pool as it scans (like BulkHaul).
        //
        // B3 — when loadHybridPathing is ON, the FINAL ranking switches from straight-line to real A* path cost over
        // the top-N straight-line candidates (the true-nearest by walkable distance, not crow-flies). The flag is OFF
        // by default, and the off-path below is byte-IDENTICAL to before (the on-path is a separate early-return
        // branch that runs no pathfinding when off — see the single `if (loadHybridPathing)` gate).
        private static Thing NearestEligible(Pawn pawn, List<Thing> pool, IntVec3 from, HashSet<Thing> claimedByOthers,
            Dictionary<ThingDef, int> claimLeft, VariantBudget variants,
            float ceiling, float running, float bulkRoom, float massLeft, out int take)
        {
            // B3 ON: re-rank the top-N straight-line-nearest QUALIFYING candidates by real path cost. Gated on the
            // OFF-by-default setting so this whole branch is skipped (zero pathfinding, zero allocation) on the
            // default path — when it's off, execution falls straight through to the unchanged straight-line loop.
            if (HaulersDreamMod.Settings?.loadHybridPathing == true)
            {
                var ranked = NearestEligibleHybrid(pawn, pool, from, claimedByOthers, claimLeft, variants,
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
                    // MP determinism: break distance ties by thingIDNumber so all clients pick the same stack
                    // (HashSet iteration order can differ per client).
                    if (d < bestDistSq
                        || (d == bestDistSq && bestIdx >= 0 && t.thingIDNumber < pool[bestIdx].thingIDNumber))
                    { bestDistSq = d; bestIdx = i; }
                }
                if (bestIdx < 0)
                    return null;
                var chosen = pool[bestIdx];
                pool.RemoveAt(bestIdx);

                int deliverable = TryQualify(pawn, chosen, claimedByOthers, claimLeft, variants,
                    ceiling, running, bulkRoom, massLeft);
                if (deliverable <= 0)
                    continue; // forbidden / claimed / too heavy / no claim budget / not a wanted variant; a neighbor may still fit
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
            Dictionary<ThingDef, int> claimLeft, VariantBudget variants,
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
                    // MP determinism: break distance ties by thingIDNumber so all clients pick the same stack
                    // (HashSet iteration order can differ per client).
                    if (d < bestDistSq
                        || (d == bestDistSq && bestIdx >= 0 && t.thingIDNumber < pool[bestIdx].thingIDNumber))
                    { bestDistSq = d; bestIdx = i; }
                }
                if (bestIdx < 0)
                    break; // no more claimable-def candidates straight-line
                var chosen = pool[bestIdx];
                pool.RemoveAt(bestIdx);

                int deliverable = TryQualify(pawn, chosen, claimedByOthers, claimLeft, variants,
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
                // MP determinism: break path-cost ties by thingIDNumber so all clients pick the same stack
                // (HashSet iteration order can differ per client).
                if (cost < bestCost
                    || (cost == bestCost && t.thingIDNumber < cands[bestCand].thingIDNumber))
                { bestCost = cost; bestCand = i; }
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
            Dictionary<ThingDef, int> claimLeft, VariantBudget variants,
            float ceiling, float running, float bulkRoom, float massLeft)
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
            // Per-variant demand gate (issue #156): cap to what a WANTED manifest variant still wants (quality/stuff/
            // hitpoints-aware). 0 when this is an off-quality item of a wanted def, or that variant is already spoken
            // for this sweep, so the def-keyed pool never loads the wrong quality onto a transporter/portal/vehicle.
            deliverable = variants.Cap(chosen, deliverable);
            if (deliverable <= 0)
                return 0;
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

        /// <summary>
        /// Per-VARIANT remaining-demand tracker for one bulk-load plan: the term HD's per-def pool had DROPPED
        /// (issue #156: a shuttle asked to load a NORMAL-quality jacket was loaded with an EXCELLENT one sitting
        /// nearer, because both share a <see cref="ThingDef"/> and the sweep only ever consulted the def). The claim
        /// ledger is per def (correct for the concurrency split, but blind to quality/stuff/hitpoints); vanilla's own
        /// loader (<c>LoadTransportersJobUtility.FindThingToLoad</c>) only picks things belonging to a WANTED
        /// transferable, so it never scoops an off-quality item of a wanted def. This restores that behaviour for the
        /// sweep: a candidate is matched to a wanted manifest transferable by vanilla's variant-strict
        /// <see cref="TransferableUtility.TransferAsOne"/> (<c>PodsOrCaravanPacking</c> mode: quality + stuff +
        /// hitpoints aware, and crucially with NO def-only fallback, unlike <c>TransferableMatchingDesperate</c>), and
        /// may take at most that variant's remaining <c>CountToTransfer</c>.
        ///
        /// SCOPE: per-pawn (built fresh from the live manifest each plan). The cross-pawn per-variant split is NOT
        /// ledger-tracked (the ledger is per def), so under heavy CONCURRENT loading of a MIXED-quality manifest two
        /// pawns could still each target the same tier; the deposit's per-member clamp
        /// (<c>LoadTransportersAdapter.MemberRemainingFor</c>) bounds the actual over-load, and the reported
        /// single-tier case is fully covered. Keyed by the transferable REFERENCE (stable within one plan, since
        /// <c>GetTransferables</c> is materialised once). <see cref="Match"/> returns the FIRST wanted transferable
        /// that BOTH matches the variant AND still has remaining demand, so a transporter GROUP whose two members each
        /// want the same variant is served correctly (each member's own entry drains in turn). Deterministic (wanted
        /// is in manifest order, identical across MP clients).
        /// </summary>
        private sealed class VariantBudget
        {
            private readonly List<TransferableOneWay> wanted;
            private readonly Dictionary<TransferableOneWay, int> left;

            /// <summary>Build from the wanted manifest (each entry already has <c>CountToTransfer &gt; 0</c>, but the
            /// guard is kept so a caller passing an unfiltered list stays correct).</summary>
            /// <param name="wanted">The manifest's wanted transferables (from <c>IManagedLoadable.GetTransferables</c>);
            /// held by reference and never mutated.</param>
            public VariantBudget(List<TransferableOneWay> wanted)
            {
                this.wanted = wanted ?? new List<TransferableOneWay>();
                left = new Dictionary<TransferableOneWay, int>(this.wanted.Count);
                for (int i = 0; i < this.wanted.Count; i++)
                {
                    var tr = this.wanted[i];
                    if (tr != null && tr.HasAnyThing && tr.CountToTransfer > 0)
                        left[tr] = tr.CountToTransfer;
                }
            }

            /// <summary>The first wanted transferable whose variant matches <paramref name="thing"/> (vanilla
            /// <see cref="TransferableUtility.TransferAsOne"/>, quality/stuff/hitpoints aware, NO def fallback) AND
            /// still has remaining demand this plan, or null when the manifest wants no such variant (an off-quality
            /// item of a wanted def) or every matching variant is already spoken for.</summary>
            private TransferableOneWay Match(Thing thing)
            {
                if (thing == null)
                    return null;
                for (int i = 0; i < wanted.Count; i++)
                {
                    var tr = wanted[i];
                    if (tr == null || !left.TryGetValue(tr, out int rem) || rem <= 0)
                        continue;
                    if (TransferableUtility.TransferAsOne(thing, tr.AnyThing, TransferAsOneMode.PodsOrCaravanPacking))
                        return tr;
                }
                return null;
            }

            /// <summary>Cap <paramref name="desired"/> to what a matching wanted variant still wants; 0 when the
            /// manifest wants no matching variant (the candidate is rejected). PURE (no mutation), so it is safe to
            /// call speculatively on the hybrid path's non-chosen candidates.</summary>
            /// <param name="thing">The candidate stack being qualified.</param>
            /// <param name="desired">Units the other budgets (claim / carry / mass) already allow for this stack.</param>
            /// <returns>The variant-capped take (≤ <paramref name="desired"/>), or 0 to reject the candidate.</returns>
            public int Cap(Thing thing, int desired)
            {
                if (desired <= 0)
                    return 0;
                var m = Match(thing);
                if (m == null)
                    return 0;
                int rem = left[m];
                return desired < rem ? desired : rem;
            }

            /// <summary>Commit <paramref name="take"/> units of <paramref name="thing"/> against its matched variant
            /// (decrement its remaining demand). Called ONLY at a real commit site, never speculatively. A no-op when
            /// nothing matches (can't happen after a positive <see cref="Cap"/> with <c>left</c> unchanged in between,
            /// but stays safe).</summary>
            /// <param name="thing">The stack just committed to the plan.</param>
            /// <param name="take">Units committed (must equal the value <see cref="Cap"/> returned for this stack).</param>
            public void Commit(Thing thing, int take)
            {
                if (take <= 0)
                    return;
                var m = Match(thing);
                if (m != null)
                    left[m] = Math.Max(0, left[m] - take);
            }
        }
    }
}
