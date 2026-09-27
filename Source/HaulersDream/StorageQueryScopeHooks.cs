using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    // A narrow optimization allowlist, not a grant of provider capacity. An excluded
    // composition continues through the immediate guarded reader, never a cached view.
    internal static class StorageQueryBindings
    {
        private static readonly FieldInfo specialFilters = AccessTools.Field(typeof(ThingFilter), "disallowedSpecialFilters");
        private static readonly FieldInfo specialWorker = AccessTools.Field(typeof(SpecialThingFilterDef), "workerInt");

        private static bool Known(MethodBase target, Patch patch, HarmonyPatchType kind)
        {
            if (patch.owner != HaulersDreamMod.HarmonyId) return false;
            var method = patch.PatchMethod;
            if (kind == HarmonyPatchType.Finalizer && method == AccessTools.Method(typeof(HDLog),
                nameof(HDLog.UniversalExceptionFinalizer), new[] { typeof(Exception), typeof(MethodBase) })) return true;
            if (target.DeclaringType == typeof(StoreUtility) && target.Name == nameof(StoreUtility.TryFindBestBetterStoreCellFor))
            {
                if (kind == HarmonyPatchType.Postfix && (method == AccessTools.Method(typeof(Patch_TryFindBestBetterStoreCellFor_HaulToStack), "Postfix")
                    || method == AccessTools.Method(typeof(Patch_TryFindBestBetterStoreCellFor_StorageFilter), "Postfix"))) return true;
                return method.DeclaringType == typeof(Patch_StorageQuery_SearchScope)
                    && (kind == HarmonyPatchType.Prefix || kind == HarmonyPatchType.Finalizer);
            }
            if (target.DeclaringType == typeof(StoreUtility) && target.Name == nameof(StoreUtility.TryFindBestBetterStoreCellForIn))
                return method.DeclaringType == typeof(Patch_StorageQuery_GroupSearchScope)
                    && (kind == HarmonyPatchType.Prefix || kind == HarmonyPatchType.Finalizer);
            if (target.DeclaringType == typeof(HaulAIUtility) && target.Name == nameof(HaulAIUtility.HaulToCellStorageJob))
                return (kind == HarmonyPatchType.Postfix && method == AccessTools.Method(typeof(Patch_HaulToCellStorageJob_ClampToCommitments), "Postfix"))
                    || (method.DeclaringType == typeof(Patch_StorageQuery_FactoryScope)
                        && (kind == HarmonyPatchType.Prefix || kind == HarmonyPatchType.Finalizer));
            return target.DeclaringType == typeof(StoreUtility) && target.Name == nameof(StoreUtility.IsGoodStoreCell)
                && kind == HarmonyPatchType.Postfix
                && method == AccessTools.Method(typeof(Patch_IsGoodStoreCell_HonourCommitments), "Postfix");
        }

        private static bool Inspected(MethodBase method)
        {
            if (method == null || method.DeclaringType.Assembly != typeof(Thing).Assembly) return false;
            var info = Harmony.GetPatchInfo(method);
            if (info == null) return true;
            foreach (var patch in info.Prefixes) if (!Known(method, patch, HarmonyPatchType.Prefix)) return false;
            foreach (var patch in info.Postfixes) if (!Known(method, patch, HarmonyPatchType.Postfix)) return false;
            foreach (var patch in info.Transpilers) if (!Known(method, patch, HarmonyPatchType.Transpiler)) return false;
            foreach (var patch in info.Finalizers) if (!Known(method, patch, HarmonyPatchType.Finalizer)) return false;
            return true;
        }

        private static bool InspectedOverloads(Type type, string name)
        {
            bool found = false;
            foreach (var method in AccessTools.GetDeclaredMethods(type))
                if (method.Name == name) { found = true; if (!Inspected(method)) return false; }
            return found;
        }

        internal static bool Inspected(MethodBase boundary, Thing subject)
        {
            if (VersionControl.CurrentMajor != 1 || VersionControl.CurrentMinor != 6
                || VersionControl.CurrentBuild != 4871 || VersionControl.CurrentRevision != 591) return false;
            // These HD postfixes can call a foreign controller without a patch on this
            // particular boundary. Keep their existing immediate compatibility path.
            if (StorageRefillHysteresisCompat.IsPresent || RimIOTCompat.IsPresent) return false;
            if (!Inspected(boundary) || !NativeSubject(subject)) return false;
            // No ASF/foreign worker replacement enters the provisional native loop. Those
            // providers retain the existing immediate observer and actual admission path.
            return Inspected(AccessTools.Method(typeof(StoreUtility), "TryFindBestBetterStoreCellForWorker"))
                && Inspected(AccessTools.Method(typeof(StoreUtility), nameof(StoreUtility.IsGoodStoreCell)))
                && Inspected(AccessTools.Method(typeof(StoreUtility), nameof(StoreUtility.NoStorageBlockersIn)))
                && Inspected(AccessTools.Method(typeof(GridsUtility), nameof(GridsUtility.GetMaxItemsAllowedInCell)))
                && Inspected(AccessTools.PropertyGetter(typeof(Building), nameof(Building.MaxItemsInCell)))
                && Inspected(AccessTools.Method(typeof(StorageSettings), nameof(StorageSettings.AllowedToAccept)))
                && Inspected(AccessTools.Method(typeof(ThingFilter), nameof(ThingFilter.Allows), new[] { typeof(Thing) }))
                && InspectedOverloads(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden))
                && InspectedOverloads(typeof(ReservationUtility), nameof(ReservationUtility.CanReserveNew))
                && InspectedOverloads(typeof(ReservationUtility), nameof(ReservationUtility.CanReserve))
                && InspectedOverloads(typeof(ReservationUtility), nameof(ReservationUtility.HasReserved))
                && InspectedOverloads(typeof(ReservationManager), nameof(ReservationManager.CanReserve))
                && InspectedOverloads(typeof(ReservationManager), nameof(ReservationManager.ReservedBy))
                && InspectedOverloads(typeof(Reachability), nameof(Reachability.CanReach))
                && InspectedOverloads(typeof(GenConstruct), nameof(GenConstruct.BlocksConstruction));
        }

        internal static bool NativeSubject(Thing subject)
        {
            if (subject == null || (subject.GetType() != typeof(Thing) && subject.GetType() != typeof(ThingWithComps))) return false;
            if (!Inspected(AccessTools.Method(typeof(Thing), nameof(Thing.CanStackWith), new[] { typeof(Thing) }))
                || !Inspected(AccessTools.Method(subject.GetType(), nameof(Thing.CanStackWith), new[] { typeof(Thing) }))) return false;
            if (subject is ThingWithComps withComps)
                foreach (var comp in withComps.AllComps)
                    if (comp.GetType().Assembly != typeof(Thing).Assembly
                        || !Inspected(AccessTools.Method(comp.GetType(), nameof(ThingComp.AllowStackWith), new[] { typeof(Thing) }))) return false;
            return true;
        }

        internal static bool NativeGroup(ISlotGroup group)
        {
            bool InspectedFilter(StorageSettings settings)
            {
                if (settings?.filter?.GetType() != typeof(ThingFilter) || specialFilters == null || specialWorker == null
                    || !(specialFilters.GetValue(settings.filter) is List<SpecialThingFilterDef> specials)) return false;
                if (!Inspected(AccessTools.PropertyGetter(typeof(SpecialThingFilterDef), nameof(SpecialThingFilterDef.Worker)))) return false;
                foreach (var special in specials)
                {
                    // Native shelf defaults contain these corpse exclusions even for steel.
                    // Their selected bodies immediately return false for the exact Thing /
                    // ThingWithComps subjects admitted above. No corpse or foreign worker is
                    // admitted by this small optimization; other filters use immediate reads.
                    var type = special?.workerClass;
                    if (type != typeof(SpecialThingFilterWorker_CorpsesColonist)
                        && type != typeof(SpecialThingFilterWorker_CorpsesStranger)
                        && type != typeof(SpecialThingFilterWorker_CorpsesLarge)) return false;
                    var actual = specialWorker.GetValue(special);
                    if (actual != null && actual.GetType() != type) return false;
                    if (!Inspected(AccessTools.Constructor(type))
                        || !Inspected(AccessTools.Constructor(typeof(SpecialThingFilterWorker)))
                        || !Inspected(AccessTools.Method(type, nameof(SpecialThingFilterWorker.Matches), new[] { typeof(Thing) }))) return false;
                }
                return true;
            }
            bool Parent(ISlotGroupParent parent)
            {
                if (parent == null || (parent.GetType() != typeof(Building_Storage) && parent.GetType() != typeof(Zone_Stockpile))) return false;
                var type = parent.GetType();
                if (!Inspected(AccessTools.Method(type, nameof(ISlotGroupParent.Accepts)))
                    || !Inspected(AccessTools.Method(type, nameof(ISlotGroupParent.GetParentStoreSettings)))
                    || !Inspected(AccessTools.Method(type, nameof(ISlotGroupParent.GetStoreSettings)))) return false;
                if (parent is Building_Storage building)
                    foreach (var comp in building.AllComps)
                        if (comp.GetType().Assembly != typeof(Thing).Assembly) return false;
                return InspectedFilter(parent.GetStoreSettings()) && InspectedFilter(parent.GetParentStoreSettings());
            }
            if (group is SlotGroup slot) return Parent(slot.parent);
            if (!(group is StorageGroup linked)) return false;
            if (linked.members.Count > 64) return false;
            foreach (var member in linked.members)
                if (!(member is ISlotGroupParent parent) || !Parent(parent)) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellFor))]
    internal static class Patch_StorageQuery_SearchScope
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Thing __0, Pawn __1, Map __2, MethodBase __originalMethod,
            out StorageCommitments.StorageAdmissionQueryScope __state)
            => __state = StorageCommitments.StorageAdmissionQueryScope.Open(__1, __2, __0, __originalMethod);

        [HarmonyPriority(Priority.Last)]
        private static void Finalizer(Exception __exception, ref bool __result, ref IntVec3 __5,
            StorageCommitments.StorageAdmissionQueryScope __state)
        {
            try
            {
                if (__exception == null && __result && __state != null && !__state.FinishSearch(__5))
                { __result = false; __5 = IntVec3.Invalid; }
            }
            finally { __state?.Dispose(); }
        }
    }

    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellForIn))]
    internal static class Patch_StorageQuery_GroupSearchScope
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Thing __0, Pawn __1, Map __2, MethodBase __originalMethod,
            out StorageCommitments.StorageAdmissionQueryScope __state)
            => __state = StorageCommitments.StorageAdmissionQueryScope.Open(__1, __2, __0, __originalMethod);

        [HarmonyPriority(Priority.Last)]
        private static void Finalizer(Exception __exception, ref bool __result, ref IntVec3 __6,
            StorageCommitments.StorageAdmissionQueryScope __state)
        {
            try
            {
                if (__exception == null && __result && __state != null && !__state.FinishSearch(__6))
                { __result = false; __6 = IntVec3.Invalid; }
            }
            finally { __state?.Dispose(); }
        }
    }

    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToCellStorageJob))]
    internal static class Patch_StorageQuery_FactoryScope
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Pawn __0, Thing __1, MethodBase __originalMethod,
            out StorageCommitments.StorageAdmissionQueryScope __state)
            => __state = StorageCommitments.StorageAdmissionQueryScope.OpenFactory(__0, __1, __originalMethod);

        [HarmonyPriority(Priority.Last)]
        private static void Finalizer(Exception __exception, StorageCommitments.StorageAdmissionQueryScope __state)
        {
            try
            {
                if (__exception == null) __state?.FinishFactory();
            }
            finally { __state?.Dispose(); }
        }
    }
}
