using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    internal sealed partial class Cap03BudgetBridge
    {
        private Assembly asf;
        private Type parentType, collectionType, pageType, cursorType;
        private MethodInfo page, hasCapacity, acceptsDef, acceptsThing, overridesThing, fixedAllows;
        private MethodInfo declaredSettingsMethod, contains, indexOf, itemAt, positionOf, cellCount, valid, storingParent, targetValid;
        private PropertyInfo stored, count, cellWise, currentLimit, packed, anyFree, fish, occupied, used;
        private FieldInfo acceptedDefs;
        private ConstructorInfo workCtor, limitsCtor;
        internal Type AsfParentType => parentType;
        internal string AsfIdentity => asf.FullName + ";mvid=" + asf.ManifestModule.ModuleVersionId;
        internal void InitializeBudgetBindings()
        {
            if (asf != null) throw new InvalidOperationException("Budget bindings initialized twice.");
            asf = SingleAssembly("AdaptiveStorageFramework");
            if (asf.GetName().Version != new Version(1, 2, 4, 0)) throw new NotSupportedException("Review actual ASF version before this fixture.");
            Assemblies.Add(Identity(asf));
            parentType = Exact(asf, "AdaptiveStorage.ThingClass"); collectionType = Exact(asf, "AdaptiveStorage.ThingCollection");
            if (parentType.BaseType != typeof(Building_Storage)) throw new MissingMemberException("ASF parent inheritance differs.");
            cursorType = Hd("StorageProjectionCursor"); pageType = Hd("StorageProjectionPage");
            workCtor = Constructor(workType, typeof(long[]));
            limitsCtor = Constructor(limitsType, workType, typeof(int), typeof(int), typeof(int), typeof(bool), typeof(long));
            page = Method(scopeType, "ObserveGroupPage", false, pageType, cursorType, limitsType);
            used = BindProperty(scopeType, "Used", workType, false);
            stored = BindProperty(parentType, "StoredThings", collectionType, false);
            count = BindProperty(collectionType, "Count", typeof(int), false);
            cellWise = BindProperty(collectionType, "CellWiseCount", typeof(int), false);
            currentLimit = BindProperty(parentType, "CurrentSlotLimit", typeof(int), false);
            anyFree = BindProperty(parentType, "AnyFreeSlots", typeof(bool), false);
            packed = BindProperty(parentType, "ContentsPacked", typeof(bool), false);
            occupied = BindProperty(parentType, "OccupiedRect", typeof(CellRect), false);
            fish = BindProperty(Exact(asf, "AdaptiveStorage.ModCompatibility.PerformanceFish"), "Active", typeof(bool), true);
            hasCapacity = Method(parentType, "HasCapacityForThing", false, typeof(bool), typeof(Thing));
            fixedAllows = Method(parentType, "FixedFilterAllows", false, typeof(bool), typeof(Thing));
            declaredSettingsMethod = Method(parentType, "GetParentStoreSettings", false, typeof(StorageSettings));
            contains = Method(collectionType, "Contains", false, typeof(bool), typeof(Thing));
            indexOf = Method(collectionType, "IndexOf", false, typeof(int), typeof(Thing));
            itemAt = Method(collectionType, "get_Item", false, typeof(Thing), typeof(int));
            positionOf = Method(collectionType, "MapPositionOf", false, typeof(IntVec3), typeof(Thing));
            cellCount = Method(collectionType, "ItemCountAtMapCell", false, typeof(int), typeof(IntVec3).MakeByRefType());
            valid = Method(parentType, "ContainsAndAllows", false, typeof(bool), typeof(Thing));
            acceptsDef = Method(collectionType, "AcceptsForStacking", false, typeof(bool), typeof(ThingDef));
            acceptsThing = Method(collectionType, "AcceptsForStacking", false, typeof(bool), typeof(Thing));
            var extensions = Exact(asf, "AdaptiveStorage.Utility.ThingExtensions");
            overridesThing = Method(extensions, "OverridesCanStackWith", true, typeof(bool), typeof(Thing));
            storingParent = Method(extensions, "StoringAdaptiveStorage", true, parentType, typeof(Thing));
            targetValid = Method(Exact(asf, "AdaptiveStorage.HarmonyPatches.StoreUtilityPatches+FixMissingValidStackDestinationCheck"),
                "IsValidStackDestination", true, typeof(bool), typeof(Thing));
            acceptedDefs = collectionType.GetField("_defsAcceptedForStacking", Flags);
            var setType = Exact(asf, "AdaptiveStorage.Fishery.Collections.IntFishSet");
            if (acceptedDefs == null || acceptedDefs.FieldType != setType || !typeof(IEnumerable<int>).IsAssignableFrom(setType))
                throw new MissingFieldException("Exact read-only accepted-definition registry binding differs.");
            Bindings.Add(collectionType.FullName + "._defsAcceptedForStacking; field-token=" + acceptedDefs.MetadataToken + "; mvid=" + acceptedDefs.Module.ModuleVersionId);
            var nativeMemberGetter = typeof(IStorageGroupMember).GetProperty("ParentStoreSettings").GetGetMethod();
            var map = parentType.GetInterfaceMap(typeof(IStorageGroupMember));
            int getterIndex = Array.IndexOf(map.InterfaceMethods, nativeMemberGetter);
            if (getterIndex < 0) throw new MissingMemberException("Actual ASF interface fixed-settings route absent.");
            var selectedGetter = map.TargetMethods[getterIndex];
            if (selectedGetter.DeclaringType != typeof(Building_Storage) || selectedGetter.ReturnType != typeof(StorageSettings))
                throw new MissingMemberException("Neat native explicit fixed-settings getter route differs.");
            Bindings.Add(selectedGetter.DeclaringType.FullName + "." + selectedGetter.Name + "; token=" + selectedGetter.MetadataToken + "; mvid=" + selectedGetter.Module.ModuleVersionId);
            Method(typeof(Book), "GenerateBook", false, typeof(void), typeof(Pawn), typeof(long?));
            var bookStack = Method(typeof(Book), "CanStackWith", false, typeof(bool), typeof(Thing));
            if (!bookStack.IsVirtual || bookStack.GetBaseDefinition().DeclaringType == typeof(Book))
                throw new MissingMethodException("Book must override the actual native stack predicate.");
        }
        private PropertyInfo BindProperty(Type type, string name, Type expected, bool isStatic)
        {
            var p = Property(type, name, expected, isStatic); var getter = p.GetGetMethod(true);
            Bindings.Add(type.FullName + "." + getter.Name + "; token=" + getter.MetadataToken + "; mvid=" + getter.Module.ModuleVersionId); return p;
        }
        internal object MakeLimits(long providerVisits)
        {
            // Bounded, generous non-provider dimensions. Eight coordinates suffice
            // for the actual single-cell page and its bounded output reservation.
            var v = new long[] { 16, 8, 8192, 8192, 256, 4096, 8192, providerVisits, 16384, 256, 256, 16384, 0, 4096 };
            var work = workCtor.Invoke(new object[] { v });
            return limitsCtor.Invoke(new object[] { work, 16, 64, 8, true, 65536L });
        }
        internal List<ProjectionFixtureWork> LimitsWork(object value) => Work(Read(value, "Work", workType));
        internal object OpenBudget(Map map, ISlotGroup group, string query, object defaults)
        {
            var request = requestCtor.Invoke(new object[] { environment, map, group, SessionId, 1L, query,
                Enum.Parse(purposeType, "PlanObservation"), Enum.Parse(priorityType, "WithinSelectedGroup"), StoragePriority.Unstored,
                Enum.Parse(contextType, "Opportunistic") });
            return open.Invoke(null, new[] { request, defaults, catalog });
        }
        internal object GroupPage(object scope, object generous) => page.Invoke(scope, new object[] { null, generous });
        internal List<object> RawPageCells(object value) => ReadList(value, "Cells", cellType).Cast<object>().ToList();
        internal Cap03BudgetPage PageRow(object value)
        {
            var unresolvedType = Hd("ProjectionUnresolvedCell");
            return new Cap03BudgetPage
            {
                status = ResultStatus(value), requestedCellsComplete = Get<bool>(value, "RequestedCellsComplete"),
                wholeGroupComplete = Get<bool>(value, "WholeGroupCoverageComplete"), hasCursor = Read(value, "Cursor", cursorType) != null,
                nextMember = Get<int?>(value, "NextMemberOrdinal"), nextCell = Get<int?>(value, "NextCellOrdinal"),
                unresolvedTotal = Get<int>(value, "UnresolvedTotal"), unresolvedSampleStart = Get<int>(value, "UnresolvedSampleStart"),
                unresolvedSampleCount = Get<int>(value, "UnresolvedSampleCount"), operationWork = Work(Read(value, "Work", workType)),
                cells = RawPageCells(value).Select(Cell).ToList(),
                unresolved = ReadList(value, "Unresolved", unresolvedType).Cast<object>().Select(x => new Cap03BudgetUnresolved
                { member = Get<string>(x, "MemberKey"), cell = Get<IntVec3>(x, "Cell").ToString(), memberOrdinal = Get<int?>(x, "MemberOrdinal"),
                    reason = Read(x, "Reason", Hd("ProjectionReason")).ToString() }).ToList()
            };
        }
        internal List<ProjectionFixtureWork> ScopeUsed(object scope) => Work(used.GetValue(scope, null));
        internal List<ProjectionFixtureWork> OpenWork(object value) => Work(Read(value, "Work", workType));
        internal bool OverridesStack(Thing thing) => (bool)overridesThing.Invoke(null, new object[] { thing });
        internal StorageSettings DeclaredFixed(Building_Storage parent) => (StorageSettings)declaredSettingsMethod.Invoke(parent, null);
        internal bool FixedAllows(Building_Storage parent, Thing thing) => (bool)fixedAllows.Invoke(parent, new object[] { thing });
        internal Cap03BudgetRegistry Registry(Building_Storage parent, IntVec3 cell, Thing novel, Thing cargo)
        {
            if (parent.GetType() != parentType) throw new InvalidOperationException("Foreign ASF parent.");
            var collection = stored.GetValue(parent, null);
            if (collection == null || collection.GetType() != collectionType) throw new InvalidOperationException("ASF collection not normally initialized.");
            var n = (int)count.GetValue(collection, null); if (n < 0 || n > 64) throw new InvalidOperationException("Unexpected registry size; refuse an unbounded fixture census.");
            var acceptedSet = (ICollection<int>)acceptedDefs.GetValue(collection);
            if (acceptedSet.Count > n) throw new InvalidOperationException("Accepted-def registry exceeds actual resident count.");
            var accepted = acceptedSet.OrderBy(x => x).ToList();
            var row = new Cap03BudgetRegistry
            {
                collectionType = collection.GetType().FullName, count = n, cellWiseCount = (int)cellWise.GetValue(collection, null),
                cellCount = (int)cellCount.Invoke(collection, new object[] { cell }), slotLimit = (int)currentLimit.GetValue(parent, null),
                anyFree = (bool)anyFree.GetValue(parent, null), packed = (bool)packed.GetValue(parent, null),
                performanceFish = (bool)fish.GetValue(null, null), occupied = ((CellRect)occupied.GetValue(parent, null)).Cells.Select(x => x.ToString()).ToList(),
                acceptedShortHashes = accepted, acceptedDefs = DefDatabase<ThingDef>.AllDefsListForReading.Where(x => accepted.Contains(x.shortHash))
                    .Select(x => x.defName).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                novelOverridesStack = OverridesStack(novel), cargoOverridesStack = OverridesStack(cargo),
                acceptsNovelDef = (bool)acceptsDef.Invoke(collection, new object[] { novel.def }),
                acceptsCargoDef = (bool)acceptsDef.Invoke(collection, new object[] { cargo.def })
            };
            // Do not call HasCapacity/AcceptsForStacking(Thing) here: snapshots
            // must not run the member-scan control before/after its scope query.
            for (int i = 0; i < n; i++)
            {
                var item = (Thing)itemAt.Invoke(collection, new object[] { i });
                row.members.Add(new Cap03BudgetMember
                {
                    index = i, thingId = item.thingIDNumber, def = item.def.defName, shortHash = item.def.shortHash,
                    count = item.stackCount, stackLimit = item.def.stackLimit, spawned = item.Spawned, everStorable = item.def.EverStorable(false),
                    mapId = item.Map?.uniqueID, mapCell = item.Position.ToString(), holderType = item.ParentHolder?.GetType().FullName,
                    mapPosition = ((IntVec3)positionOf.Invoke(collection, new object[] { item })).ToString(),
                    contains = (bool)contains.Invoke(collection, new object[] { item }), indexOf = (int)indexOf.Invoke(collection, new object[] { item }),
                    storingParentId = (storingParent.Invoke(null, new object[] { item }) as Thing)?.thingIDNumber,
                    memberValid = (bool)valid.Invoke(parent, new object[] { item }), targetValid = (bool)targetValid.Invoke(null, new object[] { item }),
                    compTypes = (item as ThingWithComps)?.AllComps.Select(x => x.GetType().FullName).ToList() ?? new List<string>()
                });
            }
            return row;
        }
        internal object RegistryIdentity(Building_Storage parent) => stored.GetValue(parent, null);
    }
}