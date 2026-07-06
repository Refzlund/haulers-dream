using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Loads a pawn's scooped/tagged inventory loot onto a PACK ANIMAL — the away-map counterpart to
    /// <see cref="JobDriver_UnloadHauledInventory"/>. Two phases:
    ///   • SWEEP (optional; the manual bulk order fills targetQueueB): pull nearby ground stacks into INVENTORY,
    ///     tagged in CompHauledToInventory exactly like <see cref="JobDriver_BulkHaul"/>. Empty queue (auto-divert
    ///     / gizmo) skips straight to deposit.
    ///   • DEPOSIT: walk to the carrier (TargetA) and transfer the tagged SURPLUS (never the pawn's personal kit)
    ///     into the carrier's inventory, re-finding the best carrier when one fills.
    ///
    /// Safety by construction: the sweep only SplitOff+TryAdd+tags (with a place-back fallback), and the deposit
    /// only TryTransferToContainer + Deregister-on-success — so whatever is NOT deposited stays tagged in
    /// inventory and rides home as caravan inventory. Nothing is ever dropped on a temporary map's ground.
    /// </summary>
    public class JobDriver_LoadPackAnimal : JobDriver
    {
        private const TargetIndex CarrierInd = TargetIndex.A; // the pack animal (deposit destination)
        private const TargetIndex StackInd = TargetIndex.B;   // scratch: the ground stack currently being swept

        private int loadIndex;
        private int depositLoops;
        private int passes;
        private const int MaxDepositLoops = 64; // backstop: bounds the re-find-carrier cycle (all animals full)
        private const int MaxPasses = 64;       // backstop: bounds the fill->deposit->refill loop

        // Reused snapshot of the tagged set for the deposit loop, replacing a fresh List<Thing>(comp.GetHashSet())
        // per deposit cycle. The snapshot is required (GetHashSet self-heals and the loop calls Deregister, mutating
        // the underlying set mid-iterate); reusing one [ThreadStatic] buffer makes the steady per-deposit alloc 0.
        // Cleared at use, never trusted empty. SAFETY: consumed within one deposit initAction (sequential on the main
        // thread, no re-entrant tagged-snapshot) before the next reuse.
        [System.ThreadStatic] private static List<Thing> scratchTagged;

        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();
        private Pawn Carrier => job.GetTarget(CarrierInd).Thing as Pawn;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdLpaLoadIndex", 0);
            Scribe_Values.Look(ref depositLoops, "hdLpaDepositLoops", 0);
            Scribe_Values.Look(ref passes, "hdLpaPasses", 0);
        }

        public override string GetReport() => "HaulersDream.LoadPackAnimal.Report".Translate();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // The carrier is re-found each deposit loop (like vanilla GiveToPackAnimal — robust to the animal
            // wandering), so it is never reserved. Sweep stacks reserve like bulk-haul: queue[0] strict, the rest
            // best-effort. A deposit-only job (auto-divert / gizmo) has an empty queue and reserves nothing.
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
            Toil fillStart = Toils_General.Label();
            Toil depositStart = Toils_General.Label();
            // Created up front so the deposit phase can jump here; configured + yielded at the very end.
            Toil loopCheck = ToilMaker.MakeToil("HD_Lpa_LoopCheck");

            // ============ FILL: pull queued ground stacks into inventory, up to the carry ceiling ============
            yield return fillStart;

            Toil sweepDecide = ToilMaker.MakeToil("HD_Lpa_SweepDecide");
            sweepDecide.initAction = delegate
            {
                var queue = job.targetQueueB;
                var counts = job.countQueue;
                if (queue == null || queue.Count == 0 || loadIndex >= queue.Count) { JumpToToil(depositStart); return; }
                float ceiling = PackAnimalLoad.CeilingKg(pawn, HaulersDreamMod.Settings);
                bool roomLeft = float.IsPositiveInfinity(ceiling)
                                || MassUtility.GearAndInventoryMass(pawn) < ceiling - 0.0001f;
                if (roomLeft && CECompat.IsActive && CECompat.AvailableBulk(pawn) <= 0f)
                    roomLeft = false;
                if (!roomLeft) { JumpToToil(depositStart); return; } // inventory full -> deposit, then refill (loopCheck)
                while (loadIndex < queue.Count)
                {
                    var t = queue[loadIndex].Thing;
                    bool valid = t != null && t.Spawned && !t.IsForbidden(pawn)
                                 && !(t.ParentHolder is Pawn_InventoryTracker)
                                 && counts != null && loadIndex < counts.Count && counts[loadIndex] > 0;
                    // Re-reserve at the walk (start-time best-effort reserve may have failed then cleared); skip
                    // a stack another pawn now holds rather than stealing it (this order is not playerForced-steal).
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

            Toil sweepGoto = ToilMaker.MakeToil("HD_Lpa_SweepGoto");
            sweepGoto.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                if (t == null || !t.Spawned) { loadIndex++; JumpToToil(sweepDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            sweepGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return sweepGoto;

            // Vanilla-like pickup pause (#121): the FILL sweep pockets ground stacks exactly like the bulk-haul
            // sweep, so it paces identically. The DEPOSIT phase (inventory -> carrier) is untouched. No fail
            // conditions: a swept stack gone mid-pause is skipped by the take's re-validation.
            yield return PickupPause.MakeToil(StackInd);

            Toil sweepTake = ToilMaker.MakeToil("HD_Lpa_SweepTake");
            sweepTake.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                var counts = job.countQueue;
                int planned = counts != null && loadIndex < counts.Count ? counts[loadIndex] : 0;
                if (t == null || !t.Spawned || planned <= 0 || t.IsForbidden(pawn)) { loadIndex++; JumpToToil(sweepDecide); return; }
                int count = BulkHaulPolicy.CountWithinCeiling(PackAnimalLoad.CeilingKg(pawn, HaulersDreamMod.Settings),
                    MassUtility.GearAndInventoryMass(pawn), t.GetStatValue(StatDefOf.Mass),
                    System.Math.Min(planned, t.stackCount));
                count = System.Math.Min(count, CECompat.MaxFitCount(pawn, t));
                if (count <= 0) { JumpToToil(depositStart); return; } // no room for even one -> deposit, then refill
                int groundBefore = t.stackCount;
                var split = t.SplitOff(count);
                var inv = Inv;
                if (inv != null && inv.TryAdd(split, canMergeWithExistingStacks: false))
                {
                    var comp = pawn.GetComp<CompHauledToInventory>();
                    if (comp != null) { comp.RegisterHauledItem(split); comp.NotifyYieldPicked(); }
                    if (!split.Spawned) split.Position = pawn.Position; // unspawned splits carry (0,0,0); stamp the pawn cell
                    if (counts != null && loadIndex < counts.Count) counts[loadIndex] = planned - count;
                    // Advance only when this item's order is met OR its ground stack is exhausted; a ceiling-capped
                    // remainder is taken on the next pass (after deposit frees inventory room — see loopCheck).
                    bool itemDone = counts == null || loadIndex >= counts.Count || counts[loadIndex] <= 0 || count >= groundBefore;
                    if (itemDone) loadIndex++;
                }
                else if (split != null && !split.Destroyed && !split.Spawned)
                {
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near); // never let an item vanish
                    loadIndex++;
                }
                JumpToToil(sweepDecide);
            };
            sweepTake.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return sweepTake;

            // ============ DEPOSIT: empty the tagged surplus onto carriers ============
            yield return depositStart;

            Toil findCarrier = ToilMaker.MakeToil("HD_Lpa_FindCarrier");
            findCarrier.initAction = delegate
            {
                if (!PackAnimalLoad.HasDepositableSurplus(pawn)) { JumpToToil(loopCheck); return; } // inventory drained -> maybe refill
                if (++depositLoops > MaxDepositLoops) { EndJobWith(JobCondition.Incompletable); return; }       // cycle backstop
                // Always target the animal with the MOST free space: if it can't fit the smallest remaining
                // stack, no animal can, and the deposit toil ends the job (the rest stays tagged and rides home).
                // Route through PackAnimalLoad.FindCarrier (not the raw util) so the in-flight re-resolution honors
                // the same VF-support-OFF vehicle skip as job selection — otherwise a master-OFF deposit loop could
                // re-target a VF vehicle here and raw-transfer into its uncapped cargo.
                var carrier = PackAnimalLoad.FindCarrier(pawn);
                if (carrier == null || MassUtility.FreeSpace(carrier) <= 0f) { EndJobWith(JobCondition.Succeeded); return; }
                job.SetTarget(CarrierInd, carrier);
            };
            findCarrier.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findCarrier;

            Toil gotoCarrier = Toils_Goto.GotoThing(CarrierInd, PathEndMode.Touch);
            gotoCarrier.FailOnDespawnedOrNull(CarrierInd);
            gotoCarrier.JumpIf(() => { var c = Carrier; return c == null || c.Dead || MassUtility.FreeSpace(c) <= 0f; }, findCarrier);
            yield return gotoCarrier;

            Toil deposit = ToilMaker.MakeToil("HD_Lpa_Deposit");
            deposit.initAction = delegate
            {
                var carrier = Carrier;
                var inner = pawn.inventory?.innerContainer;
                var comp = pawn.GetComp<CompHauledToInventory>();
                if (carrier == null || !carrier.Spawned || carrier.Dead) { JumpToToil(findCarrier); return; }
                if (comp == null || inner == null) { EndJobWith(JobCondition.Succeeded); return; }

                var carrierInv = carrier.inventory.innerContainer;
                bool movedAny = false;
                // Snapshot the tagged set (GetHashSet self-heals/mutates) before transferring out of it. Reused
                // [ThreadStatic] scratch — Cleared at use, never trusted empty.
                var tagged = scratchTagged ?? (scratchTagged = new List<Thing>());
                tagged.Clear();
                tagged.AddRange(comp.GetHashSet());
                // MP determinism: process tagged stacks in thingIDNumber order so a capacity-bound loop deposits/drops the same subset on every client.
                tagged.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
                for (int i = 0; i < tagged.Count; i++)
                {
                    var thing = tagged[i];
                    if (thing == null || thing.Destroyed || !inner.Contains(thing))
                        continue;
                    // Another pawn may hold a reservation on this stack (a bill worker fetching ingredients out of
                    // this very inventory via HD's shared-inventory path) — don't move it out from under them.
                    // Mirrors JobDriver_UnloadHauledInventory.FirstUnloadableThing's CanReserve skip.
                    if (!pawn.CanReserve(thing))
                        continue;
                    int surplus = InventorySurplus.SurplusOf(pawn, thing);
                    if (surplus <= 0)
                        continue; // personal kit stays with the pawn
                    int count = PackAnimalLoadPolicy.DepositCountWithinFreeSpace(
                        MassUtility.FreeSpace(carrier), thing.GetStatValue(StatDefOf.Mass), surplus);
                    if (count <= 0)
                        continue; // this stack won't fit the room left — a lighter one still might
                    // [SF4] If the carrier is a VF VehiclePawn, deposit through VF's event-correct AddOrTransfer (fires
                    // CargoAdded + decrements the matching cargoToLoad manifest entry) instead of a raw container move.
                    // Feature gate: master enableVehicleFramework (when OFF the raw deposit below still works via VF's
                    // Pawn polymorphism — only the manifest stays cosmetically stale). NOTE (SF4): count here is
                    // mass/free-space-clamped, NOT demand-clamped, so AddOrTransfer may drive a matching cargoToLoad
                    // entry negative→removed — the INTENDED auto-pack behavior (distinct from MF1's over-load).
                    int moved;
                    if (HaulersDreamMod.Settings != null && HaulersDreamMod.Settings.enableVehicleFramework
                        && VehicleFrameworkCompat.IsVehicle(carrier))
                    {
                        var split = inner.Take(thing, count);
                        moved = VehicleFrameworkCompat.AddOrTransfer(carrier, split, count);
                        if (moved <= 0)
                        {
                            // VF absent/unbound (-1) or AddOrTransfer moved nothing: `split` is already DETACHED from
                            // `inner`, so deposit it STRAIGHT into the carrier (raw) — do NOT put it back and re-read a
                            // handle. A put-back with merge can destroy the handle (when count==stackCount, Take returns
                            // split===thing, which then merges into a same-def stack and is Destroyed), leaving the raw
                            // transfer to operate on a dead Thing. If the carrier rejects it (in practice never — vehicle
                            // cargo is uncapped), return it to the hauler, else drop it nearby so an item never vanishes.
                            int want = split.stackCount;
                            if (carrierInv.TryAdd(split, canMergeWithExistingStacks: true))
                                moved = want;
                            else if (!inner.TryAdd(split, canMergeWithExistingStacks: true))
                                GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                        }
                    }
                    else
                    {
                        moved = inner.TryTransferToContainer(thing, carrierInv, count, out Thing _);
                    }
                    if (moved > 0)
                    {
                        movedAny = true;
                        if (!inner.Contains(thing))
                            comp.Deregister(thing); // fully moved -> drop the tag; a partial leaves the remainder tagged
                    }
                }
                // No progress on the most-free animal => nothing fits anywhere; leave the rest tagged (rides home).
                if (!movedAny) { EndJobWith(JobCondition.Incompletable); return; }
                JumpToToil(findCarrier); // more to deposit (this/another carrier) or back to loopCheck (drained)
            };
            deposit.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return deposit;

            // ============ LOOP: deposit freed inventory room; refill if queued items remain ============
            loopCheck.initAction = delegate
            {
                if (++passes > MaxPasses) { EndJobWith(JobCondition.Incompletable); return; } // cross-pass backstop
                depositLoops = 0; // each fill pass gets a fresh carrier-refind budget (passes is the cross-pass bound)
                var queue = job.targetQueueB;
                if (queue != null && loadIndex < queue.Count) { JumpToToil(fillStart); return; } // more to load (room now free)
                EndJobWith(JobCondition.Succeeded);
            };
            loopCheck.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loopCheck;
        }
    }
}
