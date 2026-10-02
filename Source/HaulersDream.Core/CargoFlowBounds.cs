using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    public enum CargoFlowStatus { NotComputed, Complete, InvalidGraph, WorkLimit, ArithmeticOverflow, InconsistentFlow }

    /// <summary>An immutable physical version; indices must follow transformation order.</summary>
    public readonly struct CargoFlowNode
    {
        public long Quantity { get; }
        public long BirthQuantity { get; }
        public long SelectedBirthQuantity { get; }
        public bool Frontier { get; }
        public bool SelectedFrontier { get; }

        public CargoFlowNode(long quantity, long birthQuantity, long selectedBirthQuantity,
            bool frontier, bool selectedFrontier)
        {
            Quantity = quantity; BirthQuantity = birthQuantity; SelectedBirthQuantity = selectedBirthQuantity;
            Frontier = frontier; SelectedFrontier = selectedFrontier;
        }
    }

    /// <summary>Observed directional quantity, not an unrestricted mixing connection.</summary>
    public readonly struct CargoFlowEdge
    {
        public int From { get; }
        public int To { get; }
        public long Quantity { get; }
        public CargoFlowEdge(int from, int to, long quantity) { From = from; To = to; Quantity = quantity; }
    }

    public readonly struct CargoFlowResult
    {
        public CargoFlowStatus Status { get; }
        public long? Minimum { get; }
        public long? Maximum { get; }
        public long WorkSpent { get; }
        public string Detail { get; }
        internal CargoFlowResult(CargoFlowStatus status, long? minimum, long? maximum, long workSpent, string detail)
        { Status = status; Minimum = minimum; Maximum = maximum; WorkSpent = workSpent; Detail = detail; }
    }

    /// <summary>
    /// Exact marginal attribution for a FIXED birth class in a complete physical DAG. All other
    /// material is its conserved complement. This cannot validate native receipts, establish
    /// custody, relabel conditional return histories, or certify simultaneous origin extrema.
    /// Call on an owned immutable component snapshot during bounded maintenance, never a work query.
    /// </summary>
    public static class CargoFlowBounds
    {
        public const int MaximumNodes = 128;
        public const int MaximumEdges = 512;

        // Work counts copied/validated rows, network construction, residual vertex/arc visits and
        // path hops. No loop runs once per unit. Exhaustion publishes neither partial endpoint.
        private sealed class Budget
        {
            internal readonly long Limit;
            internal long Spent;
            internal Budget(long limit) { Limit = limit; }
            internal bool Spend(long amount = 1)
            {
                if (amount > Limit - Spent) { Spent = Limit; return false; }
                Spent += amount; return true;
            }
        }

        private sealed class Arc
        {
            internal int To, Reverse, Cost;
            internal long Remaining;
        }

        private static CargoFlowResult Fail(CargoFlowStatus status, Budget budget, string detail)
            => new CargoFlowResult(status, null, null, budget.Spent, detail);

        public static CargoFlowResult Measure(CargoFlowNode[] nodes, CargoFlowEdge[] edges, long workLimit)
        {
            var budget = new Budget(Math.Max(0, workLimit));
            if (workLimit < 0 || nodes == null || edges == null || nodes.Length == 0
                || nodes.Length > MaximumNodes || edges.Length > MaximumEdges)
                return Fail(CargoFlowStatus.InvalidGraph, budget, "Invalid input shape or graph limit.");
            if (!budget.Spend(nodes.Length + edges.Length))
                return Fail(CargoFlowStatus.WorkLimit, budget, "Snapshot budget exhausted.");
            var snapshot = (CargoFlowNode[])nodes.Clone();
            var transfers = (CargoFlowEdge[])edges.Clone();
            try
            {
                var incoming = new long[snapshot.Length];
                var outgoing = new long[snapshot.Length];
                long selectedTotal = 0;
                foreach (var edge in transfers)
                {
                    if (!budget.Spend()) return Fail(CargoFlowStatus.WorkLimit, budget, "Graph validation budget exhausted.");
                    if (edge.From < 0 || edge.To >= snapshot.Length || edge.From >= edge.To || edge.Quantity <= 0)
                        return Fail(CargoFlowStatus.InvalidGraph, budget, "Edges require positive quantity and strictly increasing version indices.");
                    incoming[edge.To] = checked(incoming[edge.To] + edge.Quantity);
                    outgoing[edge.From] = checked(outgoing[edge.From] + edge.Quantity);
                }
                for (int i = 0; i < snapshot.Length; i++)
                {
                    if (!budget.Spend()) return Fail(CargoFlowStatus.WorkLimit, budget, "Node validation budget exhausted.");
                    var node = snapshot[i];
                    if (node.Quantity < 0 || node.BirthQuantity < 0 || node.SelectedBirthQuantity < 0
                        || node.SelectedBirthQuantity > node.BirthQuantity || node.BirthQuantity > node.Quantity
                        || node.SelectedFrontier && !node.Frontier
                        || checked(node.BirthQuantity + incoming[i]) != node.Quantity
                        || outgoing[i] != (node.Frontier ? 0 : node.Quantity))
                        return Fail(CargoFlowStatus.InvalidGraph, budget, "Physical birth, transformation or complete frontier does not conserve quantity.");
                    selectedTotal = checked(selectedTotal + node.SelectedBirthQuantity);
                }
                if (selectedTotal == 0)
                    return new CargoFlowResult(CargoFlowStatus.Complete, 0, 0, budget.Spent, null);

                var minimumStatus = Optimize(snapshot, transfers, selectedTotal, 1, budget, out long minimum);
                if (minimumStatus != CargoFlowStatus.Complete)
                    return Fail(minimumStatus, budget, "Minimum complete-flow objective is unresolved.");
                // Independent residual state is essential: the maximum must not reuse the minimum's allocation.
                var maximumStatus = Optimize(snapshot, transfers, selectedTotal, -1, budget, out long negativeMaximum);
                if (maximumStatus != CargoFlowStatus.Complete)
                    return Fail(maximumStatus, budget, "Maximum complete-flow objective is unresolved.");
                long maximum = checked(-negativeMaximum);
                if (minimum < 0 || maximum < minimum || maximum > selectedTotal)
                    return Fail(CargoFlowStatus.InconsistentFlow, budget, "Complete-flow endpoints contradict the selected birth quantity.");
                return new CargoFlowResult(CargoFlowStatus.Complete, minimum, maximum, budget.Spent, null);
            }
            catch (OverflowException)
            {
                return Fail(CargoFlowStatus.ArithmeticOverflow, budget, "Quantity arithmetic exceeds the supported integer range.");
            }
        }

        private static void AddArc(List<Arc>[] graph, int from, int to, long capacity, int cost)
        {
            var forward = new Arc { To = to, Reverse = graph[to].Count, Remaining = capacity, Cost = cost };
            var reverse = new Arc { To = from, Reverse = graph[from].Count, Remaining = 0, Cost = -cost };
            graph[from].Add(forward); graph[to].Add(reverse);
        }

        private static CargoFlowStatus Optimize(CargoFlowNode[] nodes, CargoFlowEdge[] edges, long required,
            int targetCost, Budget budget, out long cost)
        {
            cost = 0;
            int source = nodes.Length, sink = source + 1, count = sink + 1;
            var graph = new List<Arc>[count];
            for (int i = 0; i < count; i++)
            {
                if (!budget.Spend()) return CargoFlowStatus.WorkLimit;
                graph[i] = new List<Arc>();
            }
            foreach (var edge in edges)
            {
                if (!budget.Spend()) return CargoFlowStatus.WorkLimit;
                AddArc(graph, edge.From, edge.To, edge.Quantity, 0);
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!budget.Spend()) return CargoFlowStatus.WorkLimit;
                var node = nodes[i];
                if (node.SelectedBirthQuantity > 0) AddArc(graph, source, i, node.SelectedBirthQuantity, 0);
                if (node.Frontier && node.Quantity > 0)
                    AddArc(graph, i, sink, node.Quantity, node.SelectedFrontier ? targetCost : 0);
            }
            // Sending the SUM of all selected source capacities saturates every birth injection.
            // Returning a smaller flow would discard selected material and is never a valid result.
            long sent = 0;
            var distance = new int[count];
            var parentVertex = new int[count];
            var parentArc = new int[count];
            while (sent < required)
            {
                for (int i = 0; i < count; i++)
                {
                    if (!budget.Spend()) return CargoFlowStatus.WorkLimit;
                    distance[i] = int.MaxValue; parentVertex[i] = -1; parentArc[i] = -1;
                }
                distance[source] = 0;
                // Successive shortest residual paths. Initial forward graph is acyclic, so it has
                // no negative cycle; shortest-path augmentation preserves minimum cost at each value.
                for (int pass = 0; pass < count - 1; pass++)
                {
                    bool changed = false;
                    for (int from = 0; from < count; from++)
                    {
                        if (!budget.Spend()) return CargoFlowStatus.WorkLimit;
                        if (from == sink || distance[from] == int.MaxValue) continue;
                        for (int a = 0; a < graph[from].Count; a++)
                        {
                            if (!budget.Spend()) return CargoFlowStatus.WorkLimit;
                            var arc = graph[from][a];
                            if (arc.Remaining <= 0 || arc.To == source) continue;
                            int next = distance[from] + arc.Cost;
                            if (next >= distance[arc.To]) continue;
                            distance[arc.To] = next; parentVertex[arc.To] = from; parentArc[arc.To] = a;
                            changed = true;
                        }
                    }
                    if (!changed) break;
                }
                if (parentVertex[sink] < 0) return CargoFlowStatus.InconsistentFlow;
                long amount = required - sent;
                int at = sink, hops = 0, pathCost = 0;
                while (at != source)
                {
                    if (!budget.Spend()) return CargoFlowStatus.WorkLimit;
                    if (++hops >= count || parentVertex[at] < 0) return CargoFlowStatus.InconsistentFlow;
                    var arc = graph[parentVertex[at]][parentArc[at]];
                    amount = Math.Min(amount, arc.Remaining); pathCost += arc.Cost; at = parentVertex[at];
                }
                if (amount <= 0) return CargoFlowStatus.InconsistentFlow;
                at = sink;
                while (at != source)
                {
                    if (!budget.Spend()) return CargoFlowStatus.WorkLimit;
                    int from = parentVertex[at]; var arc = graph[from][parentArc[at]];
                    arc.Remaining -= amount;
                    graph[at][arc.Reverse].Remaining = checked(graph[at][arc.Reverse].Remaining + amount);
                    at = from;
                }
                sent = checked(sent + amount); cost = checked(cost + checked(amount * pathCost));
            }
            return CargoFlowStatus.Complete;
        }
    }
}
