using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    // Save labels and feature intent adapted from nullpat's GH267 (4e2b4b34101512080864d19323dc0b18da250ae9).
    public partial class HaulersDreamGameComponent
    {
        private TransporterUnloadState transporterUnloads = new TransporterUnloadState();

        internal bool BulkUnloadAllActive(int thingId) => transporterUnloads.IsActive(thingId, BulkUnloadTransporterGate.Enabled);
        internal bool BulkUnloadAllFlagged(int thingId) => transporterUnloads.IsFlagged(thingId);
        internal bool TransporterLoadOwns(int groupId, bool manifest, bool liveCustody)
            => transporterUnloads.LoadOwns(groupId, manifest, liveCustody);

        // The MP-synced endpoint must call this, not write an integer flag from an earlier menu snapshot.
        // Off always remains available, including a disabled feature or a departing/empty target.
        internal bool TrySetBulkUnloadAll(Thing target, bool on)
        {
            if (target == null) return false;
            var comp = target.TryGetComp<CompTransporter>();
            if (on && (BulkUnloadTransporterGate.TargetBlock(comp, requireFlag: false) != TransporterUnloadBlock.None
                || !BulkUnloadTransporterGate.HasPullableContents(comp))) return false;
            if (transporterUnloads.SetFlag(target.thingIDNumber, on)) TransportLoad.ClearLoadWorkCache();
            return true;
        }

        internal void TransporterLoadAccepted(IEnumerable<CompTransporter> transporters, bool accepted)
        {
            if (!accepted || transporters == null) return;
            // A caller may supply CompTransporter's shared group list; nested group queries must not mutate it.
            foreach (var comp in new List<CompTransporter>(transporters))
            {
                if (comp?.parent == null || comp.groupID < 0) continue;
                // Snapshot before the next native group call; clearing a non-primary flag is mandatory.
                var ids = new List<int> { comp.parent.thingIDNumber };
                var group = comp.Map != null ? comp.TransportersInGroup(comp.Map) : null;
                for (int i = 0; group != null && i < group.Count; i++)
                    if (group[i]?.parent != null) ids.Add(group[i].parent.thingIDNumber);
                transporterUnloads.AcceptLoad(true, comp.groupID, ids);
            }
            // Even a top-up of the same group changes the manifest, so invalidate cached probes unconditionally.
            TransportLoad.ClearLoadWorkCache();
        }

        internal void TransporterLoadEnded(int groupId)
        {
            transporterUnloads.EndLoad(groupId);
            TransportLoad.ClearLoadWorkCache();
        }

        internal void BulkUnloadAllClearIfNothingPullable(CompTransporter comp)
        {
            if (comp?.parent != null && !BulkUnloadTransporterGate.HasPullableContents(comp)
                && transporterUnloads.SetFlag(comp.parent.thingIDNumber, false)) TransportLoad.ClearLoadWorkCache();
        }

        private void ReconcileTransporterUnloads()
        {
            if (!transporterUnloads.HasRecords) return;
            var ids = new HashSet<int>(); var groups = new HashSet<int>(); bool changed = false;
            foreach (var map in Find.Maps)
            {
                var things = map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter);
                for (int i = 0; i < things.Count; i++)
                {
                    var comp = things[i]?.TryGetComp<CompTransporter>();
                    if (comp?.parent == null) continue;
                    ids.Add(comp.parent.thingIDNumber); if (comp.groupID >= 0) groups.Add(comp.groupID);
                    if (transporterUnloads.IsFlagged(comp.parent.thingIDNumber)
                        && (!BulkUnloadTransporterGate.HasPullableContents(comp) || BulkUnloadTransporterGate.ConflictActive(comp)))
                        changed |= transporterUnloads.SetFlag(comp.parent.thingIDNumber, false);
                }
            }
            changed |= transporterUnloads.Prune(ids.Contains, groups.Contains);
            if (changed) TransportLoad.ClearLoadWorkCache();
        }

        private void ExposeTransporterUnloads()
        {
            var flags = transporterUnloads.CaptureFlags(); var sessions = transporterUnloads.CaptureSessions();
            Scribe_Collections.Look(ref flags, "haulersDreamBulkUnloadAllIds", LookMode.Value);
            Scribe_Collections.Look(ref sessions, "haulersDreamLoadSessionGroups", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars) transporterUnloads = new TransporterUnloadState(flags, sessions);
        }
    }
}
