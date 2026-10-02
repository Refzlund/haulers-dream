using System;
using System.Collections.Generic;
using System.Reflection;
using HaulersDream.Core;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal static partial class StorageQueryBindings
    {
        // Exact inspected provider image. Other builds keep the established fresh
        // immediate path; this does not expand the quantitative provider contract.
        private static readonly Guid asfQueryModule = new Guid("d7c605b3-e59a-4b26-af97-594bc5417053");
        private static readonly MethodInfo asfLimitTarget = AccessTools.Method(typeof(GridsUtility), nameof(GridsUtility.GetMaxItemsAllowedInCell));
        private static readonly MethodInfo asfValidityTarget = AccessTools.Method(typeof(StoreUtility), nameof(StoreUtility.NoStorageBlockersIn));
        private static AsfQueryMetadata asfQueryMetadata;

        // Reflection identities are immutable for this exact loaded image. Only
        // these identities are retained; patch composition is observed afresh.
        private sealed class AsfQueryMetadata
        {
            internal readonly StorageProjectionAsfBinding Binding;
            internal readonly HashSet<MethodBase> Closure = new HashSet<MethodBase>();

            internal AsfQueryMetadata(StorageProjectionAsfBinding binding)
            {
                Binding = binding;
                foreach (var name in new[] { "AdaptiveStorage.ThingClass", "AdaptiveStorage.ThingCollection",
                    "AdaptiveStorage.StorageCell", "AdaptiveStorage.Utility.ThingExtensions",
                    "AdaptiveStorage.ModCompatibility.PerformanceFish" })
                {
                    var type = binding.ParentType.Assembly.GetType(name, true);
                    foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static
                        | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        StorageProgressWork.Charge(StorageWorkKind.Topology);
                        Closure.Add(method);
                    }
                }
                foreach (var method in binding.Methods) Closure.Add(method);
                Closure.Add(binding.WorkerPatch); Closure.Add(binding.LimitPatch); Closure.Add(binding.ValidityPatch);
            }
        }

        private static AsfQueryMetadata Metadata(StorageProjectionAsfBinding binding)
        {
            var current = asfQueryMetadata;
            if (current != null && current.Binding.ParentType.Assembly == binding.ParentType.Assembly) return current;
            current = new AsfQueryMetadata(binding);
            asfQueryMetadata = current;
            return current;
        }

        private static bool KnownAsf(MethodBase target, MethodInfo patch, HarmonyPatchType kind,
            StorageProjectionAsfBinding asf)
            => kind == HarmonyPatchType.Prefix && target == Patch_StorageQuery_WorkerProgress.Target && patch == asf.WorkerPatch
                || kind == HarmonyPatchType.Transpiler && target == asfLimitTarget && patch == asf.LimitPatch
                || kind == HarmonyPatchType.Transpiler && target == asfValidityTarget && patch == asf.ValidityPatch;

        internal static bool TryAsfQuery(MethodBase boundary, Thing subject, out StorageProjectionAsfBinding asf)
        {
            asf = null;
            Assembly selected = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                StorageProgressWork.Charge(StorageWorkKind.Topology);
                if (assembly.GetName().Name != "AdaptiveStorageFramework") continue;
                if (selected != null || assembly.ManifestModule.ModuleVersionId != asfQueryModule) return false;
                selected = assembly;
            }
            if (selected == null) return false;
            try
            {
                var cached = asfQueryMetadata;
                var binding = cached != null && cached.Binding.ParentType.Assembly == selected
                    ? cached.Binding : Metadata(new StorageProjectionAsfBinding(selected)).Binding;
                if (!AsfQueryCurrent(boundary, subject, binding)) return false;
                asf = binding; return true;
            }
            catch (StorageWorkExhausted) { throw; }
            catch { return false; }
        }

        internal static bool AsfQueryCurrent(MethodBase boundary, Thing subject, StorageProjectionAsfBinding asf)
        {
            var inspection = new PatchInspection();
            if (asf == null || asf.ParentType.Module.ModuleVersionId != asfQueryModule
                || !Inspected(boundary, subject, asf, inspection)) return false;
            return AsfCallbacksInspected(asf, inspection);
        }

        private static bool AsfCallbacksCurrent(StorageProjectionAsfBinding asf)
            => AsfCallbacksInspected(asf, new PatchInspection());

        private static bool AsfCallbacksInspected(StorageProjectionAsfBinding asf, PatchInspection inspection)
        {
            if (asf == null || asf.ParentType.Module.ModuleVersionId != asfQueryModule) return false;
            var closure = Metadata(asf).Closure;
            // Harmony returns a locked snapshot of its current method keys. The
            // snapshot and all patch details live only for this inspection, so a
            // newly patched helper is seen again at final certification.
            var patched = new HashSet<MethodBase>();
            foreach (var method in Harmony.GetAllPatchedMethods())
            {
                StorageProgressWork.Charge(StorageWorkKind.Topology);
                patched.Add(method);
                if (closure.Contains(method) && !Inspected(method, asf, inspection)) return false;
            }
            bool Present(MethodBase target, MethodInfo patch, HarmonyPatchType kind)
            {
                StorageProgressWork.Charge(StorageWorkKind.Predicate);
                if (target == null || !patched.Contains(target)) return false;
                var info = inspection.Read(target);
                if (info == null || !InspectedPatches(target, info, asf)) return false;
                var patches = kind == HarmonyPatchType.Prefix ? info.Prefixes : info.Transpilers;
                int count = 0;
                foreach (var entry in patches)
                { StorageProgressWork.Charge(StorageWorkKind.Predicate); if (entry.PatchMethod == patch) count++; }
                return count == 1;
            }
            if (!Present(Patch_StorageQuery_WorkerProgress.Target, asf.WorkerPatch, HarmonyPatchType.Prefix)
                || !Present(asfLimitTarget, asf.LimitPatch, HarmonyPatchType.Transpiler)
                || !Present(asfValidityTarget, asf.ValidityPatch, HarmonyPatchType.Transpiler)) return false;
            return true;
        }
    }
}
