using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal enum BillRepeatMenuKind { Native, Periodic, Everybody, Loadouts, Threshold }

    internal sealed class BillRepeatMenuAction
    {
        internal MethodInfo Method;
        internal FieldInfo BillField, ParentField, ModeField;
        internal BillRepeatMenuKind Kind;

        internal bool Matches(Action action, Bill_Production bill, out BillRepeatModeDef mode)
        {
            mode = null;
            // Multicast/foreign wrappers are unknown contributions, even if one element resembles a selection.
            if (action == null || action.Method != Method || action.Target == null
                || action.GetInvocationList().Length != 1)
                return false;
            var closure = ParentField == null ? action.Target : ParentField.GetValue(action.Target);
            if (closure == null || !ReferenceEquals(BillField.GetValue(closure), bill))
                return false;
            mode = ModeField.GetValue(ModeField.IsStatic ? null : action.Target) as BillRepeatModeDef;
            return mode != null;
        }
    }

    internal sealed class BillRepeatMenuProvider
    {
        internal BillRepeatMenuKind Kind;
        internal MethodInfo Factory;
        internal FieldInfo[] Modes;
        internal readonly List<BillRepeatMenuAction> Actions = new List<BillRepeatMenuAction>();

        internal List<FloatMenuOption> MakeOptions(Bill_Production bill)
        {
            var options = new List<FloatMenuOption>();
            // Only EGO/CL have insertion factories. Never invoke PB/IT's replacement prefixes here.
            if (Kind != BillRepeatMenuKind.Everybody && Kind != BillRepeatMenuKind.Loadouts)
                throw new InvalidOperationException("provider has no insertion factory");
            Factory.Invoke(null, new object[] { options, bill });
            if (options.Count != Modes.Length || Modes.Any(field => options.Count(option =>
                BillRepeatMenuProviders.Describe(option, bill, out var action, out var mode)
                && action.Kind == Kind && ReferenceEquals(mode, field.GetValue(null))) != 1))
                throw new InvalidOperationException("provider returned an unsupported menu contribution: " + Kind);
            return options;
        }
    }

    /// <summary>
    /// Fixed native/PB/EGO/CL/IT contracts, resolved from registered menu patches and their active assemblies.
    /// Only the delegates referenced by these known factories are inspected; no assembly-wide method scan.
    /// </summary>
    internal static class BillRepeatMenuProviders
    {
        internal static readonly MethodInfo Menu = AccessTools.Method(typeof(BillRepeatModeUtility),
            nameof(BillRepeatModeUtility.MakeConfigFloatMenu), new[] { typeof(Bill_Production) });
        private static readonly List<BillRepeatMenuProvider> providers = new List<BillRepeatMenuProvider>();
        private sealed class OwnedSelection
        {
            internal Bill_Production Bill;
            internal BillRepeatModeDef Mode;
            internal Action Action;
            internal BillRepeatMenuAction Identity;
        }
        private static readonly ConditionalWeakTable<FloatMenuOption, OwnedSelection> owned =
            new ConditionalWeakTable<FloatMenuOption, OwnedSelection>();
        private static bool initialized;
        internal static bool Ready { get; private set; }

        internal static void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;
            // First actual bill-menu call, after mod constructors and startup patch registration, avoids
            // freezing absence just because HD happened to initialize before another supported provider.
            try
            {
                var patches = Harmony.GetPatchInfo(Menu);
                providers.Add(Create(BillRepeatMenuKind.Native, Menu, NativeModes()));
                AddProvider(patches?.Prefixes, "custom.periodicbills", "PeriodicBills.Patch_FloatMenu",
                    BillRepeatMenuKind.Periodic, null, "PeriodicBills.PeriodicBillDefOf", "PB_Periodic");
                AddProvider(patches?.Transpilers, "Uuugggg.rimworld.Everybody_Gets_One.main",
                    "Everybody_Gets_One.MakeConfigFloatMenu_Patch", BillRepeatMenuKind.Everybody,
                    "InsertMode", "Everybody_Gets_One.RepeatModeDefOf", "TD_PersonCount", "TD_XPerPerson", "TD_WithSurplusIng");
                AddProvider(patches?.Transpilers, "Wiri.compositableloadouts", "Inventory.MakeConfigFloatMenu_Patch",
                    BillRepeatMenuKind.Loadouts, "GetOptions", "Inventory.InvBillRepeatModeDefOf", "W_PerTag");
                AddProvider(patches?.Prefixes, "com.rileydoggy.ingredientthreshold",
                    "IngredientThreshold.Patches.Patch_BillRepeatModeUtility", BillRepeatMenuKind.Threshold,
                    null, "IngredientThreshold.ThresholdRepeatModeDef", "IngredientThreshold");
                var harmony = new Harmony(HaulersDreamMod.HarmonyId);
                foreach (var provider in providers)
                    BillRepeatMenuActions.Install(harmony, provider);
                BillRepeatMenuComposition.BindMultiplayerWatcher(harmony);
                Ready = true;
                HDLog.Msg("Bill repeat menu composition ready: " + string.Join(", ", providers.Select(p => p.Kind)) + ".");
            }
            catch (Exception e)
            {
                // Keep the original menu/actions on a contract mismatch. Already installed observers only
                // clear HD's flag on a successful known plain selection; no takeover or guessed reconstruction.
                HDLog.Warn("Bill repeat menu integration could not bind; HD menu additions are disabled. " + e);
            }
        }

        internal static BillRepeatMenuProvider Get(BillRepeatMenuKind kind)
        {
            // Only Enter on the real bill-menu call initializes the catalogue. A legacy IsActive query during
            // another mod's startup must not freeze a partial patch inventory or install action observers early.
            return Ready ? providers.FirstOrDefault(p => p.Kind == kind) : null;
        }

        internal static bool Describe(FloatMenuOption option, Bill_Production bill,
            out BillRepeatMenuAction action, out BillRepeatModeDef mode)
        {
            if (option != null && owned.TryGetValue(option, out var known)
                && ReferenceEquals(known.Bill, bill) && ReferenceEquals(known.Action, option.action))
            {
                action = known.Identity;
                mode = known.Mode;
                return true;
            }
            foreach (var provider in providers)
                foreach (var candidate in provider.Actions)
                    if (candidate.Matches(option?.action, bill, out mode))
                    {
                        action = candidate;
                        return true;
                    }
            action = null;
            mode = null;
            return false;
        }

        internal static FloatMenuOption Own(FloatMenuOption option, Bill_Production bill,
            BillRepeatModeDef mode, BillRepeatMenuKind kind)
        {
            owned.Add(option, new OwnedSelection
            {
                Bill = bill, Mode = mode, Action = option.action,
                Identity = new BillRepeatMenuAction { Kind = kind, Method = option.action.Method }
            });
            return option;
        }

        private static FieldInfo[] NativeModes() => new[]
        {
            AccessTools.Field(typeof(BillRepeatModeDefOf), "RepeatCount"),
            AccessTools.Field(typeof(BillRepeatModeDefOf), "TargetCount"),
            AccessTools.Field(typeof(BillRepeatModeDefOf), "Forever")
        };

        private static void AddProvider(IEnumerable<Patch> patches, string owner, string patchType,
            BillRepeatMenuKind kind, string inserter, string defType, params string[] defNames)
        {
            var matches = patches?.Where(p => p.owner == owner
                && p.PatchMethod.DeclaringType?.FullName == patchType
                && p.PatchMethod.Name == (inserter == null ? "Prefix" : "Transpiler")).ToList();
            if (matches == null || matches.Count == 0)
                return;
            if (matches.Count != 1)
                throw new InvalidOperationException("ambiguous bill menu provider: " + kind);
            var patch = matches[0].PatchMethod;
            var assembly = patch.DeclaringType.Assembly;
            if (!LoadedModManager.RunningModsListForReading.Any(mod =>
                mod.assemblies?.loadedAssemblies.Contains(assembly) == true))
                throw new InvalidOperationException("bill menu provider has no active mod owner: " + kind);
            var factory = inserter == null ? patch : AccessTools.DeclaredMethod(patch.DeclaringType, inserter,
                new[] { typeof(List<FloatMenuOption>), typeof(Bill_Production) });
            var defs = assembly.GetType(defType, false);
            var modes = defNames.Select(name => defs == null ? null : AccessTools.DeclaredField(defs, name)).ToArray();
            if (modes.Any(f => f == null || !f.IsStatic || f.FieldType != typeof(BillRepeatModeDef)))
                throw new InvalidOperationException("bill repeat Def contract changed: " + kind);
            if (kind == BillRepeatMenuKind.Periodic)
                modes = NativeModes().Concat(modes).ToArray();
            providers.Add(Create(kind, factory, modes));
        }

        private static BillRepeatMenuProvider Create(BillRepeatMenuKind kind, MethodInfo factory, FieldInfo[] modes)
        {
            var isInserter = kind == BillRepeatMenuKind.Everybody || kind == BillRepeatMenuKind.Loadouts;
            var expectedReturn = kind == BillRepeatMenuKind.Native || kind == BillRepeatMenuKind.Loadouts
                ? typeof(void) : kind == BillRepeatMenuKind.Everybody ? typeof(List<FloatMenuOption>) : typeof(bool);
            var expectedParameters = isInserter
                ? new[] { typeof(List<FloatMenuOption>), typeof(Bill_Production) } : new[] { typeof(Bill_Production) };
            if (factory == null || !factory.IsStatic || factory.ReturnType != expectedReturn
                || !factory.GetParameters().Select(p => p.ParameterType).SequenceEqual(expectedParameters))
                throw new InvalidOperationException("bill menu factory contract changed: " + kind);
            var provider = new BillRepeatMenuProvider { Kind = kind, Factory = factory, Modes = modes };
            var methods = PatchProcessor.GetOriginalInstructions(factory)
                .Where(i => i.opcode == OpCodes.Ldftn).Select(i => i.operand as MethodInfo).Distinct().ToList();
            int expected = kind == BillRepeatMenuKind.Threshold ? 1 : modes.Length;
            if (methods.Count != expected)
                throw new InvalidOperationException("bill selection action count changed: " + kind);
            foreach (var method in methods)
            {
                if (method == null || method.IsStatic || method.ReturnType != typeof(void)
                    || method.GetParameters().Length != 0 || method.DeclaringType?.DeclaringType != factory.DeclaringType)
                    throw new InvalidOperationException("bill selection action signature changed: " + kind);
                var codes = PatchProcessor.GetOriginalInstructions(method);
                BillRepeatMenuActions.ValidateBody(codes);
                int store = codes.FindIndex(c => c.opcode == OpCodes.Stfld && Equals(c.operand, BillRepeatMenuActions.RepeatMode));
                var binding = new BillRepeatMenuAction { Method = method, Kind = kind };
                if (kind == BillRepeatMenuKind.Threshold)
                {
                    binding.ModeField = AccessTools.DeclaredField(method.DeclaringType, "captured");
                    binding.ParentField = AccessTools.DeclaredField(method.DeclaringType, "CS$<>8__locals1");
                    binding.BillField = binding.ParentField == null ? null
                        : AccessTools.DeclaredField(binding.ParentField.FieldType, "bill");
                    RequireLoad(codes, store - 5, OpCodes.Ldarg_0, null);
                    RequireLoad(codes, store - 4, OpCodes.Ldfld, binding.ParentField);
                    RequireLoad(codes, store - 3, OpCodes.Ldfld, binding.BillField);
                    RequireLoad(codes, store - 2, OpCodes.Ldarg_0, null);
                    RequireLoad(codes, store - 1, OpCodes.Ldfld, binding.ModeField);
                }
                else
                {
                    binding.BillField = AccessTools.DeclaredField(method.DeclaringType, "bill");
                    binding.ModeField = store > 0 ? codes[store - 1].operand as FieldInfo : null;
                    if (!modes.Contains(binding.ModeField))
                        throw new InvalidOperationException("unknown mode in known bill action: " + kind);
                    RequireLoad(codes, store - 3, OpCodes.Ldarg_0, null);
                    RequireLoad(codes, store - 2, OpCodes.Ldfld, binding.BillField);
                    RequireLoad(codes, store - 1, OpCodes.Ldsfld, binding.ModeField);
                }
                if (binding.BillField == null || binding.BillField.FieldType != typeof(Bill_Production)
                    || binding.BillField.IsStatic || binding.ModeField == null
                    || binding.ModeField.FieldType != typeof(BillRepeatModeDef)
                    || binding.ModeField.IsStatic == (kind == BillRepeatMenuKind.Threshold))
                    throw new InvalidOperationException("bill selection capture changed: " + kind);
                provider.Actions.Add(binding);
            }
            if (kind != BillRepeatMenuKind.Threshold && modes.Any(mode => provider.Actions.Count(a => a.ModeField == mode) != 1))
                throw new InvalidOperationException("ambiguous mode actions in bill provider: " + kind);
            return provider;
        }

        private static void RequireLoad(List<CodeInstruction> codes, int index, OpCode opcode, object operand)
        {
            if (index < 0 || index >= codes.Count || codes[index].opcode != opcode
                || !Equals(codes[index].operand, operand))
                throw new InvalidOperationException("bill selection receiver contract changed");
        }
    }
}
