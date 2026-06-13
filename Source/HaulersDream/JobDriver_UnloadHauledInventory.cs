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

        private Toil FindTargetOrDrop(HashSet<Thing> carried, Toil begin)
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
                            if (pawn.inventory.innerContainer.TryDrop(next.Thing, ThingPlaceMode.Near, next.Count, out _))
                                carried.Remove(next.Thing);
                            EndJobWith(JobCondition.Incompletable);
                            return;
                        }
                        countToDrop = next.Count;
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
                        bool reserveDest = HaulersDreamMod.Settings == null || !HaulersDreamMod.Settings.haulToStack;
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
                        {
                            int relinked = UnloadableCountOf(inner[i]);
                            if (relinked <= 0)
                                break; // entirely keep-stock (see below) — leave it untagged where it is
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
                {
                    // Nothing above the pawn's keep count — the stack is personal stock, not surplus.
                    // Untag it (dumping it would make vanilla's restock re-fetch it: an endless loop).
                    carried.Remove(thing);
                    continue;
                }
                return new ThingCount(thing, count);
            }
            return default;
        }

        /// <summary>
        /// How many units of this stack are actually surplus. Vanilla's unload keeps the pawn's "items
        /// to keep" (mirrors Pawn_InventoryTracker.FirstUnloadableThing: drug-policy takeToInventory +
        /// inventoryStock entries); a harvested yield that MERGED into such a stock stack must only
        /// unload the excess — dumping the whole stack makes JobGiver_TakeForInventoryStock re-fetch it
        /// and loop forever. Clamped to (total inventory count of the def − keep), at most the stack.
        /// </summary>
        private int UnloadableCountOf(Thing thing)
        {
            int keep = KeepCountOf(pawn, thing.def) + FoodKeepCountOf(pawn, thing);
            if (keep <= 0)
                return thing.stackCount;
            int surplus = YieldRouter.InventoryCountOfDef(pawn.inventory.innerContainer, thing.def) - keep;
            return System.Math.Min(thing.stackCount, surplus);
        }

        /// <summary>
        /// Vanilla parity, the THIRD tmpItemsToKeep source in Pawn_InventoryTracker.FirstUnloadableThing:
        /// a colonist keeps packable food up to its food need's MaxLevel of nutrition (JobGiver_PackFood),
        /// so the unload must not strip a packed lunch that a harvested yield merged into — vanilla would
        /// just re-pack it (the same fetch-churn loop as the drug/stock sources, need-gated and rarer).
        /// Mirrors vanilla's math: keep = stackCount − k, k = the fewest units whose removal brings the
        /// pawn's total packable nutrition within MaxLevel; 0 when the whole stack is surplus.
        /// </summary>
        private static int FoodKeepCountOf(Pawn pawn, Thing thing)
        {
            if (!pawn.IsColonist || pawn.needs?.food == null)
                return 0;
            var def = thing.def;
            if (!def.IsNutritionGivingIngestible || def.IsDrug
                || !JobGiver_PackFood.IsGoodPackableFoodFor(thing, pawn, checkMass: false))
                return 0;
            float total = JobGiver_PackFood.GetInventoryPackableFoodNutrition(pawn);
            float maxLevel = pawn.needs.food.MaxLevel;
            float perUnit = thing.GetStatValue(StatDefOf.Nutrition);
            if (perUnit <= 0f || total - perUnit * thing.stackCount > maxLevel)
                return 0; // even without this entire stack the pawn is over its cap — all surplus
            int k = 0;
            while (total - perUnit * k > maxLevel)
                k++;
            return thing.stackCount - k;
        }

        /// <summary>Vanilla parity: the count of this def the pawn wants to KEEP in inventory — drug
        /// policy entries with takeToInventory &gt; 0 plus inventoryStock stock entries (two of the three
        /// tmpItemsToKeep sources in Pawn_InventoryTracker.FirstUnloadableThing; the third, packable
        /// food, is per-stack nutrition math — see <see cref="FoodKeepCountOf"/>).</summary>
        private static int KeepCountOf(Pawn pawn, ThingDef def)
        {
            int keep = 0;
            var policy = pawn.drugs?.CurrentPolicy;
            if (policy != null)
                for (int i = 0; i < policy.Count; i++)
                    if (policy[i].drug == def && policy[i].takeToInventory > 0)
                        keep += policy[i].takeToInventory;
            var stockEntries = pawn.inventoryStock?.stockEntries;
            if (stockEntries != null)
                foreach (var entry in stockEntries.Values)
                    if (entry != null && entry.thingDef == def)
                        keep += entry.count;
            // Under CE the pawn's assigned loadout (ammo/sidearm reserve) is personal stock too — keep it.
            keep += CECompat.LoadoutKeepCount(pawn, def);
            return keep;
        }
    }
}
