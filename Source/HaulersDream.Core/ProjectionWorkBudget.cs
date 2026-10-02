using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    // Shared by the unconnected game reader and its boundedness tests. Costs are
    // deterministic visits/calls, not a claim about opaque predicate elapsed time.
    public enum ProjectionWorkKind
    {
        Members, Coordinates, GridEntries, NativeGridVisits, NativeCalls,
        Filters, Compatibility, ProviderVisits, CellLimitCalls, Reservations,
        Reachability, GuardChecks, Preparation, OutputRecords, Count
    }

    public sealed class ProjectionWork
    {
        private readonly long[] values;
        public ProjectionWork(params long[] values)
        {
            if (values == null || values.Length != (int)ProjectionWorkKind.Count)
                throw new ArgumentException("One nonnegative value is required per work kind.");
            this.values = (long[])values.Clone();
            foreach (long value in values) if (value < 0) throw new ArgumentOutOfRangeException(nameof(values));
        }
        public long this[ProjectionWorkKind kind] => values[(int)kind];
        public static ProjectionWork Zero => new ProjectionWork(new long[(int)ProjectionWorkKind.Count]);
        public static ProjectionWork Cost(ProjectionWorkKind kind, long amount)
        {
            if (kind < 0 || kind >= ProjectionWorkKind.Count || amount < 0) throw new ArgumentOutOfRangeException();
            var v = new long[(int)ProjectionWorkKind.Count]; v[(int)kind] = amount;
            return new ProjectionWork(v);
        }
        public ProjectionWork Plus(ProjectionWork other)
        {
            var v = new long[values.Length];
            for (int i = 0; i < v.Length; i++) v[i] = checked(values[i] + other.values[i]);
            return new ProjectionWork(v);
        }
        // An indexed predicate preflight followed by an opaque provider scan.
        // Comp classification/callback costs are charged separately per item.
        public static ProjectionWork ProviderMemberScan(int memberCount)
        {
            if (memberCount < 0) throw new ArgumentOutOfRangeException(nameof(memberCount));
            return Cost(ProjectionWorkKind.ProviderVisits, 2L * memberCount)
                .Plus(Cost(ProjectionWorkKind.Compatibility, memberCount));
        }
        // Four native list traversals, potential directional stack tests and
        // repeated patched limit calls; census/revalidation are charged elsewhere.
        public static ProjectionWork NativeCellPredicate(int gridCount, bool carrierPresent)
        {
            if (gridCount < 0) throw new ArgumentOutOfRangeException(nameof(gridCount));
            var v = new long[(int)ProjectionWorkKind.Count];
            v[(int)ProjectionWorkKind.NativeGridVisits] = 4L * gridCount;
            v[(int)ProjectionWorkKind.NativeCalls] = 1;
            v[(int)ProjectionWorkKind.Compatibility] = gridCount;
            v[(int)ProjectionWorkKind.CellLimitCalls] = (long)gridCount + 1;
            v[(int)ProjectionWorkKind.Reservations] = 1;
            v[(int)ProjectionWorkKind.Reachability] = carrierPresent ? 1 : 0;
            return new ProjectionWork(v);
        }
    }

    public sealed class ProjectionWorkBudget
    {
        private readonly ProjectionWork limit;
        public ProjectionWork Used { get; private set; } = ProjectionWork.Zero;
        public ProjectionWorkBudget(ProjectionWork limit) => this.limit = limit ?? throw new ArgumentNullException(nameof(limit));
        // Atomic refusal: an opaque operation must not start with only part of its
        // upper bound reserved. Grid census and native visits share one ceiling.
        public bool TryCharge(ProjectionWork cost)
        {
            if (cost == null) return false;
            ProjectionWork next;
            try { next = Used.Plus(cost); } catch (OverflowException) { return false; }
            for (var k = ProjectionWorkKind.Members; k < ProjectionWorkKind.Count; k++)
                if (next[k] > limit[k]) return false;
            if (next[ProjectionWorkKind.GridEntries] > limit[ProjectionWorkKind.GridEntries] - next[ProjectionWorkKind.NativeGridVisits])
                return false;
            Used = next;
            return true;
        }
    }

    // Copies an enumerator, not the list. MoveNext on that copy checks List's
    // version even after its original traversal finished; equal-count edits fail.
    public sealed class ProjectionListGuard<T>
    {
        private readonly List<T> source;
        private readonly List<T>.Enumerator guard;
        public int Count { get; }
        public ProjectionListGuard(List<T> source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            Count = source.Count; guard = source.GetEnumerator();
        }
        public bool Matches(List<T> candidate)
        {
            if (!ReferenceEquals(source, candidate) || candidate.Count != Count) return false;
            try { var copy = guard; copy.MoveNext(); return true; }
            catch (InvalidOperationException) { return false; }
        }
    }
}
