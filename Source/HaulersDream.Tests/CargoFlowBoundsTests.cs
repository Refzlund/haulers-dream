using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CargoFlowBoundsTests
    {
        private sealed class History
        {
            internal readonly List<CargoFlowNode> Nodes = new List<CargoFlowNode>();
            internal readonly List<CargoFlowEdge> Edges = new List<CargoFlowEdge>();
            internal int Birth(long quantity, long selected)
            { Nodes.Add(new CargoFlowNode(quantity, quantity, selected, true, false)); return Nodes.Count - 1; }
            private void Retire(int index)
            {
                var n = Nodes[index];
                if (!n.Frontier) throw new InvalidOperationException("Test reused a retired physical version.");
                Nodes[index] = new CargoFlowNode(n.Quantity, n.BirthQuantity, n.SelectedBirthQuantity, false, false);
            }
            internal int Merge(int first, int second)
            {
                long a = Nodes[first].Quantity, b = Nodes[second].Quantity;
                Retire(first); Retire(second);
                int result = Nodes.Count; Nodes.Add(new CargoFlowNode(a + b, 0, 0, true, false));
                Edges.Add(new CargoFlowEdge(first, result, a)); Edges.Add(new CargoFlowEdge(second, result, b));
                return result;
            }
            internal int[] Split(int source, long first)
            {
                long other = Nodes[source].Quantity - first;
                if (first <= 0 || other <= 0) throw new InvalidOperationException("Test requires two positive outputs.");
                Retire(source);
                int a = Nodes.Count, b = a + 1;
                Nodes.Add(new CargoFlowNode(first, 0, 0, true, false));
                Nodes.Add(new CargoFlowNode(other, 0, 0, true, false));
                Edges.Add(new CargoFlowEdge(source, a, first)); Edges.Add(new CargoFlowEdge(source, b, other));
                return new[] { a, b };
            }
            internal CargoFlowNode[] Select(params int[] selected)
            {
                return Nodes.Select((n, i) => new CargoFlowNode(n.Quantity, n.BirthQuantity,
                    n.SelectedBirthQuantity, n.Frontier, selected.Contains(i))).ToArray();
            }
        }

        private static void Bounds(History history, long minimum, long maximum, params int[] selected)
        {
            var result = CargoFlowBounds.Measure(history.Select(selected), history.Edges.ToArray(), 1000000);
            Assert.That(result.Status, Is.EqualTo(CargoFlowStatus.Complete), result.Detail);
            Assert.That(new[] { result.Minimum, result.Maximum }, Is.EqualTo(new long?[] { minimum, maximum }));
        }

        private static History Reroute(out int first, out int second)
        {
            var h = new History();
            int a = h.Birth(1, 1), b = h.Birth(1, 0);
            var u = h.Split(h.Merge(a, b), 1);
            int a2 = h.Birth(1, 1);
            var v = h.Split(h.Merge(u[0], a2), 1);
            first = u[1]; second = v[0]; return h;
        }

        [Test]
        public void CompleteObjective_ReroutesAnEarlierAllocationWithoutDroppingOtherBirths()
        {
            var h = Reroute(out int first, out int second);
            Bounds(h, 1, 2, first, second);
        }

        [Test]
        public void RetainedSplitHistory_ProvesTheSubgroupTotalThatMarginalBoxesLose()
        {
            var h = new History(); var a = h.Split(h.Birth(4, 4), 2);
            var x = h.Split(h.Merge(a[0], h.Birth(2, 0)), 2);
            var y = h.Split(h.Merge(a[1], h.Birth(2, 0)), 2);
            Bounds(h, 2, 2, x[0], x[1]);
            Bounds(h, 0, 2, x[0]);
            Bounds(h, 4, 4, x.Concat(y).ToArray());
        }

        [Test]
        public void PartialAbsorbDirection_PreventsTargetOriginFromAppearingInSourceRemainder()
        {
            var h = new History(); int source = h.Birth(2, 0), target = h.Birth(2, 2);
            var split = h.Split(source, 1); h.Merge(split[0], target);
            Bounds(h, 0, 0, split[1]);
            var pooled = new History(); int pool = pooled.Merge(pooled.Birth(2, 0), pooled.Birth(2, 2));
            var wrong = pooled.Split(pool, 1);
            Bounds(pooled, 0, 1, wrong[0]); // A different graph really describes a different physical operation.
        }

        [Test]
        public void AccountedConsumption_RemainsInTheCompleteFrontier()
        {
            var h = new History(); var cut = h.Split(h.Merge(h.Birth(2, 2), h.Birth(1, 0)), 1);
            Bounds(h, 0, 1, cut[0]);
            var missingSink = h.Select(cut[0]);
            missingSink[cut[1]] = new CargoFlowNode(2, 0, 0, false, false);
            var failed = CargoFlowBounds.Measure(missingSink, h.Edges.ToArray(), 1000000);
            Assert.That(failed.Status, Is.EqualTo(CargoFlowStatus.InvalidGraph));
            Assert.That(failed.Minimum, Is.Null); Assert.That(failed.Maximum, Is.Null);
        }

        // Enumerate conserved integer assignments in topological order, independently of shortest
        // paths, residual networks, costs or a chosen FIFO origin allocation.
        private static long[] Enumerate(CargoFlowNode[] nodes, CargoFlowEdge[] edges)
        {
            var incoming = new long[nodes.Length]; long minimum = long.MaxValue, maximum = -1;
            void Visit(int index, long score)
            {
                if (index == nodes.Length) { minimum = Math.Min(minimum, score); maximum = Math.Max(maximum, score); return; }
                long available = incoming[index] + nodes[index].SelectedBirthQuantity;
                if (nodes[index].Frontier)
                { Visit(index + 1, score + (nodes[index].SelectedFrontier ? available : 0)); return; }
                var outputs = edges.Where(e => e.From == index).ToArray();
                void Distribute(int at, long remaining)
                {
                    if (at == outputs.Length) { if (remaining == 0) Visit(index + 1, score); return; }
                    var edge = outputs[at];
                    for (long moved = 0; moved <= Math.Min(remaining, edge.Quantity); moved++)
                    {
                        incoming[edge.To] += moved; Distribute(at + 1, remaining - moved); incoming[edge.To] -= moved;
                    }
                }
                Distribute(0, available);
            }
            Visit(0, 0); return new[] { minimum, maximum };
        }

        [Test]
        public void SmallSplitMergeTopologies_MatchAllFeasibleIntegerAssignments()
        {
            var random = new Random(10402);
            for (int scene = 0; scene < 48; scene++)
            {
                var h = new History();
                var live = new List<int>();
                for (int i = 0; i < 3; i++) live.Add(h.Birth(2, random.Next(3)));
                for (int op = 0; op < 4; op++)
                {
                    var candidates = live.Where(i => h.Nodes[i].Quantity > 1).ToArray();
                    if (live.Count < 2 || candidates.Length > 0 && random.Next(2) == 0)
                    {
                        int source = candidates[random.Next(candidates.Length)];
                        long amount = random.Next(1, (int)h.Nodes[source].Quantity);
                        live.Remove(source); live.AddRange(h.Split(source, amount));
                    }
                    else
                    {
                        int first = live[random.Next(live.Count)]; live.Remove(first);
                        int second = live[random.Next(live.Count)]; live.Remove(second);
                        live.Add(h.Merge(first, second));
                    }
                }
                for (int mask = 0; mask < 1 << live.Count; mask++)
                {
                    var nodes = h.Select(live.Where((_, i) => (mask & 1 << i) != 0).ToArray());
                    var edges = h.Edges.ToArray(); var original = (CargoFlowNode[])nodes.Clone();
                    var expected = Enumerate(nodes, edges);
                    var actual = CargoFlowBounds.Measure(nodes, edges, 1000000);
                    Assert.That(actual.Status, Is.EqualTo(CargoFlowStatus.Complete), $"scene={scene},mask={mask}: {actual.Detail}");
                    Assert.That(new[] { actual.Minimum, actual.Maximum }, Is.EqualTo(expected.Cast<long?>().ToArray()));
                    Assert.That(nodes, Is.EqualTo(original)); Assert.That(edges, Is.EqualTo(h.Edges.ToArray()));
                }
            }
        }

        [Test]
        public void EveryInsufficientWorkAllowance_PublishesNoPartialEndpoint()
        {
            var h = Reroute(out int first, out int second); var nodes = h.Select(first, second); var edges = h.Edges.ToArray();
            var complete = CargoFlowBounds.Measure(nodes, edges, 1000000);
            Assert.That(complete.Status, Is.EqualTo(CargoFlowStatus.Complete));
            for (long allowance = 0; allowance < complete.WorkSpent; allowance++)
            {
                var result = CargoFlowBounds.Measure(nodes, edges, allowance);
                Assert.That(result.Status, Is.EqualTo(CargoFlowStatus.WorkLimit), $"allowance={allowance}");
                Assert.That(result.WorkSpent, Is.EqualTo(allowance));
                Assert.That(result.Minimum, Is.Null); Assert.That(result.Maximum, Is.Null);
            }
            Assert.That(CargoFlowBounds.Measure(nodes, edges, complete.WorkSpent).Status, Is.EqualTo(CargoFlowStatus.Complete));
        }

        [Test]
        public void LargeQuantities_DoNotDrivePerUnitIterationsOrWrapTheObjective()
        {
            var h = new History(); var split = h.Split(h.Birth(long.MaxValue, long.MaxValue), long.MaxValue - 1);
            var large = CargoFlowBounds.Measure(h.Select(split[0]), h.Edges.ToArray(), 1000000);
            Assert.That(large.Status, Is.EqualTo(CargoFlowStatus.Complete));
            Assert.That(new[] { large.Minimum, large.Maximum }, Is.EqualTo(new long?[] { long.MaxValue - 1, long.MaxValue - 1 }));
            var small = new History(); var cut = small.Split(small.Birth(10, 10), 9);
            Assert.That(large.WorkSpent, Is.EqualTo(CargoFlowBounds.Measure(small.Select(cut[0]), small.Edges.ToArray(), 1000000).WorkSpent));
            var overflow = CargoFlowBounds.Measure(new[] {
                new CargoFlowNode(long.MaxValue, long.MaxValue, long.MaxValue, true, true),
                new CargoFlowNode(1, 1, 1, true, false) }, Array.Empty<CargoFlowEdge>(), 1000000);
            Assert.That(overflow.Status, Is.EqualTo(CargoFlowStatus.ArithmeticOverflow));
            Assert.That(overflow.Minimum, Is.Null); Assert.That(overflow.Maximum, Is.Null);
        }

        [Test]
        public void InvalidShapeOrderConservationAndSelection_NeverProduceBounds()
        {
            var one = new[] { new CargoFlowNode(1, 1, 1, true, true) };
            var invalid = new[] {
                CargoFlowBounds.Measure(null, Array.Empty<CargoFlowEdge>(), 1000),
                CargoFlowBounds.Measure(one, null, 1000),
                CargoFlowBounds.Measure(one, Array.Empty<CargoFlowEdge>(), -1),
                CargoFlowBounds.Measure(new CargoFlowNode[129], Array.Empty<CargoFlowEdge>(), 1000),
                CargoFlowBounds.Measure(one, new CargoFlowEdge[513], 1000),
                CargoFlowBounds.Measure(one, new[] { new CargoFlowEdge(0, 0, 1) }, 1000),
                CargoFlowBounds.Measure(one, new[] { new CargoFlowEdge(-1, 0, 1) }, 1000),
                CargoFlowBounds.Measure(one, new[] { new CargoFlowEdge(0, 1, 1) }, 1000),
                CargoFlowBounds.Measure(new[] { new CargoFlowNode(1, 0, 0, true, false) }, Array.Empty<CargoFlowEdge>(), 1000),
                CargoFlowBounds.Measure(new[] { new CargoFlowNode(1, 1, 2, true, true) }, Array.Empty<CargoFlowEdge>(), 1000),
                CargoFlowBounds.Measure(new[] { new CargoFlowNode(1, 1, 1, false, true) }, Array.Empty<CargoFlowEdge>(), 1000)
            };
            foreach (var result in invalid)
            {
                Assert.That(result.Status, Is.EqualTo(CargoFlowStatus.InvalidGraph));
                Assert.That(result.Minimum, Is.Null); Assert.That(result.Maximum, Is.Null);
            }
            Assert.That(default(CargoFlowResult).Status, Is.EqualTo(CargoFlowStatus.NotComputed));
            Assert.That(default(CargoFlowResult).Minimum, Is.Null);
        }
    }
}
