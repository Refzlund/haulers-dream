using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal enum ProjectionPurpose { PlanObservation, AdmissionObservation, DeliveryObservation, CellObservation, AvailabilityObservation }
    internal enum ProjectionPriorityMode { StrictlyBetter, AtLeast, WithinSelectedGroup }
    internal enum ProjectionObservationState { Complete, Deferred, Invalidated }
    internal enum ProjectionCapability { Supported, Unsupported, BindingFault }
    internal enum ProjectionEligibilityState { Eligible, Refused, NotEvaluated }
    internal enum ProjectionReason
    {
        None, InvalidRequest, NeedsMainThread, UnityNotInitialized, NestedProjection, OperationReentry,
        Disposed, WrongScope, SessionChanged, CatalogChanged, TransferInProgress, TickChanged,
        BudgetExhausted, RetainedStateLimit, ProviderInitializing, ProviderScanRequired,
        GroupChanged, SubjectChanged, ProviderStateMismatch, BindingFault, UnsupportedProvider,
        UnreviewedPatch, UnreviewedPredicate, UnsupportedFootprint, UnsupportedSourceHolder, WrongFaction, DestinationDisabled,
        BelowPriority, ThingFilterRefused, MemberFixedFilterRefused, ProviderRefused,
        NativeCellRefused, HdContextRefused, InvalidStackTarget, SelfTarget
    }

    // Explicitly supplied by the standalone fixture/future lifecycle owner. The
    // reader never establishes or advances a session or capability generation.
    internal sealed class StorageProjectionEnvironment
    {
        private static readonly FieldInfo Initialized = typeof(UnityData).GetField("initialized", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        internal int MainThreadId { get; }
        internal Game Game { get; set; }
        internal Guid SessionId { get; set; }
        internal long CatalogGeneration { get; set; }
        internal bool TransferInProgress { get; set; }
        internal StorageProjectionEnvironment(int initializedMainThreadId, Game game, Guid sessionId, long generation)
        { MainThreadId = initializedMainThreadId; Game = game; SessionId = sessionId; CatalogGeneration = generation; }
        internal ProjectionReason CheckThread()
        {
            if (MainThreadId <= 0) return ProjectionReason.UnityNotInitialized;
            if (Thread.CurrentThread.ManagedThreadId != MainThreadId) return ProjectionReason.NeedsMainThread;
            return UnityData.IsInMainThread && Initialized != null && (bool)Initialized.GetValue(null)
                ? ProjectionReason.None : ProjectionReason.UnityNotInitialized;
        }
    }

    internal sealed class StorageProjectionLimits
    {
        internal ProjectionWork Work { get; }
        internal int RetainedMembers { get; }
        internal int RetainedCells { get; }
        internal int RetainedParcels { get; }
        internal bool AllowMemberScans { get; }
        internal long RetainedRecords { get; }
        internal StorageProjectionLimits(ProjectionWork work, int retainedMembers = 4096, int retainedCells = 16384, int retainedParcels = 256, bool allowMemberScans = true, long retainedRecords = 131072)
        {
            Work = work ?? throw new ArgumentNullException(nameof(work));
            if (retainedMembers <= 0 || retainedCells <= 0 || retainedParcels <= 0 || retainedRecords <= 0) throw new ArgumentOutOfRangeException();
            RetainedMembers = retainedMembers; RetainedCells = retainedCells; RetainedParcels = retainedParcels;
            AllowMemberScans = allowMemberScans;
            RetainedRecords = retainedRecords;
        }
        internal static StorageProjectionLimits Page => new StorageProjectionLimits(
            new ProjectionWork(32, 200, 4096, 4096, 200, 800, 4096, 4096, 8192, 200, 200, 8192, 0, 4096));
        internal static StorageProjectionLimits Cell => new StorageProjectionLimits(
            new ProjectionWork(1, 1, 128, 128, 1, 8, 256, 128, 256, 1, 1, 64, 0, 256), allowMemberScans: false);
    }

    internal sealed class StorageProjectionRequest
    {
        internal StorageProjectionEnvironment Environment { get; }
        internal Map Map { get; }
        internal ISlotGroup Group { get; }
        internal Guid MapSessionId { get; }
        internal long ProviderCatalogGeneration { get; }
        internal string QueryId { get; }
        internal ProjectionPurpose Purpose { get; }
        internal ProjectionPriorityMode PriorityMode { get; }
        internal StoragePriority PriorityFloor { get; }
        internal StorageFilterContext FilterContext { get; }
        internal StorageProjectionRequest(StorageProjectionEnvironment environment, Map map, ISlotGroup group,
            Guid sessionId, long providerGeneration, string queryId, ProjectionPurpose purpose,
            ProjectionPriorityMode priorityMode, StoragePriority priorityFloor, StorageFilterContext filterContext)
        {
            Environment = environment; Map = map; Group = group; MapSessionId = sessionId;
            ProviderCatalogGeneration = providerGeneration; QueryId = queryId; Purpose = purpose;
            PriorityMode = priorityMode; PriorityFloor = priorityFloor; FilterContext = filterContext;
        }
    }

    internal sealed class StorageParcelProbe
    {
        internal string ParcelId { get; }
        internal Thing Subject { get; }
        internal Pawn Carrier { get; }
        internal Faction Faction { get; }
        internal int RequestedUnits { get; }
        internal StorageParcelProbe(string parcelId, Thing subject, Pawn carrier, Faction faction, int requestedUnits)
        { ParcelId = parcelId; Subject = subject; Carrier = carrier; Faction = faction; RequestedUnits = requestedUnits; }
    }

    internal sealed class ProjectionStatus
    {
        internal ProjectionObservationState Observation { get; }
        internal ProjectionCapability Capability { get; }
        internal ProjectionReason Reason { get; }
        internal bool Usable => Observation == ProjectionObservationState.Complete && Capability == ProjectionCapability.Supported;
        internal ProjectionStatus(ProjectionObservationState observation, ProjectionCapability capability, ProjectionReason reason)
        { Observation = observation; Capability = capability; Reason = reason; }
        internal static ProjectionStatus Complete => new ProjectionStatus(ProjectionObservationState.Complete, ProjectionCapability.Supported, ProjectionReason.None);
        internal static ProjectionStatus Failure(ProjectionReason reason)
        {
            bool unsupported = reason == ProjectionReason.UnsupportedProvider || reason == ProjectionReason.UnsupportedFootprint
                || reason == ProjectionReason.UnsupportedSourceHolder
                || reason == ProjectionReason.UnreviewedPatch || reason == ProjectionReason.UnreviewedPredicate;
            bool invalid = reason == ProjectionReason.InvalidRequest || reason == ProjectionReason.WrongScope || reason == ProjectionReason.GroupChanged
                || reason == ProjectionReason.SubjectChanged || reason == ProjectionReason.ProviderStateMismatch || reason == ProjectionReason.BindingFault
                || reason == ProjectionReason.SessionChanged || reason == ProjectionReason.CatalogChanged || reason == ProjectionReason.TickChanged;
            return new ProjectionStatus(invalid ? ProjectionObservationState.Invalidated : ProjectionObservationState.Deferred,
                reason == ProjectionReason.BindingFault ? ProjectionCapability.BindingFault : unsupported ? ProjectionCapability.Unsupported : ProjectionCapability.Supported, reason);
        }
    }

    internal sealed class ProjectionOpenResult
    {
        internal ProjectionStatus Status { get; }
        internal StorageProjectionScope Scope { get; }
        internal ProjectionWork Work { get; }
        internal ProjectionOpenResult(ProjectionStatus status, StorageProjectionScope scope = null, ProjectionWork work = null)
        { Status = status; Scope = scope; Work = work ?? ProjectionWork.Zero; }
    }

    // Output records contain values only. Live handles and ownership are kept in
    // private scope tables keyed by these observation IDs, never in the DTOs.
    internal sealed class StorageStackResource
    {
        internal string ResourceKey { get; }
        internal int ThingId { get; }
        internal string DefName { get; }
        internal int Count { get; }
        internal int StackLimit { get; }
        internal long Deficit { get; }
        internal bool? ProviderTargetValid { get; }
        internal int FootprintWidth => 1;
        internal int FootprintHeight => 1;
        internal StorageStackResource(string key, int id, string defName, int count, int limit, bool? targetValid)
        { ResourceKey = key; ThingId = id; DefName = defName; Count = count; StackLimit = limit; Deficit = Math.Max((long)limit - count, 0); ProviderTargetValid = targetValid; }
    }
    internal sealed class CellProjection
    {
        internal long ObservationId { get; }
        internal string QueryId { get; }
        internal Guid SessionId { get; }
        internal int MapId { get; }
        internal long CatalogGeneration { get; }
        internal int Tick { get; }
        internal IntVec3 Cell { get; }
        internal string ConcreteParentKey { get; }
        internal string GroupKey { get; }
        internal string Provider { get; }
        internal string VacantResourceKey { get; }
        internal int? MaximumSlots { get; }
        internal int? ItemCount { get; }
        internal long? VacantSlots { get; }
        internal int? ProviderCellCount { get; }
        internal int? ProviderMemberCount { get; }
        internal int? ProviderCellWiseCount { get; }
        internal int? ProviderSlotLimit { get; }
        internal bool? PerformanceFish { get; }
        internal int? GridEntryCount { get; }
        internal bool? ProviderAnyFreeSlots { get; }
        internal bool? ProviderContentsPacked { get; }
        internal IReadOnlyList<StorageStackResource> Stacks { get; }
        internal ProjectionStatus Status { get; }
        internal ProjectionWork Work { get; }
        internal CellProjection(long id, StorageProjectionRequest request, int mapId, int tick, IntVec3 cell, string parent,
            string group, string provider, string vacantKey, int? maximum, int? itemCount,
            IEnumerable<StorageStackResource> stacks, ProjectionStatus status, ProjectionWork work,
            int? providerCellCount = null, int? memberCount = null, int? cellWiseCount = null, int? slotLimit = null, bool? performanceFish = null,
            int? gridEntryCount = null, bool? anyFree = null, bool? packed = null)
        {
            ObservationId = id; QueryId = request.QueryId; SessionId = request.MapSessionId; CatalogGeneration = request.ProviderCatalogGeneration;
            MapId = mapId; Tick = tick; Cell = cell; ConcreteParentKey = parent; GroupKey = group;
            Provider = provider; VacantResourceKey = vacantKey; MaximumSlots = maximum; ItemCount = itemCount;
            VacantSlots = status.Usable && maximum.HasValue && itemCount.HasValue ? (long?)Math.Max((long)maximum.Value - itemCount.Value, 0) : null;
            Stacks = new ReadOnlyCollection<StorageStackResource>(new List<StorageStackResource>(stacks ?? Array.Empty<StorageStackResource>()));
            Status = status; Work = work; ProviderCellCount = providerCellCount; ProviderMemberCount = memberCount;
            ProviderCellWiseCount = cellWiseCount; ProviderSlotLimit = slotLimit; PerformanceFish = performanceFish;
            GridEntryCount = gridEntryCount; ProviderAnyFreeSlots = anyFree; ProviderContentsPacked = packed;
        }
    }
    internal sealed class ProjectionPredicateResult
    {
        internal string Predicate { get; }
        internal ProjectionEligibilityState State { get; }
        internal ProjectionReason Reason { get; }
        internal int? TargetId { get; }
        internal ProjectionPredicateResult(string predicate, ProjectionEligibilityState state, ProjectionReason reason, int? targetId = null)
        { Predicate = predicate; State = state; Reason = reason; TargetId = targetId; }
    }
    internal sealed class StorageTopUpEdge
    {
        internal string ResourceKey { get; }
        internal int TargetId { get; }
        internal long Units { get; }
        internal StorageTopUpEdge(string key, int id, long units) { ResourceKey = key; TargetId = id; Units = units; }
    }
    internal sealed class CellEligibility
    {
        internal long ObservationId { get; }
        internal string ParcelId { get; }
        internal ProjectionStatus Status { get; }
        internal ProjectionEligibilityState State { get; }
        internal IReadOnlyList<ProjectionPredicateResult> Predicates { get; }
        internal IReadOnlyList<StorageTopUpEdge> TopUps { get; }
        internal bool VacantSlotsEligible { get; }
        internal int? UnitsPerNewStack { get; }
        internal ProjectionWork Work { get; }
        internal CellEligibility(long id, string parcel, ProjectionStatus status, ProjectionEligibilityState state,
            IList<ProjectionPredicateResult> predicates, IList<StorageTopUpEdge> edges, bool vacant, int? units, ProjectionWork work)
        {
            ObservationId = id; ParcelId = parcel; Status = status; State = state;
            Predicates = new ReadOnlyCollection<ProjectionPredicateResult>(new List<ProjectionPredicateResult>(predicates));
            TopUps = new ReadOnlyCollection<StorageTopUpEdge>(new List<StorageTopUpEdge>(edges));
            VacantSlotsEligible = vacant; UnitsPerNewStack = units; Work = work;
        }
    }
    internal sealed class ProjectionValidation
    {
        internal ProjectionStatus Status { get; }
        internal CellEligibility FreshEligibility { get; }
        internal ProjectionValidation(ProjectionStatus status, CellEligibility fresh = null) { Status = status; FreshEligibility = fresh; }
    }
    internal sealed class ProjectionUnresolvedCell
    {
        internal string MemberKey { get; }
        internal IntVec3 Cell { get; }
        internal ProjectionReason Reason { get; }
        internal int? MemberOrdinal { get; }
        internal ProjectionUnresolvedCell(string member, IntVec3 cell, ProjectionReason reason, int? memberOrdinal = null)
        { MemberKey = member; Cell = cell; Reason = reason; MemberOrdinal = memberOrdinal; }
    }
    internal sealed class StorageProjectionPage
    {
        internal ProjectionStatus Status { get; }
        internal IReadOnlyList<CellProjection> Cells { get; }
        internal IReadOnlyList<ProjectionUnresolvedCell> Unresolved { get; }
        internal StorageProjectionCursor Cursor { get; }
        internal bool RequestedCellsComplete { get; }
        internal bool WholeGroupCoverageComplete { get; }
        internal int? NextMemberOrdinal { get; }
        internal int? NextCellOrdinal { get; }
        internal int UnresolvedTotal { get; }
        internal int UnresolvedSampleStart { get; }
        internal int UnresolvedSampleCount { get; }
        internal ProjectionWork Work { get; }
        internal StorageProjectionPage(ProjectionStatus status, IList<CellProjection> cells, IList<ProjectionUnresolvedCell> unresolved,
            StorageProjectionCursor cursor, bool requestedComplete, bool wholeComplete, ProjectionWork work, int? nextMember = null, int? nextCell = null,
            int unresolvedTotal = 0, int sampleStart = 0, int sampleCount = 0)
        {
            Status = status; Cells = new ReadOnlyCollection<CellProjection>(new List<CellProjection>(cells));
            Unresolved = new ReadOnlyCollection<ProjectionUnresolvedCell>(new List<ProjectionUnresolvedCell>(unresolved));
            Cursor = cursor; RequestedCellsComplete = requestedComplete; WholeGroupCoverageComplete = wholeComplete; Work = work;
            NextMemberOrdinal = nextMember; NextCellOrdinal = nextCell;
            UnresolvedTotal = unresolvedTotal; UnresolvedSampleStart = sampleStart; UnresolvedSampleCount = sampleCount;
        }
    }
}
