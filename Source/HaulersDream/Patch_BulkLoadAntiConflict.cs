using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Anti-conflict patch: suppress the false "loading stalled" alert while HD haulers are mid-trip. Vanilla's
    /// <c>CompTransporter.AnyPawnCanLoadAnythingNow</c> only knows about <c>HaulToTransporter</c>/<c>EnterTransporter</c>
    /// jobs — it does NOT recognize HD's bulk-load driver, so without this it returns false while an HD courier is
    /// sweeping/walking, and the launchable fires <c>MessageCantLoadMore</c> every ~60 ticks. The prefix returns
    /// true when a spawned pawn is running <c>HaulersDream_LoadTransportersInBulk</c> targeting this group, OR a
    /// claim is still live on the ledger for this group.
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), "get_AnyPawnCanLoadAnythingNow")]
    public static class Patch_CompTransporter_AnyPawnCanLoadAnythingNow
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        static bool Prefix(CompTransporter __instance, ref bool __result)
        {
            if (BulkLoadAntiConflict.AnyHdLoaderForGroup(__instance)
                || (HaulersDreamGameComponent.Instance?.LoadAnyClaimsInProgress(__instance.groupID) ?? false))
            {
                __result = true;
                return false; // suppress the false stall
            }
            return true; // no HD activity -> vanilla decides
        }
    }

    /// <summary>
    /// Anti-conflict patch: don't let a pawn BOARD while HD hauling is in flight for its group — premature boarding
    /// can trigger launch before the claimed manifest is loaded. A prefix on
    /// <c>JobGiver_EnterTransporter.TryGiveJob</c> returns null (no board job) while there is potential bulk work or
    /// a live claim — but ONLY under the boarding lord (<c>DutyDefOf.LoadAndEnterTransporters</c> + a real
    /// <c>transportersGroup</c>), never for a drafted MANUAL enter.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_EnterTransporter), "TryGiveJob")]
    public static class Patch_JobGiver_EnterTransporter_BoardGate
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        static bool Prefix(Pawn pawn, ref Job __result)
        {
            var duty = pawn?.mindState?.duty;
            if (duty == null || duty.def != DutyDefOf.LoadAndEnterTransporters)
                return true; // not the loading lord (drafted manual enter, etc.) -> vanilla
            int group = duty.transportersGroup;
            if (group < 0)
                return true;
            // Authoritative "loading is done" signal overrides HD's in-flight gate: the instant the group's GOODS
            // manifest is empty, let the pawn board — otherwise a lingering claim or a still-tearing-down HD loader
            // job keeps the pawn "waiting" after the cargo is already aboard (the reported bug). NOTE the boarding
            // pawns are themselves listed in leftToLoad until they enter, so we check for any NON-PAWN transferable
            // still to load, NOT AnyInGroupHasAnythingLeftToLoad (which stays true while pawns wait). While real
            // goods remain (claimed-but-not-yet-deposited cargo is still in leftToLoad), this is false and the gate
            // below still blocks — so it can never let a pawn board before the cargo is actually in.
            if (!BulkLoadAntiConflict.GroupHasGoodsLeftToLoad(pawn.Map, group))
                return true;
            var ledger = HaulersDreamGameComponent.Instance;
            if (ledger != null && ledger.LoadAnyClaimsInProgress(group))
            {
                __result = null; // hauling still in flight -> don't board yet
                return false;
            }
            // Also gate on a spawned pawn still running the HD load driver for this group (claim may settle a tick
            // before the deposit completes; the active driver is the authoritative "still loading" signal).
            if (BulkLoadAntiConflict.AnyHdLoaderForGroupId(pawn.Map, group))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Anti-conflict patch (portal): suppress the false "can't load more into portal" notice while HD haulers are
    /// mid-trip. Vanilla's <c>MapPortal.AnyPawnCanLoadAnythingNow</c> only knows about <c>HaulToPortal</c>/
    /// <c>EnterPortal</c> jobs + the boarding duty — it does NOT recognize HD's bulk-load driver, so without this it
    /// returns false while an HD courier is sweeping/walking, and the portal fires <c>MessageCantLoadMoreIntoPortal</c>
    /// every ~60 ticks. The prefix returns true when a spawned pawn is running <c>HaulersDream_LoadPortalInBulk</c>
    /// targeting this portal, OR a claim is still live on the ledger for this portal.
    /// </summary>
    [HarmonyPatch(typeof(MapPortal), "get_AnyPawnCanLoadAnythingNow")]
    public static class Patch_MapPortal_AnyPawnCanLoadAnythingNow
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadPortal ?? true;

        static bool Prefix(MapPortal __instance, ref bool __result)
        {
            if (BulkLoadAntiConflict.AnyHdLoaderForPortal(__instance)
                || (HaulersDreamGameComponent.Instance?.LoadAnyClaimsInProgress(MapPortalBulkTarget.LedgerKey(__instance)) ?? false))
            {
                __result = true;
                return false; // suppress the false stall
            }
            return true; // no HD activity -> vanilla decides
        }
    }

    /// <summary>
    /// Anti-conflict patch (portal): don't let a pawn ENTER the portal while HD hauling is in flight for it —
    /// premature entry can leave the manifest unloaded. A prefix on <c>JobGiver_EnterPortal.TryGiveJob</c> returns
    /// null (no enter job) while there is potential bulk work or a live claim — but ONLY under the portal-boarding
    /// lord (<c>DutyDefOf.LoadAndEnterPortal</c> with the portal as the duty focus), never for a drafted MANUAL
    /// enter, and ONLY while THIS pawn could still claim manifest work itself: a pawn with nothing claimable (every
    /// remaining unit is another loader's live slice) boards instead of idling, matching vanilla, which has no enter
    /// gate and boards pawns whose FindThingToLoad comes up empty while others haul.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_EnterPortal), "TryGiveJob")]
    public static class Patch_JobGiver_EnterPortal_BoardGate
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadPortal ?? true;

        static bool Prefix(Pawn pawn, ref Job __result)
        {
            // Runtime feature check, not just the patch-time Prepare gate: this prefix WRITES ledger state below
            // (LoadRegisterOrUpdate creates/refreshes a scribed loadTasks entry), so after a mid-session toggle-off
            // it must stand down entirely (register nothing, gate nothing) and let vanilla enter behavior run
            // unchanged, exactly like every other portal registration path that re-checks the setting at runtime.
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableBulkLoadPortal)
                return true;
            var duty = pawn?.mindState?.duty;
            if (duty == null || duty.def != DutyDefOf.LoadAndEnterPortal)
                return true; // not the loading lord (drafted manual enter, etc.) -> vanilla
            var portal = duty.focus.Thing as MapPortal;
            if (portal == null)
                return true;
            // Authoritative "loading is done" signal (mirrors the transporter gate): once the portal's GOODS
            // manifest is empty, let the pawn enter even if an HD claim/loader hasn't fully torn down — otherwise
            // the pawn keeps "waiting" after the cargo is already in. Check NON-PAWN transferables (entering pawns
            // sit in leftToLoad until they enter); claimed-but-undeposited goods are still listed, so this can't let
            // a pawn enter early.
            if (!BulkLoadAntiConflict.HasGoodsLeftToLoad(portal.leftToLoad))
                return true;
            var ledger = HaulersDreamGameComponent.Instance;
            // Board when THIS pawn has nothing left to claim (goods remain, but every remaining unit is another
            // loader's live slice). Blocking such a pawn has zero coordination value and was the second half of the
            // reported "one pawn gathers the loot, the rest wander": the pawn's duty tree just tried
            // JobGiver_HaulToPortal FIRST (the LoadAndEnterPortal think tree is ordered HaulToPortal, then
            // UnloadYourInventory, then EnterPortal), so it provably has no load job this cycle, and vanilla itself
            // boards such pawns (FindThingToLoad accounts for in-flight hauls; vanilla has no enter gate at all).
            // The in-flight loaders finish the manifest. A pawn the ledger says CAN still claim stays blocked below,
            // unchanged: its HaulToPortal picks that work up on the next think cycle, and if it never can act on it
            // (say the stacks are unreachable), the block still self-releases when the in-flight claims drain.
            if (ledger != null)
            {
                var adapter = MapPortalBulkTarget.TryCreate(portal);
                if (adapter != null)
                {
                    // Refresh needed from the live manifest first (the planner's own idiom) so a stale ledger entry
                    // cannot misread "nothing claimable" while goods remain.
                    ledger.LoadRegisterOrUpdate(adapter);
                    if (!ledger.LoadHasWork(adapter, pawn))
                        return true; // nothing claimable for THIS pawn -> board; loaders finish the rest
                }
            }
            if (ledger != null && ledger.LoadAnyClaimsInProgress(MapPortalBulkTarget.LedgerKey(portal)))
            {
                __result = null; // hauling still in flight -> don't enter yet
                return false;
            }
            // Also gate on a spawned pawn still running the HD portal-load driver for this portal (the claim may
            // settle a tick before the deposit completes; the active driver is the authoritative "still loading").
            if (BulkLoadAntiConflict.AnyHdLoaderForPortal(portal))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    /// <summary>Shared helpers for the anti-conflict patches.</summary>
    internal static class BulkLoadAntiConflict
    {
        // Reused scratch for the per-group transporter lookup (board gate runs on the main think-tree thread only,
        // for the handful of pawns under a load-and-enter duty — not a hot path; cleared after each use).
        private static readonly List<CompTransporter> tmpTransporters = new List<CompTransporter>();

        /// <summary>True if any transporter in <paramref name="groupID"/> still has a NON-PAWN (cargo) transferable
        /// left to load. The boarding pawns sit in <c>leftToLoad</c> until they enter, so this deliberately ignores
        /// pawn transferables — it answers "is there still CARGO to load?", the authoritative board-release signal.</summary>
        internal static bool GroupHasGoodsLeftToLoad(Map map, int groupID)
        {
            if (map == null || groupID < 0)
                return false;
            TransporterUtility.GetTransportersInGroup(groupID, map, tmpTransporters);
            bool goods = false;
            for (int i = 0; i < tmpTransporters.Count && !goods; i++)
                goods = HasGoodsLeftToLoad(tmpTransporters[i]?.leftToLoad);
            tmpTransporters.Clear();
            return goods;
        }

        /// <summary>True if <paramref name="leftToLoad"/> contains any NON-PAWN transferable still owing units —
        /// i.e. real cargo, not a boarding/entering pawn (which sits in the manifest until it enters).</summary>
        internal static bool HasGoodsLeftToLoad(List<TransferableOneWay> leftToLoad)
        {
            if (leftToLoad == null)
                return false;
            for (int i = 0; i < leftToLoad.Count; i++)
            {
                var tr = leftToLoad[i];
                if (tr == null || tr.CountToTransfer <= 0)
                    continue;
                if (tr.AnyThing is Pawn)
                    continue; // a boarding pawn, not cargo
                return true;
            }
            return false;
        }

        /// <summary>True if a spawned pawn on this transporter's map runs HD's bulk-load driver targeting this group.</summary>
        internal static bool AnyHdLoaderForGroup(CompTransporter transporter)
        {
            if (transporter?.parent?.Map == null)
                return false;
            return AnyHdLoaderForGroupId(transporter.parent.Map, transporter.groupID);
        }

        internal static bool AnyHdLoaderForGroupId(Map map, int groupID)
        {
            if (map?.mapPawns == null || groupID < 0)
                return false;
            var spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                var p = spawned[i];
                if (p?.CurJob == null || p.CurJobDef != HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk)
                    continue;
                var comp = p.CurJob.GetTarget(TargetIndex.A).Thing?.TryGetComp<CompTransporter>();
                if (comp != null && comp.groupID == groupID)
                    return true;
            }
            return false;
        }

        /// <summary>True if a spawned pawn on this portal's map runs HD's bulk-portal-load driver targeting it.</summary>
        internal static bool AnyHdLoaderForPortal(MapPortal portal)
        {
            if (portal?.Map?.mapPawns == null)
                return false;
            var spawned = portal.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                var p = spawned[i];
                if (p?.CurJob == null || p.CurJobDef != HaulersDreamDefOf.HaulersDream_LoadPortalInBulk)
                    continue;
                if (p.CurJob.GetTarget(TargetIndex.A).Thing == portal)
                    return true;
            }
            return false;
        }
    }
}
