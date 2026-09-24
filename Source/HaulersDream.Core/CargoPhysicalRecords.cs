using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    public enum CargoPhysicalStatus
    {
        NotComputed, Created, Applied, ExactDuplicate, SnapshotCaptured, ViewCompiled,
        InvalidReceipt, SessionMismatch, ComponentMismatch, CrossComponentUnsupported,
        UnknownEntity, StaleVersion, DuplicateEntity, ConflictingReceipt, ReceiptHistoryUnavailable,
        NodeLimit, EdgeLimit, FrontierLimit, ReceiptLimit, WorkLimit, ArithmeticOverflow,
        CoverageInvalidated, StaleSnapshot, UnknownSelection, InvalidGraph
    }
    public enum CargoPhysicalOperation { Birth, Move, Split, Absorb, AccountedSink }
    public enum CargoPhysicalNodeKind { ObservedBirth, EntityVersion, MeasuredTransfer, AccountedSink }
    public enum CargoCohortBoundary { AfterMove, AfterSplit, AfterSourceSeparationBeforeAbsorb }

    /// <summary>Opaque adapter observation. A key must identify one physical entity for this session.</summary>
    public readonly struct CargoPhysicalObservation
    {
        public string EntityKey { get; }
        public long Quantity { get; }
        public string ContextKey { get; }
        public CargoPhysicalObservation(string entityKey, long quantity, string contextKey)
        { EntityKey = entityKey; Quantity = quantity; ContextKey = contextKey; }
    }
    /// <summary>Owner-issued version reference. Entity/version IDs in this API are one-based.</summary>
    public readonly struct CargoPhysicalEntityVersion
    {
        public Guid Session { get; }
        public Guid Component { get; }
        public int VersionId { get; }
        public CargoPhysicalObservation Observation { get; }
        internal CargoPhysicalEntityVersion(Guid session, Guid component, int versionId, CargoPhysicalObservation observation)
        { Session = session; Component = component; VersionId = versionId; Observation = observation; }
    }
    public readonly struct CargoPhysicalStamp
    {
        public Guid Session { get; }
        public Guid Component { get; }
        public long GraphVersion { get; }
        public long CoverageRevision { get; }
        public bool Complete { get; }
        internal CargoPhysicalStamp(Guid session, Guid component, long graphVersion, long coverageRevision, bool complete)
        { Session = session; Component = component; GraphVersion = graphVersion; CoverageRevision = coverageRevision; Complete = complete; }
    }

    /// <summary>
    /// One immutable, observed primitive, not a job or a general native observer batch. Deltas are
    /// checked against the owned before versions and independently supplied after observations.
    /// A requested cohort names an observed moved branch; it is not an executed-attempt certificate.
    /// </summary>
    public sealed class CargoPhysicalReceipt
    {
        public Guid Session { get; }
        public Guid Component { get; }
        public long OperationId { get; }
        public long Sequence { get; }
        public CargoPhysicalOperation Kind { get; }
        public CargoPhysicalEntityVersion Source { get; }
        public CargoPhysicalEntityVersion Target { get; }
        public CargoPhysicalObservation? SourceAfter { get; }
        public CargoPhysicalObservation? Output { get; }
        public long SinkQuantity { get; }
        public string SinkContext { get; }
        public bool CaptureCohort { get; }

        private CargoPhysicalReceipt(Guid session, Guid component, long operationId, long sequence,
            CargoPhysicalOperation kind, CargoPhysicalEntityVersion source, CargoPhysicalEntityVersion target,
            CargoPhysicalObservation? sourceAfter, CargoPhysicalObservation? output, long sinkQuantity,
            string sinkContext, bool captureCohort)
        {
            Session = session; Component = component; OperationId = operationId; Sequence = sequence; Kind = kind;
            Source = source; Target = target; SourceAfter = sourceAfter; Output = output;
            SinkQuantity = sinkQuantity; SinkContext = sinkContext; CaptureCohort = captureCohort;
        }
        public static CargoPhysicalReceipt Birth(Guid session, Guid component, long id, long sequence, CargoPhysicalObservation output)
            => new CargoPhysicalReceipt(session, component, id, sequence, CargoPhysicalOperation.Birth, default, default, null, output, 0, null, false);
        public static CargoPhysicalReceipt Move(Guid session, Guid component, long id, long sequence,
            CargoPhysicalEntityVersion source, CargoPhysicalObservation output, bool captureCohort = false)
            => new CargoPhysicalReceipt(session, component, id, sequence, CargoPhysicalOperation.Move, source, default, null, output, 0, null, captureCohort);
        public static CargoPhysicalReceipt Split(Guid session, Guid component, long id, long sequence,
            CargoPhysicalEntityVersion source, CargoPhysicalObservation remainder, CargoPhysicalObservation moved, bool captureCohort = false)
            => new CargoPhysicalReceipt(session, component, id, sequence, CargoPhysicalOperation.Split, source, default, remainder, moved, 0, null, captureCohort);
        public static CargoPhysicalReceipt Absorb(Guid session, Guid component, long id, long sequence,
            CargoPhysicalEntityVersion source, CargoPhysicalEntityVersion target, CargoPhysicalObservation? remainder,
            CargoPhysicalObservation targetAfter, bool captureCohort = false)
            => new CargoPhysicalReceipt(session, component, id, sequence, CargoPhysicalOperation.Absorb, source, target, remainder, targetAfter, 0, null, captureCohort);
        public static CargoPhysicalReceipt Sink(Guid session, Guid component, long id, long sequence,
            CargoPhysicalEntityVersion source, CargoPhysicalObservation? remainder, long quantity, string sinkContext)
            => new CargoPhysicalReceipt(session, component, id, sequence, CargoPhysicalOperation.AccountedSink, source, default, remainder, null, quantity, sinkContext, false);

        internal bool Same(CargoPhysicalReceipt other) => other != null && Session == other.Session && Component == other.Component
            && OperationId == other.OperationId && Sequence == other.Sequence && Kind == other.Kind && Same(Source, other.Source)
            && Same(Target, other.Target) && Same(SourceAfter, other.SourceAfter) && Same(Output, other.Output)
            && SinkQuantity == other.SinkQuantity && SinkContext == other.SinkContext && CaptureCohort == other.CaptureCohort;
        internal static bool Same(CargoPhysicalObservation a, CargoPhysicalObservation b) =>
            a.EntityKey == b.EntityKey && a.Quantity == b.Quantity && a.ContextKey == b.ContextKey;
        internal static bool Same(CargoPhysicalObservation? a, CargoPhysicalObservation? b) =>
            a.HasValue == b.HasValue && (!a.HasValue || Same(a.Value, b.Value));
        internal static bool Same(CargoPhysicalEntityVersion a, CargoPhysicalEntityVersion b) =>
            a.Session == b.Session && a.Component == b.Component && a.VersionId == b.VersionId && Same(a.Observation, b.Observation);
    }

    public readonly struct CargoPhysicalNode
    {
        public int Id { get; }
        public CargoPhysicalNodeKind Kind { get; }
        public string EntityKey { get; }
        public string ContextKey { get; }
        public long Quantity { get; }
        public long BirthQuantity { get; }
        public int OriginId { get; }
        public long CreatedSequence { get; }
        public int Suboperation { get; }
        public bool Frontier { get; }
        internal CargoPhysicalNode(int id, CargoPhysicalNodeKind kind, string entityKey, string contextKey, long quantity,
            long birthQuantity, int originId, long sequence, int suboperation, bool frontier)
        {
            Id = id; Kind = kind; EntityKey = entityKey; ContextKey = contextKey; Quantity = quantity; BirthQuantity = birthQuantity;
            OriginId = originId; CreatedSequence = sequence; Suboperation = suboperation; Frontier = frontier;
        }
        internal CargoPhysicalNode AtFrontier(bool value) => new CargoPhysicalNode(Id, Kind, EntityKey, ContextKey, Quantity,
            BirthQuantity, OriginId, CreatedSequence, Suboperation, value);
        internal CargoPhysicalObservation Observation => new CargoPhysicalObservation(EntityKey, Quantity, ContextKey);
    }
    public sealed class CargoPhysicalCohort
    {
        public int Id { get; }
        public long OperationId { get; }
        public long Sequence { get; }
        public CargoCohortBoundary Boundary { get; }
        public int SelectedNodeId { get; }
        public int NextNodeId { get; }
        public long Quantity { get; }
        public IReadOnlyList<int> CompleteCut { get; }
        internal CargoPhysicalCohort(int id, long operationId, long sequence, CargoCohortBoundary boundary,
            int selectedNodeId, int nextNodeId, long quantity, int[] cut)
        {
            Id = id; OperationId = operationId; Sequence = sequence; Boundary = boundary; SelectedNodeId = selectedNodeId;
            NextNodeId = nextNodeId; Quantity = quantity; CompleteCut = Array.AsReadOnly(cut);
        }
    }
    /// <summary>Complete historical graph; CargoFlowEdge endpoints are zero-based array indices.</summary>
    public sealed class CargoPhysicalSnapshot
    {
        public CargoPhysicalStamp Stamp { get; }
        public CargoPhysicalStatus CoverageReason { get; }
        public long TotalObservedBirthQuantity { get; }
        public int AcceptedReceiptCount { get; }
        public IReadOnlyList<CargoPhysicalReceipt> Receipts { get; }
        public IReadOnlyList<CargoPhysicalNode> Nodes { get; }
        public IReadOnlyList<CargoFlowEdge> Edges { get; }
        public IReadOnlyList<CargoPhysicalEntityVersion> FrontierEntities { get; }
        public IReadOnlyList<CargoPhysicalCohort> Cohorts { get; }
        internal CargoPhysicalSnapshot(CargoPhysicalStamp stamp, CargoPhysicalStatus reason, long total, CargoPhysicalReceipt[] receipts,
            CargoPhysicalNode[] nodes, CargoFlowEdge[] edges, CargoPhysicalEntityVersion[] entities, CargoPhysicalCohort[] cohorts)
        {
            Stamp = stamp; CoverageReason = reason; TotalObservedBirthQuantity = total; AcceptedReceiptCount = receipts.Length;
            Receipts = Array.AsReadOnly(receipts);
            Nodes = Array.AsReadOnly(nodes); Edges = Array.AsReadOnly(edges); FrontierEntities = Array.AsReadOnly(entities); Cohorts = Array.AsReadOnly(cohorts);
        }
    }
    /// <summary>Derived numerical input. OwnedNodeIds maps each zero-based view index to an owned one-based ID.</summary>
    public sealed class CargoPhysicalFlowView
    {
        public CargoPhysicalStamp Stamp { get; }
        public bool IsCohort { get; }
        public int SelectionId { get; }
        public IReadOnlyList<CargoFlowNode> Nodes { get; }
        public IReadOnlyList<CargoFlowEdge> Edges { get; }
        public IReadOnlyList<int> OwnedNodeIds { get; }
        internal CargoPhysicalFlowView(CargoPhysicalStamp stamp, bool cohort, int selectionId,
            CargoFlowNode[] nodes, CargoFlowEdge[] edges, int[] ids)
        { Stamp = stamp; IsCohort = cohort; SelectionId = selectionId; Nodes = Array.AsReadOnly(nodes); Edges = Array.AsReadOnly(edges); OwnedNodeIds = Array.AsReadOnly(ids); }
    }
    public sealed class CargoPhysicalResult
    {
        public CargoPhysicalStatus Status { get; }
        public CargoPhysicalStamp Stamp { get; }
        public long WorkSpent { get; }
        public string Detail { get; }
        public int? CohortId { get; }
        public CargoPhysicalSnapshot Snapshot { get; }
        public CargoPhysicalFlowView View { get; }
        public IReadOnlyList<CargoPhysicalEntityVersion> Outputs { get; }
        internal CargoPhysicalResult(CargoPhysicalStatus status, CargoPhysicalStamp stamp, long work, string detail = null,
            int? cohortId = null, CargoPhysicalSnapshot snapshot = null, CargoPhysicalFlowView view = null, CargoPhysicalEntityVersion[] outputs = null)
        {
            Status = status; Stamp = stamp; WorkSpent = work; Detail = detail; CohortId = cohortId; Snapshot = snapshot; View = view;
            Outputs = Array.AsReadOnly(outputs ?? Array.Empty<CargoPhysicalEntityVersion>());
        }
    }
}
