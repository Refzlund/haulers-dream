using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Show the batch state ON the repeat-mode BUTTON, not only inside its dropdown. HD's "batch" is a FLAG layered
    /// on a vanilla repeatMode (see <see cref="Patch_BillRepeatModeUtility_MakeConfigFloatMenu"/>), so the button
    /// that opens the repeat-mode dropdown kept reading the plain vanilla label ("Do forever") while batching, a
    /// reported two-hour "is batch even on?" confusion. This prepends the SAME "Batch: " prefix the dropdown's own
    /// entries use, so a batching bill's button reads "Batch: Do forever".
    ///
    /// The label is <c>bill.repeatMode.LabelCap</c> inlined at three draw sites with no shared method to postfix:
    /// the vanilla bills-tab row (<c>Bill_Production.DoConfigInterface</c>), the vanilla details dialog
    /// (<c>Dialog_BillConfig.DoWindowContents</c>, which Nice Bill Tab reuses via <c>GetBillDialog</c>),
    /// and Nice Bill Tab's own row (<c>NiceBillTab.TabBillsDrawer.DrawBillPreview</c>). ONE transpiler covers all
    /// three: it swaps the <c>ldfld repeatMode; callvirt Def.get_LabelCap</c> read for a call to
    /// <see cref="BatchRepeatLabel"/>. The <see cref="Bill_Production"/> whose field was being read is already on
    /// the stack, so it flows straight into the helper and the returned <see cref="TaggedString"/> goes through the
    /// site's unchanged downstream (Resolve/PadRight, or the implicit string conversion).
    ///
    /// Nice Bill Tab needs nothing else: it already calls <c>BillRepeatModeUtility.MakeConfigFloatMenu</c> (so HD's
    /// "Batch: …" dropdown entries appear) and reads <c>Bill_Production.RepeatInfoText</c> (so HD's "×N" row marker
    /// shows). The NBT patch here is a SOFT dependency, skipped entirely when the mod is absent.
    /// </summary>
    public static class BatchRepeatButtonLabel
    {
        /// <summary>The repeat-mode label a bill's button should show: vanilla's own <c>repeatMode.LabelCap</c>,
        /// prefixed with "Batch: " (the dropdown's own prefix) when the bill is ACTUALLY batching: the exact
        /// predicate that gates the dropdown's batch entries and the row's ×N marker, so the three never disagree.
        /// Defensive on null so a UI transpiler can never turn a cosmetic label into an exception.</summary>
        /// <param name="bill">The bill whose repeat-mode button is being drawn.</param>
        /// <returns>The (possibly batch-prefixed) repeat-mode label; the plain label when not batching.</returns>
        public static TaggedString BatchRepeatLabel(Bill_Production bill)
        {
            if (bill?.repeatMode == null)
                return default;
            TaggedString label = bill.repeatMode.LabelCap;
            var comp = HaulersDreamGameComponent.Instance;
            if (comp != null && comp.IsBatchBill(bill) && CraftBatchPlanner.CanBatch(bill)
                && !CommonSenseCompat.BatchSuppressedByCommonSense)
            {
                string prefix = "HaulersDream.Batch.MenuPrefix".Translate();
                TaggedString prefixed = prefix + ": " + label.Resolve();
                return prefixed;
            }
            return label;
        }

        /// <summary>Shared transpiler: replace every <c>bill.repeatMode.LabelCap</c> read (an <c>ldfld repeatMode</c>
        /// immediately followed by a <c>call</c>/<c>callvirt get_LabelCap</c>) with a single call to
        /// <see cref="BatchRepeatLabel"/>.
        /// The bill reference the field was read from is already on the stack, so it feeds the helper directly.
        /// Fail-safe: on no match, or on any error, the method body is returned UNCHANGED (and the reason logged), so
        /// a vanilla or Nice Bill Tab refactor degrades to the plain label rather than a broken UI method.</summary>
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase method)
        {
            // Materialize once: `code` is the untouched original returned on any failure (never re-enumerate a
            // possibly one-shot IEnumerable); `output` is the rewritten copy returned on success.
            var code = new List<CodeInstruction>(instructions);
            try
            {
                var helper = AccessTools.Method(typeof(BatchRepeatButtonLabel), nameof(BatchRepeatLabel));
                var output = new List<CodeInstruction>(code.Count);
                int wrapped = 0;
                for (int i = 0; i < code.Count; i++)
                {
                    if (i < code.Count - 1
                        && code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo f
                        && f.Name == "repeatMode" && f.DeclaringType != null
                        && typeof(Bill).IsAssignableFrom(f.DeclaringType)
                        && (code[i + 1].opcode == OpCodes.Callvirt || code[i + 1].opcode == OpCodes.Call)
                        && code[i + 1].operand is MethodInfo m && m.Name == "get_LabelCap")
                    {
                        // Replace the two-instruction read with one call, preserving any labels / exception-block
                        // boundaries that sat on either original instruction.
                        var call = new CodeInstruction(OpCodes.Call, helper);
                        call.labels.AddRange(code[i].labels);
                        call.blocks.AddRange(code[i].blocks);
                        call.labels.AddRange(code[i + 1].labels);
                        call.blocks.AddRange(code[i + 1].blocks);
                        output.Add(call);
                        i++; // skip the get_LabelCap we just folded in
                        wrapped++;
                        continue;
                    }
                    output.Add(code[i]);
                }
                if (wrapped == 0)
                {
                    HDLog.Warn($"batch repeat-label transpiler: no repeatMode.LabelCap read found in "
                               + $"{method?.DeclaringType?.Name}.{method?.Name}; the button keeps the plain label.");
                    return code;
                }
                return output;
            }
            catch (Exception e)
            {
                HDGuard.SeamDegraded(e, $"batch repeat-label transpiler ({method?.DeclaringType?.Name}.{method?.Name})",
                    null, "the repeat-mode button keeps the plain vanilla label.");
                return code;
            }
        }
    }

    /// <summary>Vanilla surfaces: the bills-tab row button (<c>Bill_Production.DoConfigInterface</c>) and the details
    /// dialog button (<c>Dialog_BillConfig.DoWindowContents</c>, which Nice Bill Tab also reuses for "Details…").</summary>
    [HarmonyPatch]
    public static class Patch_BillRepeatButtonLabel_Vanilla
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Bill_Production), "DoConfigInterface");
            yield return AccessTools.Method(typeof(Dialog_BillConfig), nameof(Dialog_BillConfig.DoWindowContents));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
            => BatchRepeatButtonLabel.Transpiler(instructions, original);
    }

    /// <summary>Nice Bill Tab's own bill-row rendering (<c>NiceBillTab.TabBillsDrawer.DrawBillPreview</c>). SOFT
    /// dependency: <see cref="Prepare"/> resolves the type silently (via <see cref="GenTypes"/>) and skips the whole
    /// patch when the mod is absent, so HD stays standalone. No conflict: this is a Nice-Bill-Tab-owned method no
    /// other mod touches, and it self-sizes its button to the label (no clipping from the longer "Batch: " text).</summary>
    [HarmonyPatch]
    public static class Patch_BillRepeatButtonLabel_NiceBillTab
    {
        private static MethodBase ResolveTarget()
        {
            var type = GenTypes.GetTypeInAnyAssembly("NiceBillTab.TabBillsDrawer");
            return type == null ? null : AccessTools.Method(type, "DrawBillPreview");
        }

        static bool Prepare() => ResolveTarget() != null;

        static MethodBase TargetMethod() => ResolveTarget();

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
            => BatchRepeatButtonLabel.Transpiler(instructions, original);
    }
}
