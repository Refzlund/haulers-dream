using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Sole-writer facade for one native session. It owns all mutable components and global entity
    /// identity. The adapter must create exactly one facade per actual session and canonicalize
    /// native observations. GUIDs are display identities; a private authority prevents handles
    /// from a different facade (even one with a reused GUID) from authorizing queries or mutation.
    /// No native hooks, causal classifications, automatic recovery or history eviction live here.
    /// </summary>
    public sealed class CargoPhysicalSession
    {
        private readonly object sync = new object(), authority = new object();
        private readonly Guid session;
        private readonly CargoSessionLimits limits;
        private readonly Dictionary<Guid, Branch> branches;
        private readonly Dictionary<string, Guid> entities;
        private readonly Dictionary<long, Accepted> operations;
        private long lastOperation, sequence, nextBranch;
        private bool retired, globalGap;
        private CargoSessionGap firstGap;
        private CargoSessionRetirementReason? retirement;

        private sealed class Branch
        {
            internal Guid Id, Successor;
            internal CargoPhysicalComponent Owner;
            internal long Revision;
            internal bool Quarantined;
            internal List<string> Keys = new List<string>();
            internal List<CargoSessionOrigin> Origins = new List<CargoSessionOrigin>();
            internal List<CargoSessionCohort> Cohorts = new List<CargoSessionCohort>();
        }
        private sealed class Accepted
        {
            internal CargoSessionReceipt Receipt;
            internal Guid Branch;
            internal CargoOriginHandle? Origin;
            internal CargoCohortHandle? Cohort;
        }
        private sealed class Stop : Exception
        {
            internal readonly CargoSessionStatus Status;
            internal readonly CargoPhysicalStatus Physical;
            internal Stop(CargoSessionStatus status, string message, CargoPhysicalStatus physical = CargoPhysicalStatus.NotComputed)
                : base(message) { Status = status; Physical = physical; }
        }
        private sealed class Budget
        {
            internal readonly long Limit;
            internal long Spent;
            internal long Remaining => Limit - Spent;
            internal Budget(long allowance) { Limit = Math.Max(0, allowance); }
            internal void Spend(long amount)
            { if (amount < 0 || amount > Remaining) { Spent = Limit; throw new Stop(CargoSessionStatus.WorkLimit, "Session staging allowance exhausted."); } Spent += amount; }
            internal void Physical(CargoPhysicalResult result, CargoPhysicalStatus expected)
            {
                Spend(result.WorkSpent);
                if (result.Status != expected) throw new Stop(result.Status == CargoPhysicalStatus.WorkLimit ? CargoSessionStatus.WorkLimit
                    : result.Status == CargoPhysicalStatus.ArithmeticOverflow ? CargoSessionStatus.ArithmeticOverflow : CargoSessionStatus.PhysicalFailure,
                    result.Detail, result.Status);
            }
        }
        private CargoPhysicalSession(Guid session, CargoSessionLimits limits)
        {
            this.session = session; this.limits = limits;
            // Capacity is bounded and allocated before any observed mutation can commit.
            branches = new Dictionary<Guid, Branch>(limits.Branches);
            entities = new Dictionary<string, Guid>(limits.Entities, StringComparer.Ordinal);
            operations = new Dictionary<long, Accepted>(limits.Operations);
        }
        public static CargoSessionResult TryCreate(Guid session, CargoSessionLimits limits, out CargoPhysicalSession owner)
        {
            owner = null;
            if (session == Guid.Empty || limits == null || !limits.Valid)
                return new CargoSessionResult(CargoSessionStatus.InvalidReceipt, detail: "Nonempty session and bounded positive lifetime limits required.");
            owner = new CargoPhysicalSession(session, limits);
            return new CargoSessionResult(CargoSessionStatus.Created);
        }
        public int RetainedBranchCount { get { lock (sync) return branches.Count; } }
        public int RetainedEntityCount { get { lock (sync) return entities.Count; } }
        public int RetainedOperationCount { get { lock (sync) return operations.Count; } }
        public long ObservationSequence { get { lock (sync) return sequence; } }
        public CargoSessionGap FirstGap { get { lock (sync) return firstGap; } }
        public CargoSessionRetirementReason? Retirement { get { lock (sync) return retirement; } }
        private static void Require(bool valid, CargoSessionStatus status, string detail)
        { if (!valid) throw new Stop(status, detail); }
        private static bool Key(string value) => value != null && value.Length > 0 && value.Length <= CargoPhysicalComponent.MaximumKeyLength && !string.IsNullOrWhiteSpace(value);
        private static void Observation(CargoPhysicalObservation value)
        { Require(Key(value.EntityKey) && Key(value.ContextKey) && value.Quantity > 0, CargoSessionStatus.InvalidReceipt, "Positive quantity and bounded nonempty entity/context keys required."); }
        private CargoSessionStamp Stamp(Branch branch)
        {
            var physical = branch.Owner.SessionStamp;
            return new CargoSessionStamp(authority, physical, branch.Revision,
                !retired && !globalGap && !branch.Quarantined && branch.Successor == Guid.Empty && physical.Complete);
        }
        private bool Current(CargoSessionStamp stamp) => ReferenceEquals(stamp.Authority, authority) && stamp.Complete
            && stamp.Physical.Session == session && branches.TryGetValue(stamp.Physical.Component, out var branch)
            && Stamp(branch).Complete && branch.Revision == stamp.RegistryRevision && branch.Owner.IsCurrent(stamp.Physical);
        public bool IsCurrent(CargoSessionStamp stamp) { lock (sync) return Current(stamp); }
        private Branch BranchFor(CargoComponentHandle handle)
        {
            Require(ReferenceEquals(handle.Authority, authority) && handle.Session == session,
                CargoSessionStatus.ForeignHandle, "Component handle belongs to another facade.");
            Require(branches.TryGetValue(handle.Component, out var branch), CargoSessionStatus.StaleHandle, "Unknown component handle.");
            return branch;
        }
        private CargoPhysicalSnapshot PhysicalSnapshot(Branch branch, Budget budget)
        {
            var result = branch.Owner.CaptureSnapshot(budget.Remaining); budget.Physical(result, CargoPhysicalStatus.SnapshotCaptured);
            return result.Snapshot;
        }
        private CargoEntityHandle[] Handles(CargoPhysicalSnapshot snapshot)
        {
            var output = new CargoEntityHandle[snapshot.FrontierEntities.Count];
            for (int i = 0; i < output.Length; i++) output[i] = new CargoEntityHandle(authority, snapshot.FrontierEntities[i]);
            return output;
        }
        private Branch Resolve(CargoSessionParticipant participant, Budget budget)
        {
            Observation(participant.Observation); budget.Spend(4L * CargoPhysicalComponent.MaximumKeyLength + 8);
            if (!participant.IsTracked)
            {
                Require(!entities.ContainsKey(participant.Observation.EntityKey), CargoSessionStatus.StaleHandle,
                    "An untracked-before claim cannot reuse any live, retired or quarantined key.");
                return null;
            }
            var branch = BranchFor(participant.Handle.Component);
            Require(Stamp(branch).Complete, CargoSessionStatus.StaleHandle, "Participant owner is superseded or has lost coverage.");
            Require(entities.TryGetValue(participant.Observation.EntityKey, out var id) && id == branch.Id,
                CargoSessionStatus.StaleHandle, "Global entity ownership differs.");
            var snapshot = PhysicalSnapshot(branch, budget);
            foreach (var current in snapshot.FrontierEntities)
            {
                budget.Spend(CargoPhysicalComponent.MaximumKeyLength + 2);
                if (CargoPhysicalReceipt.Same(current, participant.Handle.Version)) return branch;
            }
            throw new Stop(CargoSessionStatus.StaleHandle, "Expected current version, quantity or custody differs.");
        }
        private Guid BranchId(long id)
        {
            // Nonreused deterministic facade-local generation. Session/authority qualify this ID.
            var bytes = new byte[16]; for (int i = 0; i < 8; i++) bytes[i] = (byte)((ulong)id >> (i * 8));
            bytes[15] = 0x43; return new Guid(bytes);
        }
        private Branch Admission(CargoPhysicalObservation observation, long at, long generation, Budget budget)
        {
            var id = BranchId(generation);
            var result = CargoPhysicalComponent.CreateAdmission(session, id, observation, at, budget.Remaining, out var owner);
            budget.Physical(result, CargoPhysicalStatus.Created); budget.Spend(8);
            return new Branch { Id = id, Owner = owner, Revision = at, Keys = new List<string> { observation.EntityKey },
                Origins = new List<CargoSessionOrigin> { new CargoSessionOrigin(new CargoOriginHandle(authority, session, id, 1), 1) } };
        }
        private void NewKey(string key)
        { Require(!entities.ContainsKey(key), CargoSessionStatus.InvalidReceipt, "New identity was already seen, including retired and merged identities."); }
        public CargoSessionResult AdmitObserved(CargoPhysicalObservation observation, long operationId, int observedTick, long workAllowance)
            => ApplyObserved(CargoSessionReceipt.Admission(operationId, observedTick, observation), workAllowance);

        /// <summary>
        /// Called AFTER a real physical observation. Failure retains trusted histories but invalidates
        /// every known affected owner and quarantines new keys. Mandatory bounded safety invalidation
        /// runs even with zero allowance; WorkSpent counts staging/query work, not this fixed cleanup.
        /// </summary>
        public CargoSessionResult ApplyObserved(CargoSessionReceipt receipt, long workAllowance)
        {
            lock (sync)
            {
                var budget = new Budget(workAllowance); CargoSessionReceipt prior = null;
                try
                {
                    Require(!retired, CargoSessionStatus.SessionRetired, "Session has been retired.");
                    Require(workAllowance >= 0 && receipt != null, CargoSessionStatus.InvalidReceipt, "Receipt and nonnegative allowance required.");
                    budget.Spend(32L * CargoPhysicalComponent.MaximumKeyLength + 128);
                    Require(receipt.OperationId > 0 && receipt.ObservedTick >= 0, CargoSessionStatus.InvalidReceipt, "Positive canonical ID and nonnegative observed tick required.");
                    if (receipt.Output.HasValue) Observation(receipt.Output.Value);
                    if (receipt.SourceAfter.HasValue) Observation(receipt.SourceAfter.Value);
                    if (!receipt.IsAdmission) Observation(receipt.Source.Observation);
                    if (receipt.Kind == CargoPhysicalOperation.Absorb) Observation(receipt.Target.Observation);
                    if (receipt.Kind == CargoPhysicalOperation.AccountedSink)
                        Require(Key(receipt.SinkContext), CargoSessionStatus.InvalidReceipt, "Bounded sink context required.");
                    if (operations.TryGetValue(receipt.OperationId, out var accepted))
                    {
                        prior = accepted.Receipt;
                        Require(receipt.Same(prior), CargoSessionStatus.ConflictingReceipt, "Canonical ID was reused with different evidence.");
                        var duplicateBranch = branches[accepted.Branch];
                        for (int i = 0; duplicateBranch.Successor != Guid.Empty && i < limits.Branches; i++)
                            duplicateBranch = branches[duplicateBranch.Successor];
                        return new CargoSessionResult(CargoSessionStatus.ExactDuplicate, budget.Spent,
                            "Retained canonical evidence; historical outputs are not returned as current.", stamp: Stamp(duplicateBranch),
                            origin: accepted.Origin, cohort: accepted.Cohort);
                    }
                    Require(!globalGap, CargoSessionStatus.CoverageInvalidated, "Registry coverage was lost; no implicit re-admission.");
                    Require(receipt.OperationId > lastOperation, CargoSessionStatus.ReceiptHistoryUnavailable, "An unretained old canonical ID cannot be new evidence.");
                    Branch source = null, target = null;
                    if (!receipt.IsAdmission) source = Resolve(receipt.Source, budget);
                    if (receipt.Kind == CargoPhysicalOperation.Absorb) target = Resolve(receipt.Target, budget);
                    if (receipt.Kind == CargoPhysicalOperation.Absorb && source == null && target == null)
                        return new CargoSessionResult(CargoSessionStatus.Inactive, budget.Spent, "Both participants are outside tracked history.");
                    Require(receipt.IsAdmission || receipt.Kind == CargoPhysicalOperation.Absorb || source != null,
                        CargoSessionStatus.InvalidReceipt, "This primitive requires a current tracked source.");
                    Require(operations.Count < limits.Operations, CargoSessionStatus.OperationLimit, "Canonical history is retained for the full session.");
                    bool union = receipt.Kind == CargoPhysicalOperation.Absorb && source != target;
                    bool partnerAdmission = union && (source == null || target == null);
                    int addedBranches = receipt.IsAdmission ? 1 : union ? (partnerAdmission ? 2 : 1) : 0;
                    Require(branches.Count + addedBranches <= limits.Branches, CargoSessionStatus.BranchLimit, "Retained branch/alias limit reached.");
                    int addedKeys = receipt.IsAdmission || partnerAdmission || receipt.Kind == CargoPhysicalOperation.Split || receipt.Kind == CargoPhysicalOperation.Birth ? 1 : 0;
                    Require(entities.Count + addedKeys <= limits.Entities, CargoSessionStatus.EntityLimit, "Retained entity-key limit reached.");
                    if (receipt.IsAdmission || receipt.Kind == CargoPhysicalOperation.Split || receipt.Kind == CargoPhysicalOperation.Birth)
                        NewKey(receipt.Output.Value.EntityKey);
                    long nextSequence = checked(sequence + (partnerAdmission ? 2 : 1));
                    long nextGeneration = checked(nextBranch + addedBranches);
                    Branch stage, admitted = null;
                    CargoOriginHandle? origin = null; CargoCohortHandle? cohort = null;
                    if (receipt.IsAdmission)
                    {
                        stage = Admission(receipt.Output.Value, nextSequence, nextGeneration, budget);
                        origin = stage.Origins[0].Handle;
                    }
                    else
                    {
                        if (partnerAdmission)
                        {
                            var before = source == null ? receipt.Source.Observation : receipt.Target.Observation;
                            admitted = Admission(before, nextSequence - 1, nextGeneration - 1, budget);
                            if (source == null) source = admitted; else target = admitted;
                            origin = admitted.Origins[0].Handle;
                        }
                        if (union)
                        {
                            Guid id = BranchId(nextGeneration);
                            var joined = CargoPhysicalComponent.UnionForSession(source.Owner, target.Owner, id, budget.Remaining,
                                out var owner, out var sourceMap, out var targetMap);
                            budget.Physical(joined, CargoPhysicalStatus.Created);
                            stage = new Branch { Id = id, Owner = owner, Revision = nextSequence };
                            Remap(source, sourceMap, stage, budget); Remap(target, targetMap, stage, budget);
                        }
                        else
                        {
                            var copied = source.Owner.CopyForSession(budget.Remaining, out var owner);
                            budget.Physical(copied, CargoPhysicalStatus.Created);
                            budget.Spend(source.Keys.Count + source.Origins.Count + source.Cohorts.Count + 8);
                            stage = new Branch { Id = source.Id, Owner = owner, Revision = nextSequence,
                                Keys = new List<string>(source.Keys), Origins = new List<CargoSessionOrigin>(source.Origins), Cohorts = new List<CargoSessionCohort>(source.Cohorts) };
                        }
                        var beforeSnapshot = PhysicalSnapshot(stage, budget);
                        var physical = PhysicalReceipt(receipt, stage, beforeSnapshot, nextSequence, budget);
                        var applied = stage.Owner.Apply(physical, budget.Remaining); budget.Physical(applied, CargoPhysicalStatus.Applied);
                        if (receipt.Kind == CargoPhysicalOperation.Birth)
                        {
                            int local = stage.Origins.Count + 1;
                            origin = new CargoOriginHandle(authority, session, stage.Id, local);
                            stage.Origins.Add(new CargoSessionOrigin(origin.Value, local));
                        }
                        if (applied.CohortId.HasValue)
                        {
                            cohort = new CargoCohortHandle(authority, session, stage.Id, applied.CohortId.Value);
                            stage.Cohorts.Add(new CargoSessionCohort(cohort.Value, applied.CohortId.Value));
                        }
                        if (receipt.Kind == CargoPhysicalOperation.Split || receipt.Kind == CargoPhysicalOperation.Birth)
                            stage.Keys.Add(receipt.Output.Value.EntityKey);
                    }
                    var snapshot = PhysicalSnapshot(stage, budget);
                    budget.Spend(4L * snapshot.Nodes.Count + (long)stage.Keys.Count * (CargoPhysicalComponent.MaximumKeyLength + 2) + 32);
                    var outputs = Handles(snapshot);
                    var retained = new Accepted { Receipt = receipt, Branch = stage.Id, Origin = origin, Cohort = cohort };
                    var result = new CargoSessionResult(receipt.IsAdmission ? CargoSessionStatus.Admitted : CargoSessionStatus.Applied,
                        budget.Spent, stamp: Stamp(stage), outputs: outputs, origin: origin, cohort: cohort);
                    // Every fallible bounded stage/validation/allocation above completes before this
                    // single-lock publication. Dictionary capacities were reserved at construction.
                    if (union)
                    {
                        source.Successor = stage.Id; source.Owner.InvalidateFromSession();
                        target.Successor = stage.Id; target.Owner.InvalidateFromSession();
                        if (admitted != null) branches.Add(admitted.Id, admitted);
                    }
                    branches[stage.Id] = stage;
                    foreach (string key in stage.Keys) entities[key] = stage.Id;
                    operations.Add(receipt.OperationId, retained);
                    sequence = nextSequence; nextBranch = nextGeneration; lastOperation = receipt.OperationId;
                    return result;
                }
                catch (Stop error)
                {
                    if (!retired) FailObserved(receipt, prior, error.Status);
                    return new CargoSessionResult(error.Status, budget.Spent, error.Message, error.Physical);
                }
                catch (OverflowException)
                {
                    if (!retired) FailObserved(receipt, prior, CargoSessionStatus.ArithmeticOverflow);
                    return new CargoSessionResult(CargoSessionStatus.ArithmeticOverflow, budget.Spent, "Session observation arithmetic overflow.");
                }
            }
        }
        private static void Remap(Branch input, CargoPhysicalComponent.UnionMap map, Branch output, Budget budget)
        {
            budget.Spend(input.Keys.Count + input.Origins.Count + input.Cohorts.Count + 4);
            output.Keys.AddRange(input.Keys);
            foreach (var row in input.Origins) output.Origins.Add(new CargoSessionOrigin(row.Handle, map.Origins[row.LocalId]));
            foreach (var row in input.Cohorts) output.Cohorts.Add(new CargoSessionCohort(row.Handle, map.Cohorts[row.LocalId]));
        }
        private CargoPhysicalReceipt PhysicalReceipt(CargoSessionReceipt r, Branch stage, CargoPhysicalSnapshot snapshot, long at, Budget budget)
        {
            CargoPhysicalEntityVersion Version(string key)
            {
                foreach (var v in snapshot.FrontierEntities)
                { budget.Spend(CargoPhysicalComponent.MaximumKeyLength + 2); if (v.Observation.EntityKey == key) return v; }
                throw new Stop(CargoSessionStatus.StaleHandle, "Staged participant is not a live version.");
            }
            switch (r.Kind)
            {
                case CargoPhysicalOperation.Birth: return CargoPhysicalReceipt.Birth(session, stage.Id, r.OperationId, at, r.Output.Value);
                case CargoPhysicalOperation.Move: return CargoPhysicalReceipt.Move(session, stage.Id, r.OperationId, at, Version(r.Source.Observation.EntityKey), r.Output.Value, r.CaptureCohort);
                case CargoPhysicalOperation.Split: return CargoPhysicalReceipt.Split(session, stage.Id, r.OperationId, at, Version(r.Source.Observation.EntityKey), r.SourceAfter.Value, r.Output.Value, r.CaptureCohort);
                case CargoPhysicalOperation.Absorb: return CargoPhysicalReceipt.Absorb(session, stage.Id, r.OperationId, at, Version(r.Source.Observation.EntityKey), Version(r.Target.Observation.EntityKey), r.SourceAfter, r.Output.Value, r.CaptureCohort);
                default: return CargoPhysicalReceipt.Sink(session, stage.Id, r.OperationId, at, Version(r.Source.Observation.EntityKey), r.SourceAfter, r.SinkQuantity, r.SinkContext);
            }
        }
        private void FailObserved(CargoSessionReceipt receipt, CargoSessionReceipt prior, CargoSessionStatus status)
        {
            if (firstGap == null) firstGap = new CargoSessionGap(CargoSessionGapReason.ObservedFailure, status, receipt?.OperationId ?? 0);
            if (receipt == null) { GlobalGap(); return; }
            // Failed observed envelopes are not accepted physical receipts, but their canonical
            // ID still cannot later be reintroduced as new evidence on an unrelated branch.
            lastOperation = Math.Max(lastOperation, receipt.OperationId);
            // Shape checks and even the first budget reservation can fail before the normal
            // duplicate lookup. The retained canonical scope still belongs to this observed ID.
            // This single bounded integer-key lookup is mandatory safety cleanup, just like the
            // affected-key quarantines below; it does not require remaining staging allowance.
            if (operations.TryGetValue(receipt.OperationId, out var retained)) prior = retained.Receipt;
            QuarantineReceipt(receipt); if (prior != null) QuarantineReceipt(prior);
            if (prior == null && !Key(receipt.Source.Observation.EntityKey) && !Key(receipt.Target.Observation.EntityKey)
                && !Key(receipt.Output?.EntityKey) && !Key(receipt.SourceAfter?.EntityKey)) GlobalGap();
        }
        private void QuarantineReceipt(CargoSessionReceipt receipt)
        {
            QuarantineKey(receipt.Source.Observation.EntityKey); QuarantineKey(receipt.Target.Observation.EntityKey);
            if (receipt.SourceAfter.HasValue) QuarantineKey(receipt.SourceAfter.Value.EntityKey);
            if (receipt.Output.HasValue) QuarantineKey(receipt.Output.Value.EntityKey);
        }
        private void QuarantineKey(string key)
        {
            if (!Key(key)) return;
            if (entities.TryGetValue(key, out var id))
            {
                if (id != Guid.Empty) { var branch = branches[id]; branch.Quarantined = true; branch.Owner.InvalidateFromSession(); }
            }
            else if (entities.Count < limits.Entities) entities.Add(key, Guid.Empty);
            else GlobalGap(); // Cannot remember the lost identity: prevent all implicit re-admission.
        }
        private void GlobalGap()
        {
            globalGap = true;
            foreach (var branch in branches.Values) { branch.Quarantined = true; branch.Owner.InvalidateFromSession(); }
        }
        public CargoSessionResult FindCurrent(string entityKey)
        {
            lock (sync)
            {
                if (!Key(entityKey)) return new CargoSessionResult(CargoSessionStatus.InvalidReceipt);
                if (retired) return new CargoSessionResult(CargoSessionStatus.SessionRetired);
                if (globalGap) return new CargoSessionResult(CargoSessionStatus.CoverageInvalidated);
                if (!entities.TryGetValue(entityKey, out var id)) return new CargoSessionResult(CargoSessionStatus.Inactive);
                if (id == Guid.Empty) return new CargoSessionResult(CargoSessionStatus.Quarantined);
                var branch = branches[id]; var stamp = Stamp(branch);
                if (!stamp.Complete) return new CargoSessionResult(CargoSessionStatus.Quarantined, stamp: stamp);
                // Fixed at at most 128 nodes; no solver or state mutation on this lookup.
                var found = branch.Owner.FindForSession(entityKey);
                return new CargoSessionResult(found.HasValue ? CargoSessionStatus.Current : CargoSessionStatus.RetiredEntity,
                    stamp: stamp, outputs: found.HasValue ? new[] { new CargoEntityHandle(authority, found.Value) } : null);
            }
        }
        public CargoSessionResult CaptureSnapshot(CargoComponentHandle component, long workAllowance)
        {
            lock (sync)
            {
                var budget = new Budget(workAllowance);
                try
                {
                    Require(workAllowance >= 0, CargoSessionStatus.InvalidReceipt, "Nonnegative snapshot allowance required.");
                    var branch = BranchFor(component); var physical = PhysicalSnapshot(branch, budget); var stamp = Stamp(branch);
                    budget.Spend(4L * physical.Nodes.Count + branch.Origins.Count + branch.Cohorts.Count + 8);
                    var frontier = new List<CargoFrontierHandle>();
                    foreach (var node in physical.Nodes) if (node.Frontier) frontier.Add(new CargoFrontierHandle(stamp, node.Id));
                    var snapshot = new CargoSessionSnapshot(stamp, physical, frontier.ToArray(), Handles(physical), branch.Origins.ToArray(), branch.Cohorts.ToArray());
                    return new CargoSessionResult(CargoSessionStatus.SnapshotCaptured, budget.Spent, stamp: stamp, snapshot: snapshot);
                }
                catch (Stop error) { return new CargoSessionResult(error.Status, budget.Spent, error.Message, error.Physical); }
            }
        }
        public CargoSessionResult CompileOrigin(CargoSessionStamp expected, CargoOriginHandle origin, CargoFrontierHandle[] objective, long allowance)
            => Compile(expected, origin, default, false, objective, allowance);
        public CargoSessionResult CompileCohort(CargoSessionStamp expected, CargoCohortHandle cohort, CargoFrontierHandle[] objective, long allowance)
            => Compile(expected, default, cohort, true, objective, allowance);
        private CargoSessionResult Compile(CargoSessionStamp expected, CargoOriginHandle origin, CargoCohortHandle cohort,
            bool isCohort, CargoFrontierHandle[] objective, long allowance)
        {
            lock (sync)
            {
                var budget = new Budget(allowance);
                try
                {
                    Require(allowance >= 0, CargoSessionStatus.InvalidReceipt, "Nonnegative query allowance required.");
                    Require(Current(expected), CargoSessionStatus.StaleHandle, "Query requires current session coverage.");
                    Require(objective != null && objective.Length <= CargoPhysicalComponent.MaximumNodes,
                        CargoSessionStatus.UnknownSelection, "Bounded explicit frontier objective required.");
                    var branch = branches[expected.Physical.Component]; int local = 0;
                    budget.Spend(branch.Origins.Count + branch.Cohorts.Count + 4L * objective.Length + 8);
                    var requested = (CargoFrontierHandle[])objective.Clone();
                    if (isCohort)
                    {
                        Require(ReferenceEquals(cohort.Authority, authority) && cohort.Session == session, CargoSessionStatus.ForeignHandle, "Foreign cohort handle.");
                        foreach (var row in branch.Cohorts) if (row.Handle.OriginalComponent == cohort.OriginalComponent && row.Handle.OriginalId == cohort.OriginalId) local = row.LocalId;
                    }
                    else
                    {
                        Require(ReferenceEquals(origin.Authority, authority) && origin.Session == session, CargoSessionStatus.ForeignHandle, "Foreign origin handle.");
                        foreach (var row in branch.Origins) if (row.Handle.OriginalComponent == origin.OriginalComponent && row.Handle.OriginalId == origin.OriginalId) local = row.LocalId;
                    }
                    Require(local > 0, CargoSessionStatus.UnknownSelection, "Historical selection does not belong to this active component.");
                    var ids = new int[requested.Length];
                    for (int i = 0; i < ids.Length; i++)
                    {
                        Require(Current(requested[i].Stamp) && requested[i].Stamp.Physical.Component == expected.Physical.Component
                            && requested[i].Stamp.RegistryRevision == expected.RegistryRevision,
                            CargoSessionStatus.StaleHandle, "Objective comes from a different or obsolete snapshot.");
                        ids[i] = requested[i].NodeId;
                    }
                    var result = isCohort ? branch.Owner.CompileCohort(expected.Physical, local, ids, budget.Remaining)
                        : branch.Owner.CompileOrigin(expected.Physical, local, ids, budget.Remaining);
                    budget.Physical(result, CargoPhysicalStatus.ViewCompiled);
                    return new CargoSessionResult(CargoSessionStatus.ViewCompiled, budget.Spent, stamp: expected, view: result.View);
                }
                catch (Stop error) { return new CargoSessionResult(error.Status, budget.Spent, error.Message, error.Physical); }
            }
        }
        /// <summary>Explicit observed gap, not a proposed-operation preflight. At most two physical participants.</summary>
        public CargoSessionResult InvalidateObservedCoverage(CargoEntityHandle[] affected, CargoSessionGapReason reason, long operationId)
        {
            lock (sync)
            {
                if (retired) return new CargoSessionResult(CargoSessionStatus.SessionRetired);
                bool valid = affected != null && affected.Length > 0 && affected.Length <= 2 && operationId > 0 && Enum.IsDefined(typeof(CargoSessionGapReason), reason);
                if (!valid)
                {
                    if (firstGap == null) firstGap = new CargoSessionGap(CargoSessionGapReason.ObservedFailure, CargoSessionStatus.InvalidReceipt, operationId);
                    GlobalGap(); return new CargoSessionResult(CargoSessionStatus.InvalidReceipt);
                }
                lastOperation = Math.Max(lastOperation, operationId);
                if (firstGap == null) firstGap = new CargoSessionGap(reason, CargoSessionStatus.CoverageInvalidated, operationId);
                var requested = (CargoEntityHandle[])affected.Clone();
                foreach (var handle in requested)
                {
                    if (!ReferenceEquals(handle.Authority, authority) || !Key(handle.Observation.EntityKey)) { GlobalGap(); continue; }
                    QuarantineKey(handle.Observation.EntityKey);
                }
                return new CargoSessionResult(CargoSessionStatus.CoverageInvalidated);
            }
        }
        public CargoSessionResult RetireSession(CargoSessionRetirementReason reason)
        {
            lock (sync)
            {
                if (!Enum.IsDefined(typeof(CargoSessionRetirementReason), reason)) return new CargoSessionResult(CargoSessionStatus.InvalidReceipt);
                if (!retired) { retired = true; retirement = reason; foreach (var branch in branches.Values) branch.Owner.InvalidateFromSession(); }
                return new CargoSessionResult(CargoSessionStatus.SessionRetired);
            }
        }
    }
}
