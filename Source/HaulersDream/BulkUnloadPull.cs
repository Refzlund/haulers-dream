using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>Shared carrier/hold selection and physical transfer. Callers retain target permission,
    /// reservation, load ownership, job lifetime and destination delivery responsibilities.</summary>
    internal static class BulkUnloadPull
    {
        [ThreadStatic] private static List<BulkUnloadCarrierPolicy.CarrierStack> scratch;

        internal static bool IsCargo(Thing thing) => thing != null && !thing.Destroyed && !(thing is Pawn) && thing.stackCount > 0;

        internal static bool Eligible(Thing thing, Pawn pawn, bool respectForbidden)
            => IsCargo(thing) && (!respectForbidden || !thing.IsForbidden(pawn));

        private static bool BackpackAllowed(Thing thing, bool playerOrdered)
        {
            if (!(thing is Corpse)) return true;
            var settings = HaulersDreamMod.Settings;
            return CorpseSweepPolicy.CanSweepAsNeighbor(true, settings?.bulkHaulCorpses ?? true,
                (settings?.autoStripMode ?? AutoStripMode.AllHauls) == AutoStripMode.DisposalOnly, playerOrdered);
        }

        internal static bool TrySelect(Pawn pawn, ThingOwner source, bool respectForbidden, bool playerOrdered,
            out Thing thing, out int count, out bool toHands, Predicate<Thing> selectFilter = null)
        {
            thing = null; count = 0; toHands = false;
            if (source == null || pawn?.inventory == null || pawn.carryTracker?.innerContainer == null
                || pawn.carryTracker.innerContainer.Count != 0 || pawn.GetComp<CompHauledToInventory>() == null) return false;
            var stacks = scratch ?? (scratch = new List<BulkUnloadCarrierPolicy.CarrierStack>());
            stacks.Clear();
            for (int i = 0; i < source.Count; i++)
                if (Eligible(source[i], pawn, respectForbidden))
                    stacks.Add(new BulkUnloadCarrierPolicy.CarrierStack(i, source[i].GetStatValue(StatDefOf.Mass), source[i].stackCount));
            float freeSpace = MassUtility.FreeSpace(pawn);
            while (stacks.Count > 0)
            {
                var plan = BulkUnloadCarrierPolicy.PlanNextPull(stacks, freeSpace);
                if (plan.ChosenIndex < 0) return false;
                Thing selected = source[plan.ChosenIndex];
                if (!plan.ToHands)
                {
                    if (!BackpackAllowed(selected, playerOrdered))
                        plan = new BulkUnloadCarrierPolicy.PullPlan(plan.ChosenIndex, selected.stackCount, toHands: true);
                    else
                        plan = BulkUnloadCarrierPolicy.ApplyInventoryFit(plan, stacks, CECompat.MaxFitCount(pawn, selected));
                }
                if (plan.ChosenIndex < 0 || plan.ChosenIndex >= source.Count || plan.Count <= 0) return false;
                // Storage lookup is expensive: probe only the selected candidate (including any CE hands
                // fallback), not every stack in the hold for every pull. Rejected candidates are finite.
                if (selectFilter != null && !selectFilter(source[plan.ChosenIndex]))
                { stacks.RemoveAll(candidate => candidate.Index == plan.ChosenIndex); continue; }
                thing = source[plan.ChosenIndex]; count = plan.Count; toHands = plan.ToHands;
                return Eligible(thing, pawn, respectForbidden) && source.Contains(thing);
            }
            return false;
        }

        // The caller must recheck target/operation admission immediately before calling, after any visual delay.
        // The selection is only a hint: actual ownership, count, capacity, forbiddance and corpse policy are live.
        internal static int Transfer(Pawn pawn, ThingOwner source, Thing thing, int requested, bool toHands,
            bool respectForbidden, bool playerOrdered, out Thing movedThing, out bool movedToHands)
        {
            movedThing = null; movedToHands = toHands;
            var cargo = pawn?.GetComp<CompHauledToInventory>();
            if (source == null || !Eligible(thing, pawn, respectForbidden) || !source.Contains(thing)
                || !ReferenceEquals(thing.holdingOwner, source) || requested <= 0 || cargo == null
                || pawn.inventory?.innerContainer == null || pawn.carryTracker?.innerContainer == null
                || pawn.carryTracker.innerContainer.Count != 0) return 0;
            int count = Math.Min(requested, thing.stackCount);
            if (!movedToHands)
            {
                int fit = Math.Min(BulkUnloadCarrierPolicy.PullCountWithinFreeSpace(MassUtility.FreeSpace(pawn),
                    thing.GetStatValue(StatDefOf.Mass), thing.stackCount), CECompat.MaxFitCount(pawn, thing));
                if (fit <= 0 || !BackpackAllowed(thing, playerOrdered)) movedToHands = true;
                else count = Math.Min(count, fit);
            }
            ThingOwner destination = movedToHands ? pawn.carryTracker.innerContainer : pawn.inventory.innerContainer;
            int moved = source.TryTransferToContainer(thing, destination, count, out movedThing, canMergeWithExistingStacks: false);
            if (moved > 0 && movedThing != null && !movedToHands) cargo.RegisterHauledItem(movedThing);
            return moved;
        }
    }
}
