using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>Call-scoped composition before FloatMenu's own sorting, sizing and open sound.</summary>
    internal static class BillRepeatMenuComposition
    {
        internal sealed class Frame
        {
            internal Bill_Production Bill;
            internal Frame Previous;
            internal bool Closed, Composing;
            internal readonly HashSet<List<FloatMenuOption>> Outputs = new HashSet<List<FloatMenuOption>>();
            internal readonly HashSet<FloatMenuOption> BatchOptions = new HashSet<FloatMenuOption>();
        }

        [ThreadStatic] private static Frame current;

        internal static Frame Enter(Bill_Production bill)
        {
            BillRepeatMenuProviders.EnsureInitialized();
            var frame = new Frame { Bill = bill, Previous = current };
            current = frame;
            return frame;
        }

        internal static void Exit(Frame frame)
        {
            if (frame == null)
                return; // Our side-effecting prefix may have been skipped by an unsupported earlier creator.
            frame.Closed = true;
            // Normally strictly LIFO. Marking frames closed also avoids reviving a completed outer scope if
            // another patch caused unusual finalizer ordering during a nested exception.
            while (current != null && current.Closed)
                current = current.Previous;
        }

        internal static List<FloatMenuOption> Augment(List<FloatMenuOption> original)
        {
            var frame = current;
            if (frame == null || frame.Closed || frame.Composing || frame.Bill == null
                || !BillRepeatMenuProviders.Ready || original == null || frame.Outputs.Contains(original))
                return original;
            // Scope alone does not identify a menu. A contributor may open an unrelated popup inside this call.
            // A known action must capture this exact bill; labels, the open window, and arbitrary closures are
            // never used as identity. A wholly unknown replacement is retained without speculative additions.
            if (!original.Any(option => BillRepeatMenuProviders.Describe(option, frame.Bill, out _, out _)))
                return original;
            frame.Composing = true;
            try
            {
                var options = new List<FloatMenuOption>(original);
                RepairTargetCountProxy(options, frame.Bill);
                MergeProvider(options, frame.Bill, BillRepeatMenuKind.Everybody);
                MergeProvider(options, frame.Bill, BillRepeatMenuKind.Loadouts);
                MergeProvider(options, frame.Bill, BillRepeatMenuKind.Periodic);
                MergeProvider(options, frame.Bill, BillRepeatMenuKind.Threshold);
                // FloatMenu copies/sorts our list. Recognize our own option objects too if a controlled creator
                // reuses that copied list inside this same call; do not append a second set of batch controls.
                if (!options.Any(frame.BatchOptions.Contains))
                {
                    int beforeBatch = options.Count;
                    Patch_BillRepeatModeUtility_MakeConfigFloatMenu.AddBatchOptions(options, frame.Bill);
                    for (int i = beforeBatch; i < options.Count; i++)
                        frame.BatchOptions.Add(options[i]);
                }
                frame.Outputs.Add(options);
                return options;
            }
            finally
            {
                frame.Composing = false;
            }
        }

        internal static void MergeProvider(List<FloatMenuOption> options, Bill_Production bill, BillRepeatMenuKind kind)
        {
            var provider = BillRepeatMenuProviders.Get(kind);
            if (provider == null || options == null || bill == null)
                return;
            List<FloatMenuOption> additions = null;
            // PB's other three actions are its native entries, including its original disabled TargetCount.
            // They are not missing provider contributions and must not be reconstructed here.
            var fields = kind == BillRepeatMenuKind.Periodic ? provider.Modes.Skip(3) : provider.Modes;
            foreach (var field in fields)
            {
                var mode = field.GetValue(null) as BillRepeatModeDef;
                if (mode == null)
                    throw new InvalidOperationException("bill menu provider Def not initialized: " + kind);
                bool needsAuthoritativeAction = kind == BillRepeatMenuKind.Everybody || kind == BillRepeatMenuKind.Loadouts;
                var proxies = new List<int>();
                bool present = false;
                for (int i = 0; i < options.Count; i++)
                {
                    if (!BillRepeatMenuProviders.Describe(options[i], bill, out var action, out var offered)
                        || !ReferenceEquals(mode, offered))
                        continue;
                    if (needsAuthoritativeAction && action.Kind == BillRepeatMenuKind.Threshold)
                        proxies.Add(i);
                    else
                        present = true;
                }
                FloatMenuOption replacement = null;
                if (!present)
                {
                    if (needsAuthoritativeAction)
                    {
                        if (additions == null)
                            additions = provider.MakeOptions(bill);
                        replacement = additions.Single(option => BillRepeatMenuProviders.Describe(option, bill,
                            out var action, out var offered) && action.Kind == kind && ReferenceEquals(mode, offered));
                    }
                    else
                        replacement = PlainMode(bill, mode, kind); // PB/IT's actual actions are simple assignments.
                }
                // Only the identified IT proxies are replaced/removed. Unknown contributions and adequate
                // original option objects keep all presentation state and their exact original delegates.
                if (proxies.Count > 0)
                {
                    int keep = replacement == null ? -1 : proxies[0];
                    for (int i = proxies.Count - 1; i >= 0; i--)
                        if (proxies[i] == keep)
                            options[keep] = replacement;
                        else
                            options.RemoveAt(proxies[i]);
                }
                else if (replacement != null)
                    options.Add(replacement);
            }
        }

        private static void RepairTargetCountProxy(List<FloatMenuOption> options, Bill_Production bill)
        {
            var proxies = new List<int>();
            bool adequate = false;
            for (int i = 0; i < options.Count; i++)
            {
                if (!BillRepeatMenuProviders.Describe(options[i], bill, out var action, out var mode)
                    || mode != BillRepeatModeDefOf.TargetCount)
                    continue;
                if (action.Kind == BillRepeatMenuKind.Threshold)
                    proxies.Add(i);
                else
                    adequate = true;
            }
            for (int i = proxies.Count - 1; i >= 0; i--)
            {
                if (adequate || i != 0)
                    options.RemoveAt(proxies[i]);
                else
                    options[proxies[i]] = BillRepeatMenuProviders.Own(
                        new FloatMenuOption(BillRepeatModeDefOf.TargetCount.LabelCap, () =>
                    {
                        if (!bill.recipe.WorkerCounter.CanCountProducts(bill))
                            Messages.Message("RecipeCannotHaveTargetCount".Translate(), MessageTypeDefOf.RejectInput, false);
                        else
                        {
                            bill.repeatMode = BillRepeatModeDefOf.TargetCount;
                            MultiplayerCompat.CompletePlainBillSelection(bill);
                        }
                    }), bill, BillRepeatModeDefOf.TargetCount, BillRepeatMenuKind.Native);
            }
        }

        private static FloatMenuOption PlainMode(Bill_Production bill, BillRepeatModeDef mode, BillRepeatMenuKind kind) =>
            BillRepeatMenuProviders.Own(new FloatMenuOption(mode.LabelCap, () =>
            {
                bill.repeatMode = mode;
                MultiplayerCompat.CompletePlainBillSelection(bill);
            }), bill, mode, kind);
    }

    [HarmonyPatch(typeof(FloatMenu), MethodType.Constructor, new[] { typeof(List<FloatMenuOption>) })]
    internal static class Patch_FloatMenu_BillRepeatComposition
    {
        private static bool reported;

        static void Prefix(ref List<FloatMenuOption> __0)
        {
            try
            {
                __0 = BillRepeatMenuComposition.Augment(__0);
            }
            catch (Exception e)
            {
                // Only HD's composition is contained. The original constructor and original selection actions
                // still run normally, and the original list was never mutated. Report a failed adapter once.
                if (!reported)
                {
                    reported = true;
                    HDLog.Warn("Bill repeat menu composition failed; preserving the original menu. " + e);
                }
            }
        }
    }
}
