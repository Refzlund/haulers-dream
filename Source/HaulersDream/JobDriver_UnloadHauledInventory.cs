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
                if (comp == null || held == null || held.Destroyed)
                    return;
                if (pawn.carryTracker?.innerContainer?.Contains(held) == true
                    || pawn.inventory?.innerContainer?.Contains(held) == true)
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

                    var toPull = thing;
                    pawn.inventory.innerContainer.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, countToDrop, out thing);
                    if (thing == null)
                    {
                        // Nothing moved — the one-stack carry tracker is blocked by a non-mergeable passenger, or
                        // another mod is holding this stack (a combat mod re-grabbing its ammo, etc.). Do NOT
                        // end+requeue on the SAME first-ordered item forever: that churn freezes the pawn in the
                        // "unloading inventory" job (the reported caravan-return stall). Mark it skipped for THIS
                        // job and loop to the next tracked item; the tag stays, so it's retried on the next unload
                        // trigger (and the cannot-unload alert still surfaces it if it stays genuinely stuck).
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

                    if (StoreUtility.TryFindBestBetterStorageFor(next.Thing, pawn, pawn.Map, StoragePriority.Unstored,
                            pawn.Faction, out var cell, out var destination))
                    {
                        job.SetTarget(TargetIndex.A, next.Thing);
                        if (cell == IntVec3.Invalid)
                            job.SetTarget(TargetIndex.B, destination as Thing);
                        else
                            job.SetTarget(TargetIndex.B, cell);

                        // Haul-to-stack: storage CELLS are deliberately not reserved (multiple pawns may
                        // deliver to — and stack onto — the same tile; see HaulToStack). UNSTACKABLE items
                        // (stackLimit <= 1 — organs, body parts, weapons) are excluded from this: they can
                        // NEVER stack onto a shared cell, so leaving the cell unreserved gains them nothing,
                        // but it lets another hauler fill the same cell mid-carry, invalidating it and looping
                        // the pawn (issue #162 — endless pacing in hospital/prison). Mirrors the same guard on
                        // the vanilla HaulToCell path (HaulToStack.cs NoCellReservation prefix). Containers keep
                        // their reservation: their capacity coordination is the enroute/reservation system.
                        bool reserveDest = cell == IntVec3.Invalid
                                           || next.Thing.def.stackLimit <= 1
                                           || HaulersDreamMod.Settings == null || !HaulersDreamMod.Settings.haulToStack;
                        if (reserveDest && !pawn.Map.reservationManager.Reserve(pawn, job, job.targetB))
                        {
                            // Untag only when the drop actually happened — a failed drop leaves the thing in
                            // inventory, where a missing tag would strand it untracked (gizmo hidden, never retried).
                            if (pawn.inventory.innerContainer.TryDrop(next.Thing, ThingPlaceMode.Near, next.Count, out _))
                                carried.Remove(next.Thing);
                            EndJobWith(JobCondition.Incompletable);
                            return;
                        }
                        countToDrop = next.Count;
                        lastDeliveredDef = next.Thing.def; // invalidate this def's dest cache after the place loops back
                    }
                    else if (pawn.Map != null && !pawn.Map.IsPlayerHome)
                    {
                        // Non-home / temporary map (caravan, bandit camp): there is no player storage here, and
                        // dropping the tagged load on the ground abandons it when the caravan leaves. Keep it
                        // tagged in inventory — it rides home automatically as caravan inventory, or is loaded
                        // onto a pack animal (the over-encumbered auto-divert, the manual bulk-load order, or
                        // vanilla Reform Caravan). End Succeeded so the checker stops re-queuing. (A REAL stockpile
                        // on the map is still used by the TryFindBestBetterStorageFor branch above.)
                        EndJobWith(JobCondition.Succeeded);
                        return;
                    }
                    else if (StoreUtility.TryFindStoreCellNearColonyDesperate(next.Thing, pawn, out var desperateCell))
                    {
                        // No stockpile (not even a dumping zone) accepts this def — rock chunks are excluded from
                        // the default stockpile preset, and many modded materials/crops sit in a category no
                        // stockpile allows. Vanilla's own unload (JobDriver_UnloadYourInventory) does NOT give up
                        // here: it carries the item to a DESPERATE home-area cell (any reachable spot in / just
                        // outside the colony). Mirror that, so the item is actually HAULED away instead of dumped
                        // wherever the pawn happened to be standing (a workbench / dining room) — where the next
                        // work run would just re-scoop it (mine -> carry -> drop-at-feet -> re-scoop, forever).
                        job.SetTarget(TargetIndex.A, next.Thing);
                        job.SetTarget(TargetIndex.B, desperateCell);
                        // A desperate destination is always a plain cell (never a container). Match the storage
                        // branch: don't reserve the cell when haul-to-stack is on (several pawns may stack onto it).
                        // Unstackables (stackLimit <= 1) still reserve: they can't share a cell and leaving it
                        // unreserved loops them (issue #162 — same guard as the storage branch above and the
                        // vanilla HaulToCell prefix).
                        bool reserveDest = next.Thing.def.stackLimit <= 1
                                           || HaulersDreamMod.Settings == null || !HaulersDreamMod.Settings.haulToStack;
                        if (reserveDest && !pawn.Map.reservationManager.Reserve(pawn, job, job.targetB))
                        {
                            if (pawn.inventory.innerContainer.TryDrop(next.Thing, ThingPlaceMode.Near, next.Count, out _))
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
                        // fall through the toil chain: pull from inventory -> carry to the desperate cell -> place
                    }
                    else
                    {
                        // Truly nowhere reachable to store it -> drop at the pawn's feet and loop straight to the
                        // NEXT tagged item (ending per item made the drain cost one idle cycle per no-storage def).
                        // Untag only when the drop actually happened. If even the feet-drop fails (pawn boxed in /
                        // saturated area), do NOT report Succeeded while keeping the tag — that strands the item
                        // tagged in inventory and every retry re-fails on the same first-ordered item. End
                        // Incompletable so the checker re-queues once the pawn has moved and space frees; the tag
                        // stays (the item is still in inventory) so it's retried and the gizmo stays available.
                        if (pawn.inventory.innerContainer.TryDrop(next.Thing, ThingPlaceMode.Near, next.Count, out _))
                        {
                            carried.Remove(next.Thing);
                            pawn.jobs.curDriver.JumpToToil(begin);
                            return;
                        }
                        EndJobWith(JobCondition.Incompletable);
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
