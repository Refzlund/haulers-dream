using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    public sealed partial class CargoPhysicalComponent
    {
        /// <summary>
        /// Historical snapshots remain readable. Only this exact stamp, with complete coverage,
        /// is a current answer. Consumers must recheck it before using a previously compiled view.
        /// </summary>
        public bool IsCurrent(CargoPhysicalStamp expected)
        {
            lock (sync) return Current(expected);
        }
        private bool Current(CargoPhysicalStamp expected) => complete && expected.Complete
            && expected.Session == session && expected.Component == component
            && expected.GraphVersion == graphVersion && expected.CoverageRevision == coverageRevision;

        /// <summary>
        /// Captures the entire trusted graph, including accounted sinks and retained receipts.
        /// After an observed failure the snapshot is audit-only: Complete is false and the first
        /// failure is retained in CoverageReason. Read failures never change owner coverage.
        /// </summary>
        public CargoPhysicalResult CaptureSnapshot(long workAllowance)
        {
            lock (sync)
            {
                var budget = new Budget(workAllowance);
                try
                {
                    Require(workAllowance >= 0, CargoPhysicalStatus.InvalidReceipt, "Nonnegative snapshot allowance required.");
                    budget.Spend(4L * state.Nodes.Count + state.Edges.Count + state.Receipts.Count + state.Cohorts.Count + 8);
                    var nodes = new CargoPhysicalNode[state.Nodes.Count];
                    var entities = new List<CargoPhysicalEntityVersion>();
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        nodes[i] = state.Nodes[i].AtFrontier(state.Frontier.Contains(i));
                        if (nodes[i].Frontier && nodes[i].EntityKey != null) entities.Add(Version(nodes[i]));
                    }
                    var receipts = new CargoPhysicalReceipt[state.Receipts.Count];
                    for (int i = 0; i < receipts.Length; i++) receipts[i] = state.Receipts[i].Receipt;
                    var snapshot = new CargoPhysicalSnapshot(Stamp, coverageReason, state.TotalBirth, receipts,
                        nodes, state.Edges.ToArray(), entities.ToArray(), state.Cohorts.ToArray());
                    return new CargoPhysicalResult(CargoPhysicalStatus.SnapshotCaptured, Stamp, budget.Spent, snapshot: snapshot);
                }
                catch (Stop error) { return ReadFailure(error, budget); }
                catch (OverflowException) { return ReadOverflow(budget); }
            }
        }

        /// <summary>
        /// Compiles one immutable observed origin against selected current live/sink versions.
        /// Every other origin remains its conserved complement. Empty objectives are allowed;
        /// an absent/unknown origin or noncurrent objective never becomes an invented zero.
        /// </summary>
        public CargoPhysicalResult CompileOrigin(CargoPhysicalStamp expected, int originId,
            int[] selectedFrontierNodeIds, long workAllowance)
            => Compile(expected, originId, false, selectedFrontierNodeIds, workAllowance);

        /// <summary>
        /// Compiles a whole observed cohort by re-rooting a derived view at its complete physical
        /// cut. Cut roots are injections only in this view, never new births in the owned graph.
        /// For partial absorption the cut is after separation and before target merging, so that
        /// later step of the same receipt is preserved. All prior sinks, untouched complements
        /// and subsequent genuine admissions are included. This is not conditional causal history.
        /// </summary>
        public CargoPhysicalResult CompileCohort(CargoPhysicalStamp expected, int cohortId,
            int[] selectedFrontierNodeIds, long workAllowance)
            => Compile(expected, cohortId, true, selectedFrontierNodeIds, workAllowance);

        private CargoPhysicalResult Compile(CargoPhysicalStamp expected, int selectionId, bool cohort,
            int[] selectedFrontierNodeIds, long workAllowance)
        {
            lock (sync)
            {
                var budget = new Budget(workAllowance);
                try
                {
                    Require(workAllowance >= 0, CargoPhysicalStatus.InvalidReceipt, "Nonnegative view allowance required.");
                    Require(complete, CargoPhysicalStatus.CoverageInvalidated, "The trusted graph is audit-only after an observed failure.");
                    Require(Current(expected), CargoPhysicalStatus.StaleSnapshot, "View compilation requires this complete current coverage stamp.");
                    Require(selectedFrontierNodeIds != null && selectedFrontierNodeIds.Length <= MaximumNodes,
                        CargoPhysicalStatus.UnknownSelection, "A bounded explicit objective is required.");
                    budget.Spend(selectedFrontierNodeIds.Length + 4L * state.Nodes.Count + 4);
                    var requested = (int[])selectedFrontierNodeIds.Clone();
                    var objective = new bool[state.Nodes.Count];
                    foreach (int id in requested)
                    {
                        budget.Spend();
                        Require(id > 0 && id <= state.Nodes.Count && state.Frontier.Contains(id - 1) && !objective[id - 1],
                            CargoPhysicalStatus.UnknownSelection, "Objectives must name distinct current live or accounted-sink versions.");
                        objective[id - 1] = true;
                    }

                    CargoPhysicalCohort selectedCohort = null;
                    var cut = new bool[state.Nodes.Count];
                    long selectedQuantity = 0;
                    if (cohort)
                    {
                        Require(selectionId > 0 && selectionId <= state.Cohorts.Count, CargoPhysicalStatus.UnknownSelection, "Unknown retained cohort.");
                        selectedCohort = state.Cohorts[selectionId - 1];
                        budget.Spend(selectedCohort.CompleteCut.Count);
                        foreach (int id in selectedCohort.CompleteCut)
                        {
                            Require(id > 0 && id < selectedCohort.NextNodeId && id <= state.Nodes.Count && !cut[id - 1],
                                CargoPhysicalStatus.InvalidGraph, "Retained cut is not a unique pre-boundary node census.");
                            cut[id - 1] = true;
                        }
                        Require(selectedCohort.SelectedNodeId > 0 && selectedCohort.SelectedNodeId <= state.Nodes.Count
                            && cut[selectedCohort.SelectedNodeId - 1], CargoPhysicalStatus.InvalidGraph, "Selected branch is missing from its complete cut.");
                        selectedQuantity = selectedCohort.Quantity;
                    }
                    else
                    {
                        bool found = false;
                        foreach (var node in state.Nodes)
                        {
                            budget.Spend();
                            if (node.OriginId != selectionId || node.BirthQuantity == 0) continue;
                            Require(!found, CargoPhysicalStatus.InvalidGraph, "Observed origin ID is not unique.");
                            found = true; selectedQuantity = node.BirthQuantity;
                        }
                        Require(found, CargoPhysicalStatus.UnknownSelection, "Unknown observed origin.");
                    }

                    var nodes = new List<CargoFlowNode>();
                    var ids = new List<int>();
                    var map = new int[state.Nodes.Count];
                    for (int i = 0; i < map.Length; i++) map[i] = -1;
                    for (int i = 0; i < state.Nodes.Count; i++)
                    {
                        budget.Spend(3);
                        var node = state.Nodes[i];
                        bool included = !cohort || cut[i] || node.Id >= selectedCohort.NextNodeId;
                        Require(included || !state.Frontier.Contains(i), CargoPhysicalStatus.InvalidGraph, "A complete view cannot omit a current frontier.");
                        if (!included) continue;
                        long birth = cohort && cut[i] ? node.Quantity : node.BirthQuantity;
                        long selectedBirth = cohort ? (node.Id == selectedCohort.SelectedNodeId ? node.Quantity : 0)
                            : (node.OriginId == selectionId ? node.BirthQuantity : 0);
                        map[i] = nodes.Count; ids.Add(node.Id);
                        nodes.Add(new CargoFlowNode(node.Quantity, birth, selectedBirth, state.Frontier.Contains(i), objective[i]));
                    }
                    var edges = new List<CargoFlowEdge>();
                    foreach (var edge in state.Edges)
                    {
                        budget.Spend(2);
                        if (map[edge.From] < 0 || map[edge.To] < 0 || cohort && cut[edge.To]) continue;
                        edges.Add(new CargoFlowEdge(map[edge.From], map[edge.To], edge.Quantity));
                    }
                    ValidateView(nodes, edges, selectedQuantity, state.TotalBirth, budget);
                    budget.Spend(2L * nodes.Count + edges.Count + 4);
                    var view = new CargoPhysicalFlowView(Stamp, cohort, selectionId, nodes.ToArray(), edges.ToArray(), ids.ToArray());
                    return new CargoPhysicalResult(CargoPhysicalStatus.ViewCompiled, Stamp, budget.Spent, view: view);
                }
                catch (Stop error) { return ReadFailure(error, budget); }
                catch (OverflowException) { return ReadOverflow(budget); }
            }
        }

        private static void ValidateView(List<CargoFlowNode> nodes, List<CargoFlowEdge> edges,
            long selectedQuantity, long physicalTotal, Budget budget)
        {
            budget.Spend(3L * nodes.Count + edges.Count);
            Require(nodes.Count > 0 && nodes.Count <= MaximumNodes && edges.Count <= MaximumEdges,
                CargoPhysicalStatus.InvalidGraph, "Compiled graph exceeds the accepted numerical model limits.");
            var incoming = new long[nodes.Count]; var outgoing = new long[nodes.Count];
            foreach (var edge in edges)
            {
                Require(edge.From >= 0 && edge.From < edge.To && edge.To < nodes.Count && edge.Quantity > 0,
                    CargoPhysicalStatus.InvalidGraph, "Compiled edges are not positive forward transfers.");
                incoming[edge.To] = checked(incoming[edge.To] + edge.Quantity);
                outgoing[edge.From] = checked(outgoing[edge.From] + edge.Quantity);
            }
            long births = 0, selected = 0, frontier = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                Require(node.Quantity > 0 && node.BirthQuantity >= 0 && node.SelectedBirthQuantity >= 0
                    && node.SelectedBirthQuantity <= node.BirthQuantity && checked(node.BirthQuantity + incoming[i]) == node.Quantity
                    && outgoing[i] == (node.Frontier ? 0 : node.Quantity) && (!node.SelectedFrontier || node.Frontier),
                    CargoPhysicalStatus.InvalidGraph, "Compiled cut does not retain complete conserved physical coverage.");
                births = checked(births + node.BirthQuantity); selected = checked(selected + node.SelectedBirthQuantity);
                if (node.Frontier) frontier = checked(frontier + node.Quantity);
            }
            Require(births == physicalTotal && frontier == physicalTotal && selected == selectedQuantity && selected > 0,
                CargoPhysicalStatus.InvalidGraph, "Compiled injection, complement and terminal census do not reconcile.");
        }
        private CargoPhysicalResult ReadFailure(Stop error, Budget budget)
            => new CargoPhysicalResult(error.Status, Stamp, budget.Spent, error.Message);
        private CargoPhysicalResult ReadOverflow(Budget budget)
            => new CargoPhysicalResult(CargoPhysicalStatus.ArithmeticOverflow, Stamp, budget.Spent, "View quantities overflow; owner coverage is unchanged.");
    }
}
