using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Keep X in inventory": walk to the clicked stack and take it into the pawn's inventory as a KEPT item
    /// (registered on <see cref="CompHauledToInventory"/> via <see cref="CompHauledToInventory.RegisterKept"/>), so
    /// HD's unload never hauls it away and vanilla's drop-unused never sheds it. Two shapes, chosen by target B:
    /// the GROUND branch (no B) scoops a spawned stack off the ground; the CONTAINER branch (B = a spawned container
    /// building — vanilla's egg box, or a modded container that flags its contents selectable) walks to the container and pulls the clamped count
    /// straight from its inner ThingOwner (the item itself is unspawned in there, so the ground toils would insta-fail
    /// on it). The counterpart to <see cref="JobDriver_BulkHaul"/>'s "pick up to haul" — here the item is HELD, not
    /// stored: single-target, no nearby sweep, and NO forced unload. The player releases a kept item simply by
    /// consuming it or dropping it from the pawn's gear tab (the comp's heal then forgets it). The take is
    /// mass/CE-clamped live and added canMerge:false so a kept stack never folds into — and thus never wrongly
    /// keeps — the pawn's personal or HD-hauled stock. Ordered via TryTakeOrderedJob (multiplayer auto-syncs the
    /// order), and the registration runs inside the driver on every client, so the kept set stays deterministic.
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
                yield return PickupPause.MakeToil(ContainerInd).FailOnDespawnedOrNull(ContainerInd);

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
                    // canMerge:false keeps the kept stack ISOLATED from personal / HD-hauled stock, exactly like
                    // the ground branch.
                    int moved = inner.TryTransferToContainer(t, inv, count, out var transferred,
                        canMergeWithExistingStacks: false);
                    if (moved > 0 && transferred != null)
                        comp.RegisterKept(transferred);
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
            yield return PickupPause.MakeToil(ItemInd).FailOnDespawnedOrNull(ItemInd);

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
                // returns a fresh unspawned thing. Either way TryAdd takes the plain-add path.
                var split = t.SplitOff(count);
                if (split == null || split.Destroyed || split.stackCount <= 0)
                    return;
                // canMerge:false keeps the kept stack ISOLATED from personal / HD-hauled stock, so keeping never
                // accidentally protects the pawn's own kit or a scooped haul (mirrors the bulk driver's isolation).
                if (inv.TryAdd(split, canMergeWithExistingStacks: false))
                    comp.RegisterKept(split);
                else if (!split.Destroyed)
                    // Never let the split vanish if inventory somehow refuses it — put it back on the ground.
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            };
            take.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return take;
        }
    }
}
