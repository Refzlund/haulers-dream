using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// LOAD-SESSION lifecycle tracking for the load/unload MUTUAL EXCLUSION. Vanilla gives no queryable "a load
    /// session is active on this transporter" state: <c>InitiateLoading</c> only stamps groupIDs, the loading lord
    /// exists only when BOARDING PAWNS were designated (a pure-cargo load runs lordless its whole life), and on
    /// shuttles both groupID and the manifest survive landing — so neither <c>FindLord</c> nor the manifest can
    /// distinguish a fresh session from a landed shuttle's stale leftovers. HD records the two ends itself:
    ///
    /// <para>BIRTH — this postfix on <see cref="TransporterUtility.InitiateLoading"/>: every "Set to load" confirm
    /// funnels through it (and it replays identically on every multiplayer client, so the direct scribed-set writes
    /// below are desync-free). It (a) clears any "Bulk unload all" flag on the initiated transporters — setting a
    /// load takes the hold over — and (b) records the new group ID as a live session so the toggle and the unload
    /// order grey out from the very first frame, before the first hauler even picks up work.</para>
    ///
    /// <para>DEATH — the <see cref="CompTransporter.TryRemoveLord"/> postfix: that one method is the funnel for
    /// every way a session ends (CancelLoad, launch teardown, ShipJob_Unload's end after landing). The postfix reads
    /// the groupID BEFORE the caller's CleanUpLoadingVars resets it to -1.</para>
    /// </summary>
    [HarmonyPatch(typeof(TransporterUtility), nameof(TransporterUtility.InitiateLoading))]
    public static class Patch_TransporterUtility_InitiateLoading_RecordSession
    {
        static void Postfix(IEnumerable<CompTransporter> transporters, int __result)
        {
            var ledger = HaulersDreamGameComponent.Instance;
            if (ledger == null || transporters == null)
                return;
            ledger.LoadSessionSet(__result, true);
            foreach (var transporter in transporters)
            {
                if (transporter?.parent == null)
                    continue;
                // Setting a load takes the hold over: an existing "Bulk unload all" flag is turned OFF here,
                // synchronously, exactly as the feature's mutual-exclusion rule promises.
                ledger.BulkUnloadAllSet(transporter.parent.thingIDNumber, false);
            }
        }
    }

    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.TryRemoveLord))]
    public static class Patch_CompTransporter_TryRemoveLord_EndSession
    {
        static void Postfix(CompTransporter __instance)
        {
            // Read BEFORE the caller's CleanUpLoadingVars resets groupID to -1 (this postfix runs between
            // TryRemoveLord returning and that reset).
            HaulersDreamGameComponent.Instance?.LoadSessionSet(__instance.groupID, false);
        }
    }

    /// <summary>
    /// The OTHER birth seam: confirming the load dialog on an ALREADY-OPEN session ("Set to load" pressed again to
    /// add more cargo) takes the <c>LoadingInProgressOrReadyToLaunch</c> branch, which never calls
    /// <c>InitiateLoading</c> — it just assigns transferables into the existing group. Without this postfix such a
    /// top-up was invisible to the mutual exclusion: the hold started accepting unload orders again while new
    /// cargo was being committed INTO it. Recording here covers both branches of <c>TryAccept</c> uniformly.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_LoadTransporters), "TryAccept")]
    public static class Patch_Dialog_LoadTransporters_TryAccept_RecordSession
    {
        // GATED ON THE RESULT: TryAccept returns false on CheckForErrors failures (empty load, over mass
        // capacity, unreachable targets) with the dialog left open. Recording the session on a FAILED accept once
        // pinned the mutual exclusion shut on a stale group until an obscure recovery, and the unconditional flag
        // clear could strip a live "Bulk unload all" from an unrelated pod sharing the multi-select list.
        static void Postfix(bool __result, List<CompTransporter> ___transporters)
        {
            if (!__result)
                return;
            var ledger = HaulersDreamGameComponent.Instance;
            if (ledger == null || ___transporters == null)
                return;
            foreach (var transporter in ___transporters)
            {
                if (transporter == null)
                    continue;
                ledger.LoadSessionSet(transporter.groupID, true);
                ledger.BulkUnloadAllSet(transporter.parent?.thingIDNumber ?? -1, false);
            }
        }
    }

    /// <summary>
    /// FRAME-EXACT flag release for the Contents-tab red X: every item a player X-es out of a transporter hold
    /// leaves through <c>OnDropThing</c>, so this postfix can clear the "Bulk unload all" flag on the very frame
    /// the hold stops having anything pullable. Without it the clear waited for the slow-tick janitor pass, and
    /// for that window BOTH gizmos greyed out at once (the toggle "nothing to unload", Set-to-load via the
    /// still-on flag) — the reported deadlock made visible. The janitor pass remains as the backstop for paths
    /// that never touch this tab. The condition mirrors the janitor's emptied-hold arm exactly:
    /// <see cref="BulkUnloadTransporterGate.HasPullableContents"/> (pawns and destroyed husks don't count).
    /// </summary>
    [HarmonyPatch(typeof(ITab_ContentsTransporter), "OnDropThing")]
    public static class Patch_ITab_ContentsTransporter_OnDropThing_ClearFlag
    {
        static void Postfix(ITab_ContentsTransporter __instance)
        {
            var ledger = HaulersDreamGameComponent.Instance;
            var comp = __instance?.Transporter;
            if (ledger == null || comp?.parent == null || !ledger.BulkUnloadAllActive(comp.parent.thingIDNumber))
                return;
            if (!BulkUnloadTransporterGate.HasPullableContents(comp))
                ledger.BulkUnloadAllSet(comp.parent.thingIDNumber, false);
        }
    }

    /// <summary>
    /// Synchronous flag release for vanilla's own "Unload" gizmo (<c>CancelLoad()</c>): the player just dumped the
    /// hold and taken over, so the "Bulk unload all" flag must die THIS frame, not on the janitor's next pass —
    /// same no-wrong-UI-window rule as the tab X above. The session half needs no synchronous handling here:
    /// <c>CancelLoad</c> calls <c>TryRemoveLord</c> internally, whose own postfix ends the session while the
    /// groupID is still valid.
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.CancelLoad), new Type[0])]
    public static class Patch_CompTransporter_CancelLoad_ClearUnloadFlag
    {
        static void Postfix(CompTransporter __instance)
        {
            HaulersDreamGameComponent.Instance?.BulkUnloadAllSet(__instance?.parent?.thingIDNumber ?? -1, false);
        }
    }
}
