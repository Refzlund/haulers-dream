using System;
using System.Collections.Generic;
using System.Reflection;
using HaulersDream.Core;
using HarmonyLib;

namespace HaulersDream
{
    internal static partial class StorageQueryBindings
    {
        // Owned by one synchronous, callback-free composition inspection. Entry and
        // final certification always construct separate instances. This never holds
        // a permission, provider result, quantity, or state across a provider call.
        private sealed class PatchInspection
        {
            private readonly Dictionary<MethodBase, Patches> patches = new Dictionary<MethodBase, Patches>();
            internal Patches Read(MethodBase method)
            {
                if (!patches.TryGetValue(method, out var info))
                {
                    info = Harmony.GetPatchInfo(method);
                    patches.Add(method, info);
                }
                return info;
            }
        }

        // Method identities belong to the loaded native type, not its mutable
        // Harmony patch roster. Every returned method still gets a fresh inspection.
        private static readonly Dictionary<Tuple<Type, string>, MethodInfo[]> nativeOverloads
            = new Dictionary<Tuple<Type, string>, MethodInfo[]>();
        private static MethodInfo[] NativeOverloads(Type type, string name)
        {
            var key = Tuple.Create(type, name);
            lock (nativeOverloads)
            {
                if (nativeOverloads.TryGetValue(key, out var methods)) return methods;
                var found = new List<MethodInfo>();
                foreach (var method in AccessTools.GetDeclaredMethods(type))
                {
                    StorageProgressWork.Charge(StorageWorkKind.Topology);
                    if (method.Name == name) found.Add(method);
                }
                methods = found.ToArray();
                nativeOverloads.Add(key, methods);
                return methods;
            }
        }

        // HarmonyX extends Patches with inner hooks. Compile against ordinary
        // Harmony, but never silently admit an installed inner-hook capability.
        private static readonly FieldInfo innerPrefixes = typeof(Patches).GetField("InnerPrefixes");
        private static readonly FieldInfo innerPostfixes = typeof(Patches).GetField("InnerPostfixes");
        private static readonly bool hasInnerHookApi = typeof(PatchProcessor).GetMethod("AddInnerPrefix", new[] { typeof(MethodInfo) }) != null
            || typeof(PatchProcessor).GetMethod("AddInnerPostfix", new[] { typeof(MethodInfo) }) != null;

        private static bool NoInnerHooks(Patches info)
        {
            if (innerPrefixes == null && innerPostfixes == null) return !hasInnerHookApi;
            return EmptyInner(innerPrefixes, info) && EmptyInner(innerPostfixes, info);
        }

        private static bool EmptyInner(FieldInfo field, Patches info)
        {
            StorageProgressWork.Charge(StorageWorkKind.Predicate);
            if (field == null || field.IsStatic || !typeof(IEnumerable<Patch>).IsAssignableFrom(field.FieldType)
                || !(field.GetValue(info) is IEnumerable<Patch> entries)) return false;
            foreach (var entry in entries)
            {
                StorageProgressWork.Charge(StorageWorkKind.Predicate);
                return false; // No inner patch is part of the inspected allowlist.
            }
            return true;
        }
    }
}
