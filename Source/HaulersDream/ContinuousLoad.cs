using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Continuous-loading CHAIN (BLFT parity, gap #9 — opt-in <c>enableContinuousLoading</c>, default OFF). After a
    /// PLAYER-FORCED bulk-load job finishes <see cref="JobCondition.Succeeded"/>, the courier hops to the nearest OTHER
    /// load target of the same family that still has work and keeps loading — group after group — until none remain, so
    /// one "Load this" click can fill an entire caravan in a single command.
    ///
    /// Shared by all three bulk-load drivers' finish actions
    /// (<see cref="JobDriver_LoadTransportersInBulk"/>/<see cref="JobDriver_LoadPortalInBulk"/>/
    /// <see cref="JobDriver_LoadVehicleInBulk"/>) so the success-detection + scan + dedup + enqueue live in ONE place.
    ///
    /// PLAYER-FORCED ONLY: the chain reuses <see cref="TransportLoad.TryGiveBulkJob"/> with <c>playerOrder: true</c>,
    /// which is the SAME path the bulk-load float menus use — it skips the auto eligibility gate (so the chain works
    /// even for a drafted courier, exactly as the setting description promises) and returns null when nothing is
    /// claimable / reachable to sweep, which doubles as the authoritative "this target still has work" test (a stronger
    /// signal than <see cref="TransportLoad.HasPotentialBulkWork"/>, which additionally requires auto eligibility and so
    /// would wrongly refuse a drafted/forced chain). An autonomous (non-playerForced) bulk-load NEVER chains.
    ///
    /// TERMINATION: the just-finished target is excluded (dedup by <see cref="IManagedLoadable.GetUniqueLoadID"/>) and
    /// each candidate is visited at most once per call, so a chain hop happens only when a DIFFERENT target with real
    /// work is found — when none is, the chain simply ends (no job is enqueued, no loop). Because each successful hop
    /// moves to a different group (which the next finish action will in turn exclude) and the manifest strictly shrinks
    /// as cargo is deposited, the chain is finite.
    ///
    /// CLAIMS: the finishing driver RELEASES its (finished group's) claims as normal — the chain targets a DIFFERENT
    /// ledger key, so there is nothing to retain (retaining would leak the finished group's claim); the chained job
    /// re-claims its own group in its <c>Notify_Starting</c> when it starts next tick. See <c>retainClaimOnEnd</c>.
    /// </summary>
    public static class ContinuousLoad
    {
        // Courier chain scan radius: wide enough to cover a clustered caravan staging area (BLFT hard-codes 10f; HD
        // uses a roomier 24f since transports/vehicles are often spread across a loading yard), tight enough that the
        // chain never sends a courier across the whole map. A target out of range just isn't chained to.
        private const float ScanRadius = 24f;

        /// <summary>True if continuous loading is enabled AND the just-finished job both succeeded and was
        /// player-forced — the precondition every driver checks before calling <see cref="TryChainFrom"/>. Centralised
        /// so the three finish actions stay byte-identical and byte-inert when the feature is off.</summary>
        public static bool ShouldChain(JobCondition condition, Job finishedJob)
            => (HaulersDreamMod.Settings?.enableContinuousLoading ?? false)
               && condition == JobCondition.Succeeded
               && finishedJob != null
               && finishedJob.playerForced;

        /// <summary>
        /// Find the nearest OTHER load target (same family as <paramref name="finished"/>) that still has work, build a
        /// player-forced bulk-load job for it, and <c>EnqueueFirst</c> it onto the pawn's job queue so the courier
        /// chains straight into it. Returns true if a follow-up was enqueued. No-op (returns false) when the feature is
        /// off, the pawn can't haul/move, or no other target with work is in range — the chain then simply ends.
        ///
        /// Caller contract: invoke from the driver finish action ONLY after <see cref="ShouldChain"/> passed. The
        /// finished target is excluded by <see cref="IManagedLoadable.GetUniqueLoadID"/> so the courier never re-picks
        /// the group it just drained.
        /// </summary>
        public static bool TryChainFrom(Pawn pawn, IManagedLoadable finished)
        {
            if (pawn?.Map == null || pawn.jobs == null || finished == null)
                return false;
            // A downed/dead courier (e.g. the job ended Succeeded on the same tick it was incapacitated) cannot chain.
            if (pawn.Dead || pawn.Downed || !pawn.Spawned)
                return false;
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableContinuousLoading)
                return false;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return false;
            // Player order parity with the float menus: only the physical manipulation capability is required.
            if (!(pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) ?? false))
                return false;

            int finishedKey = finished.GetUniqueLoadID();
            var kind = finished.Kind;
            var map = pawn.Map;
            var origin = pawn.Position;
            float radiusSq = ScanRadius * ScanRadius;

            // Cheap distance/dedup/manifest pre-filter, then visit nearest-first. We build the adapter (and call the
            // heavier TryGiveBulkJob) only for the few candidates that pass the cheap gates. This runs ONCE per
            // completed player-forced load job (not per tick/frame), so the small fresh allocation is irrelevant.
            var candidates = new List<(IManagedLoadable adp, float distSq)>();
            var seenKeys = new HashSet<int>();
            CollectCandidates(pawn, map, kind, origin, radiusSq, finishedKey, seenKeys, candidates);
            // Nearest-first: the courier should hop to the closest remaining target.
            candidates.Sort((a, b) =>
            {
                // MP determinism: total-order tiebreak so ties don't depend on input order across clients.
                // GetUniqueLoadID() is an int here (transporter groupID / portal thingIDNumber / vehicle id).
                int c = a.distSq.CompareTo(b.distSq);
                return c != 0 ? c : a.adp.GetUniqueLoadID().CompareTo(b.adp.GetUniqueLoadID());
            });

            for (int i = 0; i < candidates.Count; i++)
            {
                var adapter = candidates[i].adp;
                // TryGiveBulkJob(playerOrder:true) is the authoritative "has reachable work" test AND the builder.
                // Null => this target has nothing claimable/reachable to sweep right now; try the next nearest.
                var job = TransportLoad.TryGiveBulkJob(pawn, adapter, playerOrder: true);
                if (job == null)
                    continue;
                job.playerForced = true;
                // EnqueueFirst so the chain runs immediately after this finish action (ahead of any idle backstop).
                pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
                HDLog.Dbg($"ContinuousLoad: {pawn} chaining to {adapter.GetParentThing()?.LabelShort} " +
                          $"(group {adapter.GetUniqueLoadID()}) after finishing group {finishedKey}.");
                return true;
            }
            return false;
        }

        /// <summary>Gather same-family load targets within range, deduped by uniqueLoadID (skipping the finished one),
        /// that pass the cheap gates (player faction, has a remaining manifest, reachable, reservable). The expensive
        /// claim/sweep check is left to <see cref="TransportLoad.TryGiveBulkJob"/> in the caller.</summary>
        private static void CollectCandidates(Pawn pawn, Map map, LoadableKind kind, IntVec3 origin, float radiusSq,
            int finishedKey, HashSet<int> seenKeys, List<(IManagedLoadable, float)> outCandidates)
        {
            switch (kind)
            {
                case LoadableKind.Portal:
                {
                    var portals = map.listerThings.ThingsInGroup(ThingRequestGroup.MapPortal);
                    for (int i = 0; i < portals.Count; i++)
                    {
                        if (!(portals[i] is MapPortal portal) || !portal.Spawned || !portal.LoadInProgress)
                            continue;
                        float distSq = (portal.Position - origin).LengthHorizontalSquared;
                        if (distSq > radiusSq)
                            continue;
                        var adapter = MapPortalBulkTarget.TryCreate(portal);
                        if (adapter == null || adapter.GetUniqueLoadID() == finishedKey || !seenKeys.Add(adapter.GetUniqueLoadID()))
                            continue;
                        if (!Passable(pawn, portal, adapter))
                            continue;
                        outCandidates.Add((adapter, distSq));
                    }
                    break;
                }
                case LoadableKind.Vehicle:
                {
                    if (!VehicleFrameworkCompat.IsActive)
                        break;
                    var spawned = map.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < spawned.Count; i++)
                    {
                        var vehicle = spawned[i];
                        if (vehicle == null || !VehicleFrameworkCompat.IsVehicle(vehicle))
                            continue;
                        var cargo = VehicleFrameworkCompat.CargoToLoad(vehicle);
                        if (cargo == null || cargo.Count == 0)
                            continue;
                        float distSq = (vehicle.Position - origin).LengthHorizontalSquared;
                        if (distSq > radiusSq)
                            continue;
                        var adapter = VehicleLoadTarget.TryCreate(vehicle);
                        if (adapter == null || adapter.GetUniqueLoadID() == finishedKey || !seenKeys.Add(adapter.GetUniqueLoadID()))
                            continue;
                        if (!Passable(pawn, vehicle, adapter))
                            continue;
                        outCandidates.Add((adapter, distSq));
                    }
                    break;
                }
                default: // Transporter / shuttle
                {
                    var transporters = map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter);
                    for (int i = 0; i < transporters.Count; i++)
                    {
                        var comp = transporters[i]?.TryGetComp<CompTransporter>();
                        if (comp?.parent == null || !comp.parent.Spawned || !comp.AnyInGroupHasAnythingLeftToLoad)
                            continue;
                        if (comp.groupID == finishedKey)
                            continue; // never re-pick the group we just drained
                        // Range check BEFORE marking the group seen: a group has many member pods, and an out-of-range
                        // lead pod must NOT suppress a closer pod of the same group. We dedup by groupID only once a pod
                        // is in range (the first in-range pod's distance represents the group — pods are adjacent).
                        float distSq = (comp.parent.Position - origin).LengthHorizontalSquared;
                        if (distSq > radiusSq)
                            continue;
                        if (!seenKeys.Add(comp.groupID))
                            continue; // a closer in-range pod of this group was already added
                        var adapter = LoadTransportersAdapter.TryCreate(comp);
                        if (adapter == null)
                            continue;
                        if (!Passable(pawn, comp.parent, adapter))
                            continue;
                        outCandidates.Add((adapter, distSq));
                    }
                    break;
                }
            }
        }

        /// <summary>Shared cheap gates: same player faction, an actual remaining manifest, reachable and reservable.
        /// (The transporter branch already checks <c>AnyInGroupHasAnythingLeftToLoad</c>; AnythingToLoad here covers
        /// the portal/vehicle manifest and keeps a uniform contract.)</summary>
        private static bool Passable(Pawn pawn, Thing anchor, IManagedLoadable adapter)
        {
            if (anchor == null)
                return false;
            if (anchor.Faction != null && anchor.Faction != pawn.Faction)
                return false;
            if (!adapter.AnythingToLoad())
                return false;
            // Chain extra (the NEXT load structure, not the clicked one): cap reach at Some so a suit-less pawn
            // won't continue-load into a vacuum-/fire-stranded transporter/portal/vehicle.
            return pawn.CanReach(anchor, PathEndMode.Touch, ExtraSweepReach.Ceiling(pawn)) && pawn.CanReserve(anchor);
        }
    }
}
