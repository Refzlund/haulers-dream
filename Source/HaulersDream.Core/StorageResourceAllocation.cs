using System;
using System.Collections.Generic;
using System.Linq;

namespace HaulersDream.Core
{
    public static class StorageResourceAllocator
    {
        /// <summary>
        /// Propose allocations against identified physical resources. The input baseline is never
        /// mutated. A baseline must be wholly valid; new requests may receive a smaller quantity.
        /// The caller owns actual job/custody liveness and supplies its fresh baseline validation.
        /// Compatibility is directional (target, incoming), never inferred from a def or key.
        /// </summary>
        public static StorageAllocationResult Allocate(IReadOnlyList<StorageAllocationCell> cells,
            StorageAllocationState baseline, IReadOnlyList<StorageAllocationRequest> requests,
            Func<object, object, bool> canStack, Func<StorageResourceAllocation, bool> baselineEligible,
            StorageAllocationOptions options = null)
        {
            if (cells == null || baseline == null || requests == null || canStack == null || baselineEligible == null)
                throw new ArgumentNullException();
            var engine = new Engine(cells, baseline, requests, canStack, baselineEligible, options ?? new StorageAllocationOptions());
            return engine.Run();
        }

        public static StorageAllocationState Release(StorageAllocationState state, object owner, object parcel = null)
        {
            if (state == null || owner == null) throw new ArgumentNullException();
            var next = state.Slices.Where(s => !ReferenceEquals(s.Owner, owner)
                || (parcel != null && !ReferenceEquals(s.Parcel, parcel))).ToList();
            return next.Count == state.Slices.Count ? state : new StorageAllocationState(next);
        }

        /// <summary>Release an observed quantity from this exact ownership, without spending another parcel.</summary>
        public static StorageAllocationState Reduce(StorageAllocationState state, object owner, object parcel, int units,
            string resourceKey = null)
        {
            if (state == null || owner == null || parcel == null) throw new ArgumentNullException();
            if (units == 0) return state;
            var owned = state.Slices.Where(s => ReferenceEquals(s.Owner, owner) && ReferenceEquals(s.Parcel, parcel)
                && (resourceKey == null || s.ResourceKey == resourceKey)).ToArray();
            if (resourceKey == null && owned.Select(s => s.ResourceKey).Distinct(StringComparer.Ordinal).Take(2).Count() > 1)
                throw new ArgumentException("A deposit spanning multiple allocated resources needs its actual resource key.");
            if (units < 0 || units > owned.Sum(s => (long)s.Units)) throw new ArgumentOutOfRangeException(nameof(units));
            var next = new List<StorageResourceAllocation>();
            foreach (var slice in state.Slices)
            {
                int remove = ReferenceEquals(slice.Owner, owner) && ReferenceEquals(slice.Parcel, parcel)
                    && (resourceKey == null || slice.ResourceKey == resourceKey)
                    ? Math.Min(units, slice.Units) : 0;
                units -= remove;
                if (remove != slice.Units) next.Add(remove == 0 ? slice : slice.WithUnits(slice.Units - remove));
            }
            return new StorageAllocationState(next);
        }

        /// <summary>
        /// Apply an actual placement receipt: all remaining slices sharing a virtual stack now
        /// name the physical stack it became. Recheck the returned state against the fresh world
        /// before publishing; this method itself cannot establish the receipt's truth.
        /// </summary>
        public static StorageAllocationState RebindVirtualSlot(StorageAllocationState state, string slotKey,
            string cellKey, string stackKey, object actualTarget)
        {
            if (state == null || actualTarget == null) throw new ArgumentNullException();
            StorageAllocationData.Key(slotKey); StorageAllocationData.Key(cellKey); StorageAllocationData.Key(stackKey);
            bool changed = false;
            var next = new List<StorageResourceAllocation>();
            foreach (var slice in state.Slices)
            {
                if (slice.Kind == StorageAllocationResourceKind.VacantSlot && slice.ResourceKey == slotKey)
                {
                    if (slice.CellKey != cellKey) throw new ArgumentException("Receipt cell differs from the allocated cell.");
                    next.Add(new StorageResourceAllocation(slice.Owner, slice.Parcel, slice.Subject, cellKey,
                        stackKey, StorageAllocationResourceKind.ExistingStack, slice.Units, actualTarget));
                    changed = true;
                }
                else next.Add(slice);
            }
            return changed ? new StorageAllocationState(next) : state;
        }

