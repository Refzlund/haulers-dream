using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    // Test-only exact reflection contract. The harness has no HD/Core reference.
    // Missing/changed members fail binding; they never stand in for corrected behavior.
    internal sealed partial class Cap03BudgetBridge
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private readonly Assembly hd, core;
        private readonly Type environmentType, catalogType, requestType, limitsType, parcelType, scopeType, cellType, eligibilityType;
        private readonly Type statusType, workType, purposeType, priorityType, contextType;
        private readonly ConstructorInfo environmentCtor, requestCtor, parcelCtor;
        private readonly MethodInfo create, prepare, open, observe, eligibility, recheck;
        private readonly object environment, limits;
        private object catalog;
        internal Guid SessionId { get; } = Guid.NewGuid();
        internal List<string> Bindings { get; } = new List<string>();
        internal List<ProjectionFixtureAssembly> Assemblies { get; } = new List<ProjectionFixtureAssembly>();

        internal Cap03BudgetBridge()
        {
            hd = SingleAssembly("HaulersDream"); core = SingleAssembly("HaulersDream.Core");
            foreach (var assembly in new[] { hd, core, typeof(Map).Assembly, typeof(HarmonyLib.Harmony).Assembly, GetType().Assembly })
                Assemblies.Add(Identity(assembly));
            environmentType = Hd("StorageProjectionEnvironment"); catalogType = Hd("StorageProviderCatalog");
            requestType = Hd("StorageProjectionRequest"); limitsType = Hd("StorageProjectionLimits");
            parcelType = Hd("StorageParcelProbe"); scopeType = Hd("StorageProjectionScope");
            cellType = Hd("CellProjection"); eligibilityType = Hd("CellEligibility"); statusType = Hd("ProjectionStatus");
            purposeType = Hd("ProjectionPurpose"); priorityType = Hd("ProjectionPriorityMode");
            workType = Exact(core, "HaulersDream.Core.ProjectionWork"); contextType = Exact(core, "HaulersDream.Core.StorageFilterContext");
            environmentCtor = Constructor(environmentType, typeof(int), typeof(Game), typeof(Guid), typeof(long));
            requestCtor = Constructor(requestType, environmentType, typeof(Map), typeof(ISlotGroup), typeof(Guid), typeof(long), typeof(string),
                purposeType, priorityType, typeof(StoragePriority), contextType);
            parcelCtor = Constructor(parcelType, typeof(string), typeof(Thing), typeof(Pawn), typeof(Faction), typeof(int));
            create = Method(catalogType, "Create", true, statusType, environmentType, catalogType.MakeByRefType());
            if (!create.GetParameters()[1].IsOut) throw new MissingMethodException("Create must expose an out catalog.");
            prepare = Method(catalogType, "PrepareMember", false, Hd("StorageProjectionPreparation"), typeof(ISlotGroupParent), typeof(long), typeof(int), typeof(bool));
            open = Method(Hd("StorageResourceProjector"), "Open", true, Hd("ProjectionOpenResult"), requestType, limitsType, catalogType);
            observe = Method(scopeType, "ObserveCell", false, cellType, typeof(IntVec3));
            eligibility = Method(scopeType, "ObserveEligibility", false, eligibilityType, parcelType, cellType);
            recheck = Method(scopeType, "Recheck", false, Hd("ProjectionValidation"), parcelType, cellType, eligibilityType);
            Method(scopeType, "Dispose", false, typeof(void));
            if (!typeof(IDisposable).IsAssignableFrom(scopeType)) throw new MissingMemberException("Scope must implement IDisposable.");
            limits = Property(limitsType, "Page", limitsType, true).GetValue(null, null);
            environment = environmentCtor.Invoke(new object[] { Thread.CurrentThread.ManagedThreadId, Current.Game, SessionId, 1L });
        }

        internal ProjectionFixtureStatus CreateCatalog()
        {
            object[] args = { environment, null };
            var status = Status(create.Invoke(null, args)); catalog = args[1];
            if (status.usable && (catalog == null || catalog.GetType() != catalogType)) throw new InvalidOperationException("Successful Create returned wrong catalog.");
            return status;
        }
        internal string NativeIdentity => Get<string>(catalog, "NativeIdentity");
        internal bool NativeReviewed => Get<bool>(catalog, "NativeSemanticsReviewed");
        internal List<string> PatchInventory => ReadList(catalog, "PatchInventory", typeof(string)).Cast<string>().ToList();
        internal bool HasOpenScope => (bool)Property(Hd("StorageResourceProjector"), "HasOpenScope", typeof(bool), true).GetValue(null, null);
        internal object Prepare(ISlotGroupParent parent, long allowance, bool warm) => prepare.Invoke(catalog, new object[] { parent, allowance, 65536, warm });
        internal object Open(Map map, ISlotGroup group, string query)
        {
            var request = requestCtor.Invoke(new object[] { environment, map, group, SessionId, 1L, query,
                Enum.Parse(purposeType, "CellObservation"), Enum.Parse(priorityType, "WithinSelectedGroup"), StoragePriority.Unstored,
                Enum.Parse(contextType, "Opportunistic") });
            return open.Invoke(null, new[] { request, limits, catalog });
        }
        internal object Scope(object opened) => Read(opened, "Scope", scopeType);
        internal object Parcel(string id, Thing subject, Pawn actor) => parcelCtor.Invoke(new object[] { id, subject, actor, actor.Faction, subject.stackCount });
        internal object Observe(object scope, IntVec3 cell) => observe.Invoke(scope, new object[] { cell });
        internal object Eligibility(object scope, object parcel, object cell) => eligibility.Invoke(scope, new[] { parcel, cell });
        internal object Recheck(object scope, object parcel, object cell, object prior) => recheck.Invoke(scope, new[] { parcel, cell, prior });
        internal object Fresh(object validation) => Read(validation, "FreshEligibility", eligibilityType);
        internal ProjectionFixtureStatus ResultStatus(object result) => Status(Read(result, "Status", statusType));

        internal ProjectionFixtureCell Cell(object value)
        {
            return new ProjectionFixtureCell
            {
                observationId = Get<long>(value, "ObservationId"), query = Get<string>(value, "QueryId"),
                session = Get<Guid>(value, "SessionId").ToString("N"), mapId = Get<int>(value, "MapId"), tick = Get<int>(value, "Tick"),
                generation = Get<long>(value, "CatalogGeneration"), cell = Get<IntVec3>(value, "Cell").ToString(),
                parentKey = Get<string>(value, "ConcreteParentKey"), groupKey = Get<string>(value, "GroupKey"), provider = Get<string>(value, "Provider"),
                vacantKey = Get<string>(value, "VacantResourceKey"), maximumSlots = Get<int?>(value, "MaximumSlots"),
                itemCount = Get<int?>(value, "ItemCount"), vacantSlots = Get<long?>(value, "VacantSlots"), gridEntries = Get<int?>(value, "GridEntryCount"),
                status = ResultStatus(value), work = Work(Read(value, "Work", workType)),
                stacks = ReadList(value, "Stacks", Hd("StorageStackResource")).Cast<object>().Select(x => new ProjectionFixtureStack
                {
                    key = Get<string>(x, "ResourceKey"), thingId = Get<int>(x, "ThingId"), def = Get<string>(x, "DefName"),
                    count = Get<int>(x, "Count"), stackLimit = Get<int>(x, "StackLimit"), deficit = Get<long>(x, "Deficit"),
                    providerTargetValid = Get<bool?>(x, "ProviderTargetValid")
                }).ToList()
            };
        }
        internal ProjectionFixtureEligibility EligibilityRow(object value)
        {
            return new ProjectionFixtureEligibility
            {
                observationId = Get<long>(value, "ObservationId"), parcelId = Get<string>(value, "ParcelId"),
                status = ResultStatus(value), state = Read(value, "State", Hd("ProjectionEligibilityState")).ToString(),
                vacantEligible = Get<bool>(value, "VacantSlotsEligible"), unitsPerNewStack = Get<int?>(value, "UnitsPerNewStack"),
                work = Work(Read(value, "Work", workType)),
                predicates = ReadList(value, "Predicates", Hd("ProjectionPredicateResult")).Cast<object>().Select(x => new ProjectionFixturePredicate
                {
                    name = Get<string>(x, "Predicate"), state = Read(x, "State", Hd("ProjectionEligibilityState")).ToString(),
                    reason = Read(x, "Reason", Hd("ProjectionReason")).ToString(), targetId = Get<int?>(x, "TargetId")
                }).ToList(),
                topUps = ReadList(value, "TopUps", Hd("StorageTopUpEdge")).Cast<object>().Select(x => new ProjectionFixtureEdge
                { key = Get<string>(x, "ResourceKey"), targetId = Get<int>(x, "TargetId"), units = Get<long>(x, "Units") }).ToList()
            };
        }
        internal T Get<T>(object target, string name) => (T)Read(target, name, typeof(T));
        internal List<ProjectionFixtureWork> Work(object value)
        {
            Type kind = Exact(core, "HaulersDream.Core.ProjectionWorkKind");
            var indexer = workType.GetProperty("Item", Flags);
            if (indexer?.PropertyType != typeof(long) || !indexer.GetIndexParameters().Select(x => x.ParameterType).SequenceEqual(new[] { kind }))
                throw new MissingMemberException("ProjectionWork indexer signature differs.");
            return Enum.GetValues(kind).Cast<object>().Where(x => x.ToString() != "Count")
                .Select(x => new ProjectionFixtureWork { kind = x.ToString(), value = (long)indexer.GetValue(value, new[] { x }) }).ToList();
        }
        private ProjectionFixtureStatus Status(object value) => new ProjectionFixtureStatus
        {
            usable = Get<bool>(value, "Usable"), observation = Read(value, "Observation", Hd("ProjectionObservationState")).ToString(),
            capability = Read(value, "Capability", Hd("ProjectionCapability")).ToString(), reason = Read(value, "Reason", Hd("ProjectionReason")).ToString()
        };
        private object Read(object target, string name, Type expected)
        {
            if (target == null || target.GetType().Assembly != hd) throw new InvalidOperationException("Unexpected null/foreign projection DTO: " + name);
            return Property(target.GetType(), name, expected, false).GetValue(target, null);
        }
        private IEnumerable ReadList(object target, string name, Type element) =>
            (IEnumerable)Read(target, name, typeof(IReadOnlyList<>).MakeGenericType(element));
        private Type Hd(string name) => Exact(hd, "HaulersDream." + name);
        private static Type Exact(Assembly assembly, string name)
        {
            var type = assembly.GetType(name, true, false);
            if (type.Assembly != assembly) throw new TypeLoadException("Forwarded fixture type: " + name);
            return type;
        }
        private MethodInfo Method(Type type, string name, bool isStatic, Type returns, params Type[] parameters)
        {
            var method = type.GetMethod(name, Flags, null, parameters, null);
            if (method == null || method.IsStatic != isStatic || method.ReturnType != returns) throw new MissingMethodException(type.FullName, name);
            Bindings.Add(type.FullName + "." + name + "; token=" + method.MetadataToken + "; mvid=" + method.Module.ModuleVersionId);
            return method;
        }
        private ConstructorInfo Constructor(Type type, params Type[] parameters)
        {
            var ctor = type.GetConstructor(Flags, null, parameters, null);
            if (ctor == null) throw new MissingMethodException(type.FullName, ".ctor");
            Bindings.Add(type.FullName + ".ctor; token=" + ctor.MetadataToken + "; mvid=" + ctor.Module.ModuleVersionId); return ctor;
        }
        private static PropertyInfo Property(Type type, string name, Type expected, bool isStatic)
        {
            var property = type.GetProperty(name, Flags);
            if (property?.PropertyType != expected || property.GetGetMethod(true) == null || property.GetGetMethod(true).IsStatic != isStatic)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }
        private static Assembly SingleAssembly(string name)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies().Where(x => x.GetName().Name == name).ToList();
            if (matches.Count != 1) throw new InvalidOperationException("Exactly one loaded " + name + " required; got " + matches.Count);
            return matches[0];
        }
        private static ProjectionFixtureAssembly Identity(Assembly assembly)
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location) || !File.Exists(assembly.Location)) throw new InvalidOperationException("Missing loaded assembly file.");
            using (var stream = File.OpenRead(assembly.Location))
            using (var sha = SHA256.Create())
                return new ProjectionFixtureAssembly { name = assembly.FullName, path = assembly.Location,
                    mvid = assembly.ManifestModule.ModuleVersionId.ToString(), sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") };
        }
    }
}
