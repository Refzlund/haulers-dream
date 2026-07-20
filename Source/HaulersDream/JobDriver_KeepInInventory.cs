using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Keep N in inventory": walk to the clicked stack and take the player-chosen amount (job.count, set by the
    /// order's slider — see <see cref="BulkHaul.BuildKeepJob"/>) into the pawn's inventory, raising the per-def keep
    /// pin on <see cref="CompHauledToInventory"/> by what was taken (<see cref="CompHauledToInventory.AddKeptCount"/>)
    /// so HD's unload keeps the first N of the def and vanilla's drop-unused never sheds it (#197). Two shapes,
    /// chosen by target B: the GROUND branch (no B) scoops a spawned stack off the ground; the CONTAINER branch
    /// (B = a spawned container building — vanilla's egg box, or a modded container that flags its contents
    /// selectable) walks to the container and pulls the clamped count straight from its inner ThingOwner (the item
    /// itself is unspawned in there, so the ground toils would insta-fail on it). The counterpart to
    /// <see cref="JobDriver_BulkHaul"/>'s "pick up to haul", where the kept amount is HELD, not stored: single-target
    /// and no nearby sweep, and the kept units themselves are never unloaded (the surplus math preserves the first N).
    /// On finish, though, any SURPLUS the pawn already carries ABOVE its keep is flushed to storage (#225: a forced,
    /// behind-queued-work unload gated on real surplus). The pawn stops keeping the def once it holds none of it (the comp's heal
    /// prunes the pin), and the Gear-tab keep control edits the amount directly. The take is mass/CE-clamped live and
    /// added canMerge:true (one merged stack per def keeps the surplus math clean; the per-def count does the
    /// keeping). Ordered via TryTakeOrderedJob (multiplayer auto-syncs the order), and the count is raised inside the
    /// driver on every client, so the keep map stays deterministic.
    /// </summary>
    public class JobDriver_KeepInInventory : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        // The spawned container building holding the item, on the container branch only (unset = ground branch).
        private const TargetIndex ContainerInd = TargetIndex.B;

        // The reservation stays on the ITEM in both branches (the stack is what two keep orders could race);
        // reserving an unspawned contained thing is fine — Meals-on-Wheels reserves stacks inside pawn
        // inventories the same way. The container itself is deliberately NOT reserved, so a hauler delivering
        // INTO the box during our walk is never blocked (ThingOwner ops keep both sides consistent).
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.GetTarget(ItemInd), job, 1, -1, null, errorOnFailed);

        public override string GetReport() => "HaulersDream.Keep.Report".Translate();

        public override IEnumerable<Toil> MakeNewToils()
        {
            // Flush any SURPLUS above the pawn's keep-stock when the keep order ends (took the amount, target gone,
            // or interrupted), shared by BOTH branches below. The keep driver otherwise never schedules an unload
            // (unlike JobDriver_BulkHaul's sweep), and the order's TryTakeOrderedJob(requestQueueing:false) clears
            // the queue, discarding the original pickup's queued forced unload, so on a busy pawn the surplus (e.g.
            // holds 9, keep 7 -> unload 2) would linger (#225). forced:true so a pawn with pending work still flushes;
            // behindQueuedWork:true EnqueueLasts it so real work is never preempted (the same contract as the
            // bulk-haul finish flush). Gated on AnyUnloadable (which reads the HEALED set + InventorySurplus.SurplusOf)
            // so it never queues a no-op unload and never touches the genuinely-kept first N units.
            AddFinishAction(_ =>
            {
                var comp = pawn.GetComp<CompHauledToInventory>();
                if (comp != null && PawnUnloadChecker.AnyUnloadable(pawn, comp.GetHashSet()))
                    PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true, behindQueuedWork: true);
            });

            if (job.GetTarget(ContainerInd).HasThing)
            {
                // CONTAINER branch: goto/fail on the CONTAINER (the item is unspawned inside it — FailOnDespawnedOrNull
                // on the item would end the job before the first step). A forbidden container is allowed like a
                // forbidden ground stack (a forced player order); its destruction mid-walk fails the goto cleanly.
                yield return Toils_Goto.GotoThing(ContainerInd, PathEndMode.Touch).FailOnDespawnedOrNull(ContainerInd);

                // Vanilla-like pickup pause (#121), anchored on the spawned CONTAINER: the item itself is
                // unspawned inside it, so anchoring on ItemInd would render the bar on the pawn; the box is the
                // thing the pawn visibly works at. Mirrors the goto's fail condition so a box destroyed
                // mid-pause ends the job promptly instead of finishing a pointless wait.
                yield return PickupPause.MakeToil(ContainerInd, PickupDelayContext.ManualCarry).FailOnDespawnedOrNull(ContainerInd);

                Toil extract = ToilMaker.MakeToil("HD_Keep_TakeFromContainer");
                extract.initAction = delegate
                {
                    var t = job.GetTarget(ItemInd).Thing;
                    var container = job.GetTarget(ContainerInd).Thing;
                    if (t == null || t.Destroyed || container == null || !container.Spawned)
                        return;
                    var s = HaulersDreamMod.Settings;
                    var comp = pawn.GetComp<CompHauledToInventory>();
                    var inv = pawn.inventory?.innerContainer;
                    if (s == null || comp == null || inv == null)
                        return;
                    // Still inside THIS container (a hauler may have moved/merged it mid-walk) — else a quiet no-op
                    // end, matching the ground branch's behavior when its stack despawns.
                    var inner = container.TryGetInnerInteractableThingOwner();
                    if (inner == null || !inner.Contains(t))
                        return;
                    int planned = job.count > 0 ? job.count : t.stackCount;
                    // Re-clamp to LIVE mass/CE room at the take, same as the ground branch below.
                    int count = BulkHaul.MassClampedTake(pawn, t, planned, s);
                    if (count <= 0)
                        return;
                    // Owner→owner transfer (splits internally; the container's own removal notifications fire).
                    // canMerge:true folds it into the pawn's existing stock of this def — the per-def keep-count
                    // (#197) does the keeping now, so there is no need to isolate a specific stack (and one merged
                    // stack per def makes the surplus math clean). Raise the keep pin by what actually moved.
                    var defKept = t.def;
                    int moved = inner.TryTransferToContainer(t, inv, count, out _,
                        canMergeWithExistingStacks: true);
                    if (moved > 0)
                        comp.AddKeptCount(defKept, moved);
                };
                extract.defaultCompleteMode = ToilCompleteMode.Instant;
                yield return extract;
                yield break;
            }

            // GROUND branch. A forced player order may target a FORBIDDEN stack (same leniency as "Pick up X"), so
            // only fail when the stack is gone, not when it is forbidden. Job-bound reservations release at job end
            // (CleanupCurrentJob).
            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(ItemInd);

            // Vanilla-like pickup pause (#121): a keep order is exactly vanilla's "pick up into inventory"
            // case, so it pays vanilla's exact wait. Mirrors the goto's fail condition (gone = fail promptly;
            // forbidden stays allowed, this is a forced order).
            yield return PickupPause.MakeToil(ItemInd, PickupDelayContext.ManualCarry).FailOnDespawnedOrNull(ItemInd);

            Toil take = ToilMaker.MakeToil("HD_Keep_Take");
            take.initAction = delegate
            {
                var t = job.GetTarget(ItemInd).Thing;
                if (t == null || !t.Spawned)
                    return;
                var s = HaulersDreamMod.Settings;
                var comp = pawn.GetComp<CompHauledToInventory>();
                var inv = pawn.inventory?.innerContainer;
                if (s == null || comp == null || inv == null)
                    return;
                int planned = job.count > 0 ? job.count : t.stackCount;
                // Re-clamp to LIVE mass/CE room at pickup (the plan-time count can be stale — gear picked up, settings
                // changed since). Same clamp the "Pick up X" builder/driver use.
                int count = BulkHaul.MassClampedTake(pawn, t, planned, s);
                if (count <= 0)
                    return;
                // SplitOff with count >= stackCount despawns the thing itself (full-stack take); a partial split
                // returns a fresh unspawned thing.
                var defKept = t.def;
                var split = t.SplitOff(count);
                if (split == null || split.Destroyed || split.stackCount <= 0)
                    return;
                int took = split.stackCount;
                // canMerge:true folds it into the pawn's existing stock of this def — the per-def keep-count (#197)
                // does the keeping, so no isolated stack is needed (one merged stack per def keeps the surplus math
                // clean). Raise the keep pin by what we took only once it is actually in the pack.
                if (inv.TryAdd(split, canMergeWithExistingStacks: true))
                    comp.AddKeptCount(defKept, took);
                else if (!split.Destroyed)
                    // Never let the split vanish if inventory somehow refuses it — put it back on the ground.
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            };
            take.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return take;
        }
    }
}
