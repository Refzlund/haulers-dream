using System;
using System.Collections.Generic;
using System.Diagnostics;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal sealed class StorageWorkExhausted : Exception
    {
        internal readonly StorageWorkKind Kind;
        internal StorageWorkExhausted(StorageWorkKind kind) { Kind = kind; }
    }

    internal static class StorageProgressWork
    {
        [ThreadStatic] private static Operation active;
        [ThreadStatic] private static Frame activeFrame;
        [ThreadStatic] private static int frameGeneration;
        [ThreadStatic] private static ContextFrame context;
        [ThreadStatic] private static int simulationDepth, observationDepth;
        [ThreadStatic] private static int contextGeneration;
        // Set by the existing startup shim before any loaded game can tick. Keep
        // the absent optional API (and mod discovery) outside every work charge.
        internal static bool MultiplayerActive;
        internal static bool PersistentAllowed => observationDepth == 0
            && (!MultiplayerActive || !MultiplayerCompat.StorageProgressInterface)
            && (simulationDepth > 0 || MultiplayerActive && MultiplayerCompat.StorageProgressCommand);
        internal static Operation Active => active != null && active.Token.Current && PersistentAllowed ? active : null;
        internal static Operation ActiveFor(Map map)
        {
            var operation = Active;
            return operation != null && ReferenceEquals(operation.Owner.Map, map) && operation.Token.Current ? operation : null;
        }
        internal static IDisposable Simulation() => new ContextFrame(false);
        internal static IDisposable ObserveOnly() => new ContextFrame(true);
        private sealed class ContextFrame : IDisposable
        {
            private readonly ContextFrame previous;
            private readonly int previousSimulation, previousObservation;
            private readonly int generation;
            private bool disposed;
            internal ContextFrame(bool observation)
            {
                previous = context; previousSimulation = simulationDepth; previousObservation = observationDepth;
                generation = contextGeneration;
                context = this;
                if (observation) observationDepth++; else simulationDepth++;
            }
            public void Dispose()
            {
                if (disposed) return; disposed = true;
                if (generation != contextGeneration) return;
                if (ReferenceEquals(context, this))
                { context = previous; simulationDepth = previousSimulation; observationDepth = previousObservation; }
                else { contextGeneration++; context = null; simulationDepth = observationDepth = 0; }
            }
        }

        // PROVISIONAL calibration candidate, not accepted production tuning. Existing
        // 200/4096/8192/100000 cheap limits remain the normal atomic reader/Core tiers.
        // Larger reader shape uses the retained diagnostic 32768/512 cardinalities.
        // Transaction allowance covers two initial operations and one final operation.
        private static long[] Atomic => new long[] { 4096, 32768, 8192, 100000, 512L * 512, 512L * 512, 512L * 512, 200, 512L * 512 };
        private static long[] Scale(long[] value, int count)
        { var copy = (long[])value.Clone(); for (int i = 0; i < copy.Length; i++) copy[i] *= count; return copy; }
        // A fair turn reserves two transactions: one discovery pass and one real
        // query after that pass. GameComponentTick can precede the native query in
        // the same tick; a single shared transaction would starve admission forever.
        internal static StorageWorkAccount NewAccount() => new StorageWorkAccount(Scale(Atomic, 6), Scale(Atomic, 6));
        internal static void Charge(StorageWorkKind kind, int amount = 1)
        {
            var operation = Active;
            if (operation == null) return;
            operation.Attempts[(int)kind] += amount;
            if (operation.CompatibilityLimit != null && operation.Attempts[(int)kind] > operation.CompatibilityLimit[(int)kind]
                || !operation.Token.TryCharge(kind, amount))
            { operation.StoppedDimension = kind.ToString(); throw new StorageWorkExhausted(kind); }
        }
        internal static T Provider<T>(Func<T> call)
        {
            var operation = Active;
            if (operation == null) return call();
            long started = Stopwatch.GetTimestamp(); operation.ProviderCalls++;
            try { return call(); }
            finally { operation.ProviderTicks += Stopwatch.GetTimestamp() - started; }
        }
        internal static void Sort<T>(List<T> values, Comparison<T> comparison, StorageWorkKind kind)
        {
            if (Active == null) { values.Sort(comparison); return; }
            try { values.Sort((left, right) => { Charge(kind); return comparison(left, right); }); }
            // BCL sort wraps comparer exceptions. These lists are owned scratch state;
            // propagate the private work stop so the caller defers without a game error.
            catch (InvalidOperationException failure) when (failure.InnerException is StorageWorkExhausted)
            { throw (StorageWorkExhausted)failure.InnerException; }
        }
        internal static void NativeGridUpperBound(int records)
        { var operation = Active; if (operation != null) operation.NativeGridRecordUpperBound += records; }
        internal static StorageAllocationObservationLimits Limits(StorageProgressQueue.Entry entry = null)
        {
            var operation = Active;
            bool large = operation?.Lane == StorageWorkLane.Mandatory || entry?.Larger == true || operation?.Entry?.Larger == true;
            var progress = entry ?? operation?.Entry;
            return large
                ? new StorageAllocationObservationLimits(64, 201, 32768, 8192, 512, progress?.NextMember ?? 0, progress?.NextCell ?? 0)
                : new StorageAllocationObservationLimits(startMember: progress?.NextMember ?? 0, startCell: progress?.NextCell ?? 0);
        }
        internal static StorageAllocationObservationLimits SelectedLimits()
        {
            var limits = Limits();
            return new StorageAllocationObservationLimits(limits.Members, limits.Cells, limits.GridThings, limits.Predicates, limits.Demands);
        }
        internal static Operation Begin(Map map, ISlotGroup group, StorageWorkLane lane, string phase)
        {
            if (!PersistentAllowed) return null;
            var owner = HaulersDreamGameComponent.Instance?.StorageWorkFor(map);
            if (owner == null) return null;
            var entry = group == null ? null : owner.Queue.Get(group);
            if (lane == StorageWorkLane.Optional && ReferenceEquals(owner.FairGroup, group)) lane = StorageWorkLane.Fair;
            var token = owner.Account.TryReserve(lane, Scale(Atomic, 2), Atomic);
            return token == null ? null : new Operation(owner, entry, token, phase);
        }
        internal static void NeedDiscovery(Map map, ISlotGroup group, string reason)
        {
            if (group == null || !PersistentAllowed) return;
            var owner = HaulersDreamGameComponent.Instance?.StorageWorkFor(map);
            if (owner != null) owner.Queue.Request(group).Reason = reason;
        }
        internal static IDisposable Enter(Operation operation) => new Frame(operation);
        private sealed class Frame : IDisposable
        {
            private readonly Operation previous;
            private readonly Frame previousFrame;
            private readonly int generation;
            private bool disposed;
            internal Frame(Operation operation)
            {
                previous = active; previousFrame = activeFrame;
                generation = frameGeneration; activeFrame = this; active = operation;
            }
            public void Dispose()
            {
                if (disposed) return; disposed = true;
                if (generation != frameGeneration) return;
                if (ReferenceEquals(activeFrame, this)) { active = previous; activeFrame = previousFrame; }
                else { frameGeneration++; activeFrame = null; active = null; }
            }
        }
        internal sealed class Operation : IDisposable
        {
            internal readonly StorageMapWork Owner;
            internal readonly StorageProgressQueue.Entry Entry;
            internal readonly StorageWorkAccount.Reservation Token;
            internal readonly long[] Attempts = StorageWorkAccount.NewVector();
            internal readonly long[] CompatibilityLimit;
            internal string StoppedDimension;
            internal StorageWorkLane Lane => Token.Lane;
            internal readonly string Phase;
            internal readonly int Tick;
            internal readonly long Id, ParentId;
            internal long ProviderCalls, ProviderTicks, NativeGridRecordUpperBound;
            private readonly long started = Stopwatch.GetTimestamp();
            private bool closed;
            internal Operation(StorageMapWork owner, StorageProgressQueue.Entry entry, StorageWorkAccount.Reservation token, string phase)
            {
                Owner = owner; Entry = entry; Token = token; Phase = phase; Tick = owner.Account.Tick;
                Id = ++owner.OperationSerial; ParentId = Active?.Id ?? 0;
                // A failed compatibility gate takes the original path. These finite
                // per-call limits are not an optional map/tick-work claim for that fallback.
                if (phase == "worker-compatibility" || phase == "group-registration") CompatibilityLimit = Atomic;
            }
            internal IDisposable FinalPass() => new FinalFrame(Token);
            public void Dispose()
            {
                if (closed) return; closed = true;
                var charged = StorageWorkAccount.NewVector();
                for (int i = 0; i < charged.Length; i++) charged[i] = Token.Spent((StorageWorkKind)i);
                Owner.LastOperation = new StorageWorkReport(Tick, Lane, Phase, Attempts, charged,
                    Stopwatch.GetTimestamp() - started, Owner.Account.MandatoryOverrun,
                    Id, ParentId, ProviderCalls, ProviderTicks, NativeGridRecordUpperBound, StoppedDimension);
                Token.Dispose();
            }
        }
        private sealed class FinalFrame : IDisposable
        {
            private readonly StorageWorkAccount.Reservation token;
            private readonly bool previous;
            internal FinalFrame(StorageWorkAccount.Reservation token)
            { this.token = token; previous = token.Finishing; if (token.Current) token.BeginFinal(); }
            public void Dispose() { token.RestorePhase(previous); }
        }
    }

    // Counters only; reading a report never invokes storage/provider callbacks.
    internal sealed class StorageWorkReport
    {
        internal readonly int Tick;
        internal readonly StorageWorkLane Lane;
        internal readonly string Phase;
        internal readonly long[] Attempts;
        internal readonly long[] Charged;
        internal readonly long Id, ParentId, ProviderCalls, ProviderTicks, NativeGridRecordUpperBound;
        internal readonly long StopwatchTicks;
        internal readonly bool MandatoryOverrun;
        internal readonly string StoppedDimension;
        internal StorageWorkReport(int tick, StorageWorkLane lane, string phase, long[] attempts, long[] charged, long elapsed,
            bool overrun, long id, long parent, long providerCalls, long providerTicks, long nativeGridUpperBound, string stoppedDimension)
        {
            Tick = tick; Lane = lane; Phase = phase; Attempts = (long[])attempts.Clone(); Charged = charged;
            StopwatchTicks = elapsed; MandatoryOverrun = overrun; Id = id; ParentId = parent;
            ProviderCalls = providerCalls; ProviderTicks = providerTicks; NativeGridRecordUpperBound = nativeGridUpperBound;
            StoppedDimension = stoppedDimension;
        }
    }

    internal sealed class StorageMapWork
    {
        internal readonly Map Map;
        internal readonly StorageWorkAccount Account = StorageProgressWork.NewAccount();
        internal readonly StorageProgressQueue Queue = new StorageProgressQueue();
        internal ISlotGroup FairGroup;
        internal StorageWorkReport LastOperation;
        internal long OperationSerial;
        private readonly Dictionary<ISlotGroup, ListStamp> topology = new Dictionary<ISlotGroup, ListStamp>();
        internal StorageMapWork(Map map) { Map = map; }
        private sealed class ListStamp
        {
            internal StorageGroup Linked;
            internal ProjectionListGuard<IStorageGroupMember> Members;
            internal readonly List<Tuple<SlotGroup, List<IntVec3>, ProjectionListGuard<IntVec3>>> Lists
                = new List<Tuple<SlotGroup, List<IntVec3>, ProjectionListGuard<IntVec3>>>();
            internal bool Current(Map map, ISlotGroup group)
            {
                if (Linked != null && !Members.Matches(Linked.members)) return false;
                foreach (var row in Lists)
                {
                    StorageProgressWork.Charge(StorageWorkKind.Topology);
                    if (row.Item1.parent?.Map != map || !ReferenceEquals(StorageAllocationObservation.Canonical(row.Item1), group)
                        || !row.Item3.Matches(row.Item1.CellsList)) return false;
                }
                return true;
            }
        }
        private bool Registered(ISlotGroup group)
        {
            // Worker classification includes this scan in its same finite gate.
            if (StorageProgressWork.ActiveFor(Map)?.CompatibilityLimit != null) return RegisteredCore(group);
            // Outside classification, retirement has its own finite measured gate.
            // Above its declared map-group envelope the operation remains deferred.
            using (var work = StorageProgressWork.Begin(Map, null, StorageWorkLane.Mandatory, "group-registration"))
            using (StorageProgressWork.Enter(work))
                return RegisteredCore(group);
        }
        private bool RegisteredCore(ISlotGroup group)
        {
            if (group is StorageGroup linked)
            {
                var manager = Map.storageGroups;
                if (linked.Map != Map || manager == null) return false;
                // Native HasStorageGroup uses List.Contains; charge its full upper bound.
                StorageProgressWork.Charge(StorageWorkKind.Topology, manager.StorageGroupsForReading.Count);
                return manager.HasStorageGroup(linked);
            }
            if (!(group is SlotGroup slot) || slot.parent?.Map != Map
                || !ReferenceEquals(StorageAllocationObservation.Canonical(slot), group)) return false;
            var slots = Map.haulDestinationManager?.AllGroupsListForReading;
            if (slots == null) return false;
            StorageProgressWork.Charge(StorageWorkKind.Topology, slots.Count);
            return slots.Contains(slot) && ReferenceEquals(slot.parent.GetSlotGroup(), slot);
        }
        internal List<SlotGroup> Members(ISlotGroup group, StorageProgressQueue.Entry entry)
        {
            if (!StorageProgressWork.PersistentAllowed) return new List<SlotGroup>();
            if (!Registered(group))
            {
                Queue.Remove(group); topology.Remove(group);
                if (ReferenceEquals(FairGroup, group)) FairGroup = null;
                return new List<SlotGroup>();
            }
            if (topology.TryGetValue(group, out var prior) && !prior.Current(Map, group))
            { entry.TopologyChanged(); topology.Remove(group); }
            var slots = new List<SlotGroup>();
            if (group is SlotGroup slot) slots.Add(slot);
            else if (group is StorageGroup linked)
            {
                if (linked.members.Count > 64) { entry.Reason = "member-envelope"; return slots; }
                foreach (var member in linked.members)
                {
                    StorageProgressWork.Charge(StorageWorkKind.Topology);
                    if (!(member is ISlotGroupParent parent)) { entry.Reason = "unsupported-member"; return new List<SlotGroup>(); }
                    slots.Add(parent.GetSlotGroup());
                }
            }
            if (!topology.ContainsKey(group))
            {
                var stamp = new ListStamp();
                if (group is StorageGroup linked)
                { stamp.Linked = linked; stamp.Members = new ProjectionListGuard<IStorageGroupMember>(linked.members); }
                foreach (var item in slots)
                {
                    StorageProgressWork.Charge(StorageWorkKind.Topology);
                    var cells = item.CellsList;
                    stamp.Lists.Add(Tuple.Create(item, cells, new ProjectionListGuard<IntVec3>(cells)));
                }
                topology.Add(group, stamp);
            }
            return slots;
        }
        internal List<IntVec3> Hints(StorageProgressQueue.Entry entry)
        {
            var result = new List<IntVec3>();
            if (entry == null) return result;
            foreach (int index in entry.Hints) result.Add(Map.cellIndices.IndexToCell(index));
            return result;
        }
        internal void ObserveProgress(StorageProgressQueue.Entry entry, StorageAllocationObservationResult result, bool copyCursor = true)
        {
            if (entry == null || !StorageProgressWork.PersistentAllowed) return;
            if (copyCursor && result.NextMember >= 0) { entry.NextMember = result.NextMember; entry.NextCell = result.NextCell; }
            else if (copyCursor && (result.Complete || result.CertifiedSubset)) entry.NextMember = entry.NextCell = 0;
            if (result.Stop != null)
            {
                entry.Reason = result.Stop.Dimension + "/" + result.Stop.Phase;
                if (result.Stop.Dimension != StorageObservationLimit.Cells && result.Stop.Dimension != StorageObservationLimit.Members
                    || result.Stop.Dimension == StorageObservationLimit.Cells && !result.CertifiedSubset)
                {
                    entry.Larger = true;
                    if (result.Stop.Position.HasValue)
                    {
                        entry.HeavyCell = Map.cellIndices.CellToIndex(result.Stop.Position.Value);
                        entry.Remember(entry.HeavyCell.Value, 201);
                    }
                }
            }
        }
        internal void Remember(StorageProgressQueue.Entry entry, IReadOnlyDictionary<string, IntVec3> locations, StorageAllocationState state)
        {
            if (entry == null || !StorageProgressWork.PersistentAllowed) return;
            foreach (var slice in state.Slices)
                if (locations.TryGetValue(slice.CellKey, out var cell)) entry.Remember(Map.cellIndices.CellToIndex(cell), 201);
        }
    }

    public partial class HaulersDreamGameComponent
    {
        private readonly Dictionary<Map, StorageMapWork> storageWork = new Dictionary<Map, StorageMapWork>();
        internal StorageMapWork StorageWorkFor(Map map)
        {
            if (!StorageProgressWork.PersistentAllowed || !UnityData.IsInMainThread || StorageCommitments.ResourceQueriesBlocked() || map == null
                || !ReferenceEquals(storageGame, Current.Game) || !storageGame.Maps.Contains(map)
                || !StorageCommitments.ActiveOn(map)) return null;
            if (!storageWork.TryGetValue(map, out var owner)) storageWork.Add(map, owner = new StorageMapWork(map));
            owner.Account.BeginTick(Find.TickManager.TicksGame);
            return owner;
        }
        private void StorageProgressTick()
        {
            if (!StorageProgressWork.PersistentAllowed || !UnityData.IsInMainThread || storageGame == null || !ReferenceEquals(storageGame, Current.Game)
                || StorageCommitments.ResourceQueriesBlocked()) return;
            foreach (var old in new List<Map>(storageWork.Keys))
                if (!storageGame.Maps.Contains(old)) storageWork.Remove(old);
            foreach (var map in storageGame.Maps)
            {
                var owner = StorageWorkFor(map);
                if (owner != null) owner.FairGroup = null;
                var next = owner?.Queue.TakeTurn();
                if (!(next?.Group is ISlotGroup group)) continue;
                owner.FairGroup = group;
                StorageCommitments.ContinueStorageGroup(owner, next, group);
            }
        }
    }
}
