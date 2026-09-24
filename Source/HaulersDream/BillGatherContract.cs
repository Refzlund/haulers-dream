using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>Live validation and context copying for one retained native ingredient selection.</summary>
    internal static class BillGatherContract
    {
        internal static void CopyContext(Job from, Job to)
        {
            to.bill = from.bill;
            to.haulMode = from.haulMode;
            to.workGiverDef = from.workGiverDef;
            to.jobGiver = from.jobGiver;
            to.jobGiverThinkTree = from.jobGiverThinkTree;
            to.locomotionUrgency = from.locomotionUrgency;
            to.expiryInterval = from.expiryInterval;
            to.checkOverrideOnExpire = from.checkOverrideOnExpire;
            to.ignoreForbidden = from.ignoreForbidden;
            to.ignoreDesignations = from.ignoreDesignations;
            to.canBashDoors = from.canBashDoors;
            to.canBashFences = from.canBashFences;
            to.collideWithPawns = from.collideWithPawns;
            to.source = from.source;
            // Neither loadID/startTick nor mutable queues/placedThings belong to a new job.
        }

        internal static bool BillMayStart(Pawn pawn, Job job)
        {
            var bill = job?.bill;
            var bench = job?.targetA.Thing as Building_WorkTable;
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.Drafted || pawn.InMentalState
                || !pawn.Spawned || !BillRouteGate.WorkerMayShareCraft(pawn)
                || bench == null || !BillRouteGate.IsRoutableBenchType(bench)
                || !bench.Spawned || bench.Map != pawn.Map || bench.IsForbidden(pawn) || bench.IsBurning()
                || !bench.CurrentlyUsableForBills() || bill?.recipe == null
                || bill.billStack == null || bill.billStack != bench.BillStack
                || bill.DeletedOrDereferenced || bill.suspended
                || !bench.BillStack.Bills.Contains(bill) || !bill.ShouldDoNow()
                || !bill.PawnAllowedToStartAnew(pawn)
                || bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn) != null)
                return false;
            if (bill is Bill_ProductionWithUft withUft && withUft.BoundUft != null)
                return false; // another actor has started unfinished work since this selection
            var requiredWork = bill.recipe.requiredGiverWorkType ?? job.workGiverDef?.workType;
            if (requiredWork != null && WorkCapabilityProbe.IsDisabled(pawn, requiredWork))
                return false;
            if (bill is Bill_Production production && production.repeatMode == BillRepeatModeDefOf.TargetCount
                && !production.recipe.products.NullOrEmpty()
                && !BatchPausePolicy.MayCraftMore(CraftBatchPlanner.EffectiveProductCount(production),
                    production.targetCount, production.paused))
                return false;
            return true;
        }

        internal static bool UsableTarget(Pawn pawn, Job job, Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0
                || !thing.SpawnedOrAnyParentSpawned || thing.MapHeld != pawn.Map
                || thing.IsForbidden(pawn) || !InventoryShare.IsUsableForBill(thing, job.bill)
                || !pawn.CanReserve(thing)) return false;
            if (thing.ParentHolder is Pawn_InventoryTracker carrier)
            {
                if (carrier.pawn == pawn) return true; // owned stock has no walk or ingredient-radius requirement
                return InventoryShare.IsEligibleCarrier(carrier.pawn, pawn)
                    && WithinSearchRadius(job, carrier.pawn.Position)
                    && pawn.CanReach(carrier.pawn, PathEndMode.Touch, Danger.Some);
            }
            return thing.Spawned && WithinSearchRadius(job, thing.Position)
                && pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Some);
        }

        private static bool WithinSearchRadius(Job job, IntVec3 position)
        {
            float radius = job.bill.ingredientSearchRadius;
            return radius >= 999f || (position - job.targetA.Cell).LengthHorizontalSquared < radius * radius;
        }

        internal static bool TryValidateSelection(Pawn pawn, Job job,
            out List<Thing> targets, out List<int> counts)
        {
            targets = null;
            counts = null;
            if (job?.targetQueueB == null || job.countQueue == null || job.bill?.recipe == null)
                return false;
            var things = new List<Thing>(job.targetQueueB.Count);
            for (int i = 0; i < job.targetQueueB.Count; i++) things.Add(job.targetQueueB[i].Thing);
            if (!BillGatherSelection.TryNormalize(things, job.countQueue,
                    t => UsableTarget(pawn, job, t) ? t.stackCount : 0, out targets, out counts))
                return false;
            return SatisfiesRecipe(job.bill, targets, counts);
        }

        private static bool SatisfiesRecipe(Bill bill, List<Thing> targets, List<int> counts)
        {
            var recipe = bill.recipe;
            // These recipes deliberately consume whole objects under specialized native rules.
            if (recipe.ignoreIngredientCountTakeEntireStacks) return false;
            var slots = new List<IngredientCount>();
            foreach (var slot in recipe.ingredients) if (slot.GetBaseCount() > 0f) slots.Add(slot);
            var required = new double[slots.Count, targets.Count];
            var mixing = new bool[slots.Count];
            var kinds = new int[targets.Count];
            for (int j = 0; j < targets.Count; j++) kinds[j] = targets[j].def.shortHash;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                mixing[i] = recipe.allowMixingIngredients;
                for (int j = 0; j < targets.Count; j++)
                {
                    var t = targets[j];
                    if (!slot.filter.Allows(t) || (!slot.IsFixedIngredient && !bill.ingredientFilter.Allows(t)))
                        continue;
                    if (!recipe.allowMixingIngredients)
                        required[i, j] = slot.CountRequiredOfFor(t.def, recipe, bill);
                    else
                    {
                        float value = recipe.IngredientValueGetter.ValuePerUnitOf(t.def);
                        if (value > 0f) required[i, j] = slot.GetBaseCount() / (double)value;
                    }
                }
            }
            return BillGatherSelection.SatisfiesRecipe(counts.ToArray(), kinds, required, mixing);
        }
    }
}
