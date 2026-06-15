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
            ledger.LoadRegisterOrUpdate(loadable);
            return ledger.LoadHasWork(loadable, pawn);
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

            // The per-def ledger-available budget (decremented as the sweep commits stacks) and the manifest-
            // remaining budget (same source, but tracked separately so DeliverableUnits sees both).
            var ledgerLeft = new Dictionary<ThingDef, int>(claimable);
            var manifestLeft = new Dictionary<ThingDef, int>(claimable); // claimable ≤ manifest, so this is a safe upper bound per def

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
            var claimedByOthers = RouteSelection.ClaimedByOtherPawns(pawn);

            var things = new List<Thing>();
            var counts = new List<int>();
            var from = loadable.GetParentThing()?.Position ?? pawn.Position;

            while (things.Count < MaxStacks && running < ceiling - 0.0001f && massLeft > 0.0001f)
            {
                var next = NearestEligible(pawn, pool, from, claimedByOthers, ledgerLeft, manifestLeft,
                    ceiling, running, bulkRoom, massLeft, out int take);
                if (next == null)
                    break;
                things.Add(next);
                counts.Add(take);
                float unit = next.GetStatValue(StatDefOf.Mass);
                running += take * unit;
                bulkRoom -= take * CECompat.BulkPerUnit(next);
                massLeft -= take * unit;
                ledgerLeft[next.def] = Math.Max(0, (ledgerLeft.TryGetValue(next.def, out int l) ? l : 0) - take);
                manifestLeft[next.def] = Math.Max(0, (manifestLeft.TryGetValue(next.def, out int m) ? m : 0) - take);
                from = next.Position;
            }

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

        // Nearest pool candidate of a CLAIMABLE def within reach, clamped per-stack via DeliverableUnits under the
        // trip-mass budget. Removes chosen/rejected candidates from the pool as it scans (like BulkHaul).
        private static Thing NearestEligible(Pawn pawn, List<Thing> pool, IntVec3 from, HashSet<Thing> claimedByOthers,
            Dictionary<ThingDef, int> ledgerLeft, Dictionary<ThingDef, int> manifestLeft,
            float ceiling, float running, float bulkRoom, float massLeft, out int take)
        {
            take = 0;
            while (true)
            {
                int bestIdx = -1;
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < pool.Count; i++)
                {
                    var t = pool[i];
                    if (t?.def == null || !ledgerLeft.TryGetValue(t.def, out int la) || la <= 0)
                        continue; // not a claimable def, or its claim budget is spent
                    float d = (t.Position - from).LengthHorizontalSquared;
                    if (d < bestDistSq) { bestDistSq = d; bestIdx = i; }
                }
                if (bestIdx < 0)
                    return null;
                var chosen = pool[bestIdx];
                pool.RemoveAt(bestIdx);

                if (chosen.IsForbidden(pawn) || claimedByOthers.Contains(chosen))
                    continue;

                float unit = chosen.GetStatValue(StatDefOf.Mass);
                // Carry-affordable (smart overload) + CE.
                int carryAffordable = BulkHaulPolicy.CountWithinCeiling(ceiling, running, unit, chosen.stackCount);
                carryAffordable = Math.Min(carryAffordable, CECompat.MaxFitCount(pawn, chosen));
                float bulkPer = CECompat.BulkPerUnit(chosen);
                if (bulkPer > 0f && !float.IsPositiveInfinity(bulkRoom))
                    carryAffordable = Math.Min(carryAffordable, (int)Math.Floor(bulkRoom / bulkPer));
                // Trip-mass budget (destination + pawn mass headroom for THIS stack).
                int massAffordable = TransportLoadPlan.UnitsWithinMassBudget(massLeft, unit, chosen.stackCount);

                int ledgerAvail = ledgerLeft.TryGetValue(chosen.def, out int l) ? l : 0;
                int manifestRem = manifestLeft.TryGetValue(chosen.def, out int m) ? m : 0;
                int deliverable = TransportLoadPlan.DeliverableUnits(
                    chosen.stackCount, manifestRem, ledgerAvail, Math.Min(carryAffordable, massAffordable));
                if (deliverable <= 0)
                    continue; // too heavy / no claim budget left — a lighter/other-def neighbor may still fit
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, chosen, forced: false))
                    continue;
                take = deliverable;
                return chosen;
            }
        }
    }
}
