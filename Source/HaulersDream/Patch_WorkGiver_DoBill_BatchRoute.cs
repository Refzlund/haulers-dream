using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Auto-dispatch for the Batch-Y bill mode: when vanilla <see cref="WorkGiver_DoBill"/> hands back a DoBill
    /// job for a bill the player flagged to BATCH, swap it for a <see cref="JobDriver_BatchCraft"/> job sized by
    /// <see cref="CraftBatchPlanner"/> — so the colonist fetches a whole batch of ingredients in one trip, makes
    /// them all at the bench, then hauls everything out. Works for every workbench automatically.
    ///
    /// Runs at <see cref="Priority.First"/> so it converts BEFORE Patch_WorkGiver_DoBill_InventoryRoute and the
    /// shared-inventory meet-in-the-middle postfix — once the result is a HaulersDream_BatchCraft job (not DoBill),
    /// those two see a non-DoBill result and correctly leave it alone, so a batch bill batches rather than being
    /// turned into a plain one-trip gather. Mirrors the inventory-route patch's structure.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_WorkGiver_DoBill_BatchRoute
    {
        [HarmonyPriority(Priority.First)]
        static void Postfix(ref Job __result, Pawn pawn, Thing thing, bool forced)
        {
            // Cheap gates FIRST (ref/type checks) so the per-pawn-scan reflection in OwnsDoBillFlow runs only on a
            // real convertible DoBill-at-a-workbench job. Reordering ahead of the cede check is behaviour-identical:
            // each gate below would short-circuit the postfix anyway, so OwnsDoBillFlow's value is moot when it bails.
            var job = __result;
            if (job == null || job.def != JobDefOf.DoBill || job.bill?.recipe == null)
                return;
            // Workbenches only — never a Pawn bill giver (surgery) / other special giver, and never an autonomous
            // worktable (mech gestator family): the building processes its own recipe and ingredients must be
            // DEPOSITED into its container, so a pawn-driven batch-craft that gathers into inventory is invalid there.
            if (!BillRouteGate.MayRouteToInventory(job.targetA.Thing))
                return;
            var bench = (Building_WorkTable)job.targetA.Thing;
            // Cede to Common Sense when it owns the DoBill driver — don't batch-convert into a re-haul loop.
            // (Moved below the cheap gates above: the reflective toggle read now happens only on a convertible job.)
            if (CommonSenseCompat.OwnsDoBillFlow)
                return;
            var bill = job.bill;

            var comp = HaulersDreamGameComponent.Instance;
            if (comp == null || !comp.IsBatchBill(bill))
                return;
            // The exact pawn/bill gate vanilla's WorkGiver applies (skill range, restriction, slavery, …) plus the
            // tracking-comp requirement — the batch tags products/leftovers for the unload pass.
            if (!CraftBatchPlanner.CanPawnBatch(pawn, bill))
                return;
            // Re-batch guard: if the pawn already holds tagged stock tied to this bill (a just-finished batch's
            // leftovers or products pending unload, or a "Batch forever" cycle's residue), don't stack a new batch
            // on top — let vanilla run / let the unload happen first. Critically this also prevents a TargetCount
            // overshoot: products banked in inventory are invisible to vanilla's CountProducts, so re-batching
            // before they're hauled out would read a stale shortfall and over-produce.
            if (HoldsBatchStockForBill(pawn, bill))
                return;

            int size = comp.BatchSizeOf(bill);
            if (size < 1)
                return;
            // No timeout for an automatic batch: the requested size and the bill's own remaining count bound it,
            // and an interruption only loses the in-progress item (everything else is carried back).
            var plan = CraftBatchPlanner.Resolve(pawn, bench, bill, size, 0);
            if (!plan.feasible)
                return; // can't batch right now (e.g. < 1 rep of reachable materials) → leave vanilla's single rep

            var bj = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_BatchCraft, job.targetA);
            bj.count = 1; // sentinel (real amounts come from the plan); vanilla TakeToInventory red-errors on count <= 0
            bj.bill = bill;
            // Forcing a batch bill ("do now") runs the WHOLE batch, consistent with the configured mode.
            if (forced || job.playerForced)
                bj.playerForced = true;
            BatchCraftHandoff.Set(bj, plan);
            __result = bj;

            if (HaulersDreamMod.Settings?.verboseLogging ?? false)
                HDLog.Dbg($"[Batch] {pawn} batch-crafting {plan.resolvedReps}× {bill.recipe.defName} (requested {size}).");
        }

        /// <summary>Does the pawn already carry tagged stock tied to this bill — either a leftover INGREDIENT still
        /// usable for it, or a PRODUCT of it not yet hauled out? Either means a batch is pending unload.</summary>
        private static bool HoldsBatchStockForBill(Pawn pawn, Bill bill)
        {
            var comp = pawn.GetComp<CompHauledToInventory>();
            var owner = pawn.inventory?.innerContainer;
            if (comp == null || owner == null || bill?.recipe == null)
                return false;
            var products = bill.recipe.products;
            // Butchery/smelting yields are dynamic specialProducts (not in recipe.products), so their defs can't
            // be matched individually; for such recipes any genuinely-surplus tagged stock counts as pending.
            bool hasSpecialProducts = !bill.recipe.specialProducts.NullOrEmpty();
            foreach (var tagged in comp.GetHashSet())
            {
                if (tagged == null || !owner.Contains(tagged))
                    continue;
                // A leftover INGREDIENT still usable for the bill: don't re-gather a fresh batch on top of it (any mode).
                if (InventoryShare.IsUsableForBill(tagged, bill))
                    return true;
                // A made PRODUCT only signals a "batch pending unload" if it is genuinely SURPLUS (will actually
                // move out). A product the cook KEEPS — e.g. a lavish meal a "Lavish"-restricted cook holds as a
                // packed lunch, whose SurplusOf is 0 — lingers in inventory forever and must NOT block re-batching,
                // or the bill batches exactly once then drops to vanilla single-item crafting. (That was the
                // "Batch only worked the first time" bug.) Gating on real surplus mirrors what the unload pass does:
                // while genuinely-surplus products are still held the batch IS mid-unload, so deferring is correct;
                // once they've unloaded, only the kept lunch remains and re-batching proceeds.
                if (InventorySurplus.SurplusOf(pawn, tagged) <= 0)
                    continue;
                if (hasSpecialProducts)
                    return true;
                if (products != null)
                    for (int i = 0; i < products.Count; i++)
                        if (products[i]?.thingDef == tagged.def)
                            return true;
            }
            return false;
        }
    }
}
