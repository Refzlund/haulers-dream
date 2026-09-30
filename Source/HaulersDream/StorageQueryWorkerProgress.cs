using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using HaulersDream.Core;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    // Replacement is enabled only for the exact reviewed original method body and
    // the existing composition allowlist. Unknown worker patches run their own path.
    [HarmonyPatch(typeof(StoreUtility), "TryFindBestBetterStoreCellForWorker")]
    internal static class Patch_StorageQuery_WorkerProgress
    {
        internal static readonly MethodInfo Target = AccessTools.Method(typeof(StoreUtility), "TryFindBestBetterStoreCellForWorker");
        internal static readonly bool ExactBody = MatchesBody();
        private static bool MatchesBody()
        {
            try
            {
                if (Target?.Module.ModuleVersionId != new Guid("61e41735-6189-4da4-9d21-0260257b5097")) return false;
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(Target.GetMethodBody().GetILAsByteArray())).Replace("-", "")
                        == "E72ACCACAE9C8A4E2AAE866010942D0B550C6EB954E46CA6EAD19CB4B18BBB6B";
            }
            catch { return false; }
        }
        private static bool Prefix(Thing __0, Pawn __1, Map __2, Faction __3, ISlotGroup __4, bool __5,
            ref IntVec3 __6, ref float __7, ref StoragePriority __8,
            out StorageCommitments.StorageAdmissionQueryScope.WorkerCall __state)
        {
            __state = StorageCommitments.BeginProgressWorker(__0, __1, __2, __6, __7, __8);
            bool replaced = ExactBody && StorageCommitments.TryProgressWorker(__0, __1, __2, __3, __4, __5,
                ref __6, ref __7, ref __8);
            __state?.Disposition(replaced);
            return !replaced;
        }
        [HarmonyPriority(Priority.Last)]
        private static void Finalizer(Exception __exception, IntVec3 __6, float __7, StoragePriority __8,
            StorageCommitments.StorageAdmissionQueryScope.WorkerCall __state)
            => __state?.Finish(__exception, __6, __7, __8);

    }

    internal static partial class StorageCommitments
    {
        internal static StorageAdmissionQueryScope.WorkerCall BeginProgressWorker(Thing thing, Pawn pawn, Map map,
            IntVec3 closest, float distance, StoragePriority priority)
            => new StorageAdmissionQueryScope.WorkerCall(currentStorageQuery, map, closest, distance, priority);

        internal static bool TryProgressWorker(Thing thing, Pawn pawn, Map map, Faction faction, ISlotGroup nativeGroup,
            bool accurate, ref IntVec3 closest, ref float distance, ref StoragePriority priority)
            => currentStorageQuery != null && currentStorageQuery.TryWorker(thing, pawn, map, faction,
                nativeGroup, accurate, ref closest, ref distance, ref priority);

        internal sealed partial class StorageAdmissionQueryScope
        {
            private IntVec3? workerSelection;
            private int originalWorkerDepth;
            internal sealed class WorkerCall
            {
                private readonly StorageAdmissionQueryScope scope;
                private readonly Map map;
                private readonly IntVec3 beforeCell;
                private readonly float beforeDistance;
                private readonly StoragePriority beforePriority;
                private StorageProgressWork.Operation fallback;
                private IDisposable frame;
                private bool replaced, entered, finished;
                internal WorkerCall(StorageAdmissionQueryScope scope, Map map, IntVec3 cell, float distance, StoragePriority priority)
                { this.scope = scope; this.map = map; beforeCell = cell; beforeDistance = distance; beforePriority = priority; }
                internal void Disposition(bool replacement)
                {
                    replaced = replacement;
                    if (replaced) return;
                    // Exact scope ownership, including nested original workers. Excluded paths
                    // must not borrow a caller's optional certificate or its map account.
                    if (scope?.factory == true && StorageProgressWork.ActiveFor(map)?.Lane == StorageWorkLane.Mandatory) return;
                    fallback = StorageProgressWork.Begin(map, null, StorageWorkLane.Mandatory, "original-worker");
                    frame = StorageProgressWork.Enter(fallback);
                    if (scope != null) scope.originalWorkerDepth++;
                    entered = true;
                }
                internal void Finish(Exception failure, IntVec3 cell, float distance, StoragePriority priority)
                {
                    if (finished) return; finished = true;
                    try
                    {
                        if (!replaced && failure == null && scope != null && !scope.disposed
                            && (cell != beforeCell || distance != beforeDistance || priority != beforePriority))
                            scope.workerSelection = null;
                    }
                    finally
                    {
                        if (entered && scope != null) scope.originalWorkerDepth--;
                        frame?.Dispose(); fallback?.Dispose();
                    }
                }
            }
            private readonly Dictionary<ISlotGroup, StorageProgressWork.Operation> groupWork
                = new Dictionary<ISlotGroup, StorageProgressWork.Operation>();
            private StorageProgressWork.Operation factoryWork;
            private IDisposable factoryMeter;
            private StorageProgressWork.Operation WorkFor(ISlotGroup group)
            {
                if (factoryWork != null) return factoryWork;
                if (groupWork.TryGetValue(group, out var operation)) return operation;
                operation = StorageProgressWork.Begin(map, group, StorageWorkLane.Optional, "native-worker-search");
                if (operation != null) groupWork.Add(group, operation);
                return operation;
            }
            internal bool TryWorker(Thing cargo, Pawn carrier, Map world, Faction faction, ISlotGroup nativeGroup,
                bool accurate, ref IntVec3 closest, ref float distance, ref StoragePriority priority)
            {
                if (factory || nativeGroup == null || !StorageProgressWork.PersistentAllowed || !Applies(carrier, world, cargo)) return false;
                var group = nativeGroup is SlotGroup concrete ? StorageAllocationObservation.Canonical(concrete) : nativeGroup;
                List<IntVec3> coordinates;
                int total;
                int nextMember, nextCell;
                // Classification is added optional work, charged to the same map/tick
                // allowance as replacement. No allowance or an incomplete classification
                // runs the original worker; only a positive inspection permits suppression.
                try
                {
                using (var gate = StorageProgressWork.Begin(map, group, StorageWorkLane.Optional, "worker-compatibility"))
                using (StorageProgressWork.Enter(gate))
                {
                    if (gate == null || !SupportedComposition() || !StorageQueryBindings.NativeGroup(nativeGroup)) return false;
                    int oldMember = gate.Entry.WorkerMember, oldCell = gate.Entry.WorkerCell;
                    try
                    {
                        coordinates = WorkerCoordinates(gate, group, nativeGroup, out total);
                        nextMember = gate.Entry.WorkerMember; nextCell = gate.Entry.WorkerCell;
                    }
                    finally { gate.Entry.WorkerMember = oldMember; gate.Entry.WorkerCell = oldCell; }
                    if (coordinates == null) return false;
                    foreach (var cell in coordinates)
                    {
                        if (!NativeWorkerResidents(world.thingGrid.ThingsListAt(cell))) return false;
                    }
                }
                }
                catch (StorageWorkExhausted) { return false; } // Unknown shape/cost keeps original semantics.
                var work = WorkFor(group);
                if (work == null)
                { StorageProgressWork.NeedDiscovery(map, group, "worker-allowance"); return true; } // Positively inspected path only.
                using (StorageProgressWork.Enter(work))
                try
                {
                    if (!Current()) return true;
                    StorageProgressWork.Charge(StorageWorkKind.Predicate);
                    if (!nativeGroup.Settings.AllowedToAccept(cargo)) return true;
                    IntVec3 origin = cargo.SpawnedOrAnyParentSpawned ? cargo.PositionHeld : carrier.PositionHeld;
                    work.Entry.WorkerMember = nextMember; work.Entry.WorkerCell = nextCell;
                    foreach (var cell in coordinates)
                    {
                        var grid = world.thingGrid.ThingsListAt(cell);
                        if (grid.Count > work.Token.Remaining(StorageWorkKind.Grid))
                        {
                            work.Entry.HeavyCell = world.cellIndices.CellToIndex(cell);
                            work.Entry.Remember(work.Entry.HeavyCell.Value, 201);
                        }
                        // Separate upper allowance for the native predicate's own grid scans.
                        StorageProgressWork.Charge(StorageWorkKind.Grid, grid.Count);
                    }
                    // Same single RNG draw, scaled to the original native cardinality.
                    int minimum = accurate ? Mathf.FloorToInt(total * Rand.Range(0.005f, 0.018f)) : 0;
                    bool foundHere = false;
                    for (int i = 0; i < coordinates.Count; i++)
                    {
                        StorageProgressWork.Charge(StorageWorkKind.Native);
                        IntVec3 cell = coordinates[i];
                        float nextDistance = (origin - cell).LengthHorizontalSquared;
                        if (!(nextDistance > distance) && StoreUtility.IsGoodStoreCell(cell, world, cargo, carrier, faction))
                        {
                            closest = cell; distance = nextDistance; priority = nativeGroup.Settings.Priority;
                            workerSelection = cell; foundHere = true;
                            if (i >= minimum) break;
                        }
                    }
                    if (!foundHere && coordinates.Count < total)
                        StorageProgressWork.NeedDiscovery(map, group, "worker-discovery");
                    return true;
                }
                catch (StorageWorkExhausted exhausted)
                {
                    work.Entry.Larger = true; work.Entry.Reason = "worker-work:" + exhausted.Kind;
                    StorageProgressWork.NeedDiscovery(map, group, work.Entry.Reason); return true;
                }
            }

            private static bool NativeWorkerResidents(List<Thing> grid)
            {
                StorageProgressWork.Charge(StorageWorkKind.Grid, grid.Count);
                foreach (var resident in grid)
                {
                    StorageProgressWork.Charge(StorageWorkKind.RawGuard);
                    if (resident == null || resident.GetType().Assembly != typeof(Thing).Assembly
                        || resident.def.category == ThingCategory.Item && !StorageQueryBindings.NativeSubject(resident)) return false;
                }
                return true;
            }

            private static List<IntVec3> WorkerCoordinates(StorageProgressWork.Operation work, ISlotGroup group,
                ISlotGroup nativeGroup, out int total)
            {
                total = 0;
                var entry = work.Entry;
                var members = work.Owner.Members(group, entry);
                if (members.Count == 0) return null; // A retired group must not be re-enqueued by WorkFor.
                var lists = new List<List<IntVec3>>();
                var ordinals = new List<int>();
                for (int ordinal = 0; ordinal < members.Count; ordinal++)
                {
                    var member = members[ordinal];
                    // Worker calls for a concrete linked member retain that member's subset.
                    if (nativeGroup is SlotGroup selected && !ReferenceEquals(member, selected)) continue;
                    var list = member.CellsList;
                    if (list.Count > int.MaxValue - total) { entry.Reason = "topology-cardinality"; return null; }
                    total += list.Count; lists.Add(list); ordinals.Add(ordinal);
                }
                var result = new List<IntVec3>(); var seen = new HashSet<IntVec3>();
                foreach (int index in entry.Hints)
                {
                    if (result.Count >= 100) break; // Always retain an ordinary discovery share.
                    var cell = work.Owner.Map.cellIndices.IndexToCell(index);
                    var slot = work.Owner.Map.haulDestinationManager.SlotGroupAt(cell);
                    if (slot == null || !ReferenceEquals(StorageAllocationObservation.Canonical(slot), group)
                        || nativeGroup is SlotGroup selected && !ReferenceEquals(slot, selected)) continue;
                    StorageProgressWork.Charge(StorageWorkKind.Topology);
                    if (seen.Add(cell)) result.Add(cell);
                }
                if (lists.Count == 0) return result;
                int memberIndex = ordinals.IndexOf(entry.WorkerMember);
                int cellIndex = memberIndex < 0 ? 0 : entry.WorkerCell;
                if (memberIndex < 0) memberIndex = 0;
                int scanned = 0, emptyMembers = 0;
                while (scanned < total && result.Count < 200)
                {
                    var list = lists[memberIndex];
                    if (cellIndex >= list.Count)
                    {
                        memberIndex = (memberIndex + 1) % lists.Count; cellIndex = 0;
                        if (++emptyMembers > lists.Count && total == 0) break;
                        continue;
                    }
                    StorageProgressWork.Charge(StorageWorkKind.Topology);
                    var cell = list[cellIndex++]; scanned++;
                    if (seen.Add(cell)) result.Add(cell);
                }
                entry.WorkerMember = ordinals[memberIndex]; entry.WorkerCell = cellIndex;
                return result;
            }
        }
    }
}
