using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// DropThenHaul mode: after a work run drops yields on the floor, the producer scoops up its own
    /// recorded fresh drops (CompHauledToInventory.pendingSelfPickups) into inventory, gated by the
    /// carry limit. Enqueued at the FRONT of the job queue, so it runs the instant the harvest/mine
    /// run ends and the remaining designated work is re-selected afterwards.
    /// </summary>
    public class JobDriver_SelfPickup : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override IEnumerable<Toil> MakeNewToils()
        {
            var comp = pawn.TryGetComp<CompHauledToInventory>();

            var loop = new Toil
            {
                initAction = () =>
                {
                    var next = comp?.TakeNextValidPending();
                    if (next == null)
                    {
                        // Scoop run complete: if the load now exceeds the pawn's carry capacity (vanilla
                        // slows it down from here), break off to unload right away instead of lugging the
                        // surplus around until the smart-overload ceiling (~2x capacity) trips. The unload
                        // is queued, so it starts the moment this job ends.
                        YieldRouter.MaybeUnloadBecauseOverEncumbered(pawn, HaulersDreamMod.Settings);
                        EndJobWith(JobCondition.Succeeded);
                        return;
                    }
                    job.SetTarget(TargetIndex.A, next);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return loop;

            var gotoThing = new Toil
            {
                initAction = () =>
                {
                    var t = job.GetTarget(TargetIndex.A).Thing;
                    if (t == null || !t.Spawned)
                    {
                        JumpToToil(loop);
                        return;
                    }
                    pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
                },
                defaultCompleteMode = ToilCompleteMode.PatherArrival
            };
            yield return gotoThing;

            var take = new Toil
            {
                initAction = () =>
                {
                    var thing = job.GetTarget(TargetIndex.A).Thing;
                    if (thing == null || !thing.Spawned || thing.IsForbidden(pawn))
                        return; // stolen / gone -> just loop to the next pending drop
                    if (thing.IsInValidStorage())
                        return; // another hauler already stored it -> done; never pull stock back OUT of a stockpile

                    var s = HaulersDreamMod.Settings;
                    int count = OverloadGate.CountToPickUp(pawn, thing, s);
                    if (count <= 0)
                    {
                        // Full. Re-queue this drop for the producer ONLY when an automatic unload can actually
                        // free space (default mode breaks off to unload now). In strict mode / with auto-unload
                        // off, NOTHING can fire to make room — re-adding would walk the pawn back to take zero,
                        // forever. Abandon the drop to normal hauling instead (it's spawned and unforbidden).
                        if (comp != null && thing != null && s != null && s.markForUnload && !s.strictCarryWeight
                            && !comp.pendingSelfPickups.Contains(thing))
                            comp.pendingSelfPickups.Add(thing);
                        YieldRouter.MaybeUnloadBecauseFull(pawn, s);
                        EndJobWith(JobCondition.Succeeded);
                        return;
                    }

                    var owner = pawn.inventory.GetDirectlyHeldThings();
                    bool fully = count >= thing.stackCount;
                    // Always SplitOff — even for a full stack. SplitOff(count>=stackCount) DESPAWNS the
                    // ground thing and clears its holdingOwner, so the following TryAddOrTransfer takes the
                    // TryAdd path. Passing the still-spawned `thing` instead would route through
                    // TryTransferToContainer, which refuses Map<->container moves ("Can't transfer to/from
                    // Maps directly") and silently moves nothing. This mirrors vanilla Toils_Haul.TakeToInventory.
                    Thing split = thing.SplitOff(count);
                    if (split == null)
                        return;

                    // Track by count delta (TryAddOrTransfer can merge part of a stack and still
                    // report false). Anything that lands in inventory is registered for unload; any
                    // un-moved remainder of a SplitOff fragment is folded BACK onto the ground thing
                    // so units are never lost (mirrors YieldRouter.RouteIntoInventory).
                    int beforeMove = split.stackCount;
                    bool allMoved = owner.TryAddOrTransfer(split, canMergeWithExistingStacks: true);

                    if (allMoved || split.stackCount < beforeMove)
                    {
                        int moved = allMoved ? beforeMove : beforeMove - split.stackCount;
                        Thing held = YieldRouter.InventoryStackOfDef(owner, split.def) ?? (allMoved ? split : null);
                        if (held != null)
                            comp?.RegisterHauledItem(held, moved);
                        comp?.NotifyYieldPicked();
                    }

                    if (!allMoved && !fully && split.stackCount > 0 && !thing.Destroyed)
                        thing.TryAbsorbStack(split, respectStackLimit: false);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return take;

            yield return Toils_Jump.Jump(loop);
        }
    }
}
