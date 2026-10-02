using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    // Mark the physical writes, before callbacks. A whole-method postfix cannot distinguish
    // a successful transfer followed by a throw from an unrelated callback's stack growth.
    internal static class NativeStorageTransferHooks
    {
        private static readonly FieldInfo Count = AccessTools.Field(typeof(Thing), nameof(Thing.stackCount));
        private static readonly FieldInfo Holder = AccessTools.Field(typeof(Thing), nameof(Thing.holdingOwner));
        private static readonly MethodInfo Insert = AccessTools.Method(typeof(NativeStorageTransferHooks), nameof(Inserted));
        private static readonly MethodInfo Merge = AccessTools.Method(typeof(NativeStorageTransferHooks), nameof(Merged));

        internal static void Inserted(ThingOwner owner, Thing item)
        { if (StorageSplitScope.Current is NativeStoragePickup.Pickup pickup) pickup.Inserted(owner, item); }
        internal static void Merged(Thing recipient, Thing source, int count)
        { if (StorageSplitScope.Current is NativeStoragePickup.Pickup pickup) pickup.Merged(recipient, source, count); }

        private static bool LocalLoad(CodeInstruction code) => code.opcode == OpCodes.Ldloc
            || code.opcode == OpCodes.Ldloc_S || code.opcode == OpCodes.Ldloc_0
            || code.opcode == OpCodes.Ldloc_1 || code.opcode == OpCodes.Ldloc_2 || code.opcode == OpCodes.Ldloc_3;
        private static bool SameLocal(CodeInstruction left, CodeInstruction right)
            => LocalLoad(left) && left.opcode == right.opcode && Equals(left.operand, right.operand);
        private static bool Field(CodeInstruction code, OpCode opcode, FieldInfo field)
            => code.opcode == opcode && Equals(code.operand, field);
        private static bool ClearInterior(List<CodeInstruction> code, int start, int end)
        {
            for (int i = start; i <= end; i++)
                if (code[i].blocks.Count != 0 || (i > start && code[i].labels.Count != 0)) return false;
            return true;
        }
        private static IEnumerable<CodeInstruction> Failed(List<CodeInstruction> code, string boundary)
        {
            StorageCommitments.Disable();
            HDLog.Err("STORAGE-SEAM TRIPWIRE: native " + boundary
                + " transfer receipt did not match its exact physical-write boundary. Storage sharing is disabled.");
            return code;
        }

        internal static IEnumerable<CodeInstruction> Insertion(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int match = -1, matches = 0, stores = 0;
            for (int i = 0; i < code.Count; i++) if (Field(code[i], OpCodes.Stfld, Holder)) stores++;
            for (int i = 0; i + 10 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldarg_1 || code[i + 1].opcode != OpCodes.Ldarg_0
                    || !Field(code[i + 2], OpCodes.Stfld, Holder) || code[i + 3].opcode != OpCodes.Ldarg_0
                    || code[i + 4].opcode != OpCodes.Ldfld || !(code[i + 4].operand is FieldInfo list)
                    || list.Name != "innerList" || !list.DeclaringType.IsGenericType
                    || list.DeclaringType.GetGenericTypeDefinition() != typeof(ThingOwner<>)
                    || !LocalLoad(code[i + 5]) || code[i + 6].opcode != OpCodes.Callvirt
                    || !(code[i + 6].operand is MethodInfo add) || add.Name != "Add"
                    || !add.DeclaringType.IsGenericType || add.DeclaringType.GetGenericTypeDefinition() != typeof(List<>)
                    || code[i + 7].opcode != OpCodes.Ldarg_0 || !SameLocal(code[i + 5], code[i + 8])) continue;
                int notify = i + 9;
                if (code[notify].opcode == OpCodes.Box) notify++;
                if (code[notify].opcode != OpCodes.Callvirt || !(code[notify].operand is MethodInfo call)
                    || call.DeclaringType != typeof(ThingOwner) || call.Name != "NotifyAdded"
                    || !ClearInterior(code, i, notify)) continue;
                match = i + 6; matches++;
            }
            if (stores != 1 || matches != 1) return Failed(code, "ThingOwner<Thing>.TryAdd insertion");
            code.InsertRange(match + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Call, Insert) });
            return code;
        }

        internal static IEnumerable<CodeInstruction> Absorption(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int match = -1, matches = 0, stores = 0;
            for (int i = 0; i < code.Count; i++) if (Field(code[i], OpCodes.Stfld, Count)) stores++;
            for (int i = 0; i + 13 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldarg_0 || code[i + 1].opcode != OpCodes.Ldarg_0
                    || !Field(code[i + 2], OpCodes.Ldfld, Count) || !LocalLoad(code[i + 3])
                    || code[i + 4].opcode != OpCodes.Add || !Field(code[i + 5], OpCodes.Stfld, Count)
                    || code[i + 6].opcode != OpCodes.Ldarg_1 || code[i + 7].opcode != OpCodes.Dup
                    || !Field(code[i + 8], OpCodes.Ldfld, Count) || !SameLocal(code[i + 3], code[i + 9])
                    || code[i + 10].opcode != OpCodes.Sub || !Field(code[i + 11], OpCodes.Stfld, Count)
                    || code[i + 12].opcode != OpCodes.Ldarg_0 || code[i + 13].opcode != OpCodes.Call
                    || !(code[i + 13].operand is MethodInfo call) || call.DeclaringType != typeof(Thing)
                    || call.Name != "get_Map" || !ClearInterior(code, i, i + 13)) continue;
                match = i; matches++;
            }
            if (stores != 2 || matches != 1) return Failed(code, "Thing.TryAbsorbStack paired count writes");
            var amount = code[match + 3];
            code.InsertRange(match + 12, new[] { new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(amount.opcode, amount.operand),
                new CodeInstruction(OpCodes.Call, Merge) });
            return code;
        }
    }

    [HarmonyPatch(typeof(ThingOwner<Thing>), nameof(ThingOwner<Thing>.TryAdd), typeof(Thing), typeof(bool))]
    internal static class Patch_NativeStorageInsertionReceipt
    {
        [HarmonyPriority(Priority.Last)]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => NativeStorageTransferHooks.Insertion(instructions);
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TryAbsorbStack), typeof(Thing), typeof(bool))]
    internal static class Patch_NativeStorageMergeReceipt
    {
        [HarmonyPriority(Priority.Last)]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => NativeStorageTransferHooks.Absorption(instructions);
    }
}
