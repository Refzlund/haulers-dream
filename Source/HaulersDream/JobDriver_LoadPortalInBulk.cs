using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Bulk-load a map portal (pit gate, cave / vault exit, "enter map" portal) — the portal counterpart to
    /// <see cref="JobDriver_LoadTransportersInBulk"/>. Same three-phase shape (sweep nearby ground stacks into tagged
    /// inventory → walk to the portal ONCE → deposit every tagged stack the portal still needs), but the deposit is
    /// THING-LESS:
    ///
    /// The portal's <c>PortalContainerProxy.TryAdd</c> teleports/consumes the deposited Thing (it fires
    /// <c>MapPortal.Notify_ThingAdded → SubtractFromToLoadList</c>, then <c>GenDrop.TryDropSpawn</c>s it onto the
    /// OTHER map). After the transfer the Thing reference is on a different map (or destroyed when fully moved), so
    /// reading the moved count off it would silently under-count. We therefore capture <c>(def, count)</c> BEFORE the
    /// transfer and settle the ledger thing-lessly via <see cref="HaulersDreamGameComponent.LoadNotifyDeposited(Pawn,
    /// IManagedLoadable, ThingDef, int)"/>. The deposit MUST go through <c>portal.GetDirectlyHeldThings()</c> (the
    /// proxy) — a manual <c>GenDrop</c> would skip BOTH the pocket-map generation AND the manifest decrement.
    /// <see cref="Global.IsExecutingManagedPortalUnload"/> is the portal-side per-thread flag (set in a try/finally
    /// around each transfer — reset even on throw, rethrow, no suppression) that makes the
    /// <c>MapPortal.SubtractFromToLoadList</c> intercept precise. There is NO group mass cap (portals are uncapped —
    /// <c>HasMassCap=false</c>), but each pull is still clamped via <see cref="TransportLoadPlan.DeliverableUnits"/>
    /// during the sweep.
    ///
    /// Concurrency: the CLAIM is recorded in <see cref="Notify_Starting"/> (so a built-but-never-started probe never
    /// claims); on every non-Success end the claim is RELEASED and the carried task item is SALVAGED back into
    /// inventory (re-tagged, rides HD's normal unload) — never dropped on a temp map, never stuck.
    /// </summary>
    public class JobDriver_LoadPortalInBulk : JobDriver
    {
        private const TargetIndex PortalInd = TargetIndex.A; // the portal (deposit dest)
        private const TargetIndex StackInd = TargetIndex.B;  // scratch: the ground stack being swept

        private int loadIndex;
        private int depositLoops;
        private int passes;
        private const int MaxDepositLoops = 64;
        private const int MaxPasses = 64;

        // Resolved on start (Notify_Starting). In-flight only — re-resolved from the live portal on load.
        [System.NonSerialized] private MapPortalBulkTarget adapter;
        // Set true on a chaining/cleanup end so the finish action RETAINS the claim (no thrash). Currently always
        // false (no chaining), but kept as the documented retain hook for a future smooth-chain.
#pragma warning disable CS0649
        [System.NonSerialized] private bool retainClaimOnEnd;
#pragma warning restore CS0649

        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();
        private MapPortal Portal => job.GetTarget(PortalInd).Thing as MapPortal;

        private static HaulersDreamSettings Settings => HaulersDreamMod.Settings;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdLpibLoadIndex", 0);
            Scribe_Values.Look(ref depositLoops, "hdLpibDepositLoops", 0);
            Scribe_Values.Look(ref passes, "hdLpibPasses", 0);
        }

        public override string GetReport() => "HaulersDream.LoadPortal.Report".Translate();

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            var portal = Portal;
            adapter = portal != null ? MapPortalBulkTarget.TryCreate(portal) : null;
            if (adapter != null)
                HaulersDreamGameComponent.Instance?.LoadClaim(pawn, job, adapter);
        }

        private MapPortalBulkTarget EnsureAdapter()
        {
            if (adapter != null)
                return adapter;
            var portal = Portal;
            adapter = portal != null ? MapPortalBulkTarget.TryCreate(portal) : null;
            return adapter;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Bulk-haul reservation shape: queue[0] strict, the rest best-effort. NEVER reserve the portal
            // (re-found each deposit). A deposit-only job (empty queue) reserves nothing.
            var queue = job.GetTargetQueue(StackInd);
            if (queue == null || queue.Count == 0)
                return true;
            if (!pawn.Reserve(queue[0], job, 1, -1, null, errorOnFailed))
                return false;
            pawn.ReserveAsManyAsPossible(queue, job);
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(PortalInd);
            // Fail cleanly if the portal's load was cancelled mid-trip (a pit-gate collapse / the Cancel-load gizmo
            // clears leftToLoad → LoadInProgress false) — the salvage finish action re-tags the carried stock so it
            // rides the normal unload. NOTE: at NATURAL completion the loopCheck toil ends the job Succeeded within
            // the same tick (global fail conditions are evaluated once at the top of DriverTick, BEFORE the instant
            // deposit→loopCheck chain drains the manifest), so this fail does not pre-empt a clean finish; and if it
            // ever does fire with stock still carried, salvage re-tags it (the correct outcome regardless).

            Toil fillStart = Toils_General.Label();
            Toil depositStart = Toils_General.Label();
            Toil loopCheck = ToilMaker.MakeToil("HD_Lpib_LoopCheck");

            // ============ FILL: sweep queued ground stacks into tagged inventory, up to the carry ceiling ============
            yield return fillStart;

            Toil sweepDecide = ToilMaker.MakeToil("HD_Lpib_SweepDecide");
            sweepDecide.initAction = delegate
            {
                var queue = job.targetQueueB;
                var counts = job.countQueue;
                if (queue == null || queue.Count == 0 || loadIndex >= queue.Count) { JumpToToil(depositStart); return; }
                float ceiling = PackAnimalLoad.CeilingKg(pawn, Settings);
                bool roomLeft = float.IsPositiveInfinity(ceiling)
                                || MassUtility.GearAndInventoryMass(pawn) < ceiling - 0.0001f;
                if (roomLeft && CECompat.IsActive && CECompat.AvailableBulk(pawn) <= 0f)
                    roomLeft = false;
                if (!roomLeft) { JumpToToil(depositStart); return; }
                while (loadIndex < queue.Count)
                {
                    var t = queue[loadIndex].Thing;
                    bool valid = t != null && t.Spawned && !t.IsForbidden(pawn)
                                 && !(t.ParentHolder is Pawn_InventoryTracker)
                                 && counts != null && loadIndex < counts.Count && counts[loadIndex] > 0;
                    if (valid && !pawn.Map.reservationManager.ReservedBy(t, pawn, job)
                        && (!pawn.CanReserve(t) || !pawn.Reserve(t, job, errorOnFailed: false)))
                        valid = false;
                    if (valid) break;
                    loadIndex++;
                }
                if (loadIndex >= queue.Count) { JumpToToil(depositStart); return; }
                job.SetTarget(StackInd, queue[loadIndex].Thing);
            };
            sweepDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return sweepDecide;

            Toil sweepGoto = ToilMaker.MakeToil("HD_Lpib_SweepGoto");
            sweepGoto.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                if (t == null || !t.Spawned) { loadIndex++; JumpToToil(sweepDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            sweepGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return sweepGoto;

            Toil sweepTake = ToilMaker.MakeToil("HD_Lpib_SweepTake");
            sweepTake.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                var counts = job.countQueue;
                int planned = counts != null && loadIndex < counts.Count ? counts[loadIndex] : 0;
                if (t == null || !t.Spawned || planned <= 0 || t.IsForbidden(pawn)) { loadIndex++; JumpToToil(sweepDecide); return; }
                int count = BulkHaulPolicy.CountWithinCeiling(PackAnimalLoad.CeilingKg(pawn, Settings),
                    MassUtility.GearAndInventoryMass(pawn), t.GetStatValue(StatDefOf.Mass),
                    System.Math.Min(planned, t.stackCount));
                count = System.Math.Min(count, CECompat.MaxFitCount(pawn, t));
                if (count <= 0) { JumpToToil(depositStart); return; }
                int groundBefore = t.stackCount;
                var split = t.SplitOff(count);
                var inv = Inv;
                if (inv != null && inv.TryAdd(split, canMergeWithExistingStacks: false))
                {
                    var comp = pawn.GetComp<CompHauledToInventory>();
                    if (comp != null) { comp.RegisterHauledItem(split); comp.NotifyYieldPicked(); }
                    if (!split.Spawned) split.Position = pawn.Position;
                    if (counts != null && loadIndex < counts.Count) counts[loadIndex] = planned - count;
                    bool itemDone = counts == null || loadIndex >= counts.Count || counts[loadIndex] <= 0 || count >= groundBefore;
                    if (itemDone) loadIndex++;
                }
                else if (split != null && !split.Destroyed && !split.Spawned)
                {
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    loadIndex++;
                }
                JumpToToil(sweepDecide);
            };
            sweepTake.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return sweepTake;

            // ============ DEPOSIT: walk to the portal ONCE, then transfer every needed tagged stack ============
            yield return depositStart;

            Toil findPortal = ToilMaker.MakeToil("HD_Lpib_FindPortal");
            findPortal.initAction = delegate
            {
                if (++depositLoops > MaxDepositLoops) { JumpToToil(loopCheck); return; }
                var portal = Portal;
                if (portal == null || !portal.Spawned) { JumpToToil(loopCheck); return; }
                if (!HasDepositableForPortal()) { JumpToToil(loopCheck); return; }
            };
            findPortal.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findPortal;

            Toil gotoPortal = Toils_Goto.GotoThing(PortalInd, PathEndMode.Touch);
            gotoPortal.FailOnDespawnedOrNull(PortalInd);
            yield return gotoPortal;

            Toil deposit = ToilMaker.MakeToil("HD_Lpib_Deposit");
            deposit.initAction = delegate
            {
                var portal = Portal;
                var inner = pawn.inventory?.innerContainer;
                var hcomp = pawn.GetComp<CompHauledToInventory>();
                var adp = EnsureAdapter();
                if (portal == null || !portal.Spawned || inner == null || hcomp == null || adp == null)
                { JumpToToil(loopCheck); return; }

                var destInner = portal.GetDirectlyHeldThings(); // the PortalContainerProxy
                if (destInner == null) { JumpToToil(loopCheck); return; }

                bool movedAny = false;
                var tagged = new List<Thing>(hcomp.GetHashSet());
                for (int i = 0; i < tagged.Count; i++)
                {
                    var thing = tagged[i];
                    if (thing == null || thing.Destroyed || !inner.Contains(thing))
                        continue;
                    if (!pawn.CanReserve(thing))
                        continue;
                    int surplus = InventorySurplus.SurplusOf(pawn, thing);
                    if (surplus <= 0)
                        continue; // personal kit stays with the pawn
                    // Clamp to what the portal's manifest still wants for this def (NOT the whole carried surplus —
                    // depositing more than the manifest needs would over-load the other map / under-count nothing,
                    // but the SubtractFromToLoadList intercept only decrements what the entry held, so leftover
                    // surplus stays tagged for HD's normal unload).
                    int portalRemaining = PortalRemainingFor(portal, thing.def);
                    int count = System.Math.Min(surplus, portalRemaining);
                    if (count <= 0)
                        continue; // portal no longer needs this def (filled by another pawn) — leave it tagged

                    // THING-LESS settle — capture (def, count) BEFORE the transfer and measure the moved amount from
                    // the SOURCE side. The proxy's TryAdd teleports the split Thing to the other map via
                    // GenDrop.TryDropSpawn(ThingPlaceMode.Near), which CAN MERGE the split into an existing stack
                    // there and DESTROY it — so the transfer's return value (= the dropped split's stackCount AFTER
                    // the drop) reads 0/partial on a merge, a silent under-count. The robust signal is how much LEFT
                    // the inventory: beforeCount − (still-in-inventory remainder). That is always observable and
                    // exactly equals the deposited count (the manifest decrement inside Notify_ThingAdded saw the
                    // full split.stackCount BEFORE the drop, so it already decremented correctly).
                    var depDef = thing.def;
                    int beforeCount = thing.stackCount;

                    // Set the per-thread portal flag so the MapPortal.SubtractFromToLoadList intercept does the
                    // PRECISE decrement. try/finally resets it even on throw; the throw RETHROWS (no suppression).
                    Global.IsExecutingManagedPortalUnload = true;
                    try
                    {
                        inner.TryTransferToContainer(thing, destInner, count, out Thing _, canMergeWithExistingStacks: false);
                    }
                    finally
                    {
                        Global.IsExecutingManagedPortalUnload = false;
                    }
                    // Units that physically left the inventory: full move -> thing removed from inner (remainder 0);
                    // partial -> thing stays with reduced stackCount. Never reads the teleported split.
                    bool fullyMoved = thing.Destroyed || !inner.Contains(thing);
                    int actuallyMoved = beforeCount - (fullyMoved ? 0 : thing.stackCount);
                    if (actuallyMoved > 0)
                    {
                        movedAny = true;
                        // Thing-less settle with (def, count) — both captured/derived without reading the teleported Thing.
                        HaulersDreamGameComponent.Instance?.LoadNotifyDeposited(pawn, adp, depDef, actuallyMoved);
                        if (fullyMoved)
                            hcomp.Deregister(thing); // fully moved -> drop the tag; a partial leaves the remainder tagged
                    }
                }
                if (!movedAny) { JumpToToil(loopCheck); return; }
                JumpToToil(findPortal); // more to deposit or fall to loopCheck (drained)
            };
            deposit.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return deposit;

            // ============ LOOP: deposit freed inventory room; refill if queued stacks remain ============
            loopCheck.initAction = delegate
            {
                if (++passes > MaxPasses) { EndJobWith(JobCondition.Incompletable); return; }
                depositLoops = 0;
                var queue = job.targetQueueB;
                if (queue != null && loadIndex < queue.Count) { JumpToToil(fillStart); return; }
                EndJobWith(JobCondition.Succeeded);
            };
            loopCheck.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loopCheck;

            // Release the claim + salvage any still-carried task items on every non-Success end (idempotent).
            AddFinishAction(delegate (JobCondition condition)
            {
                if (!retainClaimOnEnd)
                    HaulersDreamGameComponent.Instance?.LoadReleaseClaimsForPawn(pawn);
                // Re-tag survivors (idempotent self-heal) so any swept-but-undeposited stacks ride HD's normal unload.
                var hcomp = pawn.GetComp<CompHauledToInventory>();
                var inner = pawn.inventory?.innerContainer;
                if (hcomp != null && inner != null)
                {
                    var snapshot = new List<Thing>(hcomp.GetHashSet());
                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        var t = snapshot[i];
                        if (t != null && !t.Destroyed && inner.Contains(t))
                            hcomp.RegisterHauledItem(t);
                    }
                }
            });
        }

        /// <summary>Total units of <paramref name="def"/> the portal's manifest still wants (Σ CountToTransfer across
        /// its <c>leftToLoad</c> entries for that def).</summary>
        private static int PortalRemainingFor(MapPortal portal, ThingDef def)
        {
            var ltl = portal?.leftToLoad;
            if (ltl == null || def == null)
                return 0;
            int sum = 0;
            for (int i = 0; i < ltl.Count; i++)
            {
                var tr = ltl[i];
                if (tr != null && tr.ThingDef == def && tr.CountToTransfer > 0)
                    sum += tr.CountToTransfer;
            }
            return sum;
        }

        /// <summary>True if the pawn holds any tagged surplus stack of a def the portal still wants.</summary>
        private bool HasDepositableForPortal()
        {
            var portal = Portal;
            var hcomp = pawn.GetComp<CompHauledToInventory>();
            var inner = pawn.inventory?.innerContainer;
            if (portal == null || hcomp == null || inner == null)
                return false;
            foreach (var t in hcomp.PeekHashSet())
            {
                if (t == null || t.Destroyed || !inner.Contains(t))
                    continue;
                if (InventorySurplus.SurplusOf(pawn, t) <= 0)
                    continue;
                if (PortalRemainingFor(portal, t.def) > 0)
                    return true;
            }
            return false;
        }
    }
}
