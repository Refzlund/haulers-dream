using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Bulk-load a Vehicle Framework vehicle (a <c>VehiclePawn</c>, reached reflection-only via
    /// <see cref="VehicleFrameworkCompat"/> — held as a plain <see cref="Thing"/>, NO <c>Vehicles.*</c> type here) —
    /// the single-target VF counterpart to <see cref="JobDriver_LoadPortalInBulk"/>. Same three-phase shape (sweep
    /// nearby ground stacks into tagged inventory → walk to the vehicle ONCE → deposit every tagged stack the vehicle
    /// still needs), but the DEPOSIT goes through VF's own event-correct path rather than a raw container transfer:
    ///
    /// The vehicle's deposit container is <c>((Pawn)vehicle).inventory.innerContainer</c>, but there is NO
    /// <c>SubtractFromToLoadList</c> hook to intercept — the manifest decrement (and the <c>CargoAdded</c> event) is
    /// baked into <c>VehiclePawn.AddOrTransfer(thing, count)</c>. So the deposit toil splits the surplus off the
    /// inventory and hands it to <see cref="VehicleFrameworkCompat.AddOrTransfer"/> — clamped to
    /// <see cref="VehicleFrameworkCompat.RemainingDemandForThing"/> (the SINGLE matching transferable's count — MF1:
    /// over-clamping to a def-sum would drive a matching <c>cargoToLoad</c> entry negative→removed and over-load the
    /// def). The shim decrements <c>cargoToLoad</c> by the PASSED count, so the clamp lines up exactly. On a
    /// <c>moved &lt;= 0</c> / <c>-1</c> sentinel (VF absent mid-trip / member unbound) the split is put back into the
    /// inventory and re-tagged for HD's normal unload — never dropped. The <c>(def, moved)</c> pair is captured BEFORE
    /// the transfer for the thing-less <see cref="HaulersDreamGameComponent.LoadNotifyDeposited(Pawn,
    /// IManagedLoadable, ThingDef, int)"/> settle (the split may have been merged/destroyed inside the vehicle so
    /// reading the moved count off it afterward is unsafe).
    ///
    /// DIVERGENCES vs the portal/transporter drivers (addendum MF1/MF2): (a) deposit via
    /// <c>VehicleFrameworkCompat.AddOrTransfer</c> instead of <c>TryTransferToContainer</c>; (b) NO
    /// <c>SubtractFromToLoadList</c> intercept path (AddOrTransfer IS the precise decrement) — the
    /// <see cref="Global.IsExecutingManagedUnload"/> flag is still toggled around the transfer for other-patch
    /// hygiene only, never load-bearing; (c) OMIT the group-redirect (a vehicle is single-target with no Group — the
    /// per-stack <c>RemainingDemandForThing</c> re-check in the deposit loop already leaves fully-satisfied stock
    /// tagged for the normal unload); (d) <c>FailOnDespawnedOrNull</c> on the vehicle so a despawn / aerial-launch
    /// mid-load ends the job → the finish action releases the VRM claim + ledger claim + re-tags swept survivors.
    ///
    /// Concurrency: the CLAIM (ledger + belt-and-suspenders VF <c>VehicleReservationManager</c>) is recorded in
    /// <see cref="Notify_Starting"/> (so a built-but-never-started probe never claims); on every non-Success end the
    /// claim is RELEASED and the carried task item is SALVAGED back into inventory (re-tagged, rides HD's normal
    /// unload) — never dropped on a temp map, never stuck.
    /// </summary>
    public class JobDriver_LoadVehicleInBulk : JobDriver
    {
        private const TargetIndex VehicleInd = TargetIndex.A; // the vehicle (deposit dest)
        private const TargetIndex StackInd = TargetIndex.B;   // scratch: the ground stack being swept

        private int loadIndex;
        private int depositLoops;
        private int passes;
        private const int MaxDepositLoops = 64;
        private const int MaxPasses = 64;

        // Resolved on start (Notify_Starting). In-flight only — re-resolved from the live vehicle on load.
        [System.NonSerialized] private VehicleLoadTarget adapter;
        // Set true on a chaining/cleanup end so the finish action RETAINS the claim (no thrash). Currently always
        // false (no chaining), but kept as the documented retain hook for a future smooth-chain.
#pragma warning disable CS0649
        [System.NonSerialized] private bool retainClaimOnEnd;
#pragma warning restore CS0649

        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();
        private Thing Vehicle => job.GetTarget(VehicleInd).Thing;

        private static HaulersDreamSettings Settings => HaulersDreamMod.Settings;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdLvibLoadIndex", 0);
            Scribe_Values.Look(ref depositLoops, "hdLvibDepositLoops", 0);
            Scribe_Values.Look(ref passes, "hdLvibPasses", 0);
        }

        public override string GetReport() => "HaulersDream.LoadVehicle.Report".Translate();

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            var vehicle = Vehicle;
            adapter = vehicle != null ? VehicleLoadTarget.TryCreate(vehicle) : null;
            if (adapter != null)
            {
                HaulersDreamGameComponent.Instance?.LoadClaim(pawn, job, adapter);
                // Belt-and-suspenders VF claim. The WorkGiver JobOnThing redirect is the authoritative stand-down
                // (it replaces VF's single-stack job with this bulk job for every HD-eligible scanning pawn); this VRM
                // claim only covers the fail-open / non-HD-eligible case. Fail-open in the shim.
                VehicleFrameworkCompat.ReserveVehicle(vehicle, pawn, job);
            }
        }

        private VehicleLoadTarget EnsureAdapter()
        {
            if (adapter != null)
                return adapter;
            var vehicle = Vehicle;
            adapter = vehicle != null ? VehicleLoadTarget.TryCreate(vehicle) : null;
            return adapter;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Bulk-haul reservation shape: queue[0] strict, the rest best-effort. NEVER reserve the vehicle
            // (re-found each deposit; the VRM claim covers it). A deposit-only job (empty queue) reserves nothing.
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
            // A despawn / aerial-launch mid-load ends the job → the salvage finish action re-tags the carried stock so
            // it rides the normal unload (never dropped on a temp map). At natural completion the loopCheck toil ends
            // the job Succeeded within the same tick (global fail conditions are evaluated once at the top of
            // DriverTick, BEFORE the instant deposit→loopCheck chain drains the manifest), so this does not pre-empt a
            // clean finish.
            this.FailOnDespawnedOrNull(VehicleInd);

            Toil fillStart = Toils_General.Label();
            Toil depositStart = Toils_General.Label();
            Toil loopCheck = ToilMaker.MakeToil("HD_Lvib_LoopCheck");

            // ============ FILL: sweep queued ground stacks into tagged inventory, up to the carry ceiling ============
            yield return fillStart;

            Toil sweepDecide = ToilMaker.MakeToil("HD_Lvib_SweepDecide");
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

            Toil sweepGoto = ToilMaker.MakeToil("HD_Lvib_SweepGoto");
            sweepGoto.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                if (t == null || !t.Spawned) { loadIndex++; JumpToToil(sweepDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            sweepGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return sweepGoto;

            Toil sweepTake = ToilMaker.MakeToil("HD_Lvib_SweepTake");
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

            // ============ DEPOSIT: walk to the vehicle ONCE, then transfer every needed tagged stack ============
            yield return depositStart;

            Toil findVehicle = ToilMaker.MakeToil("HD_Lvib_FindVehicle");
            findVehicle.initAction = delegate
            {
                if (++depositLoops > MaxDepositLoops) { JumpToToil(loopCheck); return; }
                var vehicle = Vehicle;
                if (vehicle == null || !vehicle.Spawned) { JumpToToil(loopCheck); return; }
                if (!HasDepositableForVehicle()) { JumpToToil(loopCheck); return; }
            };
            findVehicle.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findVehicle;

            Toil gotoVehicle = Toils_Goto.GotoThing(VehicleInd, PathEndMode.Touch);
            gotoVehicle.FailOnDespawnedOrNull(VehicleInd);
            yield return gotoVehicle;

            Toil deposit = ToilMaker.MakeToil("HD_Lvib_Deposit");
            deposit.initAction = delegate
            {
                var vehicle = Vehicle;
                var inner = pawn.inventory?.innerContainer;
                var hcomp = pawn.GetComp<CompHauledToInventory>();
                var adp = EnsureAdapter();
                if (vehicle == null || !vehicle.Spawned || inner == null || hcomp == null || adp == null)
                { JumpToToil(loopCheck); return; }

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
                    // MF1 clamp: the SINGLE matching transferable's remaining demand (NOT a def-sum) — exactly what
                    // VehiclePawn.AddOrTransfer decrements. Over-clamping to a def-sum would drive a matching
                    // cargoToLoad entry negative→removed and over-load the def.
                    int remaining = VehicleFrameworkCompat.RemainingDemandForThing(vehicle, thing);
                    int count = VehicleLoadPlanPolicy.DepositUnits(surplus, remaining);
                    if (count <= 0)
                        continue; // vehicle no longer wants this def (filled by another pawn) — leave it tagged

                    // Split the exact count off the inventory; AddOrTransfer moves the SPLIT (an event-correct deposit
                    // that fires CargoAdded + decrements cargoToLoad by the passed count). Capture (def, count) BEFORE
                    // the transfer — the split may merge/destroy inside the vehicle, so reading the moved count off it
                    // after is unsafe; the thing-less LoadNotifyDeposited overload uses the captured pair. (ThingOwner.
                    // Take returns the SAME Thing when count==stackCount, or a fresh SplitOff otherwise.)
                    var split = inner.Take(thing, count);
                    if (split == null)
                        continue;
                    var depDef = split.def;

                    int moved;
                    // The IsExecutingManagedUnload flag is toggled around the transfer for OTHER-patch hygiene only —
                    // there is no SubtractFromToLoadList intercept on the vehicle path (AddOrTransfer IS the precise
                    // decrement), so this is not load-bearing. try/finally resets it even on throw; the throw RETHROWS.
                    Global.IsExecutingManagedUnload = true;
                    try
                    {
                        moved = VehicleFrameworkCompat.AddOrTransfer(vehicle, split, count);
                    }
                    finally
                    {
                        Global.IsExecutingManagedUnload = false;
                    }

                    if (moved > 0)
                    {
                        movedAny = true;
                        // Thing-less settle with the captured (def, moved) — the split may be gone inside the vehicle.
                        HaulersDreamGameComponent.Instance?.LoadNotifyDeposited(pawn, adp, depDef, moved);
                    }
                    // Put any un-MOVED remainder of the SPLIT back into the inventory (merging with the source stack)
                    // and re-tag it so it rides HD's normal unload — never dropped. On a clean full move VF owns/
                    // destroys the split (ParentHolder != null / Destroyed), so this is a no-op. The moved<=0/-1
                    // sentinel path (VF absent mid-trip / member unbound / nothing accepted) lands here too.
                    if (!split.Destroyed && !split.Spawned && split.ParentHolder == null)
                    {
                        if (inner.TryAdd(split, canMergeWithExistingStacks: true))
                            hcomp.RegisterHauledItem(split);
                        else
                            GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    }
                    // Drop the now-stale tag only when NEITHER the original stack NOR the split remains in the
                    // inventory (the original was fully consumed by the Take and the split was fully deposited);
                    // a remainder merged back leaves it tagged. Mirrors JobDriver_LoadPackAnimal's deposit dereg.
                    if (!inner.Contains(thing) && !inner.Contains(split))
                        hcomp.Deregister(thing);
                }
                if (!movedAny) { JumpToToil(loopCheck); return; }
                JumpToToil(findVehicle); // more to deposit or fall to loopCheck (drained)
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

            // Release the claim (ledger + VF VRM) + salvage any still-carried task items on every non-Success end
            // (idempotent). A finish-action fault is a real bug to surface (no swallow); the VRM release self-heals
            // via VF's VerifyAndValidateClaimants even if it somehow no-ops.
            AddFinishAction(delegate (JobCondition condition)
            {
                if (!retainClaimOnEnd)
                {
                    HaulersDreamGameComponent.Instance?.LoadReleaseClaimsForPawn(pawn);
                    VehicleFrameworkCompat.ReleaseVehicleClaims(pawn);
                }
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

        /// <summary>Units of <paramref name="def"/> the vehicle's manifest still wants — the Σ CountToTransfer across
        /// its <c>cargoToLoad</c> entries for that def (a quick "anything left for this def?" probe; the actual deposit
        /// clamps per-stack via <see cref="VehicleFrameworkCompat.RemainingDemandForThing"/> on the exact
        /// transferable identity).</summary>
        private static int VehicleRemainingForDef(Thing vehicle, ThingDef def)
        {
            if (def == null)
                return 0;
            var ltl = VehicleFrameworkCompat.CargoToLoad(vehicle);
            if (ltl == null)
                return 0;
            int sum = 0;
            for (int i = 0; i < ltl.Count; i++)
                if (ltl[i] is TransferableOneWay tr && tr.ThingDef == def && tr.CountToTransfer > 0)
                    sum += tr.CountToTransfer;
            return sum;
        }

        /// <summary>True if the pawn holds any tagged surplus stack of a def the vehicle still wants.</summary>
        private bool HasDepositableForVehicle()
        {
            var vehicle = Vehicle;
            var hcomp = pawn.GetComp<CompHauledToInventory>();
            var inner = pawn.inventory?.innerContainer;
            if (vehicle == null || hcomp == null || inner == null)
                return false;
            foreach (var t in hcomp.PeekHashSet())
            {
                if (t == null || t.Destroyed || !inner.Contains(t))
                    continue;
                if (InventorySurplus.SurplusOf(pawn, t) <= 0)
                    continue;
                if (VehicleRemainingForDef(vehicle, t.def) > 0)
                    return true;
            }
            return false;
        }
    }
}
