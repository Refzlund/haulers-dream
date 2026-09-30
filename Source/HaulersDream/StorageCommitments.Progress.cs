using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal static partial class StorageCommitments
    {
        private static long AdmittedResourceUnits(StorageAllocationResult result, StorageAllocationRequest request)
        {
            // Core's result accessor performs a full slice scan outside Engine.Work.
            StorageProgressWork.Charge(StorageWorkKind.ObservedEligibility, result.State.Slices.Count);
            return result.AdmittedUnits(request);
        }
        private static StorageAllocationResult AllocateResources(IReadOnlyList<StorageAllocationCell> cells,
            StorageAllocationState baseline, IReadOnlyList<StorageAllocationRequest> requests,
            Func<object, object, bool> compatible, Func<StorageResourceAllocation, bool> eligible,
            StorageAllocationOptions options)
        {
            var work = StorageProgressWork.Active;
            if (work == null) return StorageResourceAllocator.Allocate(cells, baseline, requests, compatible, eligible, options);
            int maximum = work.Lane == StorageWorkLane.Mandatory ? 100000
                : (int)Math.Min(100000, work.Token.Remaining(StorageWorkKind.Matching));
            if (maximum <= 0) throw new StorageWorkExhausted(StorageWorkKind.Matching);
            var charge = work.Token.ReserveMatching(maximum);
            if (charge == null) throw new StorageWorkExhausted(StorageWorkKind.Matching);
            work.Attempts[(int)StorageWorkKind.Matching] += maximum;
            // An escaping compatibility exception keeps the conservative reservation.
            var result = StorageResourceAllocator.Allocate(cells, baseline, requests, compatible, eligible,
                new StorageAllocationOptions(options.ObservationComplete, maximum, 4096,
                    work.Lane == StorageWorkLane.Mandatory || work.Entry?.Larger == true ? 512 : 256));
            charge.Returned(result.Work);
            work.Attempts[(int)StorageWorkKind.Matching] -= maximum - result.Work;
            if (result.Status == StorageAllocationStatus.BudgetExhausted && work.Entry != null)
            { work.Entry.Larger = true; work.Entry.Reason = "matching-envelope"; }
            return result;
        }

        private static bool FreshProgress(StorageAllocationObservationResult observation, ISlotGroup group = null, bool copyCursor = true)
        {
            bool current = observation.StillCurrent();
            var work = StorageProgressWork.Active;
            work?.Owner.ObserveProgress(group == null ? work.Entry : work.Owner.Queue.Get(group), observation, copyCursor);
            return current;
        }

        internal static void ContinueStorageGroup(StorageMapWork owner, StorageProgressQueue.Entry entry, ISlotGroup group)
        {
            if (!UnityData.IsInMainThread || ResourceQueriesBlocked() || resourceTransferDepth > 0 || StorageAllocationObservation.InProgress) return;
            long requestVersion = entry.RequestVersion;
            using (var work = StorageProgressWork.Begin(owner.Map, group, StorageWorkLane.Fair, "continuation"))
            {
                if (work == null) return;
                using (StorageProgressWork.Enter(work))
                try
                {
                    var members = owner.Members(group, entry);
                    if (members.Count == 0) return;
                    var snapshot = ObserveResourceResponsibilities(owner.Map);
                    if (!snapshot.Current) return;
                    var demands = new List<StorageAllocationObservationDemand>();
                    foreach (var portion in snapshot.Portions)
                    {
                        StorageProgressWork.Charge(StorageWorkKind.Attribution);
                        if (ReferenceEquals(portion.Row.Group, group)) demands.Add(PriorResourceDemand(portion));
                    }
                    var observation = StorageAllocationObservation.Observe(owner.Map, group, demands,
                        StorageProgressWork.Limits(entry), owner.Hints(entry));
                    owner.ObserveProgress(entry, observation);
                    foreach (var issue in observation.Issues)
                        if (issue.Status == StorageAllocationObservationStatus.Unsupported)
                        { owner.Queue.Complete(group, requestVersion); return; }
                    if (!observation.CertifiedSubset || observation.Requests.Count != demands.Count || !snapshot.Current) return;
                    var allocation = AllocateResources(observation.Cells, StorageAllocationState.Empty, observation.Requests,
                        CompatibleResource, observation.ObservedEligible, new StorageAllocationOptions(observation.Complete));
                    if (!allocation.CanPublish) { entry.Larger = true; entry.Reason = "matching-envelope"; return; }
                    foreach (var request in observation.Requests)
                        if (AdmittedResourceUnits(allocation, request) != request.Units)
                        {
                            // Useful observed coordinates can seed the next fresh full solve;
                            // no partial allocation or eligibility survives this operation.
                            owner.Remember(entry, observation.CellLocations, allocation.State);
                            if (!observation.Complete) entry.Larger = true;
                            else owner.Queue.Complete(group, requestVersion); // A full fresh scan has no further coordinates to discover.
                            entry.Reason = "prior-not-yet-covered"; return;
                        }
                    using var finalPass = work.FinalPass();
                    if (!observation.StillCurrent() || !snapshot.Current) { owner.ObserveProgress(entry, observation); return; }
                    owner.Remember(entry, observation.CellLocations, allocation.State);
                    // Capacity/edges/state leave this stack frame. Only coordinate hints survive.
                    entry.Reason = observation.Complete ? "fresh-priors-complete-topology" : "fresh-priors-certified-subset";
                    // This completes a discovery request, not a capacity certificate. A later
                    // query reconstructs all obligations and can wake discovery again.
                    owner.Queue.Complete(group, requestVersion);
                }
                catch (StorageWorkExhausted exhausted) { entry.Larger = true; entry.Reason = "map-work:" + exhausted.Kind; }
            }
        }
    }
}
