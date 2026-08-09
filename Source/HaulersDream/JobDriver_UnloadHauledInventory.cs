using System.Collections.Generic;
using HaulersDream.Core;
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

        // Items this job tried to pull but couldn't move (0 transfer — the one-stack carry tracker is blocked by a
        // non-mergeable passenger, or another mod is holding the stack, e.g. a combat mod that re-grabs its ammo).
        // Skipped for the rest of THIS job so one un-transferable item can't churn/freeze the unload; the tag is
        // retained, so they're retried on the next trigger. In-flight only — not scribed (an empty set after a
        // save/load just means everything is retried next pass, which is correct).
        private readonly HashSet<Thing> skippedThisJob = new HashSet<Thing>();

        // Reused scratch for FirstUnloadableThing's ordered scan: a snapshot of the carried set that the min-scan
        // pulls smallest-first from (swap-removing as it goes), replacing the per-call LINQ OrderBy().ThenBy()
        // (an OrderedEnumerable + sort keys + 2 closures) re-run once per item unloaded. [ThreadStatic] + lazy-init
        // matches the repo's hook-reachable scratch convention; Cleared at the point of use, never trusted empty.
        // SAFETY: FirstUnloadableThing runs to completion (no re-entrant call) before the next reuse, so sharing it
        // across calls on one thread is sound. Snapshotting first also preserves the LINQ's semantics of iterating a
        // FIXED order even though the loop body mutates `carried` (relink add/remove) mid-scan.
        [System.ThreadStatic] private static List<Thing> scratchOrdered;

        // Per-trip destination cache for closest-destination-first ordering (C1b). Maps a carried def to its
        // resolved best storage CELL this trip, so FirstUnloadableThing can rank candidates by pawn->destination
        // distance WITHOUT re-running the (expensive) TryFindBestBetterStorageFor probe per candidate per pick.
        // The cell — not the distance — is cached, so distance is recomputed from the pawn's CURRENT position each
        // scan (the pawn moves between picks) for free. IntVec3.Invalid means "resolved, but no destination" (sorts
        // last via UnloadDestinationOrder.NoDestination); a missing key means "not yet resolved this trip".
        // In-flight only (not scribed) and per-instance (this driver runs to completion before the cache matters
        // again). INVALIDATION: the just-delivered def's entry is dropped after each delivery (its remaining count /
        // best cell may change once a stack lands), so the next pick re-resolves it; every other def stays cached.
        private readonly Dictionary<ThingDef, IntVec3> destCellByDef = new Dictionary<ThingDef, IntVec3>();

        // The def delivered on the previous pick — its cache entry is invalidated at the top of the next
        // FindTargetOrDrop (i.e. after the place toil looped back to `begin`). -1/null = nothing delivered yet.
        private ThingDef lastDeliveredDef;

        // The loop-reentry toil (the per-item "pick the next tracked stack" wait at the head of the chain),
        // kept so a delivery pathing failure can jump back to it instead of ending the whole trip — see
        // Notify_PatherFailed. Assigned once in MakeNewToils, same convention as JobDriver_BulkHaul's
        // loadDecideToil and JobDriver_SelfPickup's loop.
        private Toil loopToil;

        // Destinations that failed to path during THIS trip (see Notify_PatherFailed), the budget
        // UnreachableDestinationPolicy bounds. In-flight only — not scribed, like skippedThisJob: a trip
        // resumed after a save/load starts with a fresh budget, which is correct (the obstruction that
        // caused the failures is very unlikely to have survived the reload unchanged).
        private int pathFailuresThisJob;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref countToDrop, "countToDrop", -1);
        }

        /// <summary>
        /// A destination the pawn cannot REACH must not cost it the rest of the load — and, unlike the two
        /// source-walking drivers that already override this (JobDriver_BulkHaul, JobDriver_SelfPickup), it
        /// must not leave the stack on the floor either. Every pathing leg in this driver is a DELIVERY leg
        /// (the load comes out of the pawn's own inventory), so the stack is in hand when the path fails.
        ///
        /// <para>The vanilla default (JobDriver.Notify_PatherFailed) ends the job as ErroredPather, and
        /// Pawn_JobTracker.EndCurrentJob's response to that condition is a hardcoded 250-tick JobDefOf.Wait
        /// (decompile-verified) — the "standing" a player reports. On its own that is a four-second hiccup;
        /// what made it unbounded here is what ending the job does to the stack. CleanupCurrentJob runs the
        /// finish action, which RE-TAGS the carried stack, and only afterwards drops it at the pawn's feet
        /// (job.def.carryThingAfterJob is false, decompile-verified). That floor stack is then re-scooped,
        /// re-tagged and routed at the same unreachable destination, and the idle backstop re-queues the
        /// unload on the SAME 250-tick period as vanilla's error wait — so the retry is phase-locked to the
        /// failure and the pawn stands there indefinitely.</para>
        ///
        /// <para>So: put the stack back in INVENTORY (nothing reaches the floor, so nothing is re-scooped, and
        /// the Thing identity stays stable — a stack that hits the floor usually merges and changes id, which
        /// is exactly why the id-keyed backoff leaks on the DropAtFeet branch, see the note there), add it to
        /// the in-flight skippedThisJob set (which already means "step over it, keep the tag, retry on a later
        /// trigger"), stamp the shared re-offer backoff so the automatic haul scan and HD's own intake paths
        /// stand down, and carry on with the rest of the load. HaulChurnPolicy.BackoffTicks (600) is longer
        /// than the 250-tick idle period — pinned by UnreachableDestinationPolicy.BreaksPhaseLock — which is
        /// the specific property that stops the loop re-forming rather than merely slowing it down.</para>
        ///
        /// <para>A permanently sealed room was never affected and is not what this handles: a destination
        /// inside one is rejected by PawnCanAutomaticallyHaulFast / IsGoodStoreCell / the unload fallback long
        /// before a job is built. The case that stalls is a destination that PASSES the storage search and
        /// then fails to path — transient blocking, a foreign mod relocating stacks, Touch-only geometry.</para>
        /// </summary>
        public override void Notify_PatherFailed()
        {
            var held = pawn.carryTracker?.CarriedThing;
            if (held == null || loopToil == null)
            {
                // Nothing in hand (or the toil chain was never built): there is no stack to rescue and no
                // re-scoop cycle to break, so keep vanilla's behaviour rather than invent a recovery.
                base.Notify_PatherFailed();
                return;
            }

            pathFailuresThisJob++;
            int carriedCount = held.stackCount;

            // Back into the pack, NOT onto the floor. canMergeWithExistingStacks:false is load-bearing twice
            // over: vanilla's ThingOwner.TryTransferToContainer hands back the transferred Thing even when a
            // merge DESTROYED it (decompile-verified — TryAdd absorbs it into the matching stack and the out
            // param still points at the husk), so a merging transfer would give us a dead reference to tag and
            // to set aside; and an unmerged add preserves this driver's tag isolation exactly as
            // JobDriver_BulkHaul.DepositSwept does, so the returning surplus can never fold itself into the
            // pawn's personal stock.
            var inventory = pawn.inventory?.innerContainer;
            Thing returned = null;
            if (inventory != null)
                pawn.carryTracker.innerContainer.TryTransferToContainer(held, inventory, carriedCount,
                    out returned, canMergeWithExistingStacks: false);

            if (returned == null)
            {
                // Effectively unreachable: a pawn's inventory has no stack cap. Stamp the backoff anyway so the
                // stack vanilla is about to drop at the pawn's feet is not instantly re-scooped, then finish the
                // trip. Succeeded, not Incompletable — the same "this trip is done, don't re-queue me on the
                // spot" meaning the no-unloadable-remainder branch in FindTargetOrDrop already uses.
                HaulChurnGuard.StampBackoff(held);
                HDLog.Dbg($"unload {pawn.LabelShort}: could not reach the destination for {held.LabelShort} "
                          + "and could not put it back in the pack; ending the trip and backing it off.");
                EndJobWith(JobCondition.Succeeded);
                return;
            }

            // Re-tag it: PullItemFromInventory dropped the tag when it pulled the stack into the hands, and an
            // untagged surplus sitting in the pack is a silent black hole (gizmo hidden, never retried). The
            // carried count is passed as the merge delta so Combat Extended's HoldTracker is re-notified for
            // the units that moved back, matching what RegisterHauledItem does for a grown stack elsewhere.
            pawn.TryGetComp<CompHauledToInventory>()?.RegisterHauledItem(returned, carriedCount);
            job.SetTarget(TargetIndex.A, returned);

            skippedThisJob.Add(returned);
            HaulChurnGuard.StampBackoff(returned);

            // Let go of the destination we can't reach. The job would release it at the end anyway, but holding
            // a container reservation on a shelf THIS pawn can't get to would block a pawn that can.
            ReleaseTargetBReservation();

            int remaining = RemainingCandidateCount();
            HDLog.Dbg($"unload {pawn.LabelShort}: could not reach the destination for {returned.LabelShort} "
                      + $"x{carriedCount} (failure {pathFailuresThisJob} this trip, {remaining} stack(s) left "
                      + "to try); putting it back in the pack, backing it off and moving on.");

            if (UnreachableDestinationPolicy.Choose(pathFailuresThisJob, remaining)
                == UnreachableDestinationAction.SetAsideAndContinue)
                JumpToToil(loopToil);
            else
                EndJobWith(JobCondition.Succeeded);
        }

        /// <summary>
        /// How many tracked stacks this trip could still deliver after the one just set aside — the
        /// "remaining" term <see cref="UnreachableDestinationPolicy.Choose"/> weighs against the failure
        /// budget.
        ///
        /// <para>Deliberately an UPPER bound: it counts every live tagged stack still in the pack that this
        /// trip has not set aside, without re-running the surplus math or the reservation checks that
        /// <see cref="FirstUnloadableThing"/> applies. Over-counting only costs one more pass through the loop
        /// toil, whose own no-unloadable-remainder branch then ends the trip; under-counting would end a trip
        /// that still had deliverable stacks, so the bias is deliberately in the safe direction.</para>
        /// </summary>
        private int RemainingCandidateCount()
        {
            var comp = pawn.TryGetComp<CompHauledToInventory>();
            var inner = pawn.inventory?.innerContainer;
            if (comp == null || inner == null)
                return 0;

            int count = 0;
            // The healed view, not PeekHashSet: this feeds a DECISION, and a stale tag left by a merge would
            // count a stack that no longer exists.
            foreach (var thing in comp.GetHashSet())
                if (thing != null && !thing.Destroyed && !skippedThisJob.Contains(thing) && inner.Contains(thing))
                    count++;
            return count;
        }

        /// <summary>Release this job's reservation on the delivery destination, if it holds one. Shared by the
        /// normal end-of-delivery toil and the unreachable-destination recovery so the two can never
        /// disagree; the ReservedBy guard is required because Release error-logs when no matching reservation
        /// exists.</summary>
        private void ReleaseTargetBReservation()
        {
            var reservations = pawn.Map?.reservationManager;
            if (reservations != null && reservations.ReservedBy(job.targetB, pawn, pawn.CurJob))
                reservations.Release(job.targetB, pawn, pawn.CurJob);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override IEnumerable<Toil> MakeNewToils()
        {
            var begin = Toils_General.Wait(3);
            loopToil = begin; // the reentry point a failed delivery jumps back to (see Notify_PatherFailed)
            yield return begin;

            var comp = pawn.TryGetComp<CompHauledToInventory>();
            var carried = comp?.GetHashSet() ?? new HashSet<Thing>();

            // If this job is interrupted mid-trip — a draft, a mod cancelling it, or CommonSense's
            // "put the carried thing back into inventory" transpiler — AFTER an item was pulled into the
            // pawn's hands but BEFORE it was placed, re-tag the still-held item so the next unload reclaims
            // it instead of orphaning it untracked (a silent black hole). On a normal success the item is
            // placed in the world (not in hands/inventory), so it is not re-tagged.
            AddFinishAction(condition =>
            {
                var held = job.GetTarget(TargetIndex.A).Thing;
                var inCarry = pawn.carryTracker?.innerContainer?.Contains(held) == true;
                var inInv = pawn.inventory?.innerContainer?.Contains(held) == true;
                if (comp == null || held == null || held.Destroyed)
                    return;
                if (inCarry || inInv)
                    comp.RegisterHauledItem(held);
            });

            yield return FindTargetOrDrop(carried, begin);
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
                initAction = ReleaseTargetBReservation
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
                        if (InventoryDrop.TryDropPreferHome(pawn, thing, countToDrop, "no-manipulation", out thing))
                            carried.Remove(original);
                        EndJobWith(JobCondition.Succeeded);
                        return;
                    }

                    var toPull = thing;
                    pawn.inventory.innerContainer.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, countToDrop, out thing);
                    if (thing == null)
                    {
                        if (toPull != null)
                            skippedThisJob.Add(toPull);
                        pawn.jobs.curDriver.JumpToToil(wait);
                        return;
                    }
                    job.count = countToDrop;
                    job.SetTarget(TargetIndex.A, thing);
                    carried.Remove(thing);
                    thing.SetForbidden(false, false);
                }
            };
        }

        private Toil FindTargetOrDrop(HashSet<Thing> carried, Toil begin)
        {
            return new Toil
            {
                initAction = () =>
                {
                    // Invalidate the previously-delivered def's cached destination cell (its remaining count /
                    // best store cell may have changed now that a stack landed); every other def stays cached for
                    // the rest of the trip. No-op when closest-dest ordering is off (the cache is never populated).
                    if (lastDeliveredDef != null)
                    {
                        destCellByDef.Remove(lastDeliveredDef);
                        lastDeliveredDef = null;
                    }

                    var next = FirstUnloadableThing(carried);
                    if (next.Count == 0)
                    {
                        // No unloadable stack right now. End Succeeded when nothing remains, OR when the only
                        // remainder is items we stepped over this job (un-transferable due to external
                        // interference, e.g. a combat mod holding its ammo) — ending Incompletable there would
                        // instantly re-queue and re-churn the same blocked item. A NON-skipped remainder is
                        // reserved by another pawn (a worker fetching from this inventory): end Incompletable so a
                        // freed reservation re-queues promptly. The tag is kept either way, so a genuinely stuck
                        // item is retried on the next trigger and still surfaces in the cannot-unload alert.
                        EndJobWith(carried.Count == 0 || skippedThisJob.Count > 0
                            ? JobCondition.Succeeded : JobCondition.Incompletable);
                        return;
                    }

                    bool hasStorage = StoreUtility.TryFindBestBetterStorageFor(next.Thing, pawn, pawn.Map,
                        StoragePriority.Unstored, pawn.Faction, out var cell, out var destination);
                    // A null map is unreachable for a running driver (the storage probe above already passes
                    // pawn.Map into vanilla), but note the one deliberate delta from the old if/else chain: it
                    // used to fall THROUGH to the home-area scan, which would have dereferenced the null map;
                    // treating "no map" as "not the home map" keeps the load tagged and ends the job cleanly.
                    bool onHomeMap = pawn.Map != null && pawn.Map.IsPlayerHome;
                    // The home-area radial scan runs ONLY when it can change the outcome — storage failed, on the
                    // player's map — which is exactly the precedence UnloadFallbackPolicy.Choose encodes. Keeping
                    // the short-circuit here (rather than probing eagerly and letting Choose discard it) is what
                    // stops the common delivery path paying for a reachability scan per stack.
                    var desperateCell = IntVec3.Invalid;
                    bool hasHomeCell = !hasStorage && onHomeMap
                                       && InventorySurplus.TryFindDesperateHomeAreaCell(pawn, next.Thing, out desperateCell);

                    // Switching on the Core policy — rather than re-spelling the same precedence inline — is the
                    // structural half of the #231 fix: UnloadPlacement has no "haul it outside the home area"
                    // member, so this dispatch cannot express the behaviour that caused the bug. The other half is
                    // scripts/check-no-desperate-leg.ts, which fails the build if the vanilla desperate search is
                    // reintroduced anywhere in this assembly.
                    switch (UnloadFallbackPolicy.Choose(hasStorage, onHomeMap, hasHomeCell))
                    {
                    case UnloadPlacement.Deliver:
                    {
                        job.SetTarget(TargetIndex.A, next.Thing);
                        if (cell == IntVec3.Invalid)
                            job.SetTarget(TargetIndex.B, destination as Thing);
                        else
                            job.SetTarget(TargetIndex.B, cell);

                        // Haul-to-stack: storage CELLS are deliberately not reserved (several pawns may
                        // deliver to — and stack onto — the same tile; see HaulToStack), but ONLY where the
                        // storage commitment ledger has taken the destination on. TryCommit is that test: it
                        // refuses a container (whose capacity vanilla's own enroute system coordinates), a
                        // cell with no slot group, and a map HD is inert on, and vanilla's reservation then
                        // stands exactly as it always did.
                        //
                        // This is where a hand-written "stackLimit <= 1" carve-out used to live, kept in
                        // lockstep with three others by hand until they drifted apart (issue #162 — endless
                        // pacing in a hospital, because an unreserved 1-capacity cell had no arbitration at
                        // all). It is gone: one organ claims one unit of one cell, so the next hauler's gate
                        // finds no room without anyone having to remember the special case.
                        //
                        // The pawn is DELIVERING here — it already holds this cargo — so the seam credits it
                        // the space it reserved rather than making it compete with its own in-flight load.
                        bool arbitrated = StorageCommitments.TryCommit(pawn, GroupAt(cell), next.Thing.def,
                            UnitsBoundFor(next), "unload");
                        if (!arbitrated && !pawn.Map.reservationManager.Reserve(pawn, job, job.targetB))
                        {
                            // Untag only when the drop actually happened — a failed drop leaves the thing in
                            // inventory, where a missing tag would strand it untracked (gizmo hidden, never retried).
                            if (InventoryDrop.TryDropPreferHome(pawn, next.Thing, next.Count, "reserve-failed-storage", out _))
                                carried.Remove(next.Thing);
                            EndJobWith(JobCondition.Incompletable);
                            return;
                        }
                        countToDrop = next.Count;
                        lastDeliveredDef = next.Thing.def; // invalidate this def's dest cache after the place loops back
                        break; // fall through the toil chain: pull from inventory -> carry to storage -> place
                    }
                    case UnloadPlacement.KeepInInventory:
                    {
                        // Non-home / temporary map (caravan, bandit camp): there is no player storage here, and
                        // dropping the tagged load on the ground abandons it when the caravan leaves. Keep it
                        // tagged in inventory — it rides home automatically as caravan inventory, or is loaded
                        // onto a pack animal (the over-encumbered auto-divert, the manual bulk-load order, or
                        // vanilla Reform Caravan). End Succeeded so the checker stops re-queuing. (A REAL stockpile
                        // on the map is still used by the TryFindBestBetterStorageFor branch above.)
                        HDLog.Dbg($"unload {pawn.LabelShort}: on a non-home map ({pawn.Map}); keeping "
                                  + $"{next.Thing.LabelShort} x{next.Count} tagged in inventory to ride home.");
                        EndJobWith(JobCondition.Succeeded);
                        return;
                    }
                    case UnloadPlacement.PlaceOnNearbyHomeCell:
                    {
                        // No stockpile (not even a dumping zone) accepts this def — rock chunks are excluded from
                        // the default stockpile preset, and many modded materials/crops sit in a category no
                        // stockpile allows. Rather than dump the load wherever the pawn happened to be standing (a
                        // workbench / dining room — where the next work run would just re-scoop it: mine -> carry
                        // -> drop-at-feet -> re-scoop, forever), carry it to a nearby HOME-AREA floor cell.
                        //
                        // This deliberately does NOT call StoreUtility.TryFindStoreCellNearColonyDesperate, whose
                        // three legs are (RimWorld 1.6):
                        //   1. TryFindBestBetterStoreCellFor (StoreUtility.cs:374) — DEAD here. The
                        //      TryFindBestBetterStorageFor probe above is a strict superset of it, so reaching
                        //      this branch already guarantees leg 1 re-fails. Nothing is lost by skipping it.
                        //   2. 20 radial cells around the carrier gated on areaManager.Home (StoreUtility.cs:
                        //      378-386) — home-constrained, radius ~2.5. This IS what we reproduce, verbatim, in
                        //      InventorySurplus.TryFindDesperateHomeAreaCell (shared with the cannot-unload alert
                        //      so the two can never disagree).
                        //   3. RCellFinder.TryFindRandomSpotJustOutsideColony (StoreUtility.cs:388) — DROPPED
                        //      (issue #231). It has NO home-area check at all: its FinalValidator requires an
                        //      OUTDOOR district that TOUCHES THE MAP EDGE, and its final pass rolls a random cell
                        //      over the whole map (its CellFinderLoose.TryGetRandomCellWith(..., 1000, ...) leg —
                        //      cited by member, not line: decompiler line numbers for vanilla shift between ILSpy
                        //      versions). Vanilla only reaches it behind the rare event-driven UnloadEverything
                        //      flag, once per job; this driver ran it per tagged stack, in a loop, for every
                        //      hauling pawn — and after each placement jumps back to `begin` and re-decides from
                        //      the NEW position, re-rolling a fresh random cell. That is exactly the reported
                        //      scattering of goods far outside the Home area. (It also NREs on a degenerate
                        //      colony — issue #76.)
                        //
                        // Dropping leg 3 cannot resurrect the mine -> drop -> re-scoop loop it was guarding
                        // against. Every path that lifts a stack off the ground IN ORDER TO PUT IT INTO STORAGE
                        // first requires a storage destination for it, and this item has just been shown to have
                        // none: the work-spot sweep (YieldRouter.cs:334), the scoop-time gate
                        // YieldRouter.HasScoopDestination (YieldRouter.cs:574, re-checked at the take toil,
                        // JobDriver_SelfPickup.cs:112), the bulk-haul pool (BulkHaul.cs:968), the en-route grab
                        // (its own midway group walk, EnRoutePickup.cs:721, hard-failing the candidate at
                        // EnRoutePickup.cs:446 when no allowed cell is found — NOT this probe), the auto-strip
                        // scoop (CorpseStripper.ScoopLoot — the one site that USED to lack the gate, issue #234),
                        // and vanilla's own HaulAIUtility.HaulToStorageJob.
                        //
                        // That quantifier is deliberately narrow — "to put it into storage", NOT "at all" — because
                        // three other kinds of intake DO lift a stack off the ground with no storage probe. None of
                        // them can cycle:
                        //   • DELIVERY drivers, which lift a stack to CONSUME it at the job's own fixed target
                        //     rather than to store it, so a storage probe would be the wrong question: bill
                        //     ingredients to the bench (tagged at JobDriver_BillPrepGather.cs:161 and
                        //     JobDriver_BatchCraft.cs:1679), fuel to the refuelable (JobDriver_BulkRefuel.cs:134),
                        //     materials to a blueprint/frame (JobDriver_OverloadConstructDeliver.cs:485, whose
                        //     phase-1 TakeToInventory walks nearby FLOOR stacks). So yes — with no stockpile
                        //     accepting WoodLog, this branch puts the wood on a home-area cell and the autonomous
                        //     construct-deliver giver may well pick it straight back up for a frame. That is not a
                        //     loop: the stack is consumed into the frame, never re-dropped.
                        //   • An explicit PLAYER ORDER, which requires no destination by design — forcing is the
                        //     whole point of the click, and it takes a fresh click each time.
                        //   • SURPLUS ADOPTION (PawnUnloadChecker.cs:334), which walks pawn.inventory
                        //     .innerContainer ONLY and so never reaches a stack on the floor at all. It also gates
                        //     on InventorySurplus.HasUnloadDestination (InventorySurplus.cs:276-296), deliberately
                        //     WIDER than the probe above — TryFindBestBetterStorageFor OR
                        //     TryFindDesperateHomeAreaCell — precisely so adoption and mech cargo shedding still
                        //     work for a load this branch can only put on a home-area cell.
                        //
                        // The churn backoff the DropAtFeet branch stamps (HaulChurnGuard.StampBackoff, honoured by the
                        // vanilla haul scan at HaulChurnGuard.cs:574, the work-spot sweep at YieldRouter.cs:305
                        // and the en-route grab at EnRoutePickup.cs:413) is a SECONDARY belt only, and a leaky
                        // one: it keys on the pre-drop inventory Thing's thingIDNumber, but the stack that reaches
                        // the floor usually has a different id (a partial drop mints a new Thing via SplitOff; a
                        // merge yields the ground stack's id), so it often does not cover what actually landed.
                        // Do not rely on it as the reason this is safe.
                        HDLog.Dbg($"unload {pawn.LabelShort}: no storage for {next.Thing.LabelShort} x{next.Count}; "
                                  + $"hauling to home-area cell {desperateCell} "
                                  + $"(dist={(desperateCell - pawn.Position).LengthHorizontal:0.#}, "
                                  + $"home={InventoryDrop.IsInHome(pawn.Map, desperateCell)}).");
                        job.SetTarget(TargetIndex.A, next.Thing);
                        job.SetTarget(TargetIndex.B, desperateCell);
                        // Same rule as the storage branch above, and the same reason it needs no unstackable
                        // carve-out any more: the cell is left unreserved only where the commitment ledger
                        // arbitrates it. A home-area fallback cell usually sits in NO slot group, so TryCommit
                        // normally refuses and vanilla's reservation stands — which is exactly right, since a
                        // bare floor cell has no group capacity for a ledger to divide.
                        bool arbitrated = StorageCommitments.TryCommit(pawn, GroupAt(desperateCell),
                            next.Thing.def, UnitsBoundFor(next), "unload-fallback");
                        if (!arbitrated && !pawn.Map.reservationManager.Reserve(pawn, job, job.targetB))
                        {
                            if (InventoryDrop.TryDropPreferHome(pawn, next.Thing, next.Count, "reserve-failed-fallback", out _))
                            {
                                carried.Remove(next.Thing);
                                pawn.jobs.curDriver.JumpToToil(begin);
                                return;
                            }
                            EndJobWith(JobCondition.Incompletable);
                            return;
                        }
                        countToDrop = next.Count;
                        lastDeliveredDef = next.Thing.def; // invalidate this def's dest cache after the place loops back
                        break; // fall through the toil chain: pull from inventory -> carry to the home cell -> place
                    }
                    default: // UnloadPlacement.DropAtFeet
                    {
                        // Truly nowhere reachable to store it -> drop at the pawn's feet and loop straight to the
                        // NEXT tagged item (ending per item made the drain cost one idle cycle per no-storage def).
                        // Untag only when the drop actually happened. If even the feet-drop fails (pawn boxed in /
                        // saturated area), do NOT report Succeeded while keeping the tag — that strands the item
                        // tagged in inventory and every retry re-fails on the same first-ordered item. End
                        // Incompletable so the checker re-queues once the pawn has moved and space frees; the tag
                        // stays (the item is still in inventory) so it's retried and the gizmo stays available.
                        //
                        // STAMP BACKOFF (issue #162): an item with NOWHERE to store (no stockpile accepts its def,
                        // e.g. body parts outside the default preset) would be dropped, immediately re-scooped by
                        // the same pawn's en-route/sweep, and re-unloaded into the same failure — an endless pacing
                        // loop. Stamp the churn backoff so both the vanilla haul scan AND the HD intake paths (en-
                        // route, area-sweep) skip it for a short window, breaking the re-scoop cycle. The window is
                        // brief and self-healing: once storage opens up (the player zones it, a slot frees), the
                        // next scan after the window hauls it normally.
                        HaulChurnGuard.StampBackoff(next.Thing);
                        HDLog.Dbg($"unload {pawn.LabelShort}: no storage and no home-area cell within "
                                  + $"{UnloadFallbackPolicy.RadialCellsToTry} radial cells for "
                                  + $"{next.Thing.LabelShort} x{next.Count}; dropping here and backing it off.");
                        if (InventoryDrop.TryDropPreferHome(pawn, next.Thing, next.Count, "nowhere", out _))
                        {
                            carried.Remove(next.Thing);
                            pawn.jobs.curDriver.JumpToToil(begin);
                            return;
                        }
                        EndJobWith(JobCondition.Incompletable);
                        break;
                    }
                    }
                }
            };
        }

        private ThingCount FirstUnloadableThing(HashSet<Thing> carried)
        {
            var inner = pawn.inventory.innerContainer;

            // Snapshot the carried set into reused scratch, then pull elements smallest-first in
            // (FirstThingCategory.index asc, null last, then ordinal defName) order — the allocation-free equivalent
            // of the old `carried.OrderBy(catIndex).ThenBy(defName)` (HD-ORDERBY). The snapshot is required because the
            // filter body below mutates `carried` (the relink Remove/Add) mid-scan; LINQ's OrderBy buffered its own
            // snapshot, so iteration order was fixed regardless of those mutations — this preserves that exactly. The
            // sort key parity (incl. the stable first-seen tiebreak among equal keys) is pinned by the Core oracle test.
            var ordered = scratchOrdered ?? (scratchOrdered = new List<Thing>());
            ordered.Clear();
            foreach (var t in carried)
                ordered.Add(t);
            // MP determinism: process tagged stacks in thingIDNumber order so a capacity-bound loop deposits/drops the same subset on every client.
            // The min-scan below resolves a (category, defName[, dest distance]) tie to the lowest ORIGINAL enumeration
            // index; ordering the snapshot here (rather than touching the Core comparator, which is oracle-tested) makes
            // that first-seen tiebreak land on the lowest thingIDNumber identically across clients.
            ordered.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

            // Closest-destination-first (C1b, WYU "efficient unloading" parity): when ON, the running-best
            // comparison ranks candidates by pawn->resolved-storage-cell distance FIRST (so the pawn empties the
            // nearest destination group before moving on), tiebreaking by the same category->defName order. When OFF
            // this flag is never read and the scan is byte-identical to the original category->defName min-scan.
            bool closestDest = HaulersDreamMod.Settings != null && HaulersDreamMod.Settings.closestDestinationUnloadOrder;

            int remaining = ordered.Count;
            while (remaining > 0)
            {
                // Min-scan for the next-smallest in sort order (matching SelectFirstByCategoryThenDef, plus the
                // optional closest-destination distance term above). Consumed slots are nulled (NOT swap-removed) so
                // the surviving entries keep their ORIGINAL enumeration index — the tie among equal keys then
                // resolves to the lowest original index, exactly reproducing the STABLE LINQ OrderBy().ThenBy()'s
                // first-seen-among-equals order even when two carried stacks share a def (so which physical stack is
                // returned is identical to the old code; closest-dest only re-ranks ACROSS distances, ties unchanged).
                int bestIdx = -1;
                Thing best = null;
                int bestCat = 0;
                string bestDef = null;
                int bestDist = UnloadDestinationOrder.NoDestination;
                for (int i = 0; i < ordered.Count; i++)
                {
                    var cand = ordered[i];
                    if (cand == null)
                        continue; // already consumed this call
                    int cat = cand.def.FirstThingCategory?.index ?? SelectFirstByCategoryThenDef.NoCategory;
                    bool better;
                    int dist = UnloadDestinationOrder.NoDestination;
                    if (closestDest)
                    {
                        dist = ResolvedDestinationDistanceSq(cand);
                        better = best == null
                                 || UnloadDestinationOrder.Less(dist, cat, cand.def.defName, bestDist, bestCat, bestDef);
                    }
                    else
                    {
                        better = best == null
                                 || SelectFirstByCategoryThenDef.LessThan(cat, cand.def.defName, bestCat, bestDef);
                    }
                    if (better)
                    {
                        bestIdx = i;
                        best = cand;
                        bestCat = cat;
                        bestDef = cand.def.defName;
                        bestDist = dist;
                    }
                }
                var thing = best;
                ordered[bestIdx] = null; // consume (keeps every survivor's original index for the stable tiebreak)
                remaining--;

                // Already tried and couldn't transfer this stack this job (see PullItemFromInventory's 0-transfer
                // branch) — step over it so a single un-transferable item can't pin the unload. Bounds the work:
                // each item is attempted at most once per job; skipped items keep their tag and retry next trigger.
                if (skippedThisJob.Contains(thing))
                    continue;
                if (!inner.Contains(thing))
                {
                    // A partially-picked-up stack merged in inventory gets a new ThingID; relink to it.
                    var def = thing.def;
                    carried.Remove(thing);
                    for (var i = 0; i < inner.Count; i++)
                    {
                        if (inner[i].def == def)
                        {
                            // Re-tag the stack we relink to BEFORE deciding what to do with it: if we returned
                            // a surplus stack but left it untagged, the keep-stock remainder after the unload
                            // would lose tracking; and if it's entirely keep-stock right now, dropping the
                            // def's last tag would strand a later-resurfacing surplus untagged (a silent black
                            // hole). Adding to the live tag set (== comp.GetHashSet()) keeps it tracked either
                            // way. (Bounded to this scooped def, so a foreign mod's stash is never claimed.)
                            carried.Add(inner[i]);
                            int relinked = UnloadableCountOf(inner[i]);
                            if (relinked <= 0)
                                break; // entirely keep-stock for now — keep the tag, move on (see below)
                            return new ThingCount(inner[i], relinked);
                        }
                    }
                    continue;
                }
                // Another pawn may hold a reservation on this stack (a bill worker fetching ingredients
                // out of this very inventory) — unloading it now would move its target out from under it.
                // CanReserve is false exactly when someone else holds the reservation; skip those.
                if (!pawn.CanReserve(thing))
                    continue;
                int count = UnloadableCountOf(thing);
                if (count <= 0)
                    // Nothing above the pawn's keep count right now — personal stock, not surplus. KEEP the
                    // tag (we never dump keep-stock: UnloadableCountOf clamps the unload to the surplus, so
                    // there's no restock-churn loop to guard against). If the keep later drops (food eaten,
                    // drug-policy / inventoryStock / CE-loadout reduced), the resurfaced surplus is still
                    // tracked and gets unloaded, instead of being stranded untagged — a silent black hole.
                    continue;
                return new ThingCount(thing, count);
            }
            return default;
        }

        /// <summary>
        /// Units of this stack's def the pawn is bringing to the destination it is about to walk to — the
        /// whole load, not just the stack in hand.
        ///
        /// <para>An unload places one stack at a time, but a pawn can hold several stacks of one def (200
        /// steel is three), and it is taking ALL of them to the same place. Claiming only the stack being
        /// placed would leave the rest of the load invisible and hand its room to another hauler, which is
        /// the reported bug in miniature at the one moment the cargo is provably real. The floor of the
        /// stack's own count keeps this honest if the surplus measurement ever comes back short.</para>
        /// </summary>
        /// <param name="next">The stack about to be delivered, with the units being placed.</param>
        /// <returns>Units to claim for the destination.</returns>
        private int UnitsBoundFor(ThingCount next)
            => System.Math.Max(next.Count, StorageCommitments.UnitsMovingOf(pawn, next.Thing.def));

        /// <summary>The budget identity of the storage at a destination cell, or null when the cell holds no
        /// slot group (a bare home-area floor cell, or a container's invalid cell). Routed through
        /// <see cref="BulkHaul.BudgetGroupOf"/> so the claim this driver records lands on the same key every
        /// other reader looks it up by.</summary>
        /// <param name="cell">The destination cell.</param>
        /// <returns>The group, or null.</returns>
        private ISlotGroup GroupAt(IntVec3 cell)
        {
            if (!cell.IsValid || pawn.Map == null)
                return null;
            return BulkHaul.BudgetGroupOf(pawn.Map.haulDestinationManager.SlotGroupAt(cell));
        }

        // The "surplus above the pawn's personal kit" math now lives in InventorySurplus, so the unload
        // driver and the cannot-unload alert agree EXACTLY on what is surplus and what is keep-stock.
        // (Vanilla parity: the three FirstUnloadableThing keep sources — drug policy, inventoryStock,
        // packable food — plus the CE loadout. See InventorySurplus.)
        private int UnloadableCountOf(Thing thing) => InventorySurplus.SurplusOf(pawn, thing);

        // Pawn->best-storage-cell squared distance for the closest-destination-first ordering (C1b), or
        // UnloadDestinationOrder.NoDestination when no storage resolves (that candidate then sorts LAST and the
        // category->defName tiebreak applies, so an unreachable-destination stack never blocks the nearer ones).
        //
        // The resolved CELL is cached per def for the trip (destCellByDef) and the just-delivered def's entry is
        // invalidated after each delivery — so TryFindBestBetterStorageFor (the SAME probe the driver uses at the
        // delivery commit, StoragePriority.Unstored) runs at most once per def per trip rather than once per
        // candidate per pick. Distance is recomputed from the pawn's CURRENT position each scan (cheap; the pawn
        // moves between picks), reflecting the live distance without re-resolving. Container destinations report
        // their position (cell == Invalid -> use the destination Thing's cell), matching the storage branch.
        private int ResolvedDestinationDistanceSq(Thing cand)
        {
            var def = cand.def;
            if (!destCellByDef.TryGetValue(def, out var storeCell))
            {
                if (StoreUtility.TryFindBestBetterStorageFor(cand, pawn, pawn.Map, StoragePriority.Unstored,
                        pawn.Faction, out var cell, out var destination))
                    // A container returns cell == Invalid + a destination Thing; rank by the container's cell.
                    storeCell = cell != IntVec3.Invalid ? cell
                              : (destination as Thing)?.Position ?? IntVec3.Invalid;
                else
                    storeCell = IntVec3.Invalid; // resolved this trip, but nowhere better to store -> sorts last
                destCellByDef[def] = storeCell;
            }

            if (storeCell == IntVec3.Invalid)
                return UnloadDestinationOrder.NoDestination;
            int distSq = (storeCell - pawn.Position).LengthHorizontalSquared;
            // Guard the sentinel: a pathologically far cell could collide with the NoDestination sentinel and read
            // as "no destination". Clamp below it so a real (if distant) destination always outranks a missing one.
            return distSq < UnloadDestinationOrder.NoDestination ? distSq : UnloadDestinationOrder.NoDestination - 1;
        }
    }
}
