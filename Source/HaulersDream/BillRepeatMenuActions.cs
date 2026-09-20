using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;

namespace HaulersDream
{
    /// <summary>
    /// Observes accepted selections in the twelve actions referenced by the five supported menu factories.
    /// No delegate is replaced, no predicate is rerun, and no unknown action or general bill setter is patched.
    /// </summary>
    internal static class BillRepeatMenuActions
    {
        internal static readonly FieldInfo RepeatMode = AccessTools.Field(typeof(Bill_Production), "repeatMode");
        private static readonly MethodInfo Observer = AccessTools.Method(typeof(BillRepeatMenuActions), nameof(Observe));
        private static readonly MethodInfo Complete = AccessTools.Method(typeof(MultiplayerCompat),
            nameof(MultiplayerCompat.CompletePlainBillSelection));

        internal static void Install(Harmony harmony, BillRepeatMenuProvider provider)
        {
            var attempted = new List<MethodInfo>();
            try
            {
                foreach (var action in provider.Actions)
                {
                    attempted.Add(action.Method);
                    harmony.Patch(action.Method, transpiler: new HarmonyMethod(Observer));
                    if (Harmony.GetPatchInfo(action.Method)?.Transpilers.Any(p =>
                        p.owner == HaulersDreamMod.HarmonyId && p.PatchMethod == Observer) != true)
                        throw new InvalidOperationException("selection observer was not registered: " + action.Method);
                }
            }
            catch
            {
                // Roll back only this observer, never another HD or foreign patch on the action.
                foreach (var method in attempted)
                    harmony.Unpatch(method, Observer);
                throw;
            }
        }

        internal static void ValidateBody(IList<CodeInstruction> codes)
        {
            if (codes.Count(c => c.opcode == OpCodes.Stfld && Equals(c.operand, RepeatMode)) != 1
                || !codes.Any(c => c.opcode == OpCodes.Ret)
                || codes.Any(c => c.blocks.Count != 0 || c.opcode == OpCodes.Tailcall
                    || c.opcode == OpCodes.Volatile || c.opcode == OpCodes.Unaligned))
                throw new InvalidOperationException("unsupported bill selection action body");
        }

        private static IEnumerable<CodeInstruction> Observe(IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            // Check the actual instruction stream passed by Harmony too: another transpiler may have changed
            // the original body after the provider contract was read. Do not guess at multiple stores or EH.
            var codes = instructions.Select(c => new CodeInstruction(c)).ToList();
            ValidateBody(codes);
            var bill = generator.DeclareLocal(typeof(Bill_Production));
            var mode = generator.DeclareLocal(typeof(BillRepeatModeDef));
            var accepted = generator.DeclareLocal(typeof(bool));
            var result = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldnull), new CodeInstruction(OpCodes.Stloc, bill),
                new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Stloc, accepted)
            };
            foreach (var code in codes)
            {
                if (code.opcode == OpCodes.Stfld && Equals(code.operand, RepeatMode))
                {
                    // Stack on entry: bill, mode. Keep the original write (including its failure behavior),
                    // recording the actual receiver, not a guessed closure field or the previously open bill.
                    var first = new CodeInstruction(OpCodes.Stloc, mode);
                    first.labels.AddRange(code.labels);
                    code.labels.Clear();
                    result.Add(first);
                    result.Add(new CodeInstruction(OpCodes.Dup));
                    result.Add(new CodeInstruction(OpCodes.Stloc, bill));
                    result.Add(new CodeInstruction(OpCodes.Ldloc, mode));
                    result.Add(code);
                    result.Add(new CodeInstruction(OpCodes.Ldc_I4_1));
                    result.Add(new CodeInstruction(OpCodes.Stloc, accepted));
                }
                else if (code.opcode == OpCodes.Ret)
                {
                    // Every original branch to this return must enter the guard. In particular a rejected
                    // TargetCount reaches accepted=false, whereas reselecting the SAME plain mode reaches true.
                    // CL's target/repeat/includeEquipped setup has finished before this helper runs.
                    var first = new CodeInstruction(OpCodes.Ldloc, accepted);
                    first.labels.AddRange(code.labels);
                    code.labels.Clear();
                    var skip = generator.DefineLabel();
                    result.Add(first);
                    result.Add(new CodeInstruction(OpCodes.Brfalse, skip));
                    result.Add(new CodeInstruction(OpCodes.Ldloc, bill));
                    result.Add(new CodeInstruction(OpCodes.Call, Complete));
                    code.labels.Add(skip);
                    result.Add(code);
                }
                else
                    result.Add(code);
            }
            return result;
        }
    }
}
