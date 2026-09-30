using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    public enum StorageWorkKind { Topology, Grid, Predicate, Matching, Attribution, Custody, RawGuard, Native, ObservedEligibility, Count }
    public enum StorageWorkLane { Optional, Fair, Mandatory }

    // One map owns one account. Reservations debit before callbacks run, so a nested
    // query cannot spend its parent's promised final-validation work.
    public sealed class StorageWorkAccount
    {
        private readonly long[] optionalLimit, fairLimit;
        private readonly long[][] used = { NewVector(), NewVector(), NewVector() };
        private readonly long[][] held = { NewVector(), NewVector(), NewVector() };
        private long generation;
        public int Tick { get; private set; } = int.MinValue;
        public bool MandatoryOverrun { get; private set; }
        public StorageWorkAccount(long[] optional, long[] fair)
        { optionalLimit = Copy(optional); fairLimit = Copy(fair); }
        public static long[] NewVector() => new long[(int)StorageWorkKind.Count];
        private static long[] Copy(long[] values)
        {
            if (values == null || values.Length != (int)StorageWorkKind.Count) throw new ArgumentException(nameof(values));
            var result = (long[])values.Clone();
            foreach (long value in result) if (value < 0) throw new ArgumentOutOfRangeException(nameof(values));
            return result;
        }
        public bool BeginTick(int tick)
        {
            if (Tick == tick) return false;
            Tick = tick; generation++; MandatoryOverrun = false;
            foreach (var values in used) Array.Clear(values, 0, values.Length);
            foreach (var values in held) Array.Clear(values, 0, values.Length);
            return true;
        }
        public long Used(StorageWorkLane lane, StorageWorkKind kind) => used[(int)lane][(int)kind];
        public long Reserved(StorageWorkLane lane, StorageWorkKind kind) => held[(int)lane][(int)kind];
        public Reservation TryReserve(StorageWorkLane lane, long[] initial, long[] final)
        {
            var first = Copy(initial); var last = Copy(final); int l = (int)lane;
            for (int k = 0; k < first.Length; k++)
                if (first[k] > long.MaxValue - last[k]) throw new ArgumentOutOfRangeException(nameof(final));
            var limit = lane == StorageWorkLane.Fair ? fairLimit : optionalLimit;
            if (lane != StorageWorkLane.Mandatory)
            {
                if (lane == StorageWorkLane.Optional && MandatoryOverrun) return null;
                for (int k = 0; k < first.Length; k++)
                    if (first[k] > limit[k] - used[l][k] - held[l][k]
                        || last[k] > limit[k] - used[l][k] - held[l][k] - first[k]) return null;
            }
            for (int k = 0; k < first.Length; k++) held[l][k] = Add(held[l][k], Add(first[k], last[k]));
            return new Reservation(this, lane, generation, first, last);
        }
        private static long Add(long a, long b) => b > long.MaxValue - a ? long.MaxValue : a + b;

        public sealed class Reservation : IDisposable
        {
            private readonly StorageWorkAccount owner;
            private readonly long epoch;
            private readonly long[] initial, final, spent = NewVector();
            private bool finishing, disposed;
            public bool Finishing => finishing;
            public StorageWorkLane Lane { get; }
            public bool Current => !disposed && epoch == owner.generation;
            public long Spent(StorageWorkKind kind) => spent[(int)kind];
            internal Reservation(StorageWorkAccount owner, StorageWorkLane lane, long epoch, long[] initial, long[] final)
            { this.owner = owner; Lane = lane; this.epoch = epoch; this.initial = initial; this.final = final; }
            public void BeginFinal() { if (!Current) throw new InvalidOperationException("Expired storage work reservation"); finishing = true; }
            public void RestorePhase(bool finalPhase) { if (Current) finishing = finalPhase; }
            public long Remaining(StorageWorkKind kind)
                => !Current ? 0 : (finishing ? final : initial)[(int)kind];
            public bool TryCharge(StorageWorkKind kind, long amount)
            {
                if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
                if (!Current) return false;
                int k = (int)kind, l = (int)Lane;
                var remaining = finishing ? final : initial;
                if (amount > remaining[k] && Lane != StorageWorkLane.Mandatory) return false;
                long fromHeld = Math.Min(amount, remaining[k]);
                remaining[k] -= fromHeld; owner.held[l][k] -= fromHeld;
                owner.used[l][k] = Add(owner.used[l][k], amount); spent[k] = Add(spent[k], amount);
                if (Lane == StorageWorkLane.Mandatory && owner.used[l][k] > owner.optionalLimit[k]) owner.MandatoryOverrun = true;
                return true;
            }
            // Core reserves before invoking compatibility callbacks. Only a returned
            // result refunds the unused part; an escaping callback retains its charge.
            private void Refund(StorageWorkKind kind, long amount, bool finalPhase)
            {
                if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
                if (!Current || amount == 0) return;
                int k = (int)kind, l = (int)Lane;
                if (amount > spent[k]) throw new InvalidOperationException("Storage work refund exceeds charged work");
                spent[k] -= amount; owner.used[l][k] -= amount;
                (finalPhase ? final : initial)[k] = Add((finalPhase ? final : initial)[k], amount);
                owner.held[l][k] = Add(owner.held[l][k], amount);
                if (Lane == StorageWorkLane.Mandatory)
                {
                    owner.MandatoryOverrun = false;
                    for (int i = 0; i < spent.Length; i++)
                        if (owner.used[l][i] > owner.optionalLimit[i]) owner.MandatoryOverrun = true;
                }
            }
            public MatchingCharge ReserveMatching(int maximum)
                => maximum > 0 && TryCharge(StorageWorkKind.Matching, maximum)
                    ? new MatchingCharge(this, maximum, finishing) : null;
            public sealed class MatchingCharge
            {
                private readonly Reservation token;
                private readonly int maximum;
                private readonly bool phase;
                private bool returned;
                internal MatchingCharge(Reservation token, int maximum, bool phase)
                { this.token = token; this.maximum = maximum; this.phase = phase; }
                public void Returned(int actual)
                {
                    if (returned) throw new InvalidOperationException("Matching reservation already settled");
                    if (actual < 0 || actual > maximum) throw new ArgumentOutOfRangeException(nameof(actual));
                    returned = true;
                    token.Refund(StorageWorkKind.Matching, maximum - actual, phase);
                }
            }
            public void Dispose()
            {
                if (disposed) return;
                if (epoch == owner.generation)
                    for (int k = 0; k < initial.Length; k++) owner.held[(int)Lane][k] -= initial[k] + final[k];
                disposed = true;
            }
        }
    }

    // Storage coordinates only. No incoming Thing, amount, edge, result or closure.
    public sealed class StorageProgressQueue
    {
        public sealed class Entry
        {
            public object Group { get; }
            public int NextMember, NextCell;
            public int WorkerMember, WorkerCell;
            public int? HeavyCell;
            public bool Larger;
            public string Reason;
            public long Turns { get; internal set; }
            public bool Pending { get; internal set; }
            public long RequestVersion { get; internal set; }
            private readonly List<int> hints = new List<int>();
            public IReadOnlyList<int> Hints => hints;
            internal Entry(object group) { Group = group; }
            public bool Remember(int cell, int maximum)
            {
                if (hints.Contains(cell)) return true;
                if (hints.Count >= maximum) { Reason = "coordinate-hint-envelope"; return false; }
                hints.Add(cell); return true;
            }
            public void TopologyChanged()
            { NextMember = NextCell = WorkerMember = WorkerCell = 0; hints.Clear(); HeavyCell = null; Reason = "topology-changed"; }
        }
        private readonly LinkedList<Entry> order = new LinkedList<Entry>();
        private readonly Dictionary<object, LinkedListNode<Entry>> entries = new Dictionary<object, LinkedListNode<Entry>>(ReferenceComparer.Instance);
        public int Count => entries.Count;
        public int PendingCount => order.Count;
        public Entry Get(object group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (entries.TryGetValue(group, out var node)) return node.Value;
            node = new LinkedListNode<Entry>(new Entry(group)); entries.Add(group, node); return node.Value;
        }
        // Query demand wakes discovery without changing an already waiting group's position.
        // Get alone only retains coordinate hints; successful reads create no background work.
        public Entry Request(object group)
        {
            var entry = Get(group); entry.RequestVersion++;
            if (!entry.Pending) { order.AddLast(entries[group]); entry.Pending = true; }
            return entry;
        }
        public bool Complete(object group, long requestVersion)
        {
            if (!entries.TryGetValue(group, out var node) || node.Value.RequestVersion != requestVersion) return false;
            if (node.Value.Pending) { order.Remove(node); node.Value.Pending = false; }
            return true;
        }
        public Entry TakeTurn()
        {
            if (order.First == null) return null;
            var node = order.First; order.RemoveFirst(); order.AddLast(node); node.Value.Turns++;
            return node.Value;
        }
        public void Remove(object group)
        { if (entries.TryGetValue(group, out var node)) { entries.Remove(group); if (node.Value.Pending) order.Remove(node); node.Value.Pending = false; } }
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }
}
