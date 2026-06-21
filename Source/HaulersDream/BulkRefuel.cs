using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The bulk-refuel planner — a refuelable (a shuttle's chemfuel, a deep drill, a generator, any
    /// <see cref="CompRefuelable"/>) is filled in ONE trip: the pawn sweeps enough nearby reservable fuel stacks into
    /// its inventory, walks to the refuelable ONCE, and deposits them all (see
    /// <see cref="JobDriver_BulkRefuel"/>) — instead of vanilla's one-stack-in-hands per walk. Mirrors HD's bulk-load
    /// philosophy (the transporter/portal/vehicle family) but stays its OWN small path, because a refuelable has no
    /// shared manifest/ledger: vanilla <see cref="CompRefuelable.Refuel(System.Collections.Generic.List{Thing})"/>
    /// just consumes things up to the deficit, so no claim-ledger is needed (a second hauler that arrives after the
    /// thing is full simply finds nothing to do).
    ///
    /// Reuses vanilla's OWN fuel finder (<see cref="RefuelWorkGiverUtility.FindEnoughReservableThings"/>) so the set
    /// of stacks (and the reachability/reservability/fogged checks) matches exactly what vanilla would pick — HD only
    /// changes the COURIER shape (one trip via inventory, not one stack per walk). The CLAIM model is just the
    /// per-stack reservation the driver makes in <c>TryMakePreToilReservations</c>; no GameComponent ledger.
    ///
    /// WORTH-IT: HD only builds a bulk job when MULTIPLE stacks/trips are needed (<c>fuels.Count &gt;= 2</c>) — a
    /// single-stack refuel is already a single trip in vanilla, so HD leaves it untouched (the redirect returns true,
    /// vanilla's job stands).
    /// </summary>
    public static class BulkRefuel
    {
        /// <summary>The feature is live: the per-feature toggle AND the master kill-switch. (The map gate +
        /// pawn-eligibility gate are applied per-pawn in <see cref="TryGiveBulkRefuelJob"/>.)</summary>
        public static bool FeatureEnabled
        {
            get
            {
                var s = HaulersDreamMod.Settings;
                return s != null && s.enableBulkRefuel && MasterEnable.Active;
            }
        }

        /// <summary>
        /// Build the bulk-refuel job, or null when HD should leave vanilla's single-stack refuel in place. Gates
        /// (mirroring vanilla <see cref="RefuelWorkGiverUtility.CanRefuel"/> plus HD's standard scoop gates):
        /// feature on, HD active on the map, the pawn is bulk-eligible (auto path) with the tracking comp + inventory,
        /// the thing has a non-atomic <see cref="CompRefuelable"/> that is not full / not fogged / wrong-faction, the
        /// auto-refuel conditions hold (unless <paramref name="playerOrder"/>), there is a real deficit, and vanilla's
        /// own finder reaches ENOUGH fuel in MULTIPLE stacks (the worth-it gate). Over-pick is trimmed so the queued
        /// total ≈ the deficit. PURE planning — the driver makes the reservations on start.
        /// </summary>
        public static Job TryGiveBulkRefuelJob(Pawn pawn, Thing refuelable, bool playerOrder)
        {
            if (!FeatureEnabled)
                return null;
            if (pawn?.Map == null || refuelable == null || !refuelable.Spawned)
                return null;
            if (!MapGate.HdActiveOnMap(pawn.Map))
                return null;
            if (pawn.GetComp<CompHauledToInventory>() == null || pawn.inventory == null)
                return null;
            // Auto path (work-scan / utility takeover): require the standard bulk-haul eligibility. A player order
            // skips it (the fuel is deposited into the refuelable, so nothing strands) — matching the float-menu
            // providers / TransportLoad.
            if (!playerOrder && !YieldRouter.IsEligible(pawn))
                return null;

            var comp = refuelable.TryGetComp<CompRefuelable>();
            if (comp == null || comp.IsFull)
                return null;
            // Atomic-fueling refuelables (e.g. a reloadable mortar barrel) need the WHOLE charge delivered at once and
            // are handled by vanilla's RefuelAtomic job (CompRefuelable.Refuel does NOTHING if an atomic list sums to
            // less than the deficit) — leave them to vanilla.
            if (comp.Props != null && comp.Props.atomicFueling)
                return null;

            // Vanilla CanRefuel gates (so HD never offers a refuel vanilla itself would refuse).
            if (refuelable.Fogged())
                return null;
            if (!playerOrder && !comp.allowAutoRefuel)
                return null;
            // Don't top up a partially-fueled thing unless its props allow refuel-if-not-empty (vanilla CanRefuel).
            if (comp.Props != null && comp.FuelPercentOfMax > 0f && !comp.Props.allowRefuelIfNotEmpty)
                return null;
            // Auto path only fires when vanilla's auto-refuel timing says so (a forced/player order bypasses it).
            if (!playerOrder && !comp.ShouldAutoRefuelNow)
                return null;
            // Vanilla only refuels things of the pawn's own faction.
            if (refuelable.Faction != pawn.Faction)
                return null;
            // Cooldown gate parity with RefuelWorkGiverUtility.CanRefuel: a thing on an interaction cooldown
            // that forbids refuelling can't be refuelled. The auto path is already protected (the work-scan
            // runs CanRefuel before JobOnThing), but the player float-menu path calls this directly, so honor it
            // here too — otherwise a forced bulk-refuel could start on e.g. a rocketswarm launcher mid-cooldown.
            if (refuelable.TryGetComp(out CompInteractable interactable)
                && interactable.Props.cooldownPreventsRefuel && interactable.OnCooldown)
                return null;
            if (!pawn.CanReserve(refuelable, 1, -1, null, playerOrder))
                return null;

            int deficit = comp.GetFuelCountToFullyRefuel();
            if (deficit <= 0)
                return null;

            var filter = comp.Props?.fuelFilter;
            if (filter == null)
                return null;

            // Anchor the region-based fuel sweep at a PASSABLE cell. Vanilla FindEnoughReservableThings derefs
            // rootCell.GetRegion(map).Map with NO null guard (decompiled), so an impassable root NREs there — and a
            // refuelable's OWN footprint is usually impassable (a deep drill, a generator, Advanced Power Plus's 6x6
            // nuclear reactor — issue #34), so refuelable.Position has no passable region. Vanilla never NREs because
            // its NON-atomic refuel anchors at pawn.Position (always passable; only the ATOMIC path passes
            // refuelable.Position, and shipped atomic content sits on passable footprints). HD originally fed
            // refuelable.Position in and NRE'd on every impassable refuelable; a stop-gap guard then turned that into a
            // silent DECLINE, disabling bulk-refuel for the very buildings it exists for. Anchor at pawn.Position like
            // vanilla: always passable (no NRE), and FindEnoughReservableThings's own validator still only collects
            // fuel the pawn can reach + reserve — and the pawn already passed CanReserve(refuelable) above, so anything
            // it can reach it can also carry to the refuelable (reachability within a connected area is symmetric), so
            // nothing strands. A defensive region check still declines cleanly in the rare case the pawn itself is in
            // an unregioned cell, deferring to vanilla's single-stack path rather than risking a throw.
            IntVec3 searchRoot = pawn.Position;
            if (!searchRoot.InBounds(pawn.Map) || searchRoot.GetRegion(pawn.Map) == null)
                return null;

            // Vanilla's own "find enough reservable fuel" finder — same reachability / reservability / fogged / filter
            // checks vanilla uses, so the picked set matches. The IntRange is min=1, max=deficit: min=1 mirrors
            // vanilla's single-stack tolerance (the finder returns its chosen list once the accumulated quantity
            // reaches the min), so we accept a PARTIAL sweep whenever ANY reachable+reservable fuel exists — a
            // deficit==min==max would instead demand the WHOLE remaining deficit be reachable in one pass, which a
            // high-capacity refuelable (e.g. a large reactor) rarely satisfies, returning null and dead-ending the bulk
            // job. max=deficit still caps accumulation at the deficit; a partial sweep deposits what it carries and a
            // later trip tops up the rest (the driver re-tags leftovers for the normal unload, so no over-pick).
            var fuels = RefuelWorkGiverUtility.FindEnoughReservableThings(
                pawn, searchRoot, new IntRange(1, deficit), t => filter.Allows(t));

            // WORTH-IT: vanilla already handles a single-stack refuel in one trip, so HD only adds value when 2+
            // stacks (2+ vanilla walks) are needed. Null / 0 / 1 stack -> leave vanilla's path.
            if (fuels == null || fuels.Count < 2)
                return null;

            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_BulkRefuel, refuelable);
            job.targetQueueB = new List<LocalTargetInfo>(fuels.Count);
            job.countQueue = new List<int>(fuels.Count);
            // Queue each stack, trimming the LAST stack so the running total doesn't wildly exceed the deficit
            // (over-pick is otherwise safe — the deposit only consumes up to the deficit and any leftover stays
            // HD-tagged for the normal unload — but trimming avoids hauling a whole extra stack we won't use).
            int running = 0;
            for (int i = 0; i < fuels.Count && running < deficit; i++)
            {
                var f = fuels[i];
                if (f == null || f.stackCount <= 0)
                    continue;
                int take = f.stackCount;
                if (running + take > deficit)
                    take = deficit - running;
                if (take <= 0)
                    break;
                job.targetQueueB.Add(new LocalTargetInfo(f));
                job.countQueue.Add(take);
                running += take;
            }
            // After trimming, we may have collapsed to a single usable stack — re-apply the worth-it gate so a deficit
            // that one big stack covers (vanilla = one trip) doesn't get a redundant HD detour.
            if (job.targetQueueB.Count < 2)
                return null;
            job.count = 1; // sentinel: a -1 Job.count reads as "broken" in some vanilla checks

            HDLog.Dbg($"BulkRefuel: {pawn} sweeping {job.targetQueueB.Count} fuel stack(s) for {refuelable.LabelShort} (deficit {deficit}, queued ~{running}).");
            return job;
        }

        /// <summary>
        /// Cheap pre-gate for the autonomous redirect (mirrors the transporter HasJob/JobOn split): feature on, the
        /// comp present, not full, non-atomic, a real deficit. Deliberately does NOT run the expensive
        /// <see cref="RefuelWorkGiverUtility.FindEnoughReservableThings"/> sweep — the redirect only pays that in
        /// <see cref="TryGiveBulkRefuelJob"/> after this passes.
        /// </summary>
        public static bool HasPotentialBulkRefuel(Pawn pawn, Thing refuelable)
        {
            if (!FeatureEnabled)
                return false;
            if (refuelable == null)
                return false;
            var comp = refuelable.TryGetComp<CompRefuelable>();
            if (comp == null || comp.IsFull)
                return false;
            if (comp.Props != null && comp.Props.atomicFueling)
                return false;
            return comp.GetFuelCountToFullyRefuel() > 0;
        }
    }
}
