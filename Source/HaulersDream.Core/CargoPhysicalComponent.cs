using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Owns one bounded physical history. Admission of genuinely untracked entities and native
    /// receipt completeness remain adapter/coordinator obligations. No causal certificate, game
    /// object, automatic suppression, cross-component union, eviction or recovery policy lives here.
    /// Work allowances count bounded structural visits/copies plus fixed reservations for key
    /// operations; they are not elapsed time or instrumented CPU instructions. All loops are
    /// limited by retained record counts, never quantities. Accounted sinks count toward the
    /// node limit, and internal measured cuts also obey the live/transit frontier limit.
    /// </summary>
    public sealed partial class CargoPhysicalComponent
    {
        public const int MaximumNodes = 128;
        public const int MaximumEdges = 512;
        public const int MaximumLiveFrontiers = 32;
        public const int MaximumReceipts = 128;
        public const int MaximumKeyLength = 128;
        private readonly object sync = new object();
        private readonly Guid session, component;
        private State state;
        private long graphVersion = 1, coverageRevision = 1;
        private bool complete = true;
        private CargoPhysicalStatus coverageReason = CargoPhysicalStatus.Created;

        private sealed class Stop : Exception
        {
            internal readonly CargoPhysicalStatus Status;
            internal Stop(CargoPhysicalStatus status, string message) : base(message) { Status = status; }
        }
        private sealed class Budget
        {
            internal readonly long Limit;
            internal long Spent;
            internal Budget(long limit) { Limit = Math.Max(0, limit); }
            internal void Spend(long amount = 1)
            {
                if (amount < 0 || amount > Limit - Spent) { Spent = Limit; throw new Stop(CargoPhysicalStatus.WorkLimit, "Explicit owner work allowance exhausted."); }
                Spent += amount;
            }
        }
        private sealed class Accepted
        {
            internal readonly CargoPhysicalReceipt Receipt;
            internal readonly int? CohortId;
            internal Accepted(CargoPhysicalReceipt receipt, int? cohortId) { Receipt = receipt; CohortId = cohortId; }
        }
        private sealed class State
        {
            internal readonly List<CargoPhysicalNode> Nodes = new List<CargoPhysicalNode>();
            internal readonly List<CargoFlowEdge> Edges = new List<CargoFlowEdge>();
            internal readonly HashSet<int> Frontier = new HashSet<int>();
            internal readonly Dictionary<string, int> Live = new Dictionary<string, int>(StringComparer.Ordinal);
            internal readonly HashSet<string> SeenEntities = new HashSet<string>(StringComparer.Ordinal);
            internal readonly List<Accepted> Receipts = new List<Accepted>();
            internal readonly List<CargoPhysicalCohort> Cohorts = new List<CargoPhysicalCohort>();
            internal long TotalBirth, LastId, LastSequence;
            internal int NextOrigin = 1;
            internal State Copy(Budget budget)
            {
                // Rows are immutable; only owned containers are copied. Account for bounded string
                // hashing during dictionary/set copies as well as array/list visits and allocation.
                budget.Spend(Nodes.Count + Edges.Count + Frontier.Count + Receipts.Count + Cohorts.Count
                    + (long)(Live.Count + SeenEntities.Count) * (MaximumKeyLength + 2));
                var copy = new State { TotalBirth = TotalBirth, LastId = LastId, LastSequence = LastSequence, NextOrigin = NextOrigin };
                copy.Nodes.AddRange(Nodes); copy.Edges.AddRange(Edges); copy.Frontier.UnionWith(Frontier);
                foreach (var pair in Live) copy.Live.Add(pair.Key, pair.Value);
                copy.SeenEntities.UnionWith(SeenEntities); copy.Receipts.AddRange(Receipts); copy.Cohorts.AddRange(Cohorts);
                return copy;
            }
        }
        private CargoPhysicalComponent(Guid session, Guid component, State state)
        { this.session = session; this.component = component; this.state = state; }
        private CargoPhysicalStamp Stamp => new CargoPhysicalStamp(session, component, graphVersion, coverageRevision, complete);
        private static void Require(bool condition, CargoPhysicalStatus status, string detail)
        { if (!condition) throw new Stop(status, detail); }
        private static bool Key(string value) => value != null && value.Length > 0 && value.Length <= MaximumKeyLength && !string.IsNullOrWhiteSpace(value);
        private static void Observation(CargoPhysicalObservation value)
        { Require(Key(value.EntityKey) && Key(value.ContextKey) && value.Quantity > 0, CargoPhysicalStatus.InvalidReceipt, "Positive quantity and bounded nonempty entity/context keys required."); }

        public static CargoPhysicalResult TryCreate(Guid session, Guid component, CargoPhysicalObservation[] observedBirths,
            long workAllowance, out CargoPhysicalComponent owner)
        {
            owner = null; var budget = new Budget(workAllowance);
            try
            {
                Require(workAllowance >= 0 && session != Guid.Empty && component != Guid.Empty && observedBirths != null
                    && observedBirths.Length > 0 && observedBirths.Length <= MaximumLiveFrontiers,
                    CargoPhysicalStatus.InvalidReceipt, "Explicit session/component and a bounded nonempty initial census required.");
                budget.Spend(observedBirths.Length);
                var inputs = (CargoPhysicalObservation[])observedBirths.Clone();
                var created = new State();
                foreach (var input in inputs)
                {
                    budget.Spend(2 * MaximumKeyLength + 8); Observation(input);
                    Admit(created, input.EntityKey);
                    created.TotalBirth = checked(created.TotalBirth + input.Quantity);
                    Add(created, CargoPhysicalNodeKind.ObservedBirth, input, input.Quantity, created.NextOrigin++, 0, 0, budget);
                }
                Validate(created, budget);
                owner = new CargoPhysicalComponent(session, component, created);
                return new CargoPhysicalResult(CargoPhysicalStatus.Created, owner.Stamp, budget.Spent);
            }
            catch (Stop error) { return new CargoPhysicalResult(error.Status, default, budget.Spent, error.Message); }
            catch (OverflowException) { return new CargoPhysicalResult(CargoPhysicalStatus.ArithmeticOverflow, default, budget.Spent, "Initial observed quantities overflow."); }
        }

        public CargoPhysicalResult Apply(CargoPhysicalReceipt receipt, long workAllowance)
        {
            lock (sync)
            {
                var budget = new Budget(workAllowance);
                try
                {
                    Require(workAllowance >= 0 && receipt != null, CargoPhysicalStatus.InvalidReceipt, "Receipt and nonnegative allowance required.");
                    // A receipt has a fixed number of bounded strings; reserve comparison/hash work
                    // before structural duplicate equality or owned-version lookups.
                    budget.Spend(32L * MaximumKeyLength + 128);
                    ValidateKeys(receipt);
                    Require(receipt.Session == session, CargoPhysicalStatus.SessionMismatch, "Receipt session differs.");
                    Require(receipt.Component == component, CargoPhysicalStatus.ComponentMismatch, "Receipt owner differs.");
                    Require(receipt.OperationId > 0 && receipt.Sequence > 0, CargoPhysicalStatus.InvalidReceipt, "Positive observed operation ID and sequence required.");
                    foreach (var accepted in state.Receipts)
                    {
                        budget.Spend();
                        if (accepted.Receipt.OperationId == receipt.OperationId)
                        {
                            Require(accepted.Receipt.Same(receipt), CargoPhysicalStatus.ConflictingReceipt, "An accepted operation ID was reused with different evidence.");
                            return new CargoPhysicalResult(CargoPhysicalStatus.ExactDuplicate, Stamp, budget.Spent,
                                "Exact retained receipt; no state changed. Outputs are not re-presented as current.", accepted.CohortId);
                        }
                        Require(accepted.Receipt.Sequence != receipt.Sequence, CargoPhysicalStatus.ConflictingReceipt, "An accepted sequence was reused under another ID.");
                    }
                    Require(complete, CargoPhysicalStatus.CoverageInvalidated, "Current coverage was invalidated; there is no implicit resync.");
                    Require(receipt.OperationId > state.LastId && receipt.Sequence > state.LastSequence,
                        CargoPhysicalStatus.ReceiptHistoryUnavailable, "Older unretained operation identity/order cannot be treated as new.");
                    Require(state.Receipts.Count < MaximumReceipts, CargoPhysicalStatus.ReceiptLimit, "Accepted receipts are never evicted in this owner.");
                    var staged = state.Copy(budget);
                    int firstNew = staged.Nodes.Count;
                    int? cohort = ApplyPrimitive(staged, receipt, budget);
                    staged.LastId = receipt.OperationId; staged.LastSequence = receipt.Sequence;
                    staged.Receipts.Add(new Accepted(receipt, cohort));
                    Validate(staged, budget);
                    budget.Spend(3L * (staged.Nodes.Count - firstNew) + 4);
                    var output = new List<CargoPhysicalEntityVersion>();
                    for (int i = firstNew; i < staged.Nodes.Count; i++)
                        if (staged.Frontier.Contains(i) && staged.Nodes[i].EntityKey != null)
                            output.Add(Version(staged.Nodes[i]));
                    var outputArray = output.ToArray();
                    long nextGraph = checked(graphVersion + 1), nextCoverage = checked(coverageRevision + 1);
                    state = staged; graphVersion = nextGraph; coverageRevision = nextCoverage; coverageReason = CargoPhysicalStatus.Applied;
                    return new CargoPhysicalResult(CargoPhysicalStatus.Applied, Stamp, budget.Spent, cohortId: cohort, outputs: outputArray);
                }
                catch (Stop error) { return Invalidate(error.Status, error.Message, budget); }
                catch (OverflowException) { return Invalidate(CargoPhysicalStatus.ArithmeticOverflow, "Observed quantity arithmetic exceeds Int64.", budget); }
            }
        }
        private CargoPhysicalResult Invalidate(CargoPhysicalStatus status, string detail, Budget budget)
        {
            // The last trusted graph remains audit evidence. It is no longer a current answer.
            if (complete) { complete = false; coverageReason = status; coverageRevision = checked(coverageRevision + 1); }
            return new CargoPhysicalResult(status, Stamp, budget.Spent, detail);
        }
        private static void ValidateKeys(CargoPhysicalReceipt receipt)
        {
            if (receipt.Kind != CargoPhysicalOperation.Birth) Observation(receipt.Source.Observation);
            if (receipt.Kind == CargoPhysicalOperation.Absorb) Observation(receipt.Target.Observation);
            if (receipt.SourceAfter.HasValue) Observation(receipt.SourceAfter.Value);
            if (receipt.Output.HasValue) Observation(receipt.Output.Value);
            if (receipt.Kind == CargoPhysicalOperation.AccountedSink)
                Require(Key(receipt.SinkContext), CargoPhysicalStatus.InvalidReceipt, "Accounted sink context required.");
        }
        private CargoPhysicalEntityVersion Version(CargoPhysicalNode node) => new CargoPhysicalEntityVersion(session, component, node.Id, node.Observation);
        private int Input(State staged, CargoPhysicalEntityVersion expected)
        {
            Require(expected.Session == session, CargoPhysicalStatus.SessionMismatch, "Participant session differs.");
            Require(expected.Component == component, CargoPhysicalStatus.CrossComponentUnsupported,
                "This slice cannot import tracked history from another component; an atomic session coordinator is required.");
            Require(staged.Live.TryGetValue(expected.Observation.EntityKey, out int at), CargoPhysicalStatus.UnknownEntity, "Participant is not a current live entity.");
            Require(CargoPhysicalReceipt.Same(Version(staged.Nodes[at]), expected), CargoPhysicalStatus.StaleVersion,
                "Expected physical version/count/custody differs from the sole current version.");
            return at;
        }
        private static void Admit(State staged, string key)
        { Require(staged.SeenEntities.Add(key), CargoPhysicalStatus.DuplicateEntity, "Birth/new split key was already observed, including retired keys."); }
        private static void SameIdentityContext(CargoPhysicalObservation before, CargoPhysicalObservation after)
        { Require(before.EntityKey == after.EntityKey && before.ContextKey == after.ContextKey, CargoPhysicalStatus.InvalidReceipt, "Native split/absorb residuals preserve identity and custody."); }
        private static void Retire(State staged, int node)
        {
            Require(staged.Frontier.Remove(node), CargoPhysicalStatus.InvalidGraph, "Input version already retired.");
            if (staged.Nodes[node].EntityKey != null)
                Require(staged.Live.Remove(staged.Nodes[node].EntityKey), CargoPhysicalStatus.InvalidGraph, "Live registry lacks retired input.");
        }
        private static int Add(State staged, CargoPhysicalNodeKind kind, CargoPhysicalObservation observation,
            long birth, int origin, long sequence, int suboperation, Budget budget)
        {
            budget.Spend(3);
            Require(staged.Nodes.Count < MaximumNodes, CargoPhysicalStatus.NodeLimit, "Physical version limit reached.");
            int index = staged.Nodes.Count;
            var node = new CargoPhysicalNode(index + 1, kind, observation.EntityKey, observation.ContextKey,
                observation.Quantity, birth, origin, sequence, suboperation, true);
            staged.Nodes.Add(node); staged.Frontier.Add(index);
            if (observation.EntityKey != null) staged.Live.Add(observation.EntityKey, index);
            // Count the measured transit fragment too: a captured internal complete cut must
            // obey the same live-frontier bound as an externally published operation boundary.
            int active = 0;
            budget.Spend(staged.Frontier.Count);
            foreach (int frontier in staged.Frontier)
                if (staged.Nodes[frontier].Kind != CargoPhysicalNodeKind.AccountedSink) active++;
            Require(active <= MaximumLiveFrontiers, CargoPhysicalStatus.FrontierLimit, "Live/transit frontier limit reached.");
            return index;
        }
        private static void Edge(State staged, int from, int to, long quantity, Budget budget)
        {
            budget.Spend();
            Require(staged.Edges.Count < MaximumEdges, CargoPhysicalStatus.EdgeLimit, "Physical edge limit reached.");
            staged.Edges.Add(new CargoFlowEdge(from, to, quantity));
        }
        private static int CaptureCut(State staged, CargoPhysicalReceipt receipt, int selected, CargoCohortBoundary boundary, Budget budget)
        {
            budget.Spend(2L * staged.Nodes.Count + 4);
            Require(staged.Frontier.Contains(selected), CargoPhysicalStatus.InvalidGraph, "Cohort branch is not on the current complete internal cut.");
            var cut = new List<int>();
            for (int i = 0; i < staged.Nodes.Count; i++) if (staged.Frontier.Contains(i)) cut.Add(i + 1);
            int id = staged.Cohorts.Count + 1;
            staged.Cohorts.Add(new CargoPhysicalCohort(id, receipt.OperationId, receipt.Sequence, boundary,
                selected + 1, staged.Nodes.Count + 1, staged.Nodes[selected].Quantity, cut.ToArray()));
            return id;
        }
        private int? ApplyPrimitive(State staged, CargoPhysicalReceipt r, Budget budget)
        {
            if (r.Kind == CargoPhysicalOperation.Birth)
            {
                var born = r.Output.Value; Admit(staged, born.EntityKey);
                staged.TotalBirth = checked(staged.TotalBirth + born.Quantity);
                Add(staged, CargoPhysicalNodeKind.ObservedBirth, born, born.Quantity, staged.NextOrigin++, r.Sequence, 1, budget);
                return null;
            }
            int source = Input(staged, r.Source); var before = staged.Nodes[source];
            if (r.Kind == CargoPhysicalOperation.Move)
            {
                var after = r.Output.Value;
                Require(after.EntityKey == before.EntityKey && after.Quantity == before.Quantity && after.ContextKey != before.ContextKey,
                    CargoPhysicalStatus.InvalidReceipt, "Owner move preserves the whole entity/count and changes observed custody context.");
                Retire(staged, source);
                int output = Add(staged, CargoPhysicalNodeKind.EntityVersion, after, 0, 0, r.Sequence, 1, budget);
                Edge(staged, source, output, before.Quantity, budget);
                return r.CaptureCohort ? CaptureCut(staged, r, output, CargoCohortBoundary.AfterMove, budget) : (int?)null;
            }
            long remainder = r.SourceAfter?.Quantity ?? 0;
            if (r.SourceAfter.HasValue) SameIdentityContext(before.Observation, r.SourceAfter.Value);
            Require(remainder >= 0 && remainder < before.Quantity, CargoPhysicalStatus.InvalidReceipt, "A positive measured quantity must leave the source.");
            long moved = before.Quantity - remainder;
            if (r.Kind == CargoPhysicalOperation.Split)
            {
                Require(r.SourceAfter.HasValue && r.Output.HasValue && r.Output.Value.Quantity == moved,
                    CargoPhysicalStatus.InvalidReceipt, "Partial split requires both actual positive outputs; use Move for whole-object movement.");
                var after = r.Output.Value; Admit(staged, after.EntityKey);
                Retire(staged, source);
                int residual = Add(staged, CargoPhysicalNodeKind.EntityVersion, r.SourceAfter.Value, 0, 0, r.Sequence, 1, budget);
                int output = Add(staged, CargoPhysicalNodeKind.EntityVersion, after, 0, 0, r.Sequence, 2, budget);
                Edge(staged, source, residual, remainder, budget); Edge(staged, source, output, moved, budget);
                return r.CaptureCohort ? CaptureCut(staged, r, output, CargoCohortBoundary.AfterSplit, budget) : (int?)null;
            }
            if (r.Kind == CargoPhysicalOperation.Absorb)
            {
                int target = Input(staged, r.Target);
                Require(source != target, CargoPhysicalStatus.InvalidReceipt, "Absorber and source must be distinct owned versions.");
                var oldTarget = staged.Nodes[target]; var targetAfter = r.Output.Value;
                SameIdentityContext(oldTarget.Observation, targetAfter);
                Require(targetAfter.Quantity == checked(oldTarget.Quantity + moved), CargoPhysicalStatus.InvalidReceipt, "Actual target growth must equal actual source loss, even when native success was false.");
                Retire(staged, source);
                int transfer = Add(staged, CargoPhysicalNodeKind.MeasuredTransfer,
                    new CargoPhysicalObservation(null, moved, null), 0, 0, r.Sequence, 1, budget);
                Edge(staged, source, transfer, moved, budget);
                if (r.SourceAfter.HasValue)
                {
                    int residual = Add(staged, CargoPhysicalNodeKind.EntityVersion, r.SourceAfter.Value, 0, 0, r.Sequence, 2, budget);
                    Edge(staged, source, residual, remainder, budget);
                }
                int? cohort = r.CaptureCohort ? CaptureCut(staged, r, transfer, CargoCohortBoundary.AfterSourceSeparationBeforeAbsorb, budget) : (int?)null;
                Retire(staged, target); Retire(staged, transfer);
                int output = Add(staged, CargoPhysicalNodeKind.EntityVersion, targetAfter, 0, 0, r.Sequence, 3, budget);
                Edge(staged, target, output, oldTarget.Quantity, budget); Edge(staged, transfer, output, moved, budget);
                return cohort;
            }
            Require(r.Kind == CargoPhysicalOperation.AccountedSink && r.SinkQuantity == moved,
                CargoPhysicalStatus.InvalidReceipt, "Accounted sink quantity must equal observed source loss.");
            Retire(staged, source);
            if (r.SourceAfter.HasValue)
            {
                int residual = Add(staged, CargoPhysicalNodeKind.EntityVersion, r.SourceAfter.Value, 0, 0, r.Sequence, 1, budget);
                Edge(staged, source, residual, remainder, budget);
            }
            int sink = Add(staged, CargoPhysicalNodeKind.AccountedSink,
                new CargoPhysicalObservation(null, moved, r.SinkContext), 0, 0, r.Sequence, 2, budget);
            Edge(staged, source, sink, moved, budget); return null;
        }

        private static void Validate(State value, Budget budget)
        {
            budget.Spend(3L * value.Nodes.Count + 3L * value.Edges.Count);
            Require(value.Nodes.Count > 0 && value.Nodes.Count <= MaximumNodes && value.Edges.Count <= MaximumEdges
                && value.Receipts.Count <= MaximumReceipts && value.Live.Count <= MaximumLiveFrontiers, CargoPhysicalStatus.InvalidGraph, "Owned graph shape exceeds fixed limits.");
            var incoming = new long[value.Nodes.Count]; var outgoing = new long[value.Nodes.Count];
            foreach (var edge in value.Edges)
            {
                Require(edge.From >= 0 && edge.From < edge.To && edge.To < value.Nodes.Count && edge.Quantity > 0,
                    CargoPhysicalStatus.InvalidGraph, "Directional positive edges must follow immutable version order.");
                incoming[edge.To] = checked(incoming[edge.To] + edge.Quantity); outgoing[edge.From] = checked(outgoing[edge.From] + edge.Quantity);
            }
            long birth = 0, frontierTotal = 0; int live = 0;
            for (int i = 0; i < value.Nodes.Count; i++)
            {
                var n = value.Nodes[i]; bool frontier = value.Frontier.Contains(i);
                Require(n.Id == i + 1 && n.Quantity > 0 && n.BirthQuantity >= 0 && checked(n.BirthQuantity + incoming[i]) == n.Quantity
                    && outgoing[i] == (frontier ? 0 : n.Quantity), CargoPhysicalStatus.InvalidGraph, "Complete physical birth, frontier and transformation quantities must balance.");
                Require((n.Kind == CargoPhysicalNodeKind.ObservedBirth) == (n.BirthQuantity > 0)
                    && (n.BirthQuantity == 0 ? n.OriginId == 0 : n.OriginId > 0 && n.BirthQuantity == n.Quantity),
                    CargoPhysicalStatus.InvalidGraph, "Only observed admission creates physical origin mass.");
                birth = checked(birth + n.BirthQuantity);
                if (!frontier) continue;
                frontierTotal = checked(frontierTotal + n.Quantity);
                Require(n.Kind != CargoPhysicalNodeKind.MeasuredTransfer, CargoPhysicalStatus.InvalidGraph, "An internal transfer cannot remain a published frontier.");
                if (n.EntityKey != null)
                {
                    budget.Spend(2L * MaximumKeyLength + 4);
                    Require(value.Live.TryGetValue(n.EntityKey, out int current) && current == i && value.SeenEntities.Contains(n.EntityKey),
                        CargoPhysicalStatus.InvalidGraph, "The live registry must own each current physical version exactly once.");
                    live++;
                }
                else Require(n.Kind == CargoPhysicalNodeKind.AccountedSink, CargoPhysicalStatus.InvalidGraph, "Entity-free published frontiers must be accounted sinks.");
            }
            Require(birth == value.TotalBirth && frontierTotal == birth && live == value.Live.Count,
                CargoPhysicalStatus.InvalidGraph, "Complete live plus accounted-terminal frontier must conserve all observed mass.");
        }
    }
}
