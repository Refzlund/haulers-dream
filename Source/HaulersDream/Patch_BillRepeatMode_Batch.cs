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
        // Run FIRST among prefixes so HD's prefix is the one that executes when another mod ALSO full-replaces
        // this method with a return-false prefix (e.g. Ingredient Threshold). Harmony runs prefixes
        // highest-priority first, and a prefix that returns false skips every remaining prefix AND the original
        // (the generated wrapper is `if (runOriginal) runOriginal = prefix(...)`), so among competing
        // full-replace prefixes ONLY the highest-priority one runs. At Priority.First HD wins that race and
        // re-adds the other mods' modes (the *Compat.TryInsertModes calls) so its menu is the COMPLETE one; at a
        // lower priority HD would be skipped entirely and its own "Batch: ..." modes (and the re-added compat
        // modes) would vanish. Transpiler-based mods (EGO, Compositable Loadouts) live in the original body,
        // which HD's return-false skips, so they are re-added via the shims the same way regardless.
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Bill_Production bill)
        {
            var comp = HaulersDreamGameComponent.Instance;
            var opts = new List<FloatMenuOption>();

            // --- the three vanilla modes (faithful copies of MakeConfigFloatMenu; picking one turns batch OFF) ---
            // MP: SetBatch writes the SCRIBED batchBills dict. These delegates fire from an interactive float-menu
            // pick, so the write must go through the [SyncMethod] shim to replay on every client (runs inline in SP).
            // bill.repeatMode is vanilla's own field and is already synced by RimWorld's bill-config UI, so only the
            // HD batch write needs routing.
            opts.Add(new FloatMenuOption(BillRepeatModeDefOf.RepeatCount.LabelCap, delegate
            {
                bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
                MultiplayerCompat.SetBillBatch(bill, false, 0);
            }));
            opts.Add(new FloatMenuOption(BillRepeatModeDefOf.TargetCount.LabelCap, delegate
            {
                if (!bill.recipe.WorkerCounter.CanCountProducts(bill))
                    Messages.Message("RecipeCannotHaveTargetCount".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                else
                {
                    bill.repeatMode = BillRepeatModeDefOf.TargetCount;
                    MultiplayerCompat.SetBillBatch(bill, false, 0);
                }
            }));
            opts.Add(new FloatMenuOption(BillRepeatModeDefOf.Forever.LabelCap, delegate
            {
                bill.repeatMode = BillRepeatModeDefOf.Forever;
                MultiplayerCompat.SetBillBatch(bill, false, 0);
            }));

            // --- COMPAT: re-add any OTHER mod's custom repeat modes that we just skipped by replacing this menu.
            // Everybody Gets One adds its "one per person" / "X per person" / "with surplus" modes, and Compositable
            // Loadouts adds its "X per Tag" mode, each via a transpiler on this very method; our full-replace prefix
            // would hide them entirely. Invoke each mod's own inserter so its modes reappear with that mod's exact
            // labels + guards. No-op when the mod isn't installed. (HD never batches a non-vanilla repeat mode —
            // CraftBatchPlanner.CanBatch only accepts the three vanilla modes — so those bills route as plain vanilla.) ---
            EverybodyGetsOneCompat.TryInsertModes(opts, bill);
            CompositableLoadoutsCompat.TryInsertModes(opts, bill);
            // Ingredient Threshold adds its mode with its OWN return-false prefix on this method (not a transpiler).
            // Two competing full-replace prefixes: only the highest-priority one runs (see the Priority.First note
            // above), so HD wins and IT's prefix never runs. Re-add its mode here so HD's winning menu still offers
            // it and its "Ingredient Threshold" mode stays selectable (issue #126).
            IngredientThresholdCompat.TryInsertModes(opts, bill);

            // --- the three batch variants (only when this recipe can actually be batched) ---
            // Each carries a hover tooltip (FloatMenuOption.tooltip) explaining what that batch mode does (issue #3
            // also asks for dropdown tooltips). The tooltip is set on the option after construction.
            // Hidden entirely while Common Sense is suppressing batching (its opt-in is OFF and CS owns the cook
            // flow): batch-flagged bills won't actually batch in that state, so offering the modes would mislead —
            // the player just sees the vanilla modes. Same predicate the route conversion uses (single source).
            if (comp != null && CraftBatchPlanner.CanBatch(bill) && !CommonSenseCompat.BatchSuppressedByCommonSense)
            {
                string prefix = "HaulersDream.Batch.MenuPrefix".Translate();
                var optDoX = new FloatMenuOption(prefix + ": " + BillRepeatModeDefOf.RepeatCount.LabelCap, delegate
                {
                    bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
                    EnableBatch(comp, bill);
                });
                optDoX.tooltip = "HaulersDream.Batch.TipDoX".Translate();
                opts.Add(optDoX);

                var optUntil = new FloatMenuOption(prefix + ": " + BillRepeatModeDefOf.TargetCount.LabelCap, delegate
                {
                    if (!bill.recipe.WorkerCounter.CanCountProducts(bill))
                        Messages.Message("RecipeCannotHaveTargetCount".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                    else
                    {
                        bill.repeatMode = BillRepeatModeDefOf.TargetCount;
                        EnableBatch(comp, bill);
                    }
                });
                optUntil.tooltip = "HaulersDream.Batch.TipUntilX".Translate();
                opts.Add(optUntil);

                var optForever = new FloatMenuOption(prefix + ": " + BillRepeatModeDefOf.Forever.LabelCap, delegate
                {
                    bill.repeatMode = BillRepeatModeDefOf.Forever;
                    EnableBatch(comp, bill);
                });
                optForever.tooltip = "HaulersDream.Batch.TipForever".Translate();
                opts.Add(optForever);

                // Per-bill size: a small slider popup (the bills tab row is too cramped for an inline stepper).
                // Shown only while this bill is batching; the label carries the current size.
                if (comp.IsBatchBill(bill))
                {
                    var optSize = new FloatMenuOption("HaulersDream.Batch.SetSize".Translate(comp.BatchSizeOf(bill)), delegate
                    {
                        Find.WindowStack.Add(new Dialog_BatchSize(bill));
                    });
                    optSize.tooltip = "HaulersDream.Batch.TipSize".Translate();
                    opts.Add(optSize);

                    // Per-bill "overshoot by Y" (issue #3): meaningful ONLY for a Do-until-X (TargetCount) batch —
                    // it widens the stop target from X to X+Y. Shown only when this bill is batching AND in
                    // TargetCount mode; the label carries the current Y (0 = off / stop exactly at X).
                    if (bill.repeatMode == BillRepeatModeDefOf.TargetCount)
                    {
                        var optOver = new FloatMenuOption("HaulersDream.Batch.SetOvershoot".Translate(comp.BatchOvershootOf(bill)), delegate
                        {
                            Find.WindowStack.Add(new Dialog_BatchOvershoot(bill));
                        });
                        optOver.tooltip = "HaulersDream.Batch.TipOvershoot".Translate();
                        opts.Add(optOver);
                    }
                }
            }

            Find.WindowStack.Add(new FloatMenu(opts));
            return false; // fully replaces vanilla's 3-entry menu
        }

        // Turn batching on, keeping any size the bill already had; a fresh batch starts at the settings default.
        // Called ONLY from the three interactive "Batch: …" float-menu delegates above, so this is a UI write:
        // MP-route it through the [SyncMethod] shim (writes the SCRIBED batchBills dict on every client; inline in
        // SP). The size is resolved locally first (read of the current value, fall back to the settings default), so
        // the synced command carries an absolute size and is idempotent across clients.
        private static void EnableBatch(HaulersDreamGameComponent comp, Bill_Production bill)
        {
            int size = comp.BatchSizeOf(bill);
            if (size < 1)
                size = Mathf.Max(1, HaulersDreamMod.Settings?.defaultBatchSize ?? 10);
            MultiplayerCompat.SetBillBatch(bill, true, size);
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
            // Gate the marker on CanBatch too: a bill that still carries the batch FLAG but is NOT actually batched
            // must not show a misleading "×N". Three ways that happens: (a) a save whose recipe is NOT batchable
            // (smelting / take-entire-stacks / unfinished-thing / a non-Bill_Production) keeps the flag but routes as
            // vanilla; (b) its repeat mode is now a non-vanilla one HD won't batch (e.g. an Everybody Gets One mode —
            // see CraftBatchPlanner.CanBatch); (c) Common Sense is suppressing batching (its opt-in is OFF and CS
            // owns the cook flow), so the flagged bill routes as a plain CS-handled bill — match the hidden dropdown
            // options. NOTE: mixing recipes (cooked meals, kibble, pemmican, chemfuel, beer) ARE batchable via the
            // mix-aware per-rep value-fill path, so CanBatch returns true and the marker shows.
            if (comp != null && comp.IsBatchBill(__instance) && CraftBatchPlanner.CanBatch(__instance)
                && !CommonSenseCompat.BatchSuppressedByCommonSense)
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
        /// <summary>The batch state carried from a source bill to its clone: <see cref="size"/> (0 = source was NOT
        /// batching, so AddBill won't re-batch a pasted plain bill) and <see cref="overshoot"/> (the "overshoot by Y"
        /// amount, carried alongside size so a pasted batch bill keeps both — issue #3).</summary>
        internal sealed class CarryState
        {
            public int size;
            public int overshoot;
            public CarryState(int size, int overshoot) { this.size = size; this.overshoot = overshoot; }
        }

        // Weak keys: an un-pasted clipboard clone is collected without leaking; entries are drained in AddBill.
        internal static readonly ConditionalWeakTable<Bill, CarryState> Carry =
            new ConditionalWeakTable<Bill, CarryState>();

        static void Postfix(Bill_Production __instance, ref Bill __result)
        {
            if (!(__result is Bill_Production clone))
                return;
            var comp = HaulersDreamGameComponent.Instance;
            int size = 0;       // 0 = source was NOT batching (kept so AddBill won't re-batch a pasted plain bill)
            int overshoot = 0;  // carried only when the source was batching (overshoot is meaningless on a plain bill)
            if (comp != null && comp.IsBatchBill(__instance))
            {
                size = comp.BatchSizeOf(__instance);              // source is a live bill (real loadID)
                overshoot = comp.BatchOvershootOf(__instance);
            }
            else if (Carry.TryGetValue(__instance, out var prev))
            {
                size = prev.size;                                  // source is itself an un-IDed clone (clipboard)
                overshoot = prev.overshoot;
            }
            Carry.Remove(clone);
            Carry.Add(clone, new CarryState(size, overshoot));
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
            // MP: these two SetBatch writes deliberately stay on the DIRECT (non-synced) path — do NOT route them
            // through MultiplayerCompat.SetBillBatch. This postfix fires from BillStack.AddBill, which runs either in
            // an already-synced bill-creation/paste command or from in-tick code (debug actions, other mods) — either
            // way it executes on EVERY client, deterministically, against a bill constructed identically on each (same
            // loadID). The write is therefore already replayed everywhere; calling our own [SyncMethod] here would be
            // an illegal NESTED sync command (MP disallows issuing a sync method while executing one) and would
            // double-apply. (AddBill is not called during save load, so loaded bills are untouched — see Clone above.)
            var comp = HaulersDreamGameComponent.Instance;
            if (comp == null)
                return;
            if (Patch_Bill_Production_Clone.Carry.TryGetValue(bill, out var carried))
            {
                Patch_Bill_Production_Clone.Carry.Remove(bill);
                if (carried.size > 0)
                {
                    comp.SetBatch(bill, true, carried.size);       // copied/pasted batch bill keeps its exact size
                    comp.SetBatchOvershoot(bill, carried.overshoot); // …and its overshoot-by-Y (0 = none; key removed)
                }
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