        private sealed class WorkExhausted : Exception { }
        private sealed class InvalidBaseline : Exception { }
        private sealed class InvalidRequest : Exception { }

        private sealed class Cell
        {
            internal StorageAllocationCell Value;
            internal int FreeSlots;
        }
        private sealed class Resource
        {
            internal string Key;
            internal Cell Cell;
            internal object Target;
            internal StorageAllocationResourceKind Kind;
            internal int Limit, Free;
            internal bool Fixed;
            internal readonly Dictionary<int, int> NewUnits = new Dictionary<int, int>();
        }
        private sealed class Demand
        {
            internal StorageAllocationRequest Value;
            internal HashSet<string> Cells, Stacks;
            internal long BaselineUnits;
        }

        private sealed class Engine
        {
            private readonly IReadOnlyList<StorageAllocationCell> input;
            private readonly StorageAllocationState baseline;
            private readonly IReadOnlyList<StorageAllocationRequest> requests;
            private readonly Func<object, object, bool> canStack;
            private readonly Func<StorageResourceAllocation, bool> baselineEligible;
            private readonly StorageAllocationOptions options;
            private readonly List<Cell> cells = new List<Cell>();
            private readonly List<Resource> resources = new List<Resource>();
            private readonly Dictionary<string, Cell> cellKeys = new Dictionary<string, Cell>(StringComparer.Ordinal);
            private readonly Dictionary<string, Resource> resourceKeys = new Dictionary<string, Resource>(StringComparer.Ordinal);
            private readonly List<Demand> demands = new List<Demand>();
            private readonly Dictionary<(int, Resource), bool> compatible = new Dictionary<(int, Resource), bool>();
            private int work, newSlots, sequence;

            internal Engine(IReadOnlyList<StorageAllocationCell> input, StorageAllocationState baseline,
                IReadOnlyList<StorageAllocationRequest> requests, Func<object, object, bool> canStack,
                Func<StorageResourceAllocation, bool> baselineEligible, StorageAllocationOptions options)
            { this.input = input; this.baseline = baseline; this.requests = requests; this.canStack = canStack;
                this.baselineEligible = baselineEligible; this.options = options; }

