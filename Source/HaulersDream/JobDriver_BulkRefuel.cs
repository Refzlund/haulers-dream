using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Bulk-refuel a <see cref="CompRefuelable"/> (a shuttle's chemfuel, a deep drill, a generator, …): sweep the
    /// queued nearby fuel stacks into tagged inventory, walk to the refuelable ONCE, then deposit every carried fuel
    /// stack the filter allows via vanilla <see cref="CompRefuelable.Refuel(System.Collections.Generic.List{Thing})"/>
    /// — instead of vanilla's one-stack-in-hands per walk. The same sweep→pickup→deposit shape as HD's bulk-load
    /// drivers, but standalone (no claim-ledger): a refuelable has no shared manifest, and vanilla's Refuel just
    /// consumes up to the deficit and destroys the consumed fuel.
    ///
    /// Concurrency / safety: each non-Success end re-tags any swept-but-undeposited fuel (idempotent self-heal) so it
    /// rides HD's normal unload — never stranded. Over-swept fuel (more than the deficit) likewise stays HD-tagged:
    /// vanilla Refuel consumes only the deficit, leaving the remainder in inventory, which the finish action keeps
    /// tagged. No try/catch swallowing — a real fault throws.
    /// </summary>
    public class JobDriver_BulkRefuel : JobDriver
    {
        private const TargetIndex RefuelableInd = TargetIndex.A; // the refuelable (deposit dest)
        private const TargetIndex FuelInd = TargetIndex.B;       // scratch: the ground fuel stack being swept

        private int loadIndex;

        private Thing Refuelable => job.GetTarget(RefuelableInd).Thing;
        private CompRefuelable Comp => Refuelable?.TryGetComp<CompRefuelable>();
        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();

        // Reused snapshot of the carried fuel for the deposit + the salvage finish action, replacing a fresh
        // List<Thing> per deposit/end. [ThreadStatic] per this assembly's hook-reachable-scratch convention; cleared
        // at use, never trusted empty. SAFETY: each consumer runs to completion in one toil initAction / finish action
        // (sequential on the main thread) before the next reuse.
        [System.ThreadStatic] private static List<Thing> scratchFuel;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdBrefLoadIndex", 0);
        }

        public override string GetReport() => "HaulersDream.BulkRefuel.Report".Translate();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Reserve the refuelable exclusively (the deposit target), then best-effort reserve the queued fuel
            // stacks. Mirrors the bulk-load reservation shape (queue[0] strict via ReserveAsManyAsPossible's first
            // element, the rest best-effort), but the deposit TARGET here IS reserved (a refuelable is a single
            // thing one pawn fuels, unlike a re-found transporter group).
            if (!pawn.Reserve(Refuelable, job, 1, -1, null, errorOnFailed))
                return false;
            var queue = job.GetTargetQueue(FuelInd);
            if (queue != null && queue.Count > 0)
                pawn.ReserveAsManyAsPossible(queue, job);
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(RefuelableInd);
            // Bail the whole job if the refuelable fills up or loses its comp before we deposit.
            this.FailOn(() =>
            {
                var c = Comp;
                return c == null || c.IsFull;
            });

            Toil depositStart = Toils_General.Label();

            // ============ FILL: sweep each queued ground fuel stack into tagged inventory ============
            Toil sweepDecide = ToilMaker.MakeToil("HD_Bref_SweepDecide");
            sweepDecide.initAction = delegate
            {
                var queue = job.targetQueueB;
                var counts = job.countQueue;
                if (queue == null || queue.Count == 0 || loadIndex >= queue.Count) { JumpToToil(depositStart); return; }
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
                job.SetTarget(FuelInd, queue[loadIndex].Thing);
            };
            sweepDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return sweepDecide;

            Toil sweepGoto = ToilMaker.MakeToil("HD_Bref_SweepGoto");
            sweepGoto.initAction = delegate
            {
                var t = job.GetTarget(FuelInd).Thing;
                if (t == null || !t.Spawned) { loadIndex++; JumpToToil(sweepDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            sweepGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return sweepGoto;

            Toil sweepTake = ToilMaker.MakeToil("HD_Bref_SweepTake");
            sweepTake.initAction = delegate
            {
                var t = job.GetTarget(FuelInd).Thing;
                var counts = job.countQueue;
                int planned = counts != null && loadIndex < counts.Count ? counts[loadIndex] : 0;
                if (t == null || !t.Spawned || planned <= 0 || t.IsForbidden(pawn)) { loadIndex++; JumpToToil(sweepDecide); return; }
                int count = System.Math.Min(planned, t.stackCount);
                if (count <= 0) { loadIndex++; JumpToToil(sweepDecide); return; }
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
                    // Couldn't stow it (full / mod block) — put it back on the ground and skip this stack.
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    loadIndex++;
                }
                JumpToToil(sweepDecide);
            };
            sweepTake.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return sweepTake;

            // ============ DEPOSIT: walk to the refuelable ONCE, then refuel from carried fuel ============
            yield return depositStart;

            Toil gotoTarget = Toils_Goto.GotoThing(RefuelableInd, PathEndMode.Touch);
            gotoTarget.FailOnDespawnedOrNull(RefuelableInd);
            yield return gotoTarget;

            Toil deposit = ToilMaker.MakeToil("HD_Bref_Deposit");
            deposit.initAction = delegate
            {
                var comp = Comp;
                var inner = pawn.inventory?.innerContainer;
                var hcomp = pawn.GetComp<CompHauledToInventory>();
                if (comp == null || comp.IsFull || inner == null || hcomp == null)
                    return; // nothing to do -> leftover fuel stays tagged, normal unload handles it

                var filter = comp.Props?.fuelFilter;
                if (filter == null)
                    return;

                // Gather the carried fuel Things whose def the filter accepts AND that are genuine surplus (so the
                // pawn's own kept fuel kit is never burned). HD-swept fuel is tagged, so SurplusOf returns its full
                // count; a kept personal stash returns 0 and is skipped.
                var fuelList = scratchFuel ?? (scratchFuel = new List<Thing>());
                fuelList.Clear();
                for (int i = 0; i < inner.Count; i++)
                {
                    var t = inner[i];
                    if (t == null || t.Destroyed || t.def == null)
                        continue;
                    if (!filter.Allows(t))
                        continue;
                    if (InventorySurplus.SurplusOf(pawn, t) <= 0)
                        continue;
                    fuelList.Add(t);
                }
                if (fuelList.Count == 0)
                    return; // carried nothing usable -> leftover (if any) stays tagged

                // Snapshot the fuel Things BEFORE refueling — vanilla Refuel POPs from the list it's given and
                // SplitOff(full)+Destroy()s each consumed thing (removing it from inventory), so we pass a COPY and
                // keep our own snapshot to reconcile the tag map afterwards.
                int beforeCount = fuelList.Count;
                var snapshot = new List<Thing>(fuelList);

                // Vanilla refuel: consumes up to the deficit, destroying fully-consumed fuel and reducing a
                // partially-consumed stack in place. No try/catch — a fault is a real bug to surface.
                comp.Refuel(fuelList);

                // Reconcile the tag map: any snapshot fuel now Destroyed or gone from inventory was consumed -> drop
                // its tag. A surviving (untouched or partial) stack stays tagged for the normal unload.
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var t = snapshot[i];
                    if (t == null)
                        continue;
                    if (t.Destroyed || !inner.Contains(t))
                        hcomp.Deregister(t);
                }
                fuelList.Clear();

                if (HaulersDreamMod.Settings?.verboseLogging == true)
                    HDLog.Dbg($"BulkRefuel: {pawn} deposited fuel into {Refuelable?.LabelShort} (from {beforeCount} carried stack(s), fuel now {comp.Fuel:0.#}).");
            };
            deposit.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return deposit;

            // Re-tag any swept-but-undeposited / leftover fuel on every end (idempotent self-heal) so it rides HD's
            // normal unload — never stranded.
            AddFinishAction(delegate (JobCondition condition)
            {
                var hcomp = pawn.GetComp<CompHauledToInventory>();
                var inner = pawn.inventory?.innerContainer;
                if (hcomp == null || inner == null)
                    return;
                var snapshot = scratchFuel ?? (scratchFuel = new List<Thing>());
                snapshot.Clear();
                snapshot.AddRange(hcomp.GetHashSet());
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var t = snapshot[i];
                    if (t != null && !t.Destroyed && inner.Contains(t))
                        hcomp.RegisterHauledItem(t);
                }
                snapshot.Clear();
            });
        }
    }
}
