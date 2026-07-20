using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Loading a pawn's scooped/tagged inventory loot onto a PACK ANIMAL — the away-map counterpart to the
    /// storage unload (there is no player storage on a caravan/encounter map). Three entry points, all producing
    /// a HaulersDream_LoadPackAnimal job (see <see cref="JobDriver_LoadPackAnimal"/>):
    ///   • <see cref="MaybeAutoDivert"/> — an over-encumbered caravan pawn breaks off to offload onto the nearest
    ///     owned pack animal (the user's "auto-divert when heavy"); gated on autoDivertToPackAnimal.
    ///   • <see cref="TryBuildBulkLoadJob"/> — the manual "Load nearby items onto pack animal (bulk)" order
    ///     (<see cref="FloatMenuOptionProvider_BulkLoadPackAnimal"/>): sweep nearby ground stacks into inventory
    ///     first, then deposit; gated on loadPackAnimalBulk.
    ///   • <see cref="GizmoLoadNearest"/> — the "Unload now" gizmo while on a non-home map.
    /// The pure gate/clamp logic lives in <see cref="PackAnimalLoadPolicy"/>.
    /// </summary>
    public static class PackAnimalLoad
    {
        /// <summary>The reachable owned pack animal with the most free space, or null. (Vanilla helper — honours
        /// forming-caravan vs free-roaming rules and skips UnloadEverything animals.)
        /// VF globally patches <c>UsablePackAnimalWithTheMostFreeSpace</c> to return a VehiclePawn whenever any player
        /// vehicle is spawned, so when HD's VF support is OFF (<c>enableVehicleFramework == false</c>) discard a
        /// vehicle candidate here — disabling VF support must mean HD never deposits into a vehicle's uncapped cargo
        /// via the pack-animal path. When the master toggle is ON, behaviour is unchanged (a vehicle stays a valid
        /// carrier). Gated on IsVehicle (false when VF absent), so this is inert without VF.</summary>
        internal static Pawn FindCarrier(Pawn pawn)
        {
            if (pawn?.Map == null)
                return null;
            var carrier = GiveToPackAnimalUtility.UsablePackAnimalWithTheMostFreeSpace(pawn);
            if (carrier != null && HaulersDreamMod.Settings?.enableVehicleFramework == false
                && VehicleFrameworkCompat.IsVehicle(carrier))
                return null; // VF support off -> a vehicle is not a usable carrier on the pack-animal path
            return carrier;
        }

        /// <summary>True if the pawn holds any HD-tagged stack with surplus above its personal kit — i.e. loot to
        /// offload onto an animal. Keep-stock (food / drugs / CE loadout) travels WITH the pawn, never onto the
        /// animal, so it uses the same <see cref="InventorySurplus"/> the storage unload does.</summary>
        internal static bool HasDepositableSurplus(Pawn pawn)
        {
            var inner = pawn?.inventory?.innerContainer;
            var comp = pawn?.GetComp<CompHauledToInventory>();
            if (inner == null || comp == null)
                return false;
            // Reads the un-healed PeekHashSet ON PURPOSE: unlike the transporter/portal/vehicle deposit GATES, this
            // is a multi-context PROBE — one caller is Alert_CannotUnloadInventory on the OnGUI render path, where
            // GetHashSet's self-heal (it mutates the tag set + CE HoldTracker) would be an MP desync. It is NOT the
            // pack-animal deposit gate: the actual deposit (JobDriver_LoadPackAnimal) reads the HEALED GetHashSet
            // directly, so a merge-survivor stack IS deposited once a load runs. The only cost of the un-healed read
            // here is that a pawn whose ONLY surplus is a merge-survivor might not get a load OFFERED — a benign
            // under-report (the loot then rides to storage), the same class as the alert's own under-report.
            foreach (var t in comp.PeekHashSet())
                if (t != null && !t.Destroyed && inner.Contains(t) && InventorySurplus.SurplusOf(pawn, t) > 0)
                    return true;
            return false;
        }

        /// <summary>Is a load-onto-pack-animal job already current or queued for this pawn? (Dedup.)</summary>
        internal static bool HasLoadJob(Pawn pawn)
        {
            if (pawn?.jobs == null)
                return false;
            if (pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_LoadPackAnimal)
                return true;
            var queue = pawn.jobs.jobQueue;
            // Indexed walk: Verse.AI.JobQueue.GetEnumerator() returns the boxed IEnumerator<QueuedJob> (the
            // List<T>.Enumerator struct boxed to the interface, ~40 B/call); the indexer returns QueuedJob directly
            // with no allocation (HD-JOBQUEUE-BOX). The indexer is already used on jobQueue elsewhere (ConstructTether).
            if (queue != null)
                for (int i = 0; i < queue.Count; i++)
                    if (queue[i]?.job?.def == HaulersDreamDefOf.HaulersDream_LoadPackAnimal)
                        return true;
            return false;
        }

        /// <summary>
        /// AUTO-DIVERT: an over-encumbered pawn on a non-home map breaks off (at the next job boundary) to load
        /// the nearest owned pack animal with its scooped loot — instead of the storage unload, which has no
        /// destination on a temporary map. No-op (loot just rides home in inventory) when no carrier is reachable.
        /// </summary>
        internal static void MaybeAutoDivert(Pawn pawn, HaulersDreamSettings s)
        {
            if (s == null || pawn?.Map == null)
                return;
            // A drafted pawn must never break off to load an animal — a queued job sits ABOVE the drafted-behavior
            // think node, so it would march off mid-combat instead of standing to orders (the project's standing
            // draft gate, mirrored from PawnUnloadChecker / OpportunisticUnload / the gizmo).
            if (pawn.Drafted)
                return;
            bool atHome = pawn.Map.IsPlayerHome;
            bool alreadyLoading = HasLoadJob(pawn);
            bool hasSurplus = !alreadyLoading && HasDepositableSurplus(pawn);
            // The carrier search pathfinds, so only run it when the cheap gates already pass.
            bool cheapPass = s.autoDivertToPackAnimal && s.enableOnNonHomeMaps && !atHome && hasSurplus;
            var carrier = cheapPass ? FindCarrier(pawn) : null;
            if (PackAnimalLoadPolicy.ShouldAutoDivert(s.autoDivertToPackAnimal, s.enableOnNonHomeMaps, atHome,
                    carrier != null, hasSurplus, alreadyLoading))
                QueueDepositOnly(pawn, carrier);
        }

        /// <summary>
        /// The OPPORTUNISTIC caravan offload — the pack-animal counterpart to the storage unload. Returns a
        /// ready deposit-only HaulersDream_LoadPackAnimal job (reservations made, divert cooldown stamped) when
        /// a non-home pawn should shed its scooped loot onto a usable pack animal NOW, or null. This is what the
        /// automatic unload TRIGGERS (end-of-run, before-downtime, interval, idle backstop) call in place of the
        /// storage unload they make at home: each caller decides the TIMING exactly as for the home path (settle
        /// window / grace / downtime), so F38's accumulate-during-work is preserved by construction; this only
        /// adds the destination-availability + standing gates. Distinct from <see cref="MaybeAutoDivert"/> (the
        /// over-encumbered ceiling, no eligibility/grace) and <see cref="TryBuildBulkLoadJob"/> (the manual order,
        /// no eligibility gate). Gated on the caravan toggle + the mod being active on non-home maps + eligibility
        /// + not drafted + a usable carrier + depositable surplus + no existing load job + the divert cooldown.
        /// markForUnload (the auto-unload master) is ALSO required — every current trigger already gates on it
        /// (matching the home automatic unloads), and it is re-checked here too, defended in depth alongside the
        /// other gates so a future caller can't bypass the master switch.
        /// </summary>
        internal static Job TryGetOpportunisticLoadJob(Pawn pawn)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || pawn?.Map == null || pawn.jobs == null)
                return null;
            bool atHome = pawn.Map.IsPlayerHome;
            bool drafted = pawn.Drafted;
            bool eligible = pawn.Faction == Faction.OfPlayerSilentFail && YieldRouter.IsEligible(pawn);
            bool alreadyLoading = HasLoadJob(pawn);
            bool hasSurplus = !alreadyLoading && HasDepositableSurplus(pawn);
            // Divert cooldown (shared with the storage opportunistic paths via lastOpportunisticUnloadTick): a
            // recent offload — stamped on a successful build below — bars another for a short while, so a trip
            // that starts but ends without clearing the load can't re-issue every tick. (A pawn with surplus but
            // no usable carrier just re-checks each trigger, exactly as the over-encumbered MaybeAutoDivert does;
            // UsablePackAnimalWithTheMostFreeSpace is cheap when there are no reachable animals.)
            var comp = pawn.GetComp<CompHauledToInventory>();
            int now = Find.TickManager?.TicksGame ?? 0;
            bool cooled = comp != null && now - comp.lastOpportunisticUnloadTick >= OpportunisticUnload.DivertCooldownTicks;
            // Cheap gates first; the carrier search pathfinds, so run it only once everything else passes
            // (mirrors MaybeAutoDivert's cheapPass discipline — the interval / idle callers are hot).
            if (!(s.markForUnload && s.autoDivertToPackAnimal && s.enableOnNonHomeMaps && !atHome && !drafted
                  && eligible && hasSurplus && !alreadyLoading && cooled))
                return null;
            var carrier = FindCarrier(pawn);
            if (!PackAnimalLoadPolicy.ShouldOffloadOpportunistically(s.autoDivertToPackAnimal, s.enableOnNonHomeMaps,
                    atHome, carrier != null, hasSurplus, alreadyLoading, eligible, drafted))
                return null;
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_LoadPackAnimal, carrier);
            if (!job.TryMakePreToilReservations(pawn, false))
                return null;
            OpportunisticUnload.NotifyDiverted(pawn); // stamp the divert cooldown (shared with the storage paths)
            HDLog.Dbg($"{pawn} offloading scooped loot onto {carrier} (opportunistic caravan unload).");
            return job;
        }

        /// <summary>The "Unload now" gizmo while on a non-home map: load the nearest pack animal now. Manual, so
        /// gated only on enableOnNonHomeMaps (not the auto-divert toggle). Messages when no carrier is around.</summary>
        internal static void GizmoLoadNearest(Pawn pawn, bool immediate = false)
        {
            var s = HaulersDreamMod.Settings;
            if (pawn?.Map == null || s == null || !s.enableOnNonHomeMaps || pawn.Drafted || HasLoadJob(pawn))
                return;
            if (!HasDepositableSurplus(pawn))
                return;
            var carrier = FindCarrier(pawn);
            if (carrier == null)
            {
                Messages.Message("HaulersDream.LoadPackAnimal.NoCarrier".Translate(), pawn,
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            // #215: the "Unload now" gizmo on an away map. A plain left-click loads the carrier immediately
            // (immediate == true); Shift queues it behind the current job (the prior behavior).
            QueueDepositOnly(pawn, carrier, immediate);
        }

        // ---- coalescing vanilla "Load onto pack animal" (GiveToPackAnimal) orders into ONE trip --------------

        /// <summary>Should a vanilla GiveToPackAnimal order be REDIRECTED into HD's inventory-based load job, so
        /// several shift-clicked "Load onto pack animal" orders coalesce into one trip (instead of one-stack-in-
        /// hands per order)? Only on a caravan/away map, with the feature on, a comp, and a usable carrier.</summary>
        internal static bool ShouldRedirectGiveToPackAnimal(Pawn pawn, Job vanillaJob)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.loadPackAnimalBulk || !s.enableOnNonHomeMaps)
                return false;
            if (pawn?.Map == null || pawn.Map.IsPlayerHome || pawn.Drafted)
                return false;
            // NO IsEligible gate: this only COALESCES the player's own vanilla "Load onto pack animal" orders into
            // one trip, and the swept loot goes onto the ANIMAL (never stranded in the pawn's inventory), so the
            // automatic-hauling eligibility that gates bulk-haul does not apply. A specialist incapable of dumb-
            // labor hauling — whose single-stack load order vanilla already accepts — should still get the
            // coalesced trip rather than being silently dropped back to one-stack-in-hands.
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return false;
            var item = vanillaJob?.targetA.Thing;
            if (item == null || !item.Spawned || item.def == null || item.def.category != ThingCategory.Item)
                return false;
            return FindCarrier(pawn) != null; // no carrier -> let vanilla handle it (it will find none and end)
        }

        /// <summary>The pawn's active (current or queued) HD load-pack-animal job, or null — the coalesce target
        /// so successive "Load onto pack animal" orders join one trip rather than each becoming a separate job.</summary>
        internal static Job FindActiveLoadJob(Pawn pawn)
        {
            if (pawn?.jobs == null)
                return null;
            if (pawn.CurJobDef == HaulersDreamDefOf.HaulersDream_LoadPackAnimal && pawn.CurJob != null)
                return pawn.CurJob;
            var queue = pawn.jobs.jobQueue;
            // Indexed walk (HD-JOBQUEUE-BOX): the JobQueue enumerator boxes; the indexer (used here) does not.
            if (queue != null)
                for (int i = 0; i < queue.Count; i++)
                    if (queue[i]?.job?.def == HaulersDreamDefOf.HaulersDream_LoadPackAnimal)
                        return queue[i].job;
            return null;
        }

        /// <summary>Append a chosen item to an existing HD load job's sweep queue (coalescing). The running
        /// job's fill loop re-reads the queue, so a freshly-appended item is swept in the same (or the next)
        /// fill pass — one job, as few trips as carry capacity allows.</summary>
        internal static void AppendToLoadJob(Job loadJob, Thing item, int count)
        {
            if (loadJob == null || item == null)
                return;
            if (loadJob.targetQueueB == null)
                loadJob.targetQueueB = new List<LocalTargetInfo>();
            if (loadJob.countQueue == null)
                loadJob.countQueue = new List<int>();
            for (int i = 0; i < loadJob.targetQueueB.Count; i++)
                if (loadJob.targetQueueB[i].Thing == item)
                    return; // already queued in this job
            loadJob.targetQueueB.Add(item);
            loadJob.countQueue.Add(count > 0 ? count : item.stackCount);
        }

        /// <summary>Build an HD load job that redirects a vanilla GiveToPackAnimal order: sweep the chosen item
        /// into inventory, then deposit onto the carrier. Null if no carrier is available.</summary>
        internal static Job BuildRedirectJob(Pawn pawn, Job vanillaJob)
        {
            var item = vanillaJob?.targetA.Thing;
            var carrier = FindCarrier(pawn);
            if (item == null || carrier == null)
                return null;
            int count = vanillaJob.count > 0 ? vanillaJob.count : item.stackCount;
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_LoadPackAnimal, carrier);
            job.targetQueueB = new List<LocalTargetInfo> { item };
            job.countQueue = new List<int> { count };
            job.count = 1; // sentinel: a -1 Job.count reads as "broken" in some vanilla checks
            return job;
        }

        private static void QueueDepositOnly(Pawn pawn, Pawn carrier, bool immediate = false)
        {
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_LoadPackAnimal, carrier);
            if (pawn.jobs != null && job.TryMakePreToilReservations(pawn, false))
            {
                pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
                HDLog.Dbg($"{pawn} diverting to load {carrier} with carried loot.");
                // #215 left-click: run the load NOW by ending the current job (the just-EnqueueFirst'd load then
                // starts at once). The automatic auto-divert caller passes immediate:false, keeping its
                // queue-behind-current behavior.
                if (immediate)
                    PawnUnloadChecker.InterruptCurrentJob(pawn);
            }
        }

        // ---- R1: the manual bulk-load order (sweep nearby ground stacks into inventory, then deposit) --------

        private const float MinSearchRadius = 12f;
        private const int MaxStacks = 24;
        private const float PoolRadiusHops = 4f;

        /// <summary>
        /// Build the manual bulk-load job: snowball nearby haulable stacks (from the clicked one) into a sweep
        /// queue up to the pawn's carry ceiling, with the carrier as the deposit target. When nothing new can be
        /// swept (pawn already full / no eligible neighbours) it still returns a deposit-only job if the pawn
        /// holds loot, so the order always makes the trip; null only when there is genuinely nothing to do.
        /// </summary>
        internal static Job TryBuildBulkLoadJob(Pawn pawn, Thing clicked, Pawn carrier)
        {
            var s = HaulersDreamMod.Settings;
            var map = pawn?.Map;
            if (s == null || map == null || clicked == null || !clicked.Spawned || carrier == null)
                return null;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return null;
            // NO IsEligible gate here: this is a PLAYER ORDER (the float-menu provider is the gatekeeper), and the
            // swept loot is deposited onto the ANIMAL — never stranded in the pawn's inventory — so the load/unload
            // symmetry that gates the AUTOMATIC bulk-haul (BulkHaul.BuildBulkJob, which keeps its IsEligible gate)
            // does not apply. A specialist incapable of dumb-labor hauling can still be ordered to load, matching
            // vanilla "Load onto pack animal".

            float ceiling = CeilingKg(pawn, s);
            float running = MassUtility.GearAndInventoryMass(pawn);
            float bulkRoom = CECompat.AvailableBulk(pawn); // +inf without CE

            var things = new List<Thing>();
            var counts = new List<int>();

            var pool = BulkHaul.BuildPool(pawn, clicked, map, MinSearchRadius * PoolRadiusHops);
            var claimed = RouteSelection.ClaimedByOtherPawns(pawn);

            // The clicked stack leads the sweep (the player's anchor), then snowball outward, nearest-first.
            AddIfEligible(pawn, clicked, ceiling, ref running, ref bulkRoom, claimed, things, counts);
            var last = clicked.Position;
            while (things.Count < MaxStacks && running < ceiling - 0.0001f)
            {
                var next = NearestEligible(pawn, pool, last, MinSearchRadius, claimed, ceiling, running, bulkRoom, out int take);
                if (next == null)
                    break;
                things.Add(next);
                counts.Add(take);
                running += take * next.GetStatValue(StatDefOf.Mass);
                bulkRoom -= take * CECompat.BulkPerUnit(next);
                last = next.Position;
            }

            if (things.Count == 0)
            {
                // Nothing new to sweep — but still deposit any loot the pawn already carries.
                if (!HasDepositableSurplus(pawn))
                    return null;
                return JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_LoadPackAnimal, carrier);
            }

            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_LoadPackAnimal, carrier);
            job.targetQueueB = new List<LocalTargetInfo>(things.Count);
            for (int i = 0; i < things.Count; i++)
                job.targetQueueB.Add(things[i]);
            job.countQueue = new List<int>(counts);
            job.count = 1; // sentinel: a -1 Job.count reads as "broken" in some vanilla checks
            return job;
        }

        // Eligibility for a sweep candidate destined for an animal: reachable, not forbidden, not claimed by
        // another pawn, fits under the carry ceiling (+ CE bulk). Storage existence is NOT required (unlike the
        // storage bulk-haul) — the destination is the animal, not a stockpile.
        private static void AddIfEligible(Pawn pawn, Thing t, float ceiling, ref float running, ref float bulkRoom,
            HashSet<Thing> claimed, List<Thing> things, List<int> counts)
        {
            if (t == null || !t.Spawned || t.IsForbidden(pawn) || claimed.Contains(t))
                return;
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced: false))
                return;
            if (!ExtraSweepReach.Allows(pawn, t))
                return; // bonus extra: don't sweep a suit-less pawn into vacuum/fire to load an animal
            int take = BulkHaulPolicy.CountWithinCeiling(ceiling, running, t.GetStatValue(StatDefOf.Mass), t.stackCount);
            take = Math.Min(take, CECompat.MaxFitCount(pawn, t));
            float bulkPer = CECompat.BulkPerUnit(t);
            if (bulkPer > 0f && !float.IsPositiveInfinity(bulkRoom))
                take = Math.Min(take, (int)Math.Floor(bulkRoom / bulkPer));
            if (take <= 0)
                return;
            things.Add(t);
            counts.Add(take);
            running += take * t.GetStatValue(StatDefOf.Mass);
            bulkRoom -= take * bulkPer;
        }

        private static Thing NearestEligible(Pawn pawn, List<Thing> pool, IntVec3 from, float radius,
            HashSet<Thing> claimed, float ceiling, float running, float bulkRoom, out int take)
        {
            take = 0;
            float radiusSq = radius * radius;
            while (true)
            {
                int bestIdx = -1;
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < pool.Count; i++)
                {
                    float d = (pool[i].Position - from).LengthHorizontalSquared;
                    // MP determinism: break distance ties by thingIDNumber so all clients pick the same stack
                    // (HashSet iteration order can differ per client).
                    if (d <= radiusSq && (d < bestDistSq
                        || (d == bestDistSq && bestIdx >= 0 && pool[i].thingIDNumber < pool[bestIdx].thingIDNumber)))
                    { bestDistSq = d; bestIdx = i; }
                }
                if (bestIdx < 0)
                    return null;
                var t = pool[bestIdx];
                pool.RemoveAt(bestIdx);
                if (t.IsForbidden(pawn) || claimed.Contains(t))
                    continue;
                int fits = BulkHaulPolicy.CountWithinCeiling(ceiling, running, t.GetStatValue(StatDefOf.Mass), t.stackCount);
                fits = Math.Min(fits, CECompat.MaxFitCount(pawn, t));
                float bulkPer = CECompat.BulkPerUnit(t);
                if (bulkPer > 0f && !float.IsPositiveInfinity(bulkRoom))
                    fits = Math.Min(fits, (int)Math.Floor(bulkRoom / bulkPer));
                if (fits <= 0)
                    continue;
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced: false))
                    continue;
                if (!ExtraSweepReach.Allows(pawn, t))
                    continue; // bonus extra: cap reach at Some (no vacuum/fire detours for an animal load)
                take = fits;
                return t;
            }
        }

        // The live worth-it carry ceiling for this pawn (the same smart-overload math as BulkHaul / JobDriver_BulkHaul).
        internal static float CeilingKg(Pawn pawn, HaulersDreamSettings s)
        {
            float baseCap = CarryMath.EffectiveCapacity(CarryCapacity.Of(pawn),
                s?.carryLimitFraction ?? CarryMath.MaxFraction);
            return BulkHaulPolicy.CeilingKg(s?.overloadLevel ?? 0, OverloadGate.NoOverloadFor(pawn, s), baseCap,
                OverloadGate.MaxCeilingKg(s));
        }
    }
}
