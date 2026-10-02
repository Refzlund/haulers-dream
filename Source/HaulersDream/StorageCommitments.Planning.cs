using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    internal static partial class StorageCommitments
    {
        // One discarded-or-returned planning proposal. These exact parcel requests are not claims:
        // every admission/revalidation observes the world and the authoritative ledger again.
        internal sealed class StoragePlanning
        {
            private sealed class GroupPlan
            {
                internal readonly ISlotGroup Group;
                internal readonly List<StorageAllocationObservationDemand> Requests = new List<StorageAllocationObservationDemand>();
                internal readonly List<IntVec3> PreferredCells = new List<IntVec3>();
                internal GroupPlan(ISlotGroup group) { Group = group; }
            }

            private readonly Pawn pawn;
            private readonly Map map;
            private readonly StorageFilterContext context;
            private readonly Thing forcedPrimary;
            private readonly object owner = new object();
            private readonly List<GroupPlan> groups = new List<GroupPlan>();
            private readonly List<Thing> sources = new List<Thing>();
            private readonly List<int> counts = new List<int>();

            internal StoragePlanning(Pawn pawn, Thing forcedPrimary = null)
            {
                this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
                map = pawn.Map; context = StorageBuildingFilter.CurrentContext;
                this.forcedPrimary = forcedPrimary is Corpse ? null : forcedPrimary;
            }

            internal bool TryAdd(Thing subject, IntVec3 preferredCell, int desired,
                StoragePriority priorityFloor, out int admitted)
            {
                admitted = 0;
                if (context != StorageBuildingFilter.CurrentContext || !SourceAvailable(subject, desired)
                    || !preferredCell.IsValid || !preferredCell.InBounds(map)
                    || sources.Contains(subject)) return false;
                var group = BulkHaul.BudgetGroupOf(map.haulDestinationManager.SlotGroupAt(preferredCell));
                if (group == null) return false;
                GroupPlan plan = null;
                foreach (var candidate in groups)
                    if (ReferenceEquals(candidate.Group, group)) { plan = candidate; break; }
                plan = plan ?? new GroupPlan(group);
                var requested = new StorageAllocationObservationDemand(owner, subject, subject, pawn, desired,
                    context, priorityFloor: priorityFloor,
                    requireBetterPriority: true, startsRefill: true);
                // A native search's selected cell is a useful first observation, not a license to
                // assign every parcel the first def's filters or the first cell's stack compatibility.
                var preferred = new List<IntVec3>(plan.PreferredCells);
                if (!preferred.Contains(preferredCell)) preferred.Add(preferredCell);
                var status = UnitsFor(plan, requested, plan.Requests, preferred, out int allowed);
                if (status != ResourceAllowance.Observed || allowed <= 0) return false;
                admitted = Math.Min(desired, allowed);
                plan.Requests.Add(new StorageAllocationObservationDemand(owner, subject, subject, pawn, admitted,
                    requested.Context, priorityFloor: priorityFloor, requireBetterPriority: true, startsRefill: true));
                if (!plan.PreferredCells.Contains(preferredCell)) plan.PreferredCells.Add(preferredCell);
                if (!groups.Contains(plan)) groups.Add(plan);
                Remember(subject, admitted);
                return true;
            }

            private ResourceAllowance UnitsFor(GroupPlan plan, StorageAllocationObservationDemand requested,
                IReadOnlyList<StorageAllocationObservationDemand> preceding, IReadOnlyList<IntVec3> preferred, out int allowed)
            {
                StorageAllocationObservationDemand primary = requested.Subject == forcedPrimary ? requested : null;
                if (primary == null && forcedPrimary != null)
                    foreach (var candidate in plan.Requests)
                        if (candidate.Subject == forcedPrimary) { primary = candidate; break; }
                return primary == null ? ResourceUnitsFor(pawn, plan.Group, requested, preceding, preferred, out allowed)
                    : ForcedStorageUnitsFor(pawn, plan.Group, primary, requested, preceding, preferred, out allowed, out _);
            }

            // True containers keep their existing native/enroute policy. Recording the source here
            // only ties the cached proposal to the exact queue; it grants no slot-group capacity.
            internal void RememberContainer(Thing subject, int units) => Remember(subject, units);

            private void Remember(Thing subject, int units)
            { sources.Add(subject); counts.Add(units); }

            private bool SourceAvailable(Thing subject, int units)
                => map != null && pawn.Map == map && subject != null && !subject.Destroyed
                    && subject.Spawned && subject.Map == map && units > 0 && subject.stackCount >= units;

            internal bool Matches(Job job)
            {
                var queue = job?.targetQueueB;
                var quantities = job?.countQueue;
                if (queue == null || quantities == null || queue.Count != sources.Count || quantities.Count != counts.Count)
                    return false;
                for (int i = 0; i < sources.Count; i++)
                    if (!ReferenceEquals(queue[i].Thing, sources[i]) || quantities[i] != counts[i]
                        || !SourceAvailable(sources[i], counts[i])) return false;
                return true;
            }

            internal bool Validate()
            {
                if (context != StorageBuildingFilter.CurrentContext) return false;
                for (int i = 0; i < sources.Count; i++)
                    if (!SourceAvailable(sources[i], counts[i])) return false;
                foreach (var plan in groups)
                {
                    int last = plan.Requests.Count - 1;
                    if (last < 0) continue;
                    var preceding = new List<StorageAllocationObservationDemand>(last);
                    for (int i = 0; i <= last; i++)
                    {
                        var parcel = plan.Requests[i];
                        if (StoreUtility.CurrentStoragePriorityOf(parcel.Subject) != parcel.PriorityFloor) return false;
                        if (i < last) preceding.Add(parcel);
                    }
                    var requested = plan.Requests[last];
                    var status = UnitsFor(plan, requested, preceding, plan.PreferredCells, out int allowed);
                    if (status != ResourceAllowance.Observed || allowed < requested.Units) return false;
                }
                return true;
            }
        }
    }
}
