using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The "Bulk unload all" flag ledger (partial of <see cref="HaulersDreamGameComponent"/>): a scribed set of
    /// transporter thing IDs the player flagged via the new gizmo. While a transporter is flagged, HD's
    /// autonomous <see cref="WorkGiver_BulkUnloadTransporters"/> keeps handing its hold to hauling pawns, one
    /// backpack-filling visit each (<see cref="JobDriver_UnloadTransporterInBulk"/>), repeated across trips and
    /// haulers until nothing pullable remains, when the driver clears the flag itself.
    ///
    /// <para>IDs rather than Thing references: a landed shuttle can despawn mid-flag (launch), and a reference
    /// would pin it; a stale ID simply never matches a spawned transporter again (the scanner resolves IDs against
    /// <c>listerThings</c>), and the tick prune drops it from the set. Player-initiated writes go through
    /// <see cref="MultiplayerCompat.SetBulkUnloadAll"/>, never through here directly.</para>
    /// </summary>
    public partial class HaulersDreamGameComponent
    {
        // Thing ID numbers of transporters flagged for autonomous bulk unloading. Lazily created (a save from
        // before this feature has no entry); Look with an init-on-null after load.
        private HashSet<int> bulkUnloadAllIds;

        // MUTUAL EXCLUSION bookkeeping: transporter GROUP IDs with a live LOAD session ("Set to load" pressed,
        // cargo still being gathered). Vanilla's InitiateLoading only stamps groupIDs, the loading lord exists
        // only when BOARDING PAWNS were designated, and both groupID and leftToLoad survive landing on shuttles —
        // so no vanilla field answers "is a load session active?" for the pure-cargo case. We record the birth
        // (Patch_TransporterLoadSessions.InitiateLoading postfix) and death (CompTransporter.TryRemoveLord postfix
        // — the funnel for cancel-load, launch teardown, and unload-drain end) ourselves. Scribed like the flags.
        private HashSet<int> loadSessionGroupIds;

        /// <summary>Flag (or unflag) a transporter for autonomous bulk unloading. Idempotent; safe on any client
        /// context because the only caller is the MP-synced <see cref="MultiplayerCompat.SetBulkUnloadAll"/>
        /// (single-player: that method runs directly).</summary>
        internal void BulkUnloadAllSet(int thingIdNumber, bool on)
        {
            if (thingIdNumber <= 0)
                return;
            if (on)
            {
                bulkUnloadAllIds ??= new HashSet<int>();
                bulkUnloadAllIds.Add(thingIdNumber);
            }
            else
                bulkUnloadAllIds?.Remove(thingIdNumber);
        }

        internal bool BulkUnloadAllActive(int thingIdNumber)
            => thingIdNumber > 0 && bulkUnloadAllIds != null && bulkUnloadAllIds.Contains(thingIdNumber);

        /// <summary>Record (or end) a load session for a transporter GROUP. Called from the deterministic
        /// InitiateLoading/TryRemoveLord postfixes, which replay identically on every multiplayer client, so these
        /// direct writes stay desync-free without synced-command machinery.</summary>
        internal void LoadSessionSet(int groupID, bool on)
        {
            if (groupID < 0)
                return;
            if (on)
            {
                loadSessionGroupIds ??= new HashSet<int>();
                loadSessionGroupIds.Add(groupID);
            }
            else
                loadSessionGroupIds?.Remove(groupID);
        }

        internal bool LoadSessionActive(int groupID)
            => groupID >= 0 && loadSessionGroupIds != null && loadSessionGroupIds.Contains(groupID);

        // Drop recorded groups whose transporters no longer exist spawned anywhere (launched away / destroyed).
        // Same rare-scale, slow-cadence shape as the flag prune.
        private void PruneLoadSessionGroupIds()
        {
            if (loadSessionGroupIds == null || loadSessionGroupIds.Count == 0)
                return;
            var maps = Find.Maps;
            if (maps == null)
                return;
            loadSessionGroupIds.RemoveWhere(groupID =>
            {
                for (int m = 0; m < maps.Count; m++)
                {
                    var things = maps[m]?.listerThings?.ThingsInGroup(ThingRequestGroup.Transporter);
                    if (things == null)
                        continue;
                    for (int i = 0; i < things.Count; i++)
                    {
                        var comp = things[i]?.TryGetComp<CompTransporter>();
                        if (comp != null && comp.groupID == groupID)
                            return false; // session's transporter still around — keep
                    }
                }
                return true; // no living member — the session is over
            });
        }

        /// <summary>The driver's finalize calls this: once the hold holds nothing the feature can pull (no stacks,
        /// or passengers only), the flag has done its job and is cleared so the workgiver scan goes quiet.
        /// Pullability comes from the one shared rule (<see cref="BulkUnloadTransporterGate.HasPullableContents"/>),
        /// the same predicate the gizmo's disabled-reason and the float-menu offer use.</summary>
        internal void BulkUnloadAllClearIfNothingPullable(CompTransporter comp)
        {
            if (comp?.parent == null || !BulkUnloadAllActive(comp.parent.thingIDNumber))
                return;
            if (BulkUnloadTransporterGate.HasPullableContents(comp))
                return; // still something to pull, leave the flag up
            bulkUnloadAllIds?.Remove(comp.parent.thingIDNumber);
        }

        // Drop IDs whose transporter no longer exists spawned anywhere (launched away / destroyed / minified while
        // flagged). Rare player-action-scale set, scanned on a slow cadence, O(ids × map transporters).
        private void PruneBulkUnloadAllIds()
        {
            if (bulkUnloadAllIds == null || bulkUnloadAllIds.Count == 0)
                return;
            var maps = Find.Maps;
            if (maps == null)
                return;
            bulkUnloadAllIds.RemoveWhere(id =>
            {
                for (int m = 0; m < maps.Count; m++)
                {
                    var things = maps[m]?.listerThings?.ThingsInGroup(ThingRequestGroup.Transporter);
                    if (things == null)
                        continue;
                    for (int i = 0; i < things.Count; i++)
                        if (things[i] != null && things[i].thingIDNumber == id)
                            return false; // still around, keep
                }
                return true; // nowhere spawned, drop
            });
        }

        // THE FLAG JANITOR — the tick pass that keeps "Bulk unload all" flags honest, clearing a flag when either
        // end-state arrives WITHOUT a driver finalize to do it (the finalize's own clear only fires if a visit
        // actually ran):
        //   1. A load owns the hold (ConflictActive || LoadSessionHasOpenManifest — the EXACT test the toggle's
        //      grey-out uses, and nothing stronger: after the manifest drains ConflictActive goes false even though
        //      vanilla's loading LORD lingers until launch, and clearing on that alone made the toggle flip itself
        //      back off within seconds of being switched on, the reported self-toggling).
        //   2. Nothing pullable remains — including paths that never involve a visit: the player destroying the
        //      remaining hold contents via the Contents-tab X, a passenger-only remainder, or an all-forbidden
        //      hold. Without this arm, a flag on such a hold sat ON forever: the toggle greyed "nothing to
        //      unload" AND the mutual exclusion kept vanilla's Set-to-load locked (the reported deadlock).
        // Runs beside the prune on the same slow cadence; reads only synced world state, so every client removes
        // exactly the same ids, no multiplayer desync, no synced-command machinery needed.
        private void ClearBulkUnloadFlagsUnderLoadFlow()
        {
            if (bulkUnloadAllIds == null || bulkUnloadAllIds.Count == 0)
                return;
            var maps = Find.Maps;
            if (maps == null)
                return;
            List<int> toClear = null; // collect first, never mutate the set mid-enumeration
            foreach (var id in bulkUnloadAllIds)
            {
                for (int m = 0; m < maps.Count; m++)
                {
                    var things = maps[m]?.listerThings?.ThingsInGroup(ThingRequestGroup.Transporter);
                    if (things == null)
                        continue;
                    bool found = false;
                    for (int i = 0; i < things.Count; i++)
                    {
                        var comp = things[i]?.TryGetComp<CompTransporter>();
                        if (comp == null || comp.parent.thingIDNumber != id)
                            continue;
                        found = true;
                        if (BulkUnloadTransporterGate.ConflictActive(comp)
                            || BulkUnloadTransporterGate.LoadSessionHasOpenManifest(comp)
                            || !BulkUnloadTransporterGate.HasPullableContents(comp))
                            (toClear ??= new List<int>()).Add(id);
                        break;
                    }
                    if (found)
                        break;
                }
            }
            if (toClear == null)
                return;
            for (int i = 0; i < toClear.Count; i++)
                bulkUnloadAllIds.Remove(toClear[i]);
        }

        // The flag-ledger scribing (additive to base.ExposeData via ExposeData() -> ExposeTransporterUnloads(),
        // appended after questPawnSnapshots like every subsystem before it).
        private void ExposeTransporterUnloads()
        {
            Scribe_Collections.Look(ref bulkUnloadAllIds, "haulersDreamBulkUnloadAllIds", LookMode.Value);
            if (bulkUnloadAllIds == null)
                bulkUnloadAllIds = new HashSet<int>();
            Scribe_Collections.Look(ref loadSessionGroupIds, "haulersDreamLoadSessionGroups", LookMode.Value);
            if (loadSessionGroupIds == null)
                loadSessionGroupIds = new HashSet<int>();
        }
    }
}
