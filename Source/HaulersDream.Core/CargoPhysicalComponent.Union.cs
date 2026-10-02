using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    public sealed partial class CargoPhysicalComponent
    {
        // Session-only entry points. The facade never exposes its mutable owners. Existing public
        // owner creation, primitive application and query behavior remain unchanged.
        internal CargoPhysicalStamp SessionStamp { get { lock (sync) return Stamp; } }
        internal CargoPhysicalEntityVersion? FindForSession(string key)
        {
            lock (sync) return state.Live.TryGetValue(key, out int node) ? Version(state.Nodes[node]) : (CargoPhysicalEntityVersion?)null;
        }
        internal void InvalidateFromSession()
        {
            lock (sync)
            {
                if (!complete) return;
                complete = false; coverageReason = CargoPhysicalStatus.CoverageInvalidated;
                // Exhausted revision space must still invalidate; completeness is independently checked.
                if (coverageRevision < long.MaxValue) coverageRevision++;
            }
        }
        internal static CargoPhysicalResult CreateAdmission(Guid session, Guid component, CargoPhysicalObservation observation,
            long sequence, long allowance, out CargoPhysicalComponent owner)
        {
            owner = null;
            if (sequence <= 0) return new CargoPhysicalResult(CargoPhysicalStatus.InvalidReceipt, default, 0);
            var result = TryCreate(session, component, new[] { observation }, allowance, out var staged);
            if (staged == null) return result;
            var old = staged.state.Nodes[0];
            staged.state.Nodes[0] = new CargoPhysicalNode(old.Id, old.Kind, old.EntityKey, old.ContextKey, old.Quantity,
                old.BirthQuantity, old.OriginId, sequence, 0, true);
            staged.state.LastSequence = sequence;
            owner = staged; return result;
        }
        internal CargoPhysicalResult CopyForSession(long allowance, out CargoPhysicalComponent copy)
        {
            lock (sync)
            {
                copy = null; var budget = new Budget(allowance);
                try
                {
                    Require(allowance >= 0 && complete, CargoPhysicalStatus.CoverageInvalidated, "Only a complete owner can be staged.");
                    copy = new CargoPhysicalComponent(session, component, state.Copy(budget))
                    { graphVersion = graphVersion, coverageRevision = coverageRevision, coverageReason = coverageReason };
                    return new CargoPhysicalResult(CargoPhysicalStatus.Created, copy.Stamp, budget.Spent);
                }
                catch (Stop error) { return ReadFailure(error, budget); }
            }
        }
        internal sealed class UnionMap
        {
            internal readonly int[] Nodes, Origins, Cohorts;
            internal UnionMap(int nodes, int origins, int cohorts)
            { Nodes = new int[nodes + 1]; Origins = new int[origins]; Cohorts = new int[cohorts + 1]; }
        }
        private readonly struct UnionNode
        {
            internal readonly CargoPhysicalNode Node;
            internal readonly int Side;
            internal UnionNode(CargoPhysicalNode node, int side) { Node = node; Side = side; }
        }
        private static int Chronology(CargoPhysicalNode a, CargoPhysicalNode b)
        { int order = a.CreatedSequence.CompareTo(b.CreatedSequence); return order != 0 ? order : a.Suboperation.CompareTo(b.Suboperation); }

        /// <summary>
        /// Combines complete histories in the facade's global observation order. The actual new
        /// primitive is applied separately to this unpublished stage. All maps are old one-based IDs
        /// to new one-based IDs. A cohort cut is rebuilt at its original suboperation boundary.
        /// </summary>
        internal static CargoPhysicalResult UnionForSession(CargoPhysicalComponent left, CargoPhysicalComponent right,
            Guid component, long allowance, out CargoPhysicalComponent combined, out UnionMap leftMap, out UnionMap rightMap)
        {
            combined = null; leftMap = null; rightMap = null; var budget = new Budget(allowance);
            // Both owners are private to the same facade writer lock; no competing reverse union
            // can enter these locks. Lower-level public owners cannot be imported by the facade.
            lock (left.sync) lock (right.sync)
            {
                try
                {
                    Require(allowance >= 0 && left != right && left.complete && right.complete && left.session == right.session
                        && component != Guid.Empty, CargoPhysicalStatus.CoverageInvalidated, "Complete distinct session-owned histories required.");
                    var a = left.state; var b = right.state;
                    Require(a.Nodes.Count + b.Nodes.Count <= MaximumNodes, CargoPhysicalStatus.NodeLimit, "Union retains every historical node.");
                    Require(a.Edges.Count + b.Edges.Count <= MaximumEdges, CargoPhysicalStatus.EdgeLimit, "Union retains every edge.");
                    Require(a.Live.Count + b.Live.Count <= MaximumLiveFrontiers, CargoPhysicalStatus.FrontierLimit, "Union frontier limit.");
                    Require(a.Receipts.Count + b.Receipts.Count <= MaximumReceipts, CargoPhysicalStatus.ReceiptLimit, "Union retains every receipt.");
                    budget.Spend(8L * (a.Nodes.Count + b.Nodes.Count) + 4L * (a.Edges.Count + b.Edges.Count)
                        + (long)(a.SeenEntities.Count + b.SeenEntities.Count) * (MaximumKeyLength + 4));
                    var maps = new[] { new UnionMap(a.Nodes.Count, a.NextOrigin, a.Cohorts.Count), new UnionMap(b.Nodes.Count, b.NextOrigin, b.Cohorts.Count) };
                    var inputs = new[] { a, b };
                    var stage = new State { TotalBirth = checked(a.TotalBirth + b.TotalBirth),
                        LastId = Math.Max(a.LastId, b.LastId), LastSequence = Math.Max(a.LastSequence, b.LastSequence) };
                    var ordered = new List<UnionNode>();
                    for (int side = 0; side < 2; side++)
                    {
                        foreach (string key in inputs[side].SeenEntities)
                            Require(stage.SeenEntities.Add(key), CargoPhysicalStatus.DuplicateEntity, "Histories overlap in a retained entity identity.");
                        foreach (var node in inputs[side].Nodes)
                        {
                            Require(node.CreatedSequence > 0, CargoPhysicalStatus.InvalidGraph, "Unordered external owners cannot enter a session union.");
                            ordered.Add(new UnionNode(node, side));
                        }
                    }
                    // At most 128 nodes. Charge a quadratic upper bound independent of the sorter.
                    budget.Spend((long)ordered.Count * ordered.Count);
                    ordered.Sort((x, y) => { int order = Chronology(x.Node, y.Node);
                        if (order != 0) return order; order = x.Side.CompareTo(y.Side); return order != 0 ? order : x.Node.Id.CompareTo(y.Node.Id); });
                    foreach (var row in ordered)
                    {
                        var n = row.Node; var map = maps[row.Side]; var input = inputs[row.Side]; int id = stage.Nodes.Count + 1;
                        int origin = n.OriginId == 0 ? 0 : stage.NextOrigin++;
                        map.Nodes[n.Id] = id; if (origin > 0) map.Origins[n.OriginId] = origin;
                        bool frontier = input.Frontier.Contains(n.Id - 1);
                        stage.Nodes.Add(new CargoPhysicalNode(id, n.Kind, n.EntityKey, n.ContextKey, n.Quantity,
                            n.BirthQuantity, origin, n.CreatedSequence, n.Suboperation, frontier));
                        if (frontier) { stage.Frontier.Add(id - 1); if (n.EntityKey != null) stage.Live.Add(n.EntityKey, id - 1); }
                    }
                    for (int side = 0; side < 2; side++) foreach (var edge in inputs[side].Edges)
                        stage.Edges.Add(new CargoFlowEdge(maps[side].Nodes[edge.From + 1] - 1, maps[side].Nodes[edge.To + 1] - 1, edge.Quantity));
                    // Earliest successor creation is the retirement boundary. Internal source
                    // separation and target merging therefore remain different suboperations.
                    var retiredAt = new int[stage.Nodes.Count];
                    for (int i = 0; i < retiredAt.Length; i++) retiredAt[i] = int.MaxValue;
                    foreach (var edge in stage.Edges) retiredAt[edge.From] = Math.Min(retiredAt[edge.From], edge.To);
                    for (int side = 0; side < 2; side++) foreach (var old in inputs[side].Cohorts)
                    {
                        budget.Spend(3L * stage.Nodes.Count + old.CompleteCut.Count + 4);
                        Require(old.NextNodeId > 1 && old.NextNodeId <= inputs[side].Nodes.Count + 1,
                            CargoPhysicalStatus.InvalidGraph, "Missing exact historical cut boundary.");
                        var boundary = inputs[side].Nodes[old.NextNodeId - 2];
                        int next = 0;
                        while (next < stage.Nodes.Count && Chronology(stage.Nodes[next], boundary) <= 0) next++;
                        var cut = new List<int>(); int active = 0;
                        for (int i = 0; i < next; i++) if (retiredAt[i] >= next)
                        { cut.Add(i + 1); if (stage.Nodes[i].Kind != CargoPhysicalNodeKind.AccountedSink) active++; }
                        Require(active <= MaximumLiveFrontiers, CargoPhysicalStatus.FrontierLimit,
                            "A retained historical complete cut exceeds the live/transit frontier limit.");
                        int id = stage.Cohorts.Count + 1; maps[side].Cohorts[old.Id] = id;
                        stage.Cohorts.Add(new CargoPhysicalCohort(id, old.OperationId, old.Sequence, old.Boundary,
                            maps[side].Nodes[old.SelectedNodeId], next + 1, old.Quantity, cut.ToArray()));
                    }
                    for (int side = 0; side < 2; side++) foreach (var accepted in inputs[side].Receipts)
                    {
                        budget.Spend(12);
                        var normalized = RemapReceipt(accepted.Receipt, left.session, component, maps[side], stage);
                        stage.Receipts.Add(new Accepted(normalized, accepted.CohortId.HasValue ? maps[side].Cohorts[accepted.CohortId.Value] : (int?)null));
                    }
                    budget.Spend((long)stage.Receipts.Count * stage.Receipts.Count);
                    stage.Receipts.Sort((x, y) => x.Receipt.Sequence.CompareTo(y.Receipt.Sequence));
                    for (int i = 1; i < stage.Receipts.Count; i++)
                        Require(stage.Receipts[i - 1].Receipt.Sequence < stage.Receipts[i].Receipt.Sequence
                            && stage.Receipts[i - 1].Receipt.OperationId < stage.Receipts[i].Receipt.OperationId,
                            CargoPhysicalStatus.ConflictingReceipt, "Global receipt order is not unique.");
                    Validate(stage, budget);
                    var owner = new CargoPhysicalComponent(left.session, component, stage);
                    // Validate every reconstructed cut using the unchanged conservation checker.
                    foreach (var cohort in stage.Cohorts)
                    {
                        var check = owner.CompileCohort(owner.Stamp, cohort.Id, Array.Empty<int>(), budget.Limit - budget.Spent);
                        budget.Spend(check.WorkSpent);
                        Require(check.Status == CargoPhysicalStatus.ViewCompiled, check.Status, check.Detail);
                    }
                    combined = owner; leftMap = maps[0]; rightMap = maps[1];
                    return new CargoPhysicalResult(CargoPhysicalStatus.Created, owner.Stamp, budget.Spent);
                }
                catch (Stop error) { return new CargoPhysicalResult(error.Status, default, budget.Spent, error.Message); }
                catch (OverflowException) { return new CargoPhysicalResult(CargoPhysicalStatus.ArithmeticOverflow, default, budget.Spent, "Union totals overflow."); }
            }
        }
        private static CargoPhysicalReceipt RemapReceipt(CargoPhysicalReceipt r, Guid session, Guid component, UnionMap map, State stage)
        {
            CargoPhysicalEntityVersion Remap(CargoPhysicalEntityVersion old)
            {
                if (old.VersionId == 0) return default;
                var n = stage.Nodes[map.Nodes[old.VersionId] - 1];
                return new CargoPhysicalEntityVersion(session, component, n.Id, n.Observation);
            }
            switch (r.Kind)
            {
                case CargoPhysicalOperation.Birth: return CargoPhysicalReceipt.Birth(session, component, r.OperationId, r.Sequence, r.Output.Value);
                case CargoPhysicalOperation.Move: return CargoPhysicalReceipt.Move(session, component, r.OperationId, r.Sequence, Remap(r.Source), r.Output.Value, r.CaptureCohort);
                case CargoPhysicalOperation.Split: return CargoPhysicalReceipt.Split(session, component, r.OperationId, r.Sequence, Remap(r.Source), r.SourceAfter.Value, r.Output.Value, r.CaptureCohort);
                case CargoPhysicalOperation.Absorb: return CargoPhysicalReceipt.Absorb(session, component, r.OperationId, r.Sequence, Remap(r.Source), Remap(r.Target), r.SourceAfter, r.Output.Value, r.CaptureCohort);
                default: return CargoPhysicalReceipt.Sink(session, component, r.OperationId, r.Sequence, Remap(r.Source), r.SourceAfter, r.SinkQuantity, r.SinkContext);
            }
        }
    }
}
