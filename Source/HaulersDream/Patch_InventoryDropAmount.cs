using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace HaulersDream
{
    /// <summary>Intercept the local inventory-row calls, before native/CE Multiplayer and Sidearms hooks.</summary>
    [HarmonyPatch]
    internal static class Patch_InventoryDropAmount
    {
        private static readonly MethodInfo NativeDrop = AccessTools.Method(typeof(ITab_Pawn_Gear),
            "InterfaceDrop", new[] { typeof(Thing) });
        private static readonly MethodInfo SelectedPawn = AccessTools.PropertyGetter(typeof(ITab_Pawn_Gear), "SelPawnForGear");
        private static readonly Type CeTab = AccessTools.TypeByName("CombatExtended.ITab_Inventory");
        private static readonly MethodInfo CeDrop = CeTab == null ? null
            : AccessTools.Method(CeTab, "SyncedInterfaceDrop", new[] { typeof(Thing) });
        private static readonly Dictionary<MethodBase, InventoryDropUiPolicy> Targets =
            new Dictionary<MethodBase, InventoryDropUiPolicy>();
        private static readonly ConstructorInfo ActionConstructor = typeof(Action).GetConstructor(
            new[] { typeof(object), typeof(IntPtr) });
        private static MethodInfo ceMenuCallback;
        private static bool prepared;
        private const BindingFlags DeclaredMethods = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static bool Prepare()
        {
            if (!prepared)
            {
                prepared = true;
                if (SelectedPawn == null || !DropSignature(NativeDrop))
                    HDLog.Warn("Inventory quantity dialog could not bind the native Gear action; original drop controls remain available.");
                else
                    AddNativeTarget();
                if (CeTab != null)
                    AddCeTargets();
            }
            return Targets.Count > 0;
        }

        private static bool DropSignature(MethodInfo method) => method != null && !method.IsStatic
            && method.ReturnType == typeof(void) && method.GetParameters().Length == 1
            && method.GetParameters()[0].ParameterType == typeof(Thing);

        private static int CallCount(IEnumerable<CodeInstruction> instructions, MethodInfo target)
        {
            int count = 0;
            foreach (var instruction in instructions)
                if (instruction.Calls(target)) count++;
            return count;
        }

        private static IEnumerable<Type> NestedTypes(Type parent)
        {
            foreach (var type in parent.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                yield return type;
                foreach (var child in NestedTypes(type)) yield return child;
            }
        }

        private static List<MethodInfo> RowCallbacks(Type parent, string rowName, MethodInfo drop)
        {
            var result = new List<MethodInfo>();
            // Match the enclosing row name and exact called method, without assuming a compiler lambda ordinal.
            var types = new List<Type> { parent };
            types.AddRange(NestedTypes(parent));
            foreach (var type in types)
                foreach (var method in type.GetMethods(DeclaredMethods))
                    if (method.Name.StartsWith("<" + rowName + ">", StringComparison.Ordinal)
                        && method.GetMethodBody() != null
                        && CallCount(PatchProcessor.GetOriginalInstructions(method), drop) > 0)
                        result.Add(method);
            return result;
        }

        private static void AddNativeTarget()
        {
            var callbacks = RowCallbacks(typeof(ITab_Pawn_Gear), "DrawThingRow", NativeDrop);
            if (callbacks.Count != 1 || CallCount(PatchProcessor.GetOriginalInstructions(callbacks[0]), NativeDrop) != 1)
            {
                HDLog.Warn("Native inventory drop callback has an unrecognized shape; quantity dialog was not attached.");
                return;
            }
            Targets.Add(callbacks[0], InventoryDropUiPolicy.NativeInventory);
        }

        private static void AddCeTargets()
        {
            var row = AccessTools.DeclaredMethod(CeTab, "DrawThingRowCE",
                new[] { typeof(float).MakeByRefType(), typeof(float), typeof(Thing), typeof(bool) });
            if (SelectedPawn == null || !DropSignature(CeDrop) || row == null || row.IsStatic)
            {
                HDLog.Warn("CE inventory quantity dialog could not bind the ordinary drop callers; CE controls remain available.");
                return;
            }
            var callbacks = RowCallbacks(CeTab, "DrawThingRowCE", CeDrop);
            if (callbacks.Count != 1 || CallCount(PatchProcessor.GetOriginalInstructions(row), CeDrop) != 1
                || CallCount(PatchProcessor.GetOriginalInstructions(callbacks[0]), CeDrop) != 1
                || MenuConstructorIndex(PatchProcessor.GetOriginalInstructions(row), callbacks[0]) < 0)
            {
                HDLog.Warn("CE inventory drop callers have an unrecognized shape; quantity dialog was not attached.");
                return;
            }
            ceMenuCallback = callbacks[0];
            Targets.Add(row, InventoryDropUiPolicy.CombatExtendedButton);
        }

        private static int MenuConstructorIndex(List<CodeInstruction> code, MethodInfo callback)
        {
            int found = -1;
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldftn || !Equals(code[i].operand, callback)) continue;
                if (found >= 0 || code[i + 1].opcode != OpCodes.Newobj
                    || !Equals(code[i + 1].operand, ActionConstructor)
                    || code[i].blocks.Count != 0 || code[i + 1].blocks.Count != 0)
                    return -1;
                found = i + 1;
            }
            return found;
        }

        private static IEnumerable<MethodBase> TargetMethods() => Targets.Keys;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            var code = new List<CodeInstruction>(instructions);
            var policy = Targets[original];
            var target = policy == InventoryDropUiPolicy.NativeInventory ? NativeDrop : CeDrop;
            int menuConstructor = policy == InventoryDropUiPolicy.CombatExtendedButton
                ? MenuConstructorIndex(code, ceMenuCallback) : -1;
            if (CallCount(code, target) != 1
                || (policy == InventoryDropUiPolicy.CombatExtendedButton && menuConstructor < 0))
            {
                HDLog.Warn($"Inventory quantity dialog left {original.DeclaringType?.FullName}.{original.Name} "
                    + "unchanged because another patch changed its drop call shape.");
                return code;
            }
            string wrapperName = policy == InventoryDropUiPolicy.NativeInventory ? nameof(NativeAction) : nameof(CeButtonAction);
            var wrapper = AccessTools.Method(typeof(Patch_InventoryDropAmount), wrapperName);
            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(target)) continue;
                var replacement = new CodeInstruction(code[i]) { opcode = OpCodes.Call, operand = wrapper };
                if (policy == InventoryDropUiPolicy.CombatExtendedButton)
                {
                    if (replacement.blocks.Count != 0)
                    {
                        HDLog.Warn("CE inventory drop call has an unexpected exception boundary; quantity dialog was not attached.");
                        return code;
                    }
                    // Instance argument4 is the row's actual showDropButtonIfPrisoner flag.
                    var loadInventoryFlag = new CodeInstruction(OpCodes.Ldarg_S, (byte)4);
                    loadInventoryFlag.MoveLabelsFrom(replacement);
                    code[i] = replacement;
                    code.Insert(i, loadInventoryFlag);
                }
                else code[i] = replacement;
                break;
            }
            if (policy == InventoryDropUiPolicy.CombatExtendedButton)
            {
                // The earlier button insertion may shift this offset. Both seams were checked before mutation;
                // the generated menu callback itself stays unpatched, so no half-attached CE path is possible.
                menuConstructor = MenuConstructorIndex(code, ceMenuCallback);
                code.InsertRange(menuConstructor + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldarg_3),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_InventoryDropAmount), nameof(CaptureCeMenuAction)))
                });
            }
            return code;
        }

        private static void NativeAction(ITab_Pawn_Gear tab, Thing thing) =>
            LocalAction(tab, thing, InventoryDropUiPolicy.NativeInventory, NativeDrop);
        private static void CeButtonAction(ITab_Pawn_Gear tab, Thing thing, bool inventoryPrisoner) =>
            LocalAction(tab, thing, inventoryPrisoner ? InventoryDropUiPolicy.CombatExtendedInventoryButton
                : InventoryDropUiPolicy.CombatExtendedButton, CeDrop);

        private static Action CaptureCeMenuAction(Action original, ITab_Pawn_Gear tab, Thing thing)
        {
            var pawn = SelectedPawn.Invoke(tab, null) as Pawn;
            var owner = thing?.holdingOwner;
            var inventory = pawn?.inventory?.innerContainer;
            bool wasInventory = owner != null && ReferenceEquals(owner, inventory);
            bool wasEquipment = thing is ThingWithComps gear && pawn?.equipment != null
                && pawn.equipment.AllEquipmentListForReading.Contains(gear);
            bool wasApparel = thing is Apparel worn && pawn?.apparel != null && pawn.apparel.WornApparel.Contains(worn);
            int openingCount = thing?.stackCount ?? 0;
            var map = pawn?.Map;
            return () =>
            {
                if (!MultiplayerCompat.InventoryQuantityDropLocalUi) return;
                bool roleMatches = wasInventory
                    ? ReferenceEquals(inventory, pawn?.inventory?.innerContainer)
                        && inventory != null && inventory.InnerListForReading.Contains(thing)
                    : wasEquipment ? pawn?.equipment != null
                        && pawn.equipment.AllEquipmentListForReading.Contains(thing as ThingWithComps)
                    : wasApparel && pawn?.apparel != null && pawn.apparel.WornApparel.Contains(thing as Apparel);
                if (pawn == null || !ReferenceEquals(SelectedPawn.Invoke(tab, null), pawn)
                    || thing == null || thing.Destroyed || thing.Spawned || !roleMatches
                    || !ReferenceEquals(thing.holdingOwner, owner) || thing.stackCount != openingCount
                    || pawn.Map != map || !InventoryDropCommand.CanControl(pawn)
                    || !InventoryDropCompat.CanUseCombatExtendedDrop(pawn, thing)
                    || (wasInventory && (!pawn.Spawned || pawn.Dead)))
                {
                    Dialog_DropInventoryAmount.LocalFeedback("HD_DropAmountChanged");
                    return;
                }
                if (wasInventory && thing.def.stackLimit > 1 && openingCount > 1)
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    LocalAction(tab, thing, InventoryDropUiPolicy.CombatExtendedMenu, CeDrop);
                }
                else original();
            };
        }

        private static void LocalAction(ITab_Pawn_Gear tab, Thing thing, InventoryDropUiPolicy policy,
            MethodInfo originalDrop)
        {
            var pawn = SelectedPawn.Invoke(tab, null) as Pawn;
            bool inventory = thing != null && pawn?.inventory?.innerContainer != null
                && ReferenceEquals(thing.holdingOwner, pawn.inventory.innerContainer)
                && pawn.inventory.innerContainer.InnerListForReading.Contains(thing);
            bool equipment = thing is ThingWithComps gear && pawn?.equipment != null
                && pawn.equipment.AllEquipmentListForReading.Contains(gear);
            bool apparel = thing is Apparel worn && pawn?.apparel != null
                && pawn.apparel.WornApparel.Contains(worn);
            // A CE menu can outlive the selected stack's custody. Calling InterfaceDrop on that stale action
            // would run Sidearms' prefix against the new owner's memory before native dropping rejects it.
            if (thing == null || thing.Destroyed || thing.Spawned || (!inventory && !equipment && !apparel))
            {
                if (MultiplayerCompat.InventoryQuantityDropLocalUi)
                    Dialog_DropInventoryAmount.LocalFeedback("HD_DropAmountChanged");
                return;
            }
            if (inventory && (!pawn.Spawned || pawn.Dead))
            {
                if (MultiplayerCompat.InventoryQuantityDropLocalUi)
                    Dialog_DropInventoryAmount.LocalFeedback("HD_DropAmountChanged");
                return;
            }
            bool candidate = thing?.def != null && thing.def.stackLimit > 1 && thing.stackCount > 1
                && inventory;
            if (!candidate)
            {
                try { originalDrop.Invoke(tab, new object[] { thing }); }
                catch (TargetInvocationException e) when (e.InnerException != null)
                { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
                return;
            }
            // A synchronized/replayed caller must never create a local dialog or become a whole-stack fallback.
            if (!MultiplayerCompat.InventoryQuantityDropLocalUi)
                return;
            if (!InventoryDropCommand.Eligible(pawn, thing, policy))
            {
                Dialog_DropInventoryAmount.LocalFeedback("HD_DropAmountChanged");
                return;
            }
            if (!MultiplayerCompat.InventoryQuantityDropAvailable || !InventoryDropCompat.AvailableFor(pawn, thing))
            {
                Dialog_DropInventoryAmount.LocalFeedback("HD_DropAmountUnavailable");
                return;
            }
            Find.WindowStack.Add(new Dialog_DropInventoryAmount(pawn, thing, policy));
        }
    }
}
