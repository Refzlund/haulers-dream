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

        // The loop toil, kept so a pathing failure can jump back to it instead of ending the whole job (see
        // Notify_PatherFailed below).
        private Toil loop;

        /// <summary>
        /// A mid-walk pathing failure to ONE pending drop must not cost the pawn the WHOLE scoop run (issue
        /// #160): this job pops through a queue accumulated over an entire work run, and by the time an older
        /// entry is walked to, another pawn or a freshly-dropped stack in the same busy work area can have
        /// transiently blocked its only approach a plain reachability check at pop time can't foresee (the
        /// TakeNextValidPending reachability gate only rules out a target that's ALREADY unreachable when
        /// queued; a race with several other self-picking colonists in the same field is a live, moving
        /// target). The vanilla default (JobDriver.Notify_PatherFailed) ends the job as ErroredPather, and
        /// Pawn_JobTracker.EndCurrentJob's response to that condition is a hardcoded, uninterruptible 250-tick
        /// JobDefOf.Wait (decompile-verified) that a freshly queued job can't preempt: exactly the reported
        /// "colonists standing 'Wait' for ~10 seconds", compounding once per stuck target and once per pawn.
        /// The failed target was already popped out of pendingSelfPickups (TakeNextValidPending), so it's
        /// already left for normal hauling; jumping back to the loop just tries the next one, mirroring how
        /// the loop toil already skips a target that despawned mid-walk.
        /// </summary>
        public override void Notify_PatherFailed()
        {
            JumpToToil(loop);
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            var comp = pawn.TryGetComp<CompHauledToInventory>();

            loop = new Toil
            {
                initAction = () =>
                {
                    var next = comp?.TakeNextValidPending();
                    if (next == null)
                    {
                        // Scoop run complete. KEEP the accumulated load — the pawn overloads up to the
                        // smart-overload ceiling (where MaybeUnloadBecauseFull fires) and otherwise unloads
                        // only when it's been done with this kind of work for a while (the settle period) or
                        // on the interval / idle backstop. We deliberately do NOT unload just for being past
                        // 100% here: overloading past capacity to make fewer trips is the whole point.
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

            // Vanilla-like pickup pause (#121): scooping an own-work drop is still a pickup into inventory, so
            // it pays the same per-stack wait (with progress bar) as the bulk-haul sweep. No fail conditions:
            // a drop stolen or stored mid-pause just no-ops in the take below and the loop moves on. (Only the
            // DropThenHaul route comes through here; the DirectToInventory route pockets yields inside the
            // GenPlace prefix, where no pawn action exists to pace; see YieldRouter.)
            //
            // Pace an ISOLATED harvest collected on the spot by the direct-harvest opt-in instead of the hauling
            // one — but decide that at RUN time from how many drops this job will actually sweep, not just the
            // create-time flag. DirectHarvest applies ONLY to a LONE isolated harvest: exactly one pending drop
            // AND the job was flagged a direct harvest. A clustered field's SECTION (or an isolated collect that
            // also sweeps a leftover pile / swept-nearby clutter) has >1 pending, so it keeps the ordinary AutoHaul
            // pace — a section sweep must follow the hauling toggle, not the direct-harvest one. The flag alone is
            // frozen by whichever drop CREATED the job (a grow-zone section's job is created by its isolated first
            // plant), so the Count == 1 guard is what keeps a batched section on AutoHaul.
            var pickupContext = comp != null && comp.selfPickupDirectHarvest && comp.pendingSelfPickups.Count == 1
                ? PickupDelayContext.DirectHarvest
                : PickupDelayContext.AutoHaul;
            yield return PickupPause.MakeToil(TargetIndex.A, pickupContext);

            var take = new Toil
            {
                initAction = () =>
                {
                    var thing = job.GetTarget(TargetIndex.A).Thing;
                    if (thing == null || !thing.Spawned || thing.IsForbidden(pawn))
                        return; // stolen / gone -> just loop to the next pending drop
                    if (thing.IsInValidStorage())
                        return; // another hauler already stored it -> done; never pull stock back OUT of a stockpile
                    if (!YieldRouter.HasScoopDestination(pawn, thing))
                        return; // destination vanished since we queued it -> leave on the ground, don't scoop-then-random-drop

                    var s = HaulersDreamMod.Settings;
                    int count = OverloadGate.CountToPickUp(pawn, thing, s);
                    if (count <= 0)
                    {
                        // Full. Re-queue this drop for the producer ONLY when an automatic unload can actually
                        // free space (default mode breaks off to unload now). In strict mode, with auto-unload
                        // off, OR with keep-working-when-full ON, NOTHING fires to make room DURING the run —
                        // re-adding would walk the pawn back to take zero, forever. Abandon the drop to normal
                        // hauling instead (it's spawned and unforbidden); keep-working-when-full deliberately
                        // leaves the overflow on the ground for normal hauling. Re-claimed through
                        // SelfPickupClaims (TakeNextValidPending already released it on pop), so a colleague who
                        // is now closer can pick it up instead of it silently reserving this pawn's spot.
                        if (comp != null && thing != null && s != null && s.markForUnload && !s.strictCarryWeight
                            && !s.keepWorkingWhenFull)
                            SelfPickupClaims.Claim(thing, pawn);
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
                        // Tag the SPECIFIC scooped Thing for any non-stacking item (stackLimit 1 — every weapon and
                        // every quality/HP-bearing item). Those never merge, so `split` is always the exact loot we
                        // just picked up, NOT a same-def sidearm InventoryStackOfDef might otherwise return (which
                        // would ship the pawn's own 99%-quality sidearm to storage and keep a hauled 3% one).
                        // Stackable items (stackLimit > 1) are fungible and carry no per-instance quality, so keep
                        // the by-def relink unchanged — it tags the grown stack and re-notifies CE's HoldTracker of
                        // the merged delta via `moved` (which a tag on a folded-in residue would otherwise drop).
                        Thing held = split.def.stackLimit == 1
                            ? split
                            : (YieldRouter.InventoryStackOfDef(owner, split.def, pawn) ?? (allMoved ? split : null));
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
