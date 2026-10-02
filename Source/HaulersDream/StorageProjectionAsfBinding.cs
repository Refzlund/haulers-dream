using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace HaulersDream
{
    // Soft dependency. Only inspected 1.2.4 APIs are bound; absence, mismatched
    // signatures and exceptions remain distinguishable from a provider refusal.
    internal sealed class StorageProjectionAsfBinding
    {
        internal Type ParentType { get; }
        internal Type CollectionType { get; }
        internal string Identity { get; }
        internal IReadOnlyList<MethodInfo> Methods { get; }
        internal MethodInfo LimitPatch { get; }
        internal MethodInfo ValidityPatch { get; }
        internal MethodInfo WorkerPatch { get; }
        private readonly MethodInfo limit, parentSettings, fixedAllows, capacity, targetValid, storingParent;
        private readonly MethodInfo contains, indexOf, itemAt, positionOf, cellCount, valid;
        private readonly PropertyInfo stored, count, cellWise, currentLimit, packed, anyFree, performanceFish, occupied;
        private readonly FieldInfo fixedCache;

        internal StorageProjectionAsfBinding(Assembly assembly)
        {
            if (assembly.GetName().Version != new Version(1, 2, 4, 0)) throw new NotSupportedException("ASF semantics require review for this version.");
            ParentType = NeedType(assembly, "AdaptiveStorage.ThingClass");
            CollectionType = NeedType(assembly, "AdaptiveStorage.ThingCollection");
            if (ParentType.BaseType != typeof(Building_Storage)) throw new MissingMemberException("Unexpected ASF parent base.");
            var methods = new List<MethodInfo>();
            MethodInfo M(Type type, string name, Type result, params Type[] args)
            {
                var m = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
                if (m == null || m.ReturnType != result) throw new MissingMethodException(type.FullName, name);
                methods.Add(m); return m;
            }
            PropertyInfo P(Type type, string name, Type result)
            {
                var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (p == null || p.PropertyType != result || p.GetGetMethod() == null) throw new MissingMemberException(type.FullName, name);
                methods.Add(p.GetGetMethod()); return p;
            }
            limit = M(ParentType, "GetMaxItemsForCell", typeof(int), typeof(IntVec3).MakeByRefType());
            parentSettings = M(ParentType, "GetParentStoreSettings", typeof(StorageSettings));
            fixedAllows = M(ParentType, "FixedFilterAllows", typeof(bool), typeof(Thing));
            capacity = M(ParentType, "HasCapacityForThing", typeof(bool), typeof(Thing));
            valid = M(ParentType, "ContainsAndAllows", typeof(bool), typeof(Thing));
            stored = P(ParentType, "StoredThings", CollectionType);
            currentLimit = P(ParentType, "CurrentSlotLimit", typeof(int));
            anyFree = P(ParentType, "AnyFreeSlots", typeof(bool));
            packed = P(ParentType, "ContentsPacked", typeof(bool));
            occupied = P(ParentType, "OccupiedRect", typeof(CellRect));
            count = P(CollectionType, "Count", typeof(int));
            cellWise = P(CollectionType, "CellWiseCount", typeof(int));
            contains = M(CollectionType, "Contains", typeof(bool), typeof(Thing));
            indexOf = M(CollectionType, "IndexOf", typeof(int), typeof(Thing));
            itemAt = M(CollectionType, "get_Item", typeof(Thing), typeof(int));
            positionOf = M(CollectionType, "MapPositionOf", typeof(IntVec3), typeof(Thing));
            cellCount = M(CollectionType, "ItemCountAtMapCell", typeof(int), typeof(IntVec3).MakeByRefType());
            performanceFish = P(NeedType(assembly, "AdaptiveStorage.ModCompatibility.PerformanceFish"), "Active", typeof(bool));
            targetValid = M(NeedType(assembly, "AdaptiveStorage.HarmonyPatches.StoreUtilityPatches+FixMissingValidStackDestinationCheck"),
                "IsValidStackDestination", typeof(bool), typeof(Thing));
            storingParent = M(NeedType(assembly, "AdaptiveStorage.Utility.ThingExtensions"), "StoringAdaptiveStorage", ParentType, typeof(Thing));
            fixedCache = ParentType.GetField("_fixedStorageSettings", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (fixedCache == null || fixedCache.FieldType != typeof(StorageSettings)) throw new MissingFieldException("ASF fixed filter cache");
            LimitPatch = NeedType(assembly, "AdaptiveStorage.HarmonyPatches.StorageLimit").GetMethod("Transpiler");
            ValidityPatch = targetValid.DeclaringType.GetMethod("Transpiler");
            WorkerPatch = NeedType(assembly, "AdaptiveStorage.HarmonyPatches.StoreUtilityPatches+PreventStorageLookupFaster").GetMethod("Prefix");
            if (LimitPatch == null || ValidityPatch == null || WorkerPatch == null) throw new MissingMethodException("ASF storage patches");
            // No custom footprint override is silently accepted. The inspected ASF
            // type inherits Building_Storage's occupied rectangle enumeration.
            if (ParentType.GetMethod("AllSlotCells").DeclaringType != typeof(Building_Storage)) throw new NotSupportedException("ASF footprint override");
            Methods = methods.AsReadOnly();
            Identity = assembly.FullName + ";mvid=" + assembly.ManifestModule.ModuleVersionId;
        }
        private static Type NeedType(Assembly assembly, string name) => assembly.GetType(name, true);
        internal StorageSettings PrepareFixed(Building_Storage parent) => (StorageSettings)parentSettings.Invoke(parent, null);
        internal StorageSettings ReadyFixed(Building_Storage parent) => (StorageSettings)fixedCache.GetValue(parent);
        internal bool FixedAllows(Building_Storage parent, Thing subject) => (bool)fixedAllows.Invoke(parent, new object[] { subject });
        internal bool HasCapacity(Building_Storage parent, Thing subject) => (bool)capacity.Invoke(parent, new object[] { subject });
        internal AsfProjectionState Read(Building_Storage parent, IntVec3 cell)
        {
            object collection = stored.GetValue(parent, null);
            if (collection == null || collection.GetType() != CollectionType) throw new InvalidOperationException("ASF registry not initialized.");
            return new AsfProjectionState(collection, (int)limit.Invoke(parent, new object[] { cell }),
                (int)cellCount.Invoke(collection, new object[] { cell }), (int)count.GetValue(collection, null),
                (int)cellWise.GetValue(collection, null), (int)currentLimit.GetValue(parent, null),
                (bool)packed.GetValue(parent, null), (bool)anyFree.GetValue(parent, null),
                (bool)performanceFish.GetValue(null, null), (CellRect)occupied.GetValue(parent, null));
        }
        internal bool RegisteredExactly(AsfProjectionState state, Building_Storage parent, Thing item, IntVec3 cell)
        {
            if (!(bool)contains.Invoke(state.Collection, new object[] { item })) return false;
            int index = (int)indexOf.Invoke(state.Collection, new object[] { item });
            if (index < 0 || index >= state.Count) return false;
            var actual = (Thing)itemAt.Invoke(state.Collection, new object[] { index });
            return ReferenceEquals(actual, item) && actual.thingIDNumber == item.thingIDNumber
                && (IntVec3)positionOf.Invoke(state.Collection, new object[] { item }) == cell
                && ReferenceEquals(storingParent.Invoke(null, new object[] { item }), parent);
        }
        internal bool TargetValid(Building_Storage parent, Thing item)
        {
            bool member = (bool)valid.Invoke(parent, new object[] { item });
            bool actual = (bool)targetValid.Invoke(null, new object[] { item });
            if (actual != member) throw new InvalidOperationException("ASF target predicates disagree.");
            return actual;
        }
        internal Thing ItemAt(AsfProjectionState state, int index) => (Thing)itemAt.Invoke(state.Collection, new object[] { index });
    }
    internal sealed class AsfProjectionState
    {
        internal object Collection { get; }
        internal int CellLimit { get; }
        internal int CellCount { get; }
        internal int Count { get; }
        internal int CellWiseCount { get; }
        internal int SlotLimit { get; }
        internal bool Packed { get; }
        internal bool AnyFree { get; }
        internal bool PerformanceFish { get; }
        internal CellRect Occupied { get; }
        internal AsfProjectionState(object collection, int cellLimit, int cellCount, int count, int cellWise, int slotLimit, bool packed, bool free, bool fish, CellRect occupied)
        { Collection = collection; CellLimit = cellLimit; CellCount = cellCount; Count = count; CellWiseCount = cellWise; SlotLimit = slotLimit; Packed = packed; AnyFree = free; PerformanceFish = fish; Occupied = occupied; }
        internal bool Same(AsfProjectionState other) => other != null && ReferenceEquals(Collection, other.Collection)
            && CellLimit == other.CellLimit && CellCount == other.CellCount && Count == other.Count && CellWiseCount == other.CellWiseCount
            && SlotLimit == other.SlotLimit && Packed == other.Packed && AnyFree == other.AnyFree && PerformanceFish == other.PerformanceFish && Occupied.Equals(other.Occupied);
    }
}
