using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal static partial class BillRepeatMenuComposition
    {
        private static MethodInfo multiplayerWatcher, multiplayerTranspiler;

        // MP's native transpiler wraps the existing list before FloatMenu is constructed. Compose at
        // that exact boundary so the provider keeps its own field watcher and the constructor sees
        // our already-composed list. Do not unwrap arbitrary multicast or foreign selection actions.
        internal static void BindMultiplayerWatcher(Harmony harmony)
        {
            if (!MultiplayerCompat.Active)
                return;
            var patches = Harmony.GetPatchInfo(BillRepeatMenuProviders.Menu);
            var matches = patches?.Transpilers.Where(p =>
                p.PatchMethod?.DeclaringType?.FullName == "Multiplayer.Client.SyncFields"
                && p.PatchMethod.Name == "BillConfigFloatMenuTranspiler"
                && p.PatchMethod.Module.Assembly.GetName().Name == "Multiplayer").ToArray();
            if (matches == null || matches.Length != 1)
                throw new InvalidOperationException("Native Multiplayer bill field watcher is unavailable.");
            var owner = matches[0].PatchMethod.DeclaringType;
            var helper = owner.GetMethod("SyncBillConfigFloatMenuOptions", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(Bill_Production), typeof(List<FloatMenuOption>) }, null);
            if (helper == null || helper.ReturnType != typeof(void) || helper.Module != matches[0].PatchMethod.Module)
                throw new InvalidOperationException("Unsupported Multiplayer bill field watcher signature.");
            var existing = Harmony.GetPatchInfo(helper);
            if (existing != null && (existing.Prefixes.Count != 0 || existing.Transpilers.Count != 0
                || existing.Postfixes.Count != 0 || existing.Finalizers.Count != 0))
                throw new InvalidOperationException("Multiplayer bill field watcher already has an unknown composition.");
            harmony.Patch(helper, prefix: new HarmonyMethod(typeof(BillRepeatMenuComposition), nameof(BeforeMultiplayerWatch)));
            multiplayerWatcher = helper;
            multiplayerTranspiler = matches[0].PatchMethod;
        }

        internal static List<FloatMenuOption> ComposeForConstructor(List<FloatMenuOption> original)
        {
            var frame = current;
            // The exact PB replacement returns false before MP's native transpiler can run. Its
            // original menu must be identified before augmentation or any provider wraps actions.
            bool periodic = MultiplayerCompat.Active && frame != null && !frame.Closed
                && !frame.MultiplayerLists.Contains(original) && IsPeriodicReplacement(original, frame.Bill);
            var composed = Augment(original);
            if (!periodic || ReferenceEquals(composed, original) || multiplayerWatcher == null
                || !frame.Outputs.Contains(composed) || !composed.Take(original.Count).SequenceEqual(original)
                || composed.Skip(original.Count).Any(option => !frame.BatchOptions.Contains(option)))
                return composed;

            // Recheck the admitted native helper and PB creator at this boundary. An unknown late
            // patch or contribution does not grant permission to wrap a foreign action list.
            var menuPatches = Harmony.GetPatchInfo(BillRepeatMenuProviders.Menu);
            var provider = BillRepeatMenuProviders.Get(BillRepeatMenuKind.Periodic);
            var helperPatches = Harmony.GetPatchInfo(multiplayerWatcher);
            var prefix = AccessTools.Method(typeof(BillRepeatMenuComposition), nameof(BeforeMultiplayerWatch));
            if (menuPatches == null || provider == null
                || menuPatches.Transpilers.Count(p => p.PatchMethod == multiplayerTranspiler) != 1
                || menuPatches.Prefixes.Count(p => p.owner == "custom.periodicbills" && p.PatchMethod == provider.Factory) != 1
                || helperPatches == null || helperPatches.Prefixes.Count != 1
                || helperPatches.Prefixes[0].PatchMethod != prefix
                || helperPatches.Postfixes.Count != 0 || helperPatches.Transpilers.Count != 0
                || helperPatches.Finalizers.Count != 0)
                throw new InvalidOperationException("Periodic Bills Multiplayer watcher composition changed.");

            var actions = composed.Select(option => option.action).ToArray();
            try
            {
                // Keep every original option and selection body. MP supplies its own field-watch
                // prefix/watch/postfix; HD's existing successful-selection observer clears batch once.
                multiplayerWatcher.Invoke(null, new object[] { frame.Bill, composed });
                return composed;
            }
            catch
            {
                // The helper may have wrapped only part of the list before throwing. Restore the
                // exact actions before the constructor's existing containment preserves the menu.
                for (int i = 0; i < composed.Count; i++)
                    composed[i].action = actions[i];
                frame.MultiplayerLists.Remove(composed);
                throw;
            }
        }

        private static bool IsPeriodicReplacement(List<FloatMenuOption> options, Bill_Production bill)
        {
            var provider = BillRepeatMenuProviders.Get(BillRepeatMenuKind.Periodic);
            if (provider == null || bill == null || options == null || options.Count != 4 || provider.Modes.Length != 4)
                return false;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] == null)
                    return false;
                // The actual PB factory leaves only its second (TargetCount) entry disabled. Do
                // not rerun its eligibility predicate or infer action identity from translated text.
                if (i == 1 && options[i].action == null)
                    continue;
                if (!provider.Actions.Any(action => action.Matches(options[i].action, bill, out var mode)
                    && ReferenceEquals(mode, provider.Modes[i].GetValue(null))))
                    return false;
            }
            return true;
        }

        private static bool reportedMultiplayerComposition;

        private static void BeforeMultiplayerWatch(Bill_Production __0, List<FloatMenuOption> __1)
        {
            var frame = current;
            if (frame == null || frame.Closed || !ReferenceEquals(frame.Bill, __0) || __1 == null)
                return;
            try
            {
                var composed = Augment(__1);
                frame.MultiplayerLists.Add(__1);
                if (ReferenceEquals(composed, __1))
                    return;
                // MP takes this list by value; replacing its argument would not replace the native
                // caller's local. Commit only after successful composition, keeping original option
                // objects/delegates. MP then wraps every option itself before native construction.
                __1.Capacity = Math.Max(__1.Capacity, composed.Count);
                __1.Clear();
                __1.AddRange(composed);
                frame.Outputs.Add(__1);
            }
            catch (Exception e)
            {
                if (!reportedMultiplayerComposition)
                {
                    reportedMultiplayerComposition = true;
                    HDLog.Warn("Bill repeat menu Multiplayer composition failed; preserving the original menu. " + e);
                }
            }
        }
    }
}
