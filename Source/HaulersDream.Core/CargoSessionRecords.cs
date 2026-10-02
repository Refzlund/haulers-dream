using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    public enum CargoSessionStatus
    {
        Created, Admitted, Applied, ExactDuplicate, Current, Inactive, RetiredEntity, Quarantined,
        SnapshotCaptured, ViewCompiled, InvalidReceipt, ForeignHandle, StaleHandle, UnknownSelection,
        ConflictingReceipt, ReceiptHistoryUnavailable, BranchLimit, EntityLimit, OperationLimit,
        PhysicalFailure, WorkLimit, ArithmeticOverflow, CoverageInvalidated, SessionRetired
    }
    public enum CargoSessionGapReason { ObservedFailure, MissingObservation, UnsupportedMutation, RegistryCapacity }
    public enum CargoSessionRetirementReason { MapRemoved, LoadBoundary, Shutdown }

    /// <summary>Lifetime limits include superseded branches, retired keys and canonical receipts. No eviction.</summary>
    public sealed class CargoSessionLimits
    {
        public const int MaximumBranches = 64, MaximumEntities = 8192, MaximumOperations = 8192;
        public int Branches { get; }
        public int Entities { get; }
        public int Operations { get; }
        public CargoSessionLimits(int branches = MaximumBranches, int entities = MaximumEntities, int operations = MaximumOperations)
        { Branches = branches; Entities = entities; Operations = operations; }
        internal bool Valid => Branches > 0 && Branches <= MaximumBranches && Entities > 0 && Entities <= MaximumEntities
            && Operations > 0 && Operations <= MaximumOperations;
    }
    public readonly struct CargoComponentHandle
    {
        internal object Authority { get; }
        public Guid Session { get; }
        public Guid Component { get; }
        internal CargoComponentHandle(object authority, Guid session, Guid component)
        { Authority = authority; Session = session; Component = component; }
    }
    public readonly struct CargoEntityHandle
    {
        internal object Authority { get; }
        public CargoPhysicalEntityVersion Version { get; }
        public CargoPhysicalObservation Observation => Version.Observation;
        public CargoComponentHandle Component => new CargoComponentHandle(Authority, Version.Session, Version.Component);
        internal CargoEntityHandle(object authority, CargoPhysicalEntityVersion version) { Authority = authority; Version = version; }
    }
    public readonly struct CargoOriginHandle
    {
        internal object Authority { get; }
        public Guid Session { get; }
        public Guid OriginalComponent { get; }
        public int OriginalId { get; }
        internal CargoOriginHandle(object authority, Guid session, Guid component, int id)
        { Authority = authority; Session = session; OriginalComponent = component; OriginalId = id; }
    }
    public readonly struct CargoCohortHandle
    {
        internal object Authority { get; }
        public Guid Session { get; }
        public Guid OriginalComponent { get; }
        public int OriginalId { get; }
        internal CargoCohortHandle(object authority, Guid session, Guid component, int id)
        { Authority = authority; Session = session; OriginalComponent = component; OriginalId = id; }
    }
    public readonly struct CargoSessionStamp
    {
        internal object Authority { get; }
        public CargoPhysicalStamp Physical { get; }
        public long RegistryRevision { get; }
        public bool Complete { get; }
        public CargoComponentHandle Component => new CargoComponentHandle(Authority, Physical.Session, Physical.Component);
        internal CargoSessionStamp(object authority, CargoPhysicalStamp physical, long revision, bool complete)
        { Authority = authority; Physical = physical; RegistryRevision = revision; Complete = complete; }
    }
    /// <summary>A live or accounted-sink objective at one exact snapshot. Bare remappable node IDs are not query authority.</summary>
    public readonly struct CargoFrontierHandle
    {
        public CargoSessionStamp Stamp { get; }
        public int NodeId { get; }
        internal CargoFrontierHandle(CargoSessionStamp stamp, int nodeId) { Stamp = stamp; NodeId = nodeId; }
    }
    /// <summary>An absent partner supplies its independently observed BEFORE count and custody, never the merged count.</summary>
    public readonly struct CargoSessionParticipant
    {
        public bool IsTracked { get; }
        public CargoEntityHandle Handle { get; }
        public CargoPhysicalObservation Observation { get; }
        private CargoSessionParticipant(bool tracked, CargoEntityHandle handle, CargoPhysicalObservation observation)
        { IsTracked = tracked; Handle = handle; Observation = observation; }
        public static CargoSessionParticipant Tracked(CargoEntityHandle handle) => new CargoSessionParticipant(true, handle, handle.Observation);
        public static CargoSessionParticipant Untracked(CargoPhysicalObservation before) => new CargoSessionParticipant(false, default, before);
        internal bool Same(CargoSessionParticipant other) => IsTracked == other.IsTracked
            && (!IsTracked || ReferenceEquals(Handle.Authority, other.Handle.Authority) && CargoPhysicalReceipt.Same(Handle.Version, other.Handle.Version))
            && CargoPhysicalReceipt.Same(Observation, other.Observation);
    }
    /// <summary>
    /// Canonical outer observed envelope. Positive IDs increase across the session; retained exact replays are idempotent.
    /// Ticks are evidence only. The facade assigns a separate strictly ordered observation sequence.
    /// Native nested callbacks must be canonicalized by the adapter before this boundary.
    /// </summary>
    public sealed class CargoSessionReceipt
    {
        public long OperationId { get; }
        public int ObservedTick { get; }
        public CargoPhysicalOperation Kind { get; }
        public bool IsAdmission { get; }
        public CargoSessionParticipant Source { get; }
        public CargoSessionParticipant Target { get; }
        public CargoPhysicalObservation? SourceAfter { get; }
        public CargoPhysicalObservation? Output { get; }
        public long SinkQuantity { get; }
        public string SinkContext { get; }
        public bool CaptureCohort { get; }
        private CargoSessionReceipt(long id, int tick, CargoPhysicalOperation kind, bool admission,
            CargoSessionParticipant source, CargoSessionParticipant target, CargoPhysicalObservation? remainder,
            CargoPhysicalObservation? output, long quantity, string context, bool cohort)
        { OperationId = id; ObservedTick = tick; Kind = kind; IsAdmission = admission; Source = source; Target = target;
            SourceAfter = remainder; Output = output; SinkQuantity = quantity; SinkContext = context; CaptureCohort = cohort; }
        internal static CargoSessionReceipt Admission(long id, int tick, CargoPhysicalObservation observation)
            => new CargoSessionReceipt(id, tick, CargoPhysicalOperation.Birth, true, default, default, null, observation, 0, null, false);
        public static CargoSessionReceipt Birth(long id, int tick, CargoEntityHandle componentWitness, CargoPhysicalObservation output)
            => new CargoSessionReceipt(id, tick, CargoPhysicalOperation.Birth, false, CargoSessionParticipant.Tracked(componentWitness), default, null, output, 0, null, false);
        public static CargoSessionReceipt Move(long id, int tick, CargoEntityHandle source, CargoPhysicalObservation output, bool cohort = false)
            => new CargoSessionReceipt(id, tick, CargoPhysicalOperation.Move, false, CargoSessionParticipant.Tracked(source), default, null, output, 0, null, cohort);
        public static CargoSessionReceipt Split(long id, int tick, CargoEntityHandle source, CargoPhysicalObservation remainder, CargoPhysicalObservation output, bool cohort = false)
            => new CargoSessionReceipt(id, tick, CargoPhysicalOperation.Split, false, CargoSessionParticipant.Tracked(source), default, remainder, output, 0, null, cohort);
        public static CargoSessionReceipt Absorb(long id, int tick, CargoSessionParticipant source, CargoSessionParticipant target,
            CargoPhysicalObservation? remainder, CargoPhysicalObservation output, bool cohort = false)
            => new CargoSessionReceipt(id, tick, CargoPhysicalOperation.Absorb, false, source, target, remainder, output, 0, null, cohort);
        public static CargoSessionReceipt Sink(long id, int tick, CargoEntityHandle source, CargoPhysicalObservation? remainder, long quantity, string context)
            => new CargoSessionReceipt(id, tick, CargoPhysicalOperation.AccountedSink, false, CargoSessionParticipant.Tracked(source), default, remainder, null, quantity, context, false);
        internal bool Same(CargoSessionReceipt other) => other != null && OperationId == other.OperationId && ObservedTick == other.ObservedTick
            && Kind == other.Kind && IsAdmission == other.IsAdmission && Source.Same(other.Source) && Target.Same(other.Target)
            && CargoPhysicalReceipt.Same(SourceAfter, other.SourceAfter) && CargoPhysicalReceipt.Same(Output, other.Output)
            && SinkQuantity == other.SinkQuantity && SinkContext == other.SinkContext && CaptureCohort == other.CaptureCohort;
    }
    public readonly struct CargoSessionOrigin
    {
        public CargoOriginHandle Handle { get; }
        public int LocalId { get; }
        internal CargoSessionOrigin(CargoOriginHandle handle, int id) { Handle = handle; LocalId = id; }
    }
    public readonly struct CargoSessionCohort
    {
        public CargoCohortHandle Handle { get; }
        public int LocalId { get; }
        internal CargoSessionCohort(CargoCohortHandle handle, int id) { Handle = handle; LocalId = id; }
    }
    public sealed class CargoSessionSnapshot
    {
        public CargoSessionStamp Stamp { get; }
        public CargoPhysicalSnapshot Physical { get; }
        public IReadOnlyList<CargoFrontierHandle> Frontier { get; }
        public IReadOnlyList<CargoEntityHandle> Entities { get; }
        public IReadOnlyList<CargoSessionOrigin> Origins { get; }
        public IReadOnlyList<CargoSessionCohort> Cohorts { get; }
        internal CargoSessionSnapshot(CargoSessionStamp stamp, CargoPhysicalSnapshot physical, CargoFrontierHandle[] frontier,
            CargoEntityHandle[] entities, CargoSessionOrigin[] origins, CargoSessionCohort[] cohorts)
        { Stamp = stamp; Physical = physical; Frontier = Array.AsReadOnly(frontier); Entities = Array.AsReadOnly(entities);
            Origins = Array.AsReadOnly(origins); Cohorts = Array.AsReadOnly(cohorts); }
    }
    public sealed class CargoSessionGap
    {
        public CargoSessionGapReason Reason { get; }
        public CargoSessionStatus Status { get; }
        public long OperationId { get; }
        internal CargoSessionGap(CargoSessionGapReason reason, CargoSessionStatus status, long operationId)
        { Reason = reason; Status = status; OperationId = operationId; }
    }
    public sealed class CargoSessionResult
    {
        public CargoSessionStatus Status { get; }
        public CargoPhysicalStatus PhysicalStatus { get; }
        public CargoSessionStamp Stamp { get; }
        public long WorkSpent { get; }
        public string Detail { get; }
        public CargoOriginHandle? Origin { get; }
        public CargoCohortHandle? Cohort { get; }
        public IReadOnlyList<CargoEntityHandle> Outputs { get; }
        public CargoSessionSnapshot Snapshot { get; }
        public CargoPhysicalFlowView View { get; }
        internal CargoSessionResult(CargoSessionStatus status, long work = 0, string detail = null,
            CargoPhysicalStatus physicalStatus = CargoPhysicalStatus.NotComputed, CargoSessionStamp stamp = default,
            CargoEntityHandle[] outputs = null, CargoOriginHandle? origin = null, CargoCohortHandle? cohort = null,
            CargoSessionSnapshot snapshot = null, CargoPhysicalFlowView view = null)
        { Status = status; WorkSpent = work; Detail = detail; PhysicalStatus = physicalStatus; Stamp = stamp;
            Outputs = Array.AsReadOnly(outputs ?? Array.Empty<CargoEntityHandle>()); Origin = origin; Cohort = cohort; Snapshot = snapshot; View = view; }
    }
}