            internal StorageAllocationResult Run()
            {
                try
                {
                    ReadPhysical(); ReadBaseline(); ReadRequests();
                    // Take existing free capacity first. Augmentation is only necessary once
                    // a request has neither a compatible free tail nor an eligible free slot.
                    for (int i = 0; i < demands.Count; i++) FillAvailable(i, Remaining(i), null);
                    var order = Enumerable.Range(0, demands.Count).OrderBy(i => demands[i].Cells.Count).ThenBy(i => i).ToArray();
                    foreach (int i in order)
                    {
                        while (Remaining(i) > 0)
                        {
                            Step();
                            FillAvailable(i, Remaining(i), null);
                            if (Remaining(i) == 0) break;
                            var cell = FindVacancy(i, false);
                            if (cell == null)
                            {
                                // Reassign only tentative top-ups/tails. Published leases remain
                                // fixed, and constrained cell matching is still the final fallback.
                                Fill(i, Remaining(i), new HashSet<Resource>(), 0);
                                if (Remaining(i) == 0) break;
                                cell = FindVacancy(i, true);
                                if (cell == null && EmptyTentativeSlot(i)) cell = FindVacancy(i, true);
                            }
                            if (cell == null) break;
                            FillNewSlot(cell, i, Remaining(i));
                        }
                    }
                    var slices = new List<StorageResourceAllocation>(baseline.Slices);
                    foreach (var node in resources)
                        foreach (var pair in node.NewUnits.OrderBy(p => p.Key))
                        {
                            if (pair.Value <= 0) continue;
                            var demand = demands[pair.Key].Value;
                            slices.Add(new StorageResourceAllocation(demand.Owner, demand.Parcel, demand.Subject,
                                node.Cell.Value.Key, node.Key, node.Kind, pair.Value, node.Target,
                                node.Kind == StorageAllocationResourceKind.VacantSlot ? node.Limit : 0));
                        }
                    var state = slices.Count == baseline.Slices.Count ? baseline : new StorageAllocationState(slices);
                    bool full = Enumerable.Range(0, demands.Count).All(i => Remaining(i) == 0);
                    return new StorageAllocationResult(!options.ObservationComplete ? StorageAllocationStatus.IncompleteObservation
                        : full ? StorageAllocationStatus.Complete : StorageAllocationStatus.CapacityLimited, state, work);
                }
                catch (WorkExhausted) { return Result(StorageAllocationStatus.BudgetExhausted); }
                catch (InvalidBaseline) { return Result(StorageAllocationStatus.NeedsReconciliation); }
                catch (InvalidRequest) { return Result(StorageAllocationStatus.InvalidRequest); }
            }
            private StorageAllocationResult Result(StorageAllocationStatus status) => new StorageAllocationResult(status, baseline, work);
            private void Step()
            {
                if (work >= options.MaximumWork) throw new WorkExhausted();
                work++;
            }
            private void ReadPhysical()
            {
                foreach (var value in input)
                {
                    Step();
                    if (value == null || cellKeys.ContainsKey(value.Key)) throw new InvalidRequest();
                    var cell = new Cell { Value = value, FreeSlots = value.VacantSlots };
                    cells.Add(cell); cellKeys.Add(value.Key, cell);
                    foreach (var stack in value.Stacks)
                    {
                        Step();
                        if (resourceKeys.ContainsKey(stack.Key)) throw new InvalidRequest();
                        var node = new Resource { Key = stack.Key, Cell = cell, Target = stack.Target,
                            Kind = StorageAllocationResourceKind.ExistingStack, Limit = stack.FreeUnits,
                            Free = stack.FreeUnits, Fixed = true };
                        resources.Add(node); resourceKeys.Add(node.Key, node);
                    }
                }
            }
            private void ReadBaseline()
            {
                foreach (var slice in baseline.Slices)
                {
                    Step();
                    if (!cellKeys.TryGetValue(slice.CellKey, out var cell) || !baselineEligible(slice)) throw new InvalidBaseline();
                    if (!resourceKeys.TryGetValue(slice.ResourceKey, out var node))
                    {
                        if (slice.Kind != StorageAllocationResourceKind.VacantSlot || cell.FreeSlots <= 0) throw new InvalidBaseline();
                        node = new Resource { Key = slice.ResourceKey, Cell = cell, Target = slice.Target,
                            Kind = slice.Kind, Free = slice.SlotLimit, Limit = slice.SlotLimit, Fixed = true };
                        resources.Add(node); resourceKeys.Add(node.Key, node); cell.FreeSlots--;
                    }
                    if (node.Cell != cell || node.Kind != slice.Kind || !ReferenceEquals(node.Target, slice.Target)
                        || (slice.Kind == StorageAllocationResourceKind.VacantSlot && node.Limit != slice.SlotLimit)
                        || slice.Units > node.Free) throw new InvalidBaseline();
                    if (!(node.Kind == StorageAllocationResourceKind.VacantSlot && ReferenceEquals(node.Target, slice.Subject))
                        && !canStack(node.Target, slice.Subject)) throw new InvalidBaseline();
                    node.Free -= slice.Units;
                }
            }
            private void ReadRequests()
            {
                if (requests.Count > options.MaximumRequests) throw new WorkExhausted();
                foreach (var value in requests)
                {
                    Step();
                    if (value == null) throw new InvalidRequest();
                    foreach (var prior in demands)
                    { Step(); if (ReferenceEquals(prior.Value.Owner, value.Owner) && ReferenceEquals(prior.Value.Parcel, value.Parcel)) throw new InvalidRequest(); }
                    var demand = new Demand { Value = value, Cells = new HashSet<string>(StringComparer.Ordinal),
                        Stacks = new HashSet<string>(StringComparer.Ordinal) };
                    // Charge before hashing/retaining each edge; a bounded rejection must not first
                    // allocate a copy of an arbitrarily large eligibility list.
                    foreach (var cell in value.EligibleCells) { Step(); demand.Cells.Add(cell); }
                    foreach (var stack in value.EligibleStacks) { Step(); demand.Stacks.Add(stack); }
                    foreach (var slice in baseline.Slices)
                    {
                        Step();
                        if (!ReferenceEquals(slice.Owner, value.Owner) || !ReferenceEquals(slice.Parcel, value.Parcel)) continue;
                        if (!ReferenceEquals(slice.Subject, value.Subject) || !demand.Cells.Contains(slice.CellKey)
                            || (slice.Kind == StorageAllocationResourceKind.ExistingStack && !demand.Stacks.Contains(slice.ResourceKey)))
                            throw new InvalidBaseline();
                        demand.BaselineUnits += slice.Units;
                    }
                    if (demand.BaselineUnits > value.Units) throw new InvalidBaseline();
                    demands.Add(demand);
                }
            }
            private int Remaining(int i)
            {
                long units = demands[i].BaselineUnits;
                foreach (var node in resources)
                { Step(); if (node.NewUnits.TryGetValue(i, out int n)) units += n; }
                return (int)Math.Max(0L, demands[i].Value.Units - units);
            }
            private bool Eligible(int i, Resource node)
            {
                Step();
                var d = demands[i];
                if (!d.Cells.Contains(node.Cell.Value.Key)
                    || (node.Kind == StorageAllocationResourceKind.ExistingStack && !d.Stacks.Contains(node.Key))) return false;
                if (compatible.TryGetValue((i, node), out bool found)) return found;
                bool allowed = node.Kind == StorageAllocationResourceKind.VacantSlot && ReferenceEquals(node.Target, d.Value.Subject)
                    || canStack(node.Target, d.Value.Subject);
                compatible.Add((i, node), allowed);
                return allowed;
            }
            private void Add(Resource node, int i, int units)
            {
                if (units <= 0) return;
                node.NewUnits.TryGetValue(i, out int old);
                node.NewUnits[i] = checked(old + units); node.Free -= units;
            }
            private int FillAvailable(int i, int wanted, HashSet<Resource> blocked)
            {
                if (wanted <= 0) return 0;
                int left = wanted;
                foreach (var node in resources)
                {
                    Step();
                    if ((blocked != null && blocked.Contains(node)) || node.Free <= 0 || !Eligible(i, node)) continue;
                    int take = Math.Min(left, node.Free); Add(node, i, take); left -= take;
                    if (left == 0) return wanted;
                }
                return wanted - left;
            }
            private int Fill(int i, int wanted, HashSet<Resource> blocked, int depth)
            {
                if (wanted <= 0) return 0;
                if (depth >= options.MaximumRequests) throw new WorkExhausted();
                int left = wanted - FillAvailable(i, wanted, blocked);
                if (left == 0) return wanted;
                // A tentative physical top-up may need to move into a still-unmaterialized
                // slot so a restricted neighbor can use its old stack. This spends only a
                // genuine free cell slot; published resources are never moved or released.
                Cell vacant;
                while (left > 0 && (vacant = FindFreeCell(i)) != null)
                    left -= FillNewSlot(vacant, i, left);
                if (left == 0) return wanted;
                // Recursion can append a new slot. Index traversal is deliberate: no resource
                // removal occurs inside Fill, and each visited (including appended) node is charged.
                for (int n = 0; n < resources.Count; n++)
                {
                    Step();
                    var node = resources[n];
                    if (blocked.Contains(node) || !Eligible(i, node)) continue;
                    blocked.Add(node);
                    foreach (var pair in node.NewUnits.OrderBy(p => p.Key).ToArray())
                    {
                        Step();
                        if (pair.Key == i || pair.Value <= 0) continue;
                        int moved = Fill(pair.Key, Math.Min(left, pair.Value), blocked, depth + 1);
                        if (moved <= 0) continue;
                        node.NewUnits[pair.Key] -= moved; node.Free += moved;
                        Add(node, i, moved); left -= moved;
                        if (left == 0) break;
                    }
                    blocked.Remove(node);
                    if (left == 0) break;
                }
                return wanted - left;
            }
            private int FillNewSlot(Cell cell, int i, int wanted)
            {
                if (newSlots >= options.MaximumNewSlots) throw new WorkExhausted();
                string key;
                do { Step(); key = "hd-virtual:" + (++sequence); } while (resourceKeys.ContainsKey(key));
                var node = new Resource { Key = key, Cell = cell, Target = demands[i].Value.Subject,
                    Kind = StorageAllocationResourceKind.VacantSlot,
                    Limit = demands[i].Value.StackLimit, Free = demands[i].Value.StackLimit };
                cell.FreeSlots--; newSlots++; resources.Add(node); resourceKeys.Add(key, node);
                int units = Math.Min(node.Free, wanted);
                Add(node, i, units);
                return units;
            }
            private Cell FindFreeCell(int request)
            {
                foreach (var cell in cells)
                { Step(); if (cell.FreeSlots > 0 && demands[request].Cells.Contains(cell.Value.Key)) return cell; }
                return null;
            }
            private bool EmptyTentativeSlot(int request)
            {
                // Compatibility with an incoming parcel is not required to evacuate an
                // incompatible tentative host. Its owners can move into real top-ups, freeing
                // the slot for a new host. Only inspect nodes present on entry: Fill may append
                // slots, but neither this loop nor Fill removes any resource while traversing.
                int count = resources.Count;
                for (int n = 0; n < count; n++)
                {
                    Step();
                    var node = resources[n];
                    if (node.Fixed || node.Kind != StorageAllocationResourceKind.VacantSlot
                        || !demands[request].Cells.Contains(node.Cell.Value.Key)) continue;
                    var blocked = new HashSet<Resource> { node };
                    foreach (var pair in node.NewUnits.OrderBy(p => p.Key).ToArray())
                    {
                        Step();
                        if (pair.Value <= 0) continue;
                        int moved = Fill(pair.Key, pair.Value, blocked, 0);
                        node.NewUnits[pair.Key] -= moved; node.Free += moved;
                    }
                    if (node.Free == node.Limit) return true;
                }
                return false;
            }
            private Cell FindVacancy(int request, bool reassign)
            {
                // A bounded augmenting path can move every NEW slice off a virtual slot.
                // It then owns no capacity and must not preserve an obsolete host restriction.
                for (int n = resources.Count - 1; n >= 0; n--)
                {
                    Step();
                    var node = resources[n];
                    if (node.Fixed || node.Kind != StorageAllocationResourceKind.VacantSlot || node.Free != node.Limit) continue;
                    node.Cell.FreeSlots++; resourceKeys.Remove(node.Key); resources.RemoveAt(n);
                }
                var free = FindFreeCell(request);
                if (free != null) return free;
                if (!reassign) return null;
                foreach (var cell in cells)
                    if (demands[request].Cells.Contains(cell.Value.Key) && MakeVacancy(cell, new HashSet<Cell>())) return cell;
                return null;
            }
            private bool MakeVacancy(Cell wanted, HashSet<Cell> path)
            {
                Step();
                if (wanted.FreeSlots > 0) return true;
                if (path.Count >= options.MaximumRequests) throw new WorkExhausted();
                if (!path.Add(wanted)) return false;
                foreach (var node in resources)
                {
                    Step();
                    if (node.Fixed || node.Cell != wanted) continue;
                    foreach (var alternative in cells)
                    {
                        Step();
                        if (path.Contains(alternative)) continue;
                        bool all = true;
                        foreach (var pair in node.NewUnits)
                        { Step(); if (pair.Value > 0 && !demands[pair.Key].Cells.Contains(alternative.Value.Key)) { all = false; break; } }
                        if (!all || !MakeVacancy(alternative, path)) continue;
                        wanted.FreeSlots++; alternative.FreeSlots--; node.Cell = alternative;
                        path.Remove(wanted); return true;
                    }
                }
                path.Remove(wanted); return false;
            }
        }
    }
}
