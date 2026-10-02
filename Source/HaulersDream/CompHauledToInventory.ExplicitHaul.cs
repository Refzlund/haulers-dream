using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    public partial class CompHauledToInventory : IThingHolder
    {
        private List<ExplicitHaulOrder> explicitHaulOrders = new List<ExplicitHaulOrder>();
        private long nextExplicitHaulId = 1;
        private ThingOwner<Thing> explicitRecovery;
        internal ThingOwner<Thing> ExplicitRecovery => explicitRecovery ??= new ThingOwner<Thing>(this);
        public ThingOwner GetDirectlyHeldThings() => ExplicitRecovery;
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, ExplicitRecovery);
            if (refuelRecoveryCustody != null) outChildren.Add(refuelRecoveryCustody);
        }
        internal IReadOnlyList<ExplicitHaulOrder> ExplicitOrders => explicitHaulOrders;

        internal ExplicitHaulOrder ExplicitOrder(long id)
        {
            var found = explicitHaulOrders.Where(o => o != null && o.id == id).Take(2).ToArray();
            return found.Length == 1 ? found[0] : null;
        }
        internal ExplicitHaulOrder ExplicitOrder(Job job)
        {
            if (!ExplicitHaulCommand.IsJob(job)) return null;
            var found = explicitHaulOrders.Where(o => o != null && o.jobId == job.loadID).Take(2).ToArray();
            return found.Length == 1 ? found[0] : null;
        }
        internal ExplicitHaulOrder NewExplicitOrder(Thing source, IntVec3 cell, int total, Building_Storage shelf = null)
        {
            if (nextExplicitHaulId <= 0 || nextExplicitHaulId == long.MaxValue) return null;
            var order = new ExplicitHaulOrder { id = nextExplicitHaulId++, source = source,
                sourceId = source.thingIDNumber, mapId = parent.Map.uniqueID, requested = total,
                destination = cell, state = ExplicitHaulState.Queued,
                sourceForbiddenAtIssue = source.IsForbidden((Pawn)parent),
                destinationKind = shelf == null ? ExplicitHaulDestination.BareCell : ExplicitHaulDestination.Shelf,
                shelf = shelf, shelfId = shelf?.thingIDNumber ?? -1,
                shelfPosition = shelf?.Position ?? IntVec3.Invalid, shelfRotation = shelf?.Rotation.AsInt ?? 0,
                tripDestination = cell };
            explicitHaulOrders.Add(order);
            return order;
        }
        private void ExposeExplicitOrders()
        {
            Scribe_Values.Look(ref nextExplicitHaulId, "hdNextExplicitHaulId", 1L);
            Scribe_Collections.Look(ref explicitHaulOrders, "hdExplicitHaulOrders", LookMode.Deep);
            Scribe_Deep.Look(ref explicitRecovery, "hdExplicitHaulRecovery", this);
            explicitHaulOrders ??= new List<ExplicitHaulOrder>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                ExplicitHaulLifecycle.RegisterLoaded(this);
        }
        internal void ReconcileExplicitOrders()
        {
            if (Scribe.mode != LoadSaveMode.Inactive) return;
            var pawn = parent as Pawn;
            var jobs = new List<Job>();
            if (pawn?.CurJob != null) jobs.Add(pawn.CurJob);
            if (pawn?.jobs?.jobQueue != null) jobs.AddRange(pawn.jobs.jobQueue.Where(q => q?.job != null).Select(q => q.job));
            foreach (var order in explicitHaulOrders.Where(o => o != null))
            {
                if (order.id >= nextExplicitHaulId) nextExplicitHaulId = order.id == long.MaxValue ? long.MaxValue : order.id + 1;
                if (order.Terminal) continue;
                bool invalid = order.id <= 0 || order.requested <= 0 || order.delivered < 0 || order.delivered > order.requested
                    || order.tripUnits < 0 || order.tripUnits > order.Remaining || !Enum.IsDefined(typeof(ExplicitHaulState), order.state)
                    || !Enum.IsDefined(typeof(ExplicitHaulDestination), order.destinationKind)
                    || explicitHaulOrders.Count(o => o?.id == order.id) != 1;
                int links = jobs.Count(j => ExplicitHaulCommand.IsJob(j) && j.loadID == order.jobId);
                if (invalid || links != 1 || explicitHaulOrders.Count(o => o?.jobId == order.jobId) != 1)
                { order.Block(invalid ? "Invalid saved order" : "Saved job link missing or ambiguous"); order.jobId = -1; }
            }
        }
    }
}
