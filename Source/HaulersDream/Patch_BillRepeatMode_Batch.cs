using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The Batch-Y bill mode UI + lifecycle. "Batch" is deliberately NOT a new <see cref="BillRepeatModeDef"/>:
    /// RimWorld's repeat-mode system is hardcoded (Bill_Production.ShouldDoNow / RepeatInfoText THROW on an
    /// unrecognised mode every frame), so batch is instead an HD FLAG layered on TOP of the three vanilla modes.
    /// The repeat-mode dropdown gains three "Batch: …" entries that set the SAME underlying vanilla repeatMode the
    /// matching plain entry would — so vanilla's counting/gating/+/- UI keep working untouched — plus the HD flag.
    /// A flagged bill is then routed to <see cref="JobDriver_BatchCraft"/> by Patch_WorkGiver_DoBill_BatchRoute.
    /// The per-bill flag+size persist in <see cref="HaulersDreamGameComponent"/> keyed by bill loadID.
    /// </summary>
    [HarmonyPatch(typeof(BillRepeatModeUtility), nameof(BillRepeatModeUtility.MakeConfigFloatMenu))]
    public static class Patch_BillRepeatModeUtility_MakeConfigFloatMenu
    {
        static bool Prefix(Bill_Production bill)
        {
            var comp = HaulersDreamGameComponent.Instance;
            var opts = new List<FloatMenuOption>();

            // --- the three vanilla modes (faithful copies of MakeConfigFloatMenu; picking one turns batch OFF) ---
            opts.Add(new FloatMenuOption(BillRepeatModeDefOf.RepeatCount.LabelCap, delegate
            {
                bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
                comp?.SetBatch(bill, false, 0);
            }));
            opts.Add(new FloatMenuOption(BillRepeatModeDefOf.TargetCount.LabelCap, delegate
            {
                if (!bill.recipe.WorkerCounter.CanCountProducts(bill))
                    Messages.Message("RecipeCannotHaveTargetCount".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                else
                {
                    bill.repeatMode = BillRepeatModeDefOf.TargetCount;
                    comp?.SetBatch(bill, false, 0);
                }
            }));
            opts.Add(new FloatMenuOption(BillRepeatModeDefOf.Forever.LabelCap, delegate
            {
                bill.repeatMode = BillRepeatModeDefOf.Forever;
                comp?.SetBatch(bill, false, 0);
            }));

            // --- the three batch variants (only when this recipe can actually be batched) ---
            if (comp != null && CraftBatchPlanner.CanBatch(bill))
            {
                string prefix = "HaulersDream.Batch.MenuPrefix".Translate();
                opts.Add(new FloatMenuOption(prefix + ": " + BillRepeatModeDefOf.RepeatCount.LabelCap, delegate
                {
                    bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
                    EnableBatch(comp, bill);
                }));
                opts.Add(new FloatMenuOption(prefix + ": " + BillRepeatModeDefOf.TargetCount.LabelCap, delegate
                {
                    if (!bill.recipe.WorkerCounter.CanCountProducts(bill))
                        Messages.Message("RecipeCannotHaveTargetCount".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                    else
                    {
                        bill.repeatMode = BillRepeatModeDefOf.TargetCount;
                        EnableBatch(comp, bill);
                    }
                }));
                opts.Add(new FloatMenuOption(prefix + ": " + BillRepeatModeDefOf.Forever.LabelCap, delegate
                {
                    bill.repeatMode = BillRepeatModeDefOf.Forever;
                    EnableBatch(comp, bill);
                }));

                // Per-bill size: a small slider popup (the bills tab row is too cramped for an inline stepper).
                // Shown only while this bill is batching; the label carries the current size.
                if (comp.IsBatchBill(bill))
                    opts.Add(new FloatMenuOption("HaulersDream.Batch.SetSize".Translate(comp.BatchSizeOf(bill)), delegate
                    {
                        Find.WindowStack.Add(new Dialog_BatchSize(bill));
                    }));
            }

            Find.WindowStack.Add(new FloatMenu(opts));
            return false; // fully replaces vanilla's 3-entry menu
        }

        // Turn batching on, keeping any size the bill already had; a fresh batch starts at the settings default.
        private static void EnableBatch(HaulersDreamGameComponent comp, Bill_Production bill)
        {
            int size = comp.BatchSizeOf(bill);
            if (size < 1)
                size = Mathf.Max(1, HaulersDreamMod.Settings?.defaultBatchSize ?? 10);
            comp.SetBatch(bill, true, size);
        }
    }

    /// <summary>Prepend the batch size to the bill row's repeat-info text (e.g. "×10 12/40") so a batching bill
    /// is recognisable at a glance — the cramped bills tab row has no room for an inline size control.</summary>
    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.RepeatInfoText), MethodType.Getter)]
    public static class Patch_Bill_Production_RepeatInfoText
    {
        static void Postfix(Bill_Production __instance, ref string __result)
        {
            var comp = HaulersDreamGameComponent.Instance;
            // Gate on CanBatch too: a bill flagged in a save whose recipe is NOT batchable (smelting / take-entire-
            // stacks / unfinished-thing / a non-Bill_Production) still carries the flag but routes as vanilla, so
            // don't show a misleading "×N" marker the batch driver will never honour. NOTE: mixing recipes (cooked
            // meals, kibble, pemmican, chemfuel, beer) ARE batchable now via the mix-aware per-rep value-fill path,
            // so CanBatch returns true for them and the marker correctly shows.
            if (comp != null && comp.IsBatchBill(__instance) && CraftBatchPlanner.CanBatch(__instance))
                __result = "HaulersDream.Batch.RowMarker".Translate(comp.BatchSizeOf(__instance)) + __result;
        }
    }

    /// <summary>Carry a bill's batch state through a clone (the row "copy" button / clipboard paste). The clone
    /// does NOT have its real loadID inside <c>Bill_Production.Clone()</c> — <c>Bill.Clone()</c> is
    /// <c>Activator.CreateInstance</c>, and the caller assigns the id via <c>InitializeAfterClone()</c> AFTER
    /// Clone() returns — so reading <c>GetUniqueLoadID()</c> here would key the dict under a bogus "_-1". Instead
    /// we stash the source's state against the CLONE OBJECT and write it to the dict (under the now-real loadID)
    /// in the AddBill postfix. We stash even "not batching" (size 0) so AddBill can tell a pasted plain bill from
    /// a brand-new one and NOT apply the batch-by-default to it.</summary>
    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.Clone))]
    public static class Patch_Bill_Production_Clone
    {
        // Weak keys: an un-pasted clipboard clone is collected without leaking; entries are drained in AddBill.
        internal static readonly ConditionalWeakTable<Bill, StrongBox<int>> Carry =
            new ConditionalWeakTable<Bill, StrongBox<int>>();

        static void Postfix(Bill_Production __instance, ref Bill __result)
        {
            if (!(__result is Bill_Production clone))
                return;
            var comp = HaulersDreamGameComponent.Instance;
            int size = 0; // 0 = source was NOT batching (kept so AddBill won't re-batch a pasted plain bill)
            if (comp != null && comp.IsBatchBill(__instance))
                size = comp.BatchSizeOf(__instance);              // source is a live bill (real loadID)
            else if (Carry.TryGetValue(__instance, out var box))
                size = box.Value;                                  // source is itself an un-IDed clone (clipboard)
            Carry.Remove(clone);
            Carry.Add(clone, new StrongBox<int>(size));
        }
    }

    /// <summary>Drain a clone's carried batch state once it's added with its real loadID; otherwise, when "batch
    /// new bills by default" is on, start a freshly-added batchable bill in batch mode. A carried entry (copy/paste)
    /// always wins over the default, INCLUDING a carried "not batching" (so a pasted plain bill stays plain even
    /// with the default on). BillStack.AddBill is not called during save load, so loaded bills are untouched.</summary>
    [HarmonyPatch(typeof(BillStack), nameof(BillStack.AddBill))]
    public static class Patch_BillStack_AddBill_BatchDefault
    {
        static void Postfix(Bill bill)
        {
            var comp = HaulersDreamGameComponent.Instance;
            if (comp == null)
                return;
            if (Patch_Bill_Production_Clone.Carry.TryGetValue(bill, out var box))
            {
                Patch_Bill_Production_Clone.Carry.Remove(bill);
                if (box.Value > 0)
                    comp.SetBatch(bill, true, box.Value);          // copied/pasted batch bill keeps its exact size
                return;                                            // a clone never falls through to the default
            }
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.batchByDefault)
                return;
            if (comp.IsBatchBill(bill) || !CraftBatchPlanner.CanBatch(bill))
                return;
            comp.SetBatch(bill, true, Mathf.Max(1, s.defaultBatchSize));
        }
    }
}
