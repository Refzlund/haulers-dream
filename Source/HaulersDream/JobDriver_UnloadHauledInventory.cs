using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// The single consolidated unload pass: repeatedly take one tracked item out of inventory, find
    /// the best storage for it, carry it there and place it. Mirrors the canonical RimWorld unload
    /// toil chain (find → reserve → pull → carry to cell/container → place → repeat).
    /// </summary>
    public class JobDriver_UnloadHauledInventory : JobDriver
    {
        private int countToDrop = -1;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref countToDrop, "countToDrop", -1);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override IEnumerable<Toil> MakeNewToils()
        {
            var begin = Toils_General.Wait(3);
            yield return begin;

            var carried = pawn.TryGetComp<CompHauledToInventory>()?.GetHashSet() ?? new HashSet<Thing>();
            yield return FindTargetOrDrop(carried);
            yield return PullItemFromInventory(carried, begin);

            var releaseReservation = ReleaseReservation();
            var carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);

            // if (TargetB is a cell) jump straight to the cell branch
            yield return Toils_Jump.JumpIf(carryToCell, () => !TargetB.HasThing);

            // ---- container branch ----
            var carryToContainer = Toils_Haul.CarryHauledThingToContainer();
            yield return carryToContainer;
            yield return Toils_Haul.DepositHauledThingInContainer(TargetIndex.B, TargetIndex.None);
            yield return Toils_Haul.JumpToCarryToNextContainerIfPossible(carryToContainer, TargetIndex.B);
            yield return Toils_Jump.Jump(releaseReservation);

            // ---- cell branch ----
            yield return carryToCell;
            yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, carryToCell, true);

            yield return releaseReservation;
            yield return Toils_Jump.Jump(begin); // loop to next tracked item
        }

        private Toil ReleaseReservation()
        {
            return new Toil
            {
                initAction = () =>
                {
                    if (pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, pawn.CurJob))
                        pawn.Map.reservationManager.Release(job.targetB, pawn, pawn.CurJob);
                }
            };
        }

        private Toil PullItemFromInventory(HashSet<Thing> carried, Toil wait)
        {
            return new Toil
            {
                initAction = () =>
                {
                    var thing = job.GetTarget(TargetIndex.A).Thing;
                    if (thing == null || !pawn.inventory.innerContainer.Contains(thing))
                    {
                        carried.Remove(thing);
                        pawn.jobs.curDriver.JumpToToil(wait);
                        return;
                    }

                    if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || !thing.def.EverStorable(false))
                    {
                        // Hold the ORIGINAL tracked reference (TryDrop reassigns `thing`; the out param can be a
                        // different object, merged into a ground stack) and untag only when the drop actually
                        // happened — a failed drop leaves the item in inventory, where a missing tag would
                        // strand it untracked (gizmo hidden, never retried).
                        var original = thing;
                        if (pawn.inventory.innerContainer.TryDrop(thing, ThingPlaceMode.Near, countToDrop, out thing))
                            carried.Remove(original);
                        EndJobWith(JobCondition.Succeeded);
                        return;
                    }

                    pawn.inventory.innerContainer.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, countToDrop, out thing);
                    if (thing == null)
                    {
                        // Nothing moved (e.g. the carry tracker still holds a remainder from a partial container
                        // deposit) — end cleanly rather than NRE; the tag stays, so the item is retried next pass.
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    job.count = countToDrop;
                    job.SetTarget(TargetIndex.A, thing);
                    carried.Remove(thing);
                    thing.SetForbidden(false, false);
                }
            };
        }

        private Toil FindTargetOrDrop(HashSet<Thing> carried)
        {
            return new Toil
            {
                initAction = () =>
                {
                    var next = FirstUnloadableThing(carried);
                    if (next.Count == 0)
                    {
                        // No unloadable stack right now. If tagged stock remains it is reserved by
                        // another pawn (a worker fetching from this inventory) — end instead of
                        // spinning in place; the unload checker re-queues once the reservation clears.
                        EndJobWith(carried.Count == 0 ? JobCondition.Succeeded : JobCondition.Incompletable);
                        return;
                    }

                    if (StoreUtility.TryFindBestBetterStorageFor(next.Thing, pawn, pawn.Map, StoragePriority.Unstored,
                            pawn.Faction, out var cell, out var destination))
                    {
                        job.SetTarget(TargetIndex.A, next.Thing);
                        if (cell == IntVec3.Invalid)
                            job.SetTarget(TargetIndex.B, destination as Thing);
                        else
                            job.SetTarget(TargetIndex.B, cell);

                        // Haul-to-stack: storage CELLS are deliberately not reserved (multiple pawns may
                        // deliver to — and stack onto — the same tile; see HaulToStack). Containers keep
                        // their reservation: their capacity coordination is the enroute/reservation system.
                        bool reserveDest = cell == IntVec3.Invalid
                                           || HaulersDreamMod.Settings == null || !HaulersDreamMod.Settings.haulToStack;
                        if (reserveDest && !pawn.Map.reservationManager.Reserve(pawn, job, job.targetB))
                        {
                            // Untag only when the drop actually happened — a failed drop leaves the thing in
                            // inventory, where a missing tag would strand it untracked (gizmo hidden, never retried).
                            if (pawn.inventory.innerContainer.TryDrop(next.Thing, ThingPlaceMode.Near, next.Thing.stackCount, out _))
                                carried.Remove(next.Thing);
                            EndJobWith(JobCondition.Incompletable);
                            return;
                        }
                        countToDrop = next.Thing.stackCount;
                    }
                    else
                    {
                        // Nowhere better -> drop it here and call it done for this item. Untag only when the
                        // drop actually happened; a failed drop keeps the tag with the item still in inventory.
                        if (pawn.inventory.innerContainer.TryDrop(next.Thing, ThingPlaceMode.Near, next.Thing.stackCount, out _))
                            carried.Remove(next.Thing);
                        EndJobWith(JobCondition.Succeeded);
                    }
                }
            };
        }

        private ThingCount FirstUnloadableThing(HashSet<Thing> carried)
        {
            var inner = pawn.inventory.innerContainer;

            foreach (var thing in carried.OrderBy(t => t.def.FirstThingCategory?.index).ThenBy(t => t.def.defName))
            {
                if (!inner.Contains(thing))
                {
                    // A partially-picked-up stack merged in inventory gets a new ThingID; relink to it.
                    var def = thing.def;
                    carried.Remove(thing);
                    for (var i = 0; i < inner.Count; i++)
                    {
                        if (inner[i].def == def)
                            return new ThingCount(inner[i], inner[i].stackCount);
                    }
                    continue;
                }
                // Another pawn may hold a reservation on this stack (a bill worker fetching ingredients
                // out of this very inventory) — unloading it now would move its target out from under it.
                // CanReserve is false exactly when someone else holds the reservation; skip those.
                if (!pawn.CanReserve(thing))
                    continue;
                return new ThingCount(thing, thing.stackCount);
            }
            return default;
        }
    }
}
