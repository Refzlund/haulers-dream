using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// THE CONFLICT SEAM for transporter/shuttle BULK UNLOAD, the one place that answers "is anything loading
    /// INTO this transporter right now?" Every HD entry point that can empty a hold consults THIS method and no
    /// other: the float-menu offer (<see cref="FloatMenuOptionProvider_BulkUnloadTransporter"/>) and the driver's
    /// periodic yield check (<see cref="JobDriver_UnloadTransporterInBulk"/>). A per-site copy of this condition is
    /// what produced the reported bug, the offer originally checked ONLY vanilla's load lord
    /// (<c>TransporterUtility.FindLord</c>), so an HD bulk-load in flight (whose couriers run
    /// <see cref="HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk"/> under that lord's think tree, and whose
    /// claims live in the ledger rather than the lord) could be ordered to unload the very hold it was filling.
    ///
    /// <para>Four signals, cheapest first, any one of which means items are moving IN or being handled by their
    /// owner:</para>
    /// <list type="bullet">
    /// <item><description>A vanilla <c>LoadAndEnterTransporters</c> lord for this group (<c>FindLord</c>), 
    /// "Set to load" created it; its haulers and boarders run under it.</description></item>
    /// <item><description>An HD bulk-load courier targeting this group (<see
    /// cref="BulkLoadAntiConflict.AnyHdLoaderForGroup"/>), the authoritative "still loading" signal the boarding
    /// gate already uses.</description></item>
    /// <item><description>A vanilla <c>HaulToTransporter</c> job targeting this group (target B is the transporter
    /// parent), catches a plain vanilla loader even if the lord lookup ever misses.</description></item>
    /// <item><description>An in-flight vanilla <c>ShipJob_Unload</c> drain on the shuttle's ship parent, an
    /// arriving shuttle already ejecting its hold onto the floor one stack per second, with groupID still set and
    /// no lord, i.e. invisible to the first three checks.</description></item>
    /// </list>
    ///
    /// <para>Plus two ownership states: the transporter is despawned (nothing to walk to), or sits in a caravan
    /// (packing owns the hold). A STALE ready-to-launch group (groupID set, lord gone and no drain running, e.g.
    /// a parked player shuttle) is NOT a conflict: nothing is flowing, so removal conflicts with nothing.</para>
    ///
    /// <para>All reads are synced world state and no <c>Rand</c> is consumed, so the answer is identical on every
    /// multiplayer client. The driver re-checks this periodically rather than trusting the offer-time answer,
    /// because the state can change between the click and the visit (a save resumed mid-walk, a queued order,
    /// another player pressing "Set to load").</para>
    /// </summary>
    internal static class BulkUnloadTransporterGate
    {
        internal static bool ConflictActive(CompTransporter comp)
        {
            if (comp == null || comp.parent == null || !comp.parent.Spawned)
                return true;
            // Caravan packing owns the hold during gather/arrival.
            if (comp.parent.IsInCaravan())
                return true;
            // An ARRIVING shuttle's vanilla unloading may be ALREADY draining the hold, ShipJob_Unload drops one
            // stack every 60 ticks at the interaction cell, and that drain runs with groupID still set and NO
            // loading lord, i.e. exactly the "stale ready-to-launch" state the checks below treat as idle. So it
            // is asked explicitly, BEFORE the groupID early-out: joining an ITEM-dropping drain would just race it
            // (the hold mutates under us between select and transfer until one side empties). The RETURN-trip
            // drain on player shuttles is PAWNS-ONLY though: it walks passengers out and never touches cargo, and
            // this driver skips pawns too, so it competes with us for NOTHING. Treating it as a conflict made HD
            // refuse to start for the whole passenger-exit window after every landing (the reported "toggled it on
            // and nothing happened for a while"). Item-dropping modes (All / NonRequired) stay conflicts.
            var ship = comp.Shuttle?.shipParent;
            if (ship?.curJob is ShipJob_Unload drain && drain.dropMode != TransportShipDropMode.PawnsOnly)
                return true;
            // NOTHING is committed INTO the hold anymore once the manifest has drained (everything loaded out, or
            // the player removed the remaining rows via the Contents tab). At that point flow signals must not
            // pin the mutual exclusion shut: a straggler hauler still walking its last stack is benign (a lone
            // deposit into an otherwise-idle hold cannot fight our pulls, which skip pawns and re-check each
            // stack at transfer time). Sessions with pawns awaiting boarding stay protected, pawns sit in the
            // manifest until they enter, and a live lord below keeps fully-loaded ready-to-launch transporters
            // owned regardless.
            if (!comp.AnyInGroupHasAnythingLeftToLoad)
                return false;
            return LoadFlowActive(comp);
        }

        /// <summary>
        /// The LOAD half of <see cref="ConflictActive"/>: something is actively loading INTO this group, vanilla's
        /// boarding lord (<c>FindLord</c>), an HD bulk-load courier, or a vanilla <c>HaulToTransporter</c> hauler.
        /// Shared by the conflict gate AND the flag-ledger's auto-clear pass, which must agree exactly on when a
        /// flagged transporter has been taken over by a load. All reads are synced world state (deterministic on
        /// every multiplayer client); O(pawns) worst case, called at offer/toggle time and on slow cadences.
        /// </summary>
        internal static bool LoadFlowActive(CompTransporter comp)
        {
            if (comp == null || comp.parent == null || !comp.parent.Spawned)
                return false;
            if (comp.groupID < 0)
                return false;
            var map = comp.parent.Map;
            if (map == null)
                return false;
            // 1. Vanilla's own loading/boarding lord for this group.
            if (TransporterUtility.FindLord(comp.groupID, map) != null)
                return true;
            // 2. An HD bulk-load courier still working this group (its claims + active driver).
            if (BulkLoadAntiConflict.AnyHdLoaderForGroup(comp))
                return true;
            // 3. Any spawned pawn running a vanilla HaulToTransporter job INTO this group (target B = the
            //    transporter parent).
            var spawned = map.mapPawns?.AllPawnsSpawned;
            if (spawned == null)
                return false;
            for (int i = 0; i < spawned.Count; i++)
            {
                var p = spawned[i];
                var cur = p?.CurJob;
                if (cur == null || cur.def != JobDefOf.HaulToTransporter)
                    continue;
                var targetComp = cur.GetTarget(TargetIndex.B).Thing?.TryGetComp<CompTransporter>();
                if (targetComp != null && targetComp.groupID == comp.groupID)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when the player flagged THIS transporter with "Bulk unload all". The MUTUAL-EXCLUSION fact: every
        /// origin that can start or continue a LOAD consults this (the Set-to-load gizmo grays out, and
        /// <c>TransportLoad</c>'s work-check/job-build pair refuses), so cargo can never be ordered into a hold
        /// haulers are actively emptying, the two flows racing each other looked to the player like both quietly
        /// misbehaving. Read-only over synced world state.
        /// </summary>
        internal static bool UnloadFlagActive(CompTransporter comp)
            => comp?.parent != null
               && (HaulersDreamGameComponent.Instance?.BulkUnloadAllActive(comp.parent.thingIDNumber) ?? false);

        /// <summary>
        /// True while a LOAD SESSION owns this hold ("Set to load" pressed and not yet torn down). This is the
        /// early-window complement to <see cref="ConflictActive"/>: between the load confirm and the first hauler
        /// acting there is no lord, no courier and no haul job yet (vanilla's <c>InitiateLoading</c> only stamps
        /// group IDs, and a pure-cargo load never grows a lord at all), so the flow checks alone see nothing — but
        /// HD recorded the session's birth itself (<c>Patch_TransporterLoadSessions</c>). The unload toggle, the
        /// right-click order and the driver's yield check all consult this so a load can never be raced during its
        /// first ticks.
        /// </summary>
        internal static bool LoadSessionActive(CompTransporter comp)
            => comp != null
               && (HaulersDreamGameComponent.Instance?.LoadSessionActive(comp.groupID) ?? false);

        /// <summary>
        /// True while a RECORDED load session still has an open manifest (goods or boarding pawns left to load).
        /// This carries the grey-out past its two event blind spots: topping up an already-open session ("Set to
        /// load" again) never calls <c>InitiateLoading</c>, so only the manifest shows the new commitment; and
        /// finishing or hand-removing everything (<c>leftToLoad</c> drained, via loading or the Contents-tab X)
        /// closes the hold for the load side even though no teardown method ran. Requires the session RECORD, not
        /// just a stale groupID + manifest: landed shuttles keep both forever, and they are exactly what this
        /// feature must stay usable on.
        /// </summary>
        internal static bool LoadSessionHasOpenManifest(CompTransporter comp)
            => comp != null && comp.AnyInGroupHasAnythingLeftToLoad
               && (HaulersDreamGameComponent.Instance?.LoadSessionActive(comp.groupID) ?? false);

        /// <summary>
        /// THE PULLABILITY RULE, true when the hold holds at least one stack this feature can pull: non-null,
        /// not destroyed, and not a <see cref="Pawn"/> (passengers leave via their own boarding/exit mechanics, 
        /// yanking one into a backpack is not a thing). Every site that counts "is there anything for us here"
        /// consults THIS and no other: the float-menu offer, the gizmo's disabled-reason, the driver's
        /// flag-clear-on-finish, and (by construction) the planner snapshot, which applies the same three skips.
        /// The autonomous workgiver's scan additionally respects forbiddance, so it keeps its own loop.
        /// </summary>
        internal static bool HasPullableContents(CompTransporter comp)
        {
            var hold = comp?.innerContainer;
            for (int i = 0; hold != null && i < hold.Count; i++)
            {
                var t = hold[i];
                if (t != null && !t.Destroyed && !(t is Pawn))
                    return true;
            }
            return false;
        }
    }
}
