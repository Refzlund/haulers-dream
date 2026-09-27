using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal static partial class StorageAllocationObservation
    {
        private sealed partial class Reader
        {
            private readonly Dictionary<Type, StorageProjectionAsfBinding> asfBindings = new Dictionary<Type, StorageProjectionAsfBinding>();
            private readonly Dictionary<MethodBase, bool> quantityMethods = new Dictionary<MethodBase, bool>();

            private bool Provider(ISlotGroupParent parent, IntVec3 cell, out StorageProjectionAsfBinding asf)
            {
                asf = null;
                var type = parent.GetType();
                if (type != typeof(Zone_Stockpile) && type != typeof(Building_Storage))
                {
                    if (type.FullName != "AdaptiveStorage.ThingClass")
                        return Problem(cell, null, StorageAllocationObservationStatus.Unsupported, "unhandled-storage-parent:" + type.FullName);
                    if (!asfBindings.TryGetValue(type, out asf))
                    {
                        try { asf = new StorageProjectionAsfBinding(type.Assembly); }
                        catch (Exception e)
                        { return Problem(cell, null, StorageAllocationObservationStatus.Unsupported, "asf-binding:" + e.GetType().Name); }
                        asfBindings.Add(type, asf);
                    }
                    if (type != asf.ParentType)
                        return Problem(cell, null, StorageAllocationObservationStatus.Unsupported, "unhandled-asf-subclass");
                }
                if (parent is Building_Storage building)
                {
                    // A comp can impose a mass, per-def or member-wide quantitative cap that the
                    // cell slot count cannot represent. Do not certify an unfamiliar comp by
                    // observing only one successful Boolean cell predicate.
                    foreach (var comp in building.AllComps)
                    {
                        Predicate();
                        var assembly = comp.GetType().Assembly;
                        if (assembly != typeof(ThingComp).Assembly && (asf == null || assembly != asf.ParentType.Assembly))
                            return Problem(cell, null, StorageAllocationObservationStatus.Unsupported,
                                "unhandled-storage-comp:" + comp.GetType().FullName);
                    }
                    if (type.GetMethod(nameof(Building_Storage.AllSlotCells))?.DeclaringType != typeof(Building_Storage))
                        return Problem(cell, null, StorageAllocationObservationStatus.Unsupported, "unhandled-storage-footprint");
                }
                // These are quantitative hooks, not a global catalogue of every filter or mod.
                // Unknown registrations have no proven applicability boundary and cannot be
                // translated from a Boolean result to independent slot/top-up capacities.
                foreach (var method in new MethodBase[]
                {
                    AccessTools.Method(typeof(GridsUtility), nameof(GridsUtility.GetMaxItemsAllowedInCell)),
                    AccessTools.PropertyGetter(typeof(Building), nameof(Building.MaxItemsInCell)),
                    AccessTools.Method(typeof(StoreUtility), nameof(StoreUtility.NoStorageBlockersIn))
                })
                {
                    Predicate();
                    if (method == null) return Problem(cell, null, StorageAllocationObservationStatus.Unsupported, "missing-quantity-binding");
                    if (!quantityMethods.TryGetValue(method, out bool accepted))
                    {
                        accepted = true;
                        var patches = Harmony.GetPatchInfo(method);
                        if (patches != null)
                            foreach (var patch in patches.Prefixes.ConcatPatches(patches.Postfixes, patches.Transpilers, patches.Finalizers))
                            {
                                Predicate();
                                if (patch.PatchMethod.DeclaringType?.Assembly == typeof(StorageAllocationObservation).Assembly) continue;
                                var name = patch.PatchMethod.DeclaringType?.FullName;
                                bool knownAsf = patch.PatchMethod.Module.Assembly.GetName().Name == "AdaptiveStorageFramework"
                                    && patch.PatchMethod.Module.Assembly.GetName().Version == new Version(1, 2, 4, 0)
                                    && patch.PatchMethod.Name == "Transpiler"
                                    && ((method.Name == nameof(GridsUtility.GetMaxItemsAllowedInCell) && name == "AdaptiveStorage.HarmonyPatches.StorageLimit")
                                        || (method.Name == nameof(StoreUtility.NoStorageBlockersIn)
                                            && name == "AdaptiveStorage.HarmonyPatches.StoreUtilityPatches+FixMissingValidStackDestinationCheck"));
                                if (!knownAsf) { accepted = false; break; }
                            }
                        quantityMethods.Add(method, accepted);
                    }
                    if (!accepted) return Problem(cell, null, StorageAllocationObservationStatus.Unsupported, "unhandled-quantity-patch:" + method.Name);
                }
                return true;
            }
        }

        // Keep patch iteration ordered without allocating a merged collection on every cell.
        private static IEnumerable<Patch> ConcatPatches(this IEnumerable<Patch> first,
            IEnumerable<Patch> second, IEnumerable<Patch> third, IEnumerable<Patch> fourth)
        {
            foreach (var p in first) yield return p;
            foreach (var p in second) yield return p;
            foreach (var p in third) yield return p;
            foreach (var p in fourth) yield return p;
        }
    }
}
